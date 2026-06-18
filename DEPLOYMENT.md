# Deploying MusicCrawler (Podman + Komodo)

Two containers: the **app** (ASP.NET Core API + the built React SPA, served together on one HTTP
port) and **MongoDB**. The app speaks plain HTTP — put it behind your own reverse proxy for TLS and
public routing. The Aspire AppHost (`src/AppHost`) is a local-dev orchestrator and is **not** used
here; the backend DLL runs directly and every setting comes from environment variables.

```
  your reverse proxy ──HTTP──▶  app  :43105 ┬─ /            React SPA (static, with deep-link fallback)
   (TLS, public DNS)                        ├─ /api/*        REST API
                                            ├─ /auth/*, /signin-oidc, /signout-callback-oidc  (BFF/OIDC)
                                            └─ rip (streamrip) ─▶ /music  (your Plex library)
                                       app ─▶ mongo :27017
```

| Service | Image (built from) | Purpose                                              |
|---------|--------------------|------------------------------------------------------|
| `app`   | `Dockerfile`       | API + SPA on one HTTP port, with bundled `streamrip` |
| `mongo` | `mongo:7`          | Primary data store                                   |

## Files (all at repo root)

- `compose.yaml` — the stack
- `Dockerfile` — builds the SPA and the API into one image
- `.env.example` — copy to `.env` and fill in (`.env` is gitignored)

## 1. Configure

```bash
cp .env.example .env
# edit .env — see the inline comments
```

Required: `PUBLIC_ORIGIN`, the three `OIDC_*` values, `PLEX_ENDPOINT`, `PLEX_TOKEN`,
`MUSIC_DOWNLOAD_DIR_HOST`, `STREAMRIP_CONFIG_HOST`.

`MUSIC_DOWNLOAD_DIR_HOST` **must be the same storage Plex scans** for its music library — that's how
downloaded albums show up in Plex (the app can also trigger a Plex rescan via
`PLEX_RESCAN_AFTER_DOWNLOAD=true`).

## 2. Reverse proxy + Authentik

Point your reverse proxy at `app:${HTTP_PORT}` and serve it at `PUBLIC_ORIGIN` over HTTPS (the
auth session cookies need HTTPS in practice). Then, in the Authentik OAuth2/OIDC provider for this
app, register the redirect URI:

- `${PUBLIC_ORIGIN}/signin-oidc`  (e.g. `https://music.example.com/signin-oidc`)

Authentik must be reachable from the app container at `OIDC_AUTHORITY`.

## 3. Deploy via Komodo

Create a **Stack** in Komodo pointing at this repo:

- **Compose file:** `compose.yaml`
- **Environment:** paste the contents of your `.env`, or have Komodo write the `.env` file
- Deploy. Komodo (with the Podman engine) builds the `app` image from the top-level `Dockerfile`
  (which builds the SPA too) and starts the stack.

The `condition: service_healthy` on `mongo` uses Compose-spec healthchecks; modern `docker compose`
and Podman both honor it. If your engine doesn't, change it to a plain `depends_on: [mongo]`.

## 4. First-run streamrip (Deezer ARL)

The download path shells out to `streamrip` (`rip`), which keeps the Deezer **ARL** session token in
its own config — not in this app's env. After the stack is up:

```bash
# generate a default config into the mounted config dir, if not present
podman exec -it musiccrawler-app-1 /opt/streamrip/bin/rip config
```

Then edit `config.toml` on the host (at `STREAMRIP_CONFIG_HOST/streamrip/config.toml`) and set:

```toml
[deezer]
arl = "<your deezer ARL>"

[filepaths]
folder_format = "{albumartist}/{title}"
track_format  = "{tracknumber}. {title}"
```

The "Download now" button works as soon as the ARL is set; set `DEEZER_DOWNLOADS_AUTOMATIC=true` to
also drain the queue in the background.

## Notes / troubleshooting

- **Logs:** the app writes rolling logs to the `app_logs` volume (`/app/logs`) and to stdout
  (`podman logs musiccrawler-app-1`).
- **External Mongo:** point `MONGO_URI` at an existing instance and remove the bundled `mongo`
  service.
- **Rebuild after code changes:** redeploy the Stack in Komodo (it rebuilds the image), or
  `podman compose build && podman compose up -d`.
