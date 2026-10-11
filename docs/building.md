# Building

How the solution is laid out and built. See the [README](../README.md) for an overview.

```
src/MomentumPorter.Core    the porting code (class library)
src/MomentumPorter.Cli     MomentumPorter-CLI.exe, the command line
src/MomentumPorter.UI      MomentumPorter-UI.exe, the Avalonia desktop app
tests/MomentumPorter.Tests xunit tests (dotnet test); the ones that need installed games skip when they aren't there
```

Needs the .NET 10 SDK. [BSPConvert](https://github.com/momentum-mod/BSPConvert) is a submodule (`external/BSPConvert`,
referenced as a project), so clone with `--recursive` or run `git submodule update --init` after cloning, then
`dotnet build`. LibBSP comes with it: the porter uses the build of [LibBSP](https://github.com/Panzerhandschuh/LibBSP)'s
fork that BSPConvert ships (its LZMA support is needed for compressed maps).

To work on BSPConvert or LibBSP alongside the porter, copy `Directory.Build.local.props.example` to
`Directory.Build.local.props` and point it at your own checkouts (LibBSP's Release build is used). To move the
submodule to a newer BSPConvert, push it first, then `git submodule update --remote external/BSPConvert` and commit.
`MomentumPorter.slnx` also lists Lumper's library projects (`../lumper`) for reference; build the projects individually
if you don't have that checkout.

## Releases

`tools/publish-release.ps1` builds the archive to share: it publishes `MomentumPorter-UI.exe` and
`MomentumPorter-CLI.exe` as single-file, framework-dependent win-x64 executables (the .NET 10 Desktop Runtime has to
be installed; the managed dependencies live inside each executable and the native ones are extracted on first run),
with the bundled bspPack and BSPConvert's content folders next to them, then 7-zips the folder as
`artifacts/MomentumPorter-win-x64-v<version>.7z` (`artifacts/` is git ignored).

```
powershell -File tools/publish-release.ps1 -Version 1.2.0
```

To publish it, create a GitHub release tagged `v1.2.0` (not a draft or prerelease) and attach the archive. Without
`-Version` the build is a development build (version 0.0.0) and the archive is named after the commit.

### Updates

When the desktop app starts, it asks GitHub for the latest release
(`api.github.com/repos/Panzerhandschuh/MomentumPorter/releases/latest`). If that release's tag is a newer version
than its own and it has a `win-x64` `.7z` or `.zip` asset, the app asks whether to update. If the user says yes, it
downloads the archive into `.update` next to the executable and checks it against the SHA-256 GitHub lists. Then it
extracts the archive and moves each file over the old one; the old files are renamed first, since Windows won't let a
running executable be overwritten. Finally it starts the new `MomentumPorter-UI.exe` and exits, and the new copy deletes
the renamed files. Files the new release doesn't have are left alone. If jobs are running, the user can choose to have
the update installed when they close the app instead. Development builds and builds that aren't single-file don't check,
and the check can be turned off on the Settings page.

To try an update without publishing a release, publish two versions and serve a copy of GitHub's release JSON
locally. Point the older copy at the JSON with the `MOMENTUMPORTER_UPDATE_FEED` environment variable:

```
powershell -File tools/publish-release.ps1 -Version 0.1.0 -Output C:\temp\v010
powershell -File tools/publish-release.ps1 -Version 0.2.0 -Output C:\temp\feed
```

In `C:\temp\feed`, write `latest.json` with the fields the updater reads, such as
`{"tag_name": "v0.2.0", "html_url": "...", "body": "...", "assets": [{"name": "MomentumPorter-win-x64-v0.2.0.7z", "size": <bytes>, "browser_download_url": "http://127.0.0.1:8765/MomentumPorter-win-x64-v0.2.0.7z"}]}`.
Then run `python -m http.server 8765 --bind 127.0.0.1 --directory C:\temp\feed`, set
`MOMENTUMPORTER_UPDATE_FEED=http://127.0.0.1:8765/latest.json` and start `C:\temp\v010\MomentumPorter\MomentumPorter-UI.exe`.
