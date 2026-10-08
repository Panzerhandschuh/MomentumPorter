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

## Desktop app

`src/MomentumPorter.UI` (`MomentumPorterUI.exe`) does what `pack_for_momentum.bat` did, with a window: drop maps on it
(or pass them on the command line), pick options, Run. Each queued Source map is ported, packed with bspPack
(`-M -C`) and copied into Momentum's `momentum/maps` folder, one map at a time. Everything happens in a temp work folder
and the map is only moved into `maps/` once every step succeeded, so a failed or cancelled map never leaves a broken
file there. If the map is already installed you're asked whether to overwrite it, unless "Overwrite existing maps" is
on. Maps that aren't installed are saved to the output folder instead (Documents/MomentumPorter/maps by default).
Files the conversion writes next to the map, such as GOKZ's `zones/local/<map>.json`, go along with it.

- **Maps**: the queue (status per map, detected kind and game), the options form (generated from the
  `[PortOption]` attributes on `MapPortOptions`), and the log and report of the selected map. Options can be saved and
  loaded as JSON presets. Quake 3 and GoldSrc maps can be queued but are skipped until BSPConvert is integrated.
- **Settings**: Momentum's folder (found in your Steam libraries unless overridden), bspPack's location, an optional
  pre-pack command, the output and work folders, whether to install by default, and which known games were found.
  Saved to `%AppData%/MomentumPorter/settings.json`.
- **Weapons, Player Sounds, Player Models**: placeholders for later phases.

bspPack reads its `config.ini` (the game folder it packs assets from) and `ResourceConfig.ini` from its own folder, so
set those up for the source game, e.g. a stub game folder that only mounts KZ's content. The pre-pack command runs
through the shell before every bspPack run; `{resourceconfig}`, `{bsppack}`, `{bsppackdir}` and `{bsp}` are replaced
with paths, so the bat's ResourceConfig update becomes
`python "<path>\kz_shadowed_excludes.py" "{resourceconfig}"`.

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
