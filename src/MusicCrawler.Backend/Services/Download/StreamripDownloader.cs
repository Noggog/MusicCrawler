using System.Diagnostics;
using MusicCrawler.Interfaces;

namespace MusicCrawler.Backend.Services.Download;

/// <summary>
/// Acquires music by shelling out to <b>streamrip</b> (https://github.com/nathom/streamrip), a
/// maintained Deezer/Qobuz/Tidal CLI. streamrip owns the fragile parts — the Deezer ARL session,
/// per-track Blowfish decryption, quality selection and tagging — while we own orchestration and
/// throttling (see <c>DownloadService</c>).
///
/// Only <see cref="FeedKind.MissingAlbum"/> items with a Deezer album id are downloadable (the
/// "albums only" policy): we hand streamrip the album URL so it lays down a properly-tagged album
/// folder. Artist items are skipped. This is the one place that knows streamrip's CLI — if your
/// install differs, adjust the invocation here; failures log the exact command + stderr.
/// </summary>
public class StreamripDownloader : IDownloader
{
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

        // Global options (folder/quality/codec) precede the subcommand; ARL is configured in streamrip.
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(_config.DownloadDir))
        {
            args.Add("--folder");
            args.Add(_config.DownloadDir);
        }
        if (!string.IsNullOrWhiteSpace(_config.Quality))
        {
            args.Add("--quality");
            args.Add(_config.Quality);
        }
        if (!string.IsNullOrWhiteSpace(_config.Codec))
        {
            args.Add("--codec");
            args.Add(_config.Codec);
        }
        args.Add("url");
        args.Add(url);

        return await Run(args, item);
    }

    private async Task<bool> Run(IReadOnlyList<string> args, PurchaseItem item)
    {
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
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Downloaded {Artist} — {Album}", item.Artist.ArtistName, item.Album);
                return true;
            }

            _logger.LogWarning(
                "streamrip exited {Code} for {Cmd} {Args}\nstdout: {Out}\nstderr: {Err}",
                process.ExitCode, _config.RipBinary, string.Join(' ', args), stdout, stderr);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch streamrip ({Bin}) for {Id}", _config.RipBinary, item.Id);
            return false;
        }
    }
}
