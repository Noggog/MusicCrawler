using System.Diagnostics;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Download;

/// <summary>
/// Acquires music by shelling out to <b>streamrip</b> (https://github.com/nathom/streamrip), a
/// maintained Deezer/Qobuz/Tidal CLI. streamrip owns the fragile parts — the Deezer ARL session,
/// per-track Blowfish decryption, quality selection and tagging — while we own orchestration and
/// throttling (see <c>DownloadService</c>).
///
/// We invoke the configured binary (default <c>rip</c>, resolved via the backend process's PATH, or
/// an absolute path via <c>STREAMRIP_BIN</c>) as:
///   <c>rip --no-db --folder DIR --quality Q [--codec C] url https://www.deezer.com/album/{id}</c>
/// (<c>--no-db</c> bypasses streamrip's download-history DB — we dedup via purchase status, and the DB
/// would otherwise skip tracks it thinks are already downloaded, leaving just the cover.)
/// Quality defaults to FLAC; if a FLAC pass cleanly fails (e.g. the album isn't available lossless on
/// the account) we retry once at the configured fallback quality (320 kbps MP3). A pass that hangs is
/// killed after <see cref="DownloaderConfig.DownloadTimeout"/> and reported — we do <i>not</i> burn a
/// second timeout on the fallback in that case. Only <see cref="FeedKind.MissingAlbum"/> items with a
/// Deezer album id are downloadable ("albums only"). This is the one place that knows streamrip's CLI;
/// every attempt logs its command, and timeouts/failures log streamrip's captured stdout+stderr.
/// </summary>
public class StreamripDownloader : IDownloader
{
    private enum RunResult { Success, Failed, TimedOut }

    private readonly DownloaderConfig _config;
    private readonly ILogger<StreamripDownloader> _logger;

    public StreamripDownloader(DownloaderConfig config, ILogger<StreamripDownloader> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string Name => "streamrip (Deezer)";

    public async Task<bool> Request(PurchaseItem item)
    {
        if (item.Kind != FeedKind.MissingAlbum || item.DeezerAlbumId is null or 0)
        {
            _logger.LogInformation(
                "Skipping {Id}: only Deezer albums are downloadable (artists are wishlist-only)", item.Id);
            return false;
        }

        var url = $"https://www.deezer.com/album/{item.DeezerAlbumId.Value}";

        var first = await RunAt(_config.Quality, url, item);
        if (first == RunResult.Success)
        {
            return true;
        }

        // A hang/timeout is a systemic problem (bad ARL, network) — retrying at MP3 would just burn
        // another timeout. Only downgrade when the preferred quality cleanly failed (e.g. no lossless).
        var fallback = _config.FallbackQuality;
        if (first == RunResult.Failed && !string.IsNullOrWhiteSpace(fallback) && fallback != _config.Quality)
        {
            _logger.LogInformation(
                "Quality {Q} pass failed for {Artist} — {Album}; retrying at fallback {Fallback}",
                _config.Quality, item.Artist.ArtistName, item.Album, fallback);
            return await RunAt(fallback, url, item) == RunResult.Success;
        }

        return false;
    }

    private async Task<RunResult> RunAt(string quality, string url, PurchaseItem item)
    {
        var args = new List<string>();
        // We own dedup (purchase status), so streamrip's download-history DB must never skip a track —
        // otherwise re-downloading after a deleted/partial grab silently fetches only the cover.
        args.Add("--no-db");
        if (!string.IsNullOrWhiteSpace(_config.DownloadDir))
        {
            args.Add("--folder");
            args.Add(_config.DownloadDir);
        }
        if (!string.IsNullOrWhiteSpace(quality))
        {
            args.Add("--quality");
            args.Add(quality);
        }
        if (!string.IsNullOrWhiteSpace(_config.Codec))
        {
            args.Add("--codec");
            args.Add(_config.Codec);
        }
        args.Add("url");
        args.Add(url);

        var cmd = $"{_config.RipBinary} {string.Join(' ', args)}";
        _logger.LogInformation(
            "streamrip start: {Artist} — {Album} (quality {Quality}) → {Cmd}",
            item.Artist.ArtistName, item.Album, quality, cmd);

        var psi = new ProcessStartInfo
        {
            FileName = _config.RipBinary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            // Read both pipes concurrently so a full buffer can't deadlock the child.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(_config.DownloadTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogError(
                    "streamrip TIMED OUT after {Timeout} for {Artist} — {Album}; killing it. {Cmd}",
                    _config.DownloadTimeout, item.Artist.ArtistName, item.Album, cmd);
                TryKill(process);
                var (to, te) = await Capture(stdoutTask, stderrTask);
                _logger.LogWarning("streamrip output before timeout for {Id}:\nstdout: {Out}\nstderr: {Err}",
                    item.Id, to, te);
                return RunResult.TimedOut;
            }

            var (stdout, stderr) = await Capture(stdoutTask, stderrTask);
            if (process.ExitCode == 0)
            {
                _logger.LogInformation(
                    "Downloaded {Artist} — {Album} (quality {Quality})", item.Artist.ArtistName, item.Album, quality);
                return RunResult.Success;
            }

            _logger.LogWarning(
                "streamrip exited {Code} for {Artist} — {Album}. {Cmd}\nstdout: {Out}\nstderr: {Err}",
                process.ExitCode, item.Artist.ArtistName, item.Album, cmd, stdout, stderr);
            return RunResult.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch streamrip ({Bin}) for {Id}", _config.RipBinary, item.Id);
            return RunResult.Failed;
        }
    }

    /// <summary>Awaits the captured output streams, tolerating either one faulting.</summary>
    private static async Task<(string Out, string Err)> Capture(Task<string> stdout, Task<string> stderr)
    {
        try
        {
            return (await stdout, await stderr);
        }
        catch
        {
            return ("<unavailable>", "<unavailable>");
        }
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not kill streamrip process {Pid}", SafePid(process));
        }
    }

    private static int SafePid(Process p)
    {
        try { return p.Id; }
        catch { return -1; }
    }
}
