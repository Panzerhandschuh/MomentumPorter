# MomentumPorter

Converts BSPs made for other Source games into Momentum Mod maps: KZ/game-specific entities become their Momentum
equivalents, and brush entities can be restructured where Momentum's entities work differently.

Built on [LibBSP](https://github.com/Panzerhandschuh/LibBSP), which has to be checked out next to this repo (`../LibBSP`) and
built in Release, since it's referenced as `LibBSP/bin/Release/libBSP.dll`. Reading and writing compressed maps needs
the LZMA support in this fork.

## Usage

```
MomentumPorter <input.bsp> <output.bsp> [--game kzmod] [--compress] [--game-dir <folder>]
```

`--game-dir` is the source game's folder (the one with `gameinfo.txt`), for game files the map relies on, such as
shared soundscapes. It defaults to the folder above the input's `maps` folder.

The output is written as a new file, uncompressed unless `--compress` is given, and the tool prints what it converted
and anything it couldn't. Pack assets afterwards (e.g. with bspPack) as usual. Packed files named after the map, such as
cubemap materials, aren't renamed, so keep the map's name or rename it with Lumper afterwards.

## Quake 3 and GoldSrc maps

Quake 3 (`.bsp`/`.pk3`) and GoldSrc (Half-Life, CS 1.6; `.bsp`/`.zip`) maps are converted in process with
[BSPConvert](https://github.com/Panzerhandschuh/BSPConvert), which has to be checked out next to this repo
(`../BSPConvert`, or set `BspConvertDir` in `Directory.Build.local.props`). The CLI takes BSPConvert's options under the
same names (`--wads`, `--subdiv`, `--scale`, `--nopak`, `--prefix`, `--maps`...; see `--help` or BSPConvert's README):

```
MomentumPorter <map.pk3|map.bsp|maps.zip> <outputGameDir> [BSPConvert options]
MomentumPorter "...aseq3\pak0.pk3" "...\Momentum Mod Playtest\momentum" --maps q3dm1
MomentumPorter "...\Half-Life\cstrike\maps\kz_persia.bsp" "...\Momentum Mod Playtest\momentum"
```

The output is a game folder: the map goes into `maps/<prefix><name>.bsp`, Quake 3 timer zones into
`maps/zones/local/`, and with `--nopak` materials (and GoldSrc sounds and models) into their folders. BSPConvert writes
into the job's temp folder first and the files are copied over at the end, replacing existing ones like BSPConv.exe does
(the app asks instead). The log is BSPConvert's own, and its warnings are repeated in the report at the end.

What BSPConvert would otherwise need to be told is filled in from Steam:

- `--wads`: the Half-Life install's folders, the map's own mod first (when it's in one), then `cstrike` and `valve`,
  each followed by its `_downloads` folder.
- `--studiomdl`: Momentum Mod's `bin/win64/studiomdl.exe`, which compiles the models GoldSrc entities use.
- Quake 3's base content: BSPConvert reads the textures, shaders and sounds maps share with the base game from a
  `Q3Content` folder next to the program. When it's empty, the porter extracts Quake 3's `baseq3/pak*.pk3` into it
  first (about 650 MB, once). `--noq3content` turns that off. Third-party map dependencies go in `CustomContent` as
  described in its `readme.txt`.
- In the app, the output folder defaults to Momentum's `momentum` folder.

In code, `BspConvertOptions` (`src/MomentumPorter.Core/BspConvert/`) mirrors BSPConvert's options with the same defaults,
with `[PortOption]` metadata grouped General / Quake 3 / GoldSrc / Animated textures for the generated options form, and
`ToConverterOptions` maps them onto BSPConvert's `BSPConverterOptions` as its command line does. `BspConvertJobs.Create`
builds the job (`BspConvertStep`, then `CopyConvertedFilesStep`); `BspConvertState` lists the converted maps and zone
files. `Pk3Inspector.ListMaps` lists the maps in an archive for a map picker (`BspConvertOptions.Maps`).

BSPConvert can't be cancelled midway: a cancelled job stops before the next map of an archive, and a map that has started
converting finishes first. Conversions run one at a time, since BSPConvert extracts each input to `%TEMP%\<input name>`.
GoldSrc maps don't get timer zones, and `--scale` isn't supported for them.

## Games

| `--game` | Game |
|---|---|
| `kzmod` | Kreedz Climbing Mod |

Each game is an `IGameProfile` in `src/MomentumPorter/Games/<Game>/`, registered in `GameProfiles.All`. Profiles work on
a `PortContext`, which provides the entities and generic operations such as `SplitByBrush` and `RewriteOutputs`.

### Kreedz Climbing Mod

- `trigger_bhop_teleport` becomes `trigger_momentum_bhop`.
  - `allowedtime` becomes `TimeLimit`. KZ's "keep angles" (32) and "keep velocity" (64) spawnflags become
    `UseDestinationAngles` and `ResetVelocityOnTeleport`.
  - KZ applies `mbhoprestrictions` 1 and 2 to each brush ("area") of the trigger, so those triggers are split into one
    entity per brush. Restrictions become `LandingLimit 1` with these spawnflags:
    - 1 (can't land in the same area twice in a row): 5, reset when landing anywhere else
    - 2 (can't land in any area twice): 3, reset when landing on something that isn't a bhop trigger. KZ also resets
      when landing in a different trigger, which Momentum can't express yet.
    - 3 (can't land in the trigger twice): 5, not split
- `func_brush_kzmod` becomes `func_brush`. Without spawnflag 4096 it starts disabled.
- `trigger_teleport` resets velocity unless KZ's "keep velocity" spawnflag (64) is set. With spawnflag 512 ("only
  allowed players"), it starts disabled unless 4096 is also set.
- Inputs that allow players to use those entities (`AddAllowedPlayer`, `RemoveAllowedPlayer`, `TogglePlayer`,
  `ResetPlayer`) become `Enable`, `Disable` and `Toggle`, since Momentum is single player. Team inputs are left alone.
- On maps with HDR lighting, a `logic_auto` sets KZ's (Source 2013's) tonemap defaults on the `env_tonemap_controller`,
  adding one if needed, unless the map sets them itself on spawn. Bloom scales are doubled, since Momentum draws bloom
  at half strength.
- KZ loads the soundscape files in its manifest for every map, but Momentum only loads its own and the map's
  `scripts/soundscapes_<map>.txt`. The soundscapes a map uses from KZ-only files (e.g. `soundscapes_soul4.txt`) are
  copied into its packed soundscape file, after the map's own soundscapes. Upstream bspPack replaces a packed
  soundscape file with the game's loose one and doesn't pack its sounds, so pack with a bspPack that keeps it.

## Splitting brush entities

`BrushModelEditor.SplitModel` gives each brush of a brush model its own model with a one node, one leaf collision
tree. The new models share the original's faces and bounds, so each still draws the whole original entity (e.g. with
`showtriggers`). That's fine for triggers, but visible brush entities would need their faces divided up as well.
