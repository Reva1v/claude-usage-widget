# Contract: GitHub latest release and the update flow

Owners: `cuw-release` (parse, compare, decisions, texts), `cuw-updater` (IO). Source: INV 2A §5.

## Request

`GET https://api.github.com/repos/Reva1v/claude-usage-widget/releases/latest`, headers
`Accept: application/vnd.github+json`, `User-Agent: ClaudeUsageWidget/<version>`; unauthenticated;
5 min timeout. Non-2xx -> error `Response status code does not indicate success: <code> (<reason>).`
(text shown as `Update failed: <msg>`; exact wording is cosmetic, keep the code in it).

## Parse (`LatestRelease::parse`)

- Invalid JSON -> error (update failure). Root not an object -> `None`.
- `tag_name` non-empty string and `html_url` absolute URL required, else `None` (-> error
  `GitHub returned no release.`).
- `asset_url` = first `assets[]` with `name` ending `-win-x64.zip` (ASCII case-insensitive) and an
  absolute `browser_download_url`; a bad URL skips that asset.
- `version = tag_name` with leading `v`/`V` characters trimmed.

## Compare (`is_newer(latest, current)`)

`System.Version` rules (data-model.md §10); unparseable either side -> false; strictly greater.
Version source in Rust: `CARGO_PKG_VERSION` of `cuw-app` cut at the first `+`; empty -> `unknown`
(CoreInfo.ParseVersion). Must stay `MAJOR.MINOR.PATCH` without pre-release suffix (C# updater
compatibility, FR-022).

## Flow (Rust updater)

1. Single flight (busy flag). Log `- update check current=<v>`.
2. Not newer -> log `update up-to-date latest=<v>`, MessageBox (Information) `You have the latest
   version (v<current>).`.
3. Block reasons -> MessageBox Yes/No (default Yes) `Version v<latest> is available.\n\n<why>\n\nOpen
   the release page?`; Yes -> log `browser-open site=update url=<page>` and open it.
   `<why>`: no asset `The release has no Windows build attached.`; source build (not built with
   `CUW_RELEASE_BUILD=1`) `This copy runs from a source build, so it can't replace itself.`; folder
   not writable (probe file `.update-probe-<32 hex>` create+delete fails) `The app folder is not
   writable:\n<dir>`.
4. Ask `Version v<latest> is available (you have v<current>).\n\nDownload and install it now? The
   widget will restart.` (Yes/No, default Yes).
5. Work dir `%TEMP%\ClaudeUsageWidget-update-<latest>` recreated; log `update download <url>`;
   stream to `release.zip` (redirects followed); extract to `files\`; find
   `ClaudeUsageWidget.App.exe` anywhere (missing -> `ClaudeUsageWidget.App.exe is not in the release
   zip.`); its directory is the source.
6. Log `update install v<ver> into <dir>`; swap (research R-15); start new exe with
   `--after-update <pid>`; quit.
7. Any error -> log `update failed <Type>: <msg>`, MessageBox (Error) `Update failed: <msg>`.

Message boxes: `MessageBoxW(NULL, text, "Claude Usage Widget", flags | MB_DEFAULT_DESKTOP_ONLY)`.

## Release asset produced by the release flow (S6)

`ClaudeUsageWidget-vX.Y.Z-win-x64.zip` containing `ClaudeUsageWidget.App.exe` at the root (plus
`README.md`/`LICENSE` optional). The C# updater picks it by suffix and exe name.

Fixtures: `github/release-with-assets.json`, `release-no-assets.json`, `not-found.json`
(`{"message":"Not Found"}`), `empty-tag.json`, `no-html-url.json`, `array.json`.
