# Building

How the solution is laid out and built. See the [README](../README.md) for an overview.

```
src/MomentumPorter.Core    the porting code (class library)
src/MomentumPorter.Cli     MomentumPorter.exe, the command line
src/MomentumPorter.UI      MomentumPorterUI.exe, the Avalonia desktop app
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
