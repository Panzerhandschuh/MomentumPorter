# bspPack

A prebuilt [bspPack](https://github.com/Natanxp2/bspPack) (GPLv3, see [LICENSE](LICENSE)) that the UI and the command
line copy next to themselves as `bspPack/`, where the porter looks for it first.

It's built from `feat/packing-fixes` (465c016030d95f3f82dbf563dcf0e2ae9f94de03), upstream's `main` plus two fixes the
KZ ports need:

- Pack materials of entity models
- Keep soundscapes that are already packed in the map

Built with:

```
dotnet publish bspPack.csproj -c Release -r win-x64
```

bspPack still needs a `config.ini` next to it naming the game folder to pack assets from (see
[docs/maps.md](../../docs/maps.md)). The porter won't run a copy without one, since bspPack would otherwise ask which
game to use and wait for an answer.

This goes away once the fixes are upstream and bspPack becomes a submodule like BSPConvert.
