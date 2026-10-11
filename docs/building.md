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
`artifacts/MomentumPorter-win-x64-<commit>.7z` (`artifacts/` is git ignored).

```
powershell -File tools/publish-release.ps1
```
