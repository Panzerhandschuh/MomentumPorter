# Maps

Porting maps made for other games: Source maps (Kreedz Climbing Mod, CS:GO with GOKZ) in the porter itself, Quake 3 and GoldSrc maps through BSPConvert. See the [README](../README.md) for an overview.

## Command line

The verbs `sounds` and `weapon` build asset packs (see [weapons.md](weapons.md) and [player-sounds.md](player-sounds.md)). Without a verb, the arguments are a map to port:

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
file there. An installed map of the same name is replaced. The game a Source map was made for is detected from its
entities. The options panel only shows the options for the kinds of map in the queue, and the rarely needed ones only
with its **Advanced** toggle on. Files the conversion writes next to the map, such as GOKZ's `zones/local/<map>.json`,
go along with it.

- **Maps**: the queue (status per map, detected kind and game), the options form (generated from the
  `[PortOption]` attributes on `MapPortOptions` and `BspConvertOptions`), and the log and report of the selected map.
  Options can be saved and loaded as JSON presets. Quake 3 and GoldSrc maps in the queue go through BSPConvert instead
  (its options follow the Source map options in the same list) into Momentum's game folder.
- **Settings**: Momentum's folder (found in your Steam libraries unless overridden), the bspPack and studiomdl
  executables (Momentum's own studiomdl unless overridden), an optional pre-pack command, the work folder, the output
  folder packs fall back to when Momentum isn't found, and which known games were found. Saved to
  `%AppData%/MomentumPorter/settings.json`.
- **Weapons**: pick a game mode and the Momentum weapon to replace, then the game and the weapon that goes in its
  place, and build the pack (see [weapons.md](weapons.md)).
- **Player Sounds**: pick a game (and a Quake 3 character or CS:GO team) and the jump/land/footstep options, then build
  the pack (see [player-sounds.md](player-sounds.md)).
- **Player Models**: placeholder; see [player-models.md](player-models.md) for the feasibility study.

A Windows build of bspPack with the fixes these ports need comes with the porter (`bspPack/` next to the app, from
[tools/bspPack](../tools/bspPack)). bspPack reads its `config.ini` (the game folder it packs assets from) and
`ResourceConfig.ini` from its own folder, so set those up for the source game, e.g. a stub game folder that only
mounts KZ's content. bspPack runs bspzip from `<game folder>/../bin/win64`. The porter won't run a bspPack without a
`config.ini`, and when Settings doesn't name one it uses the first copy it finds that has one. The pre-pack command runs
through the shell before every bspPack run; `{resourceconfig}`, `{bsppack}`, `{bsppackdir}` and `{bsp}` are replaced
with paths, so the bat's ResourceConfig update becomes
`python "<path>\kz_shadowed_excludes.py" "{resourceconfig}"`.

## Quake 3 and GoldSrc maps

Quake 3 (`.bsp`/`.pk3`) and GoldSrc (Half-Life, CS 1.6; `.bsp`/`.zip`) maps are converted in process with
[BSPConvert](https://github.com/momentum-mod/BSPConvert) (the `external/BSPConvert` submodule, see [building.md](building.md)). The CLI
takes BSPConvert's options under the
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
- `--studiomdl`: Momentum Mod's `bin/win64/studiomdl.exe`, which compiles the models GoldSrc entities use. The app
  takes the Settings page's studiomdl instead when one is set there.
- Quake 3's base content: BSPConvert reads the textures, shaders and sounds maps share with the base game from a
  `Q3Content` folder next to the program. When it's empty, the porter extracts Quake 3's `baseq3/pak*.pk3` into it
  first (about 650 MB, once). `--noq3content` turns that off. Third-party map dependencies go in `CustomContent` as
  described in its `readme.txt`.
- In the app, the output folder defaults to Momentum's `momentum` folder.

GoldSrc bhop maps make their blocks touch-activated `func_door`s (or `func_button`s) that drop the player into a
teleport. After converting a GoldSrc map, the porter turns those into `func_bhop`s that teleport the player to the same
destination once they've stood on the block too long, keeping the teleport's angle and velocity settings (`Fix bhop
blocks` in the app's GoldSrc options, `--noblockfix` to turn it off). A block counts when the player can fall from it
into a `trigger_teleport` without landing on anything solid first, so this also finds blocks over a pit, which
Momentum's runtime block fix (and the entity tools' Stripper export of it) misses because it only looks for teleports
the block dips into as it drops. Named blocks lose their name and outputs, which the report warns about.

In code, `BspConvertOptions` (`src/MomentumPorter.Core/BspConvert/`) mirrors BSPConvert's options with the same defaults,
with `[PortOption]` metadata grouped General / Quake 3 / GoldSrc / Animated textures for the generated options form, and
`ToConverterOptions` maps them onto BSPConvert's `BSPConverterOptions` as its command line does. `BspConvertJobs.Create`
builds the job (`BspConvertStep`, `BhopBlockFixStep`, then `CopyConvertedFilesStep`); `BspConvertState` lists the converted maps and zone
files. `Pk3Inspector.ListMaps` lists the maps in an archive for a map picker (`BspConvertOptions.Maps`).

BSPConvert can't be cancelled midway: a cancelled job stops before the next map of an archive, and a map that has started
converting finishes first. Conversions run one at a time, since BSPConvert extracts each input to `%TEMP%\<input name>`.
GoldSrc maps don't get timer zones, and `--scale` isn't supported for them.

## Games

| `--game` | Game |
|---|---|
| `kzmod` | Kreedz Climbing Mod |
| `gokz` | Counter-Strike: Global Offensive (GOKZ) |

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

### Counter-Strike: Global Offensive (GOKZ)

For CS:GO maps made with GOKZ's [mapping API](https://github.com/KZGlobalTeam/gokz/wiki/Mapping-API), which gives
meaning to targetnames and `kz_mapping_api.fgd` keyvalues on stock entities. The trigger rules are the same as Momentum's
in-game entity tools ("Climb Trigger Converter"), so maps ported either way end up the same. They only apply to brush
`trigger_multiple`s whose name matches exactly (case sensitive). Converted triggers keep `model`, `targetname`,
`StartDisabled`, `solid`, `origin`, `filtername`, `hammerid` and their outputs, lose every other keyvalue (the
`climb_*` ones included), and are tagged `_ported_by_tools 2` so the in-game tools know they're done.

- `climb_teleport` with `climb_teleport_type` 0 (Normal) becomes `trigger_momentum_teleport`, keeping its spawnflags
  (1 if it has none). `climb_teleport_destination` becomes `target`, `climb_teleport_delay` (if above 0) becomes
  `TeleportDelay`, `climb_teleport_reset_speed` (default on) becomes `VelocityMode` 1 (reset) or 0 (retain), always
  written since Momentum retains by default, and `climb_teleport_use_dest_angles` (default on) becomes `UseLandmarkAngles`.
- `climb_teleport` with type 1 (Multi Bhop), 2 (Single Bhop) or 3 (Sequential Bhop) becomes `trigger_momentum_bhop`
  with spawnflags 1 (reset on activate) for multi bhops and 3 (also reset when landing on something that isn't a bhop
  trigger) for the others, and `LandingLimit` 0 for multi bhops, 1 otherwise. `climb_teleport_delay` becomes
  `TimeLimit`, at least 0.08 (GOKZ's minimum) and always written, otherwise `sv_bhop_time_limit` would apply. Reset speed
  and destination angles become `ResetVelocityOnTeleport` and `UseDestinationAngles`.
- `climb_anti_checkpoint` becomes `trigger_momentum_disallowcheckpoint`, keeping its spawnflags.
- `climb_anti_bhop` becomes `trigger_momentum_limitmovement` with the limit bhop spawnflag (524288) added to its own, and
  `climb_anti_bhop_time` (default 0.2) as `BhopCooldown`.
- `climb_anti_pause`, `climb_anti_jumpstat` and `climb_bhop_reset` are removed, since Momentum has nothing like them.
- The worldspawn's `climb_mapping_api_version` is removed.
- An `env_cascade_light` is added when the map has none, as the in-game entity tools' "CSM Fix" does, so cascaded
  shadow maps work. It follows the map's `light_environment`; a map without one is left alone, with a warning.

Momentum's timer comes from a zone file rather than entities, so the timer entities become
`zones/local/<map>.json` next to the ported BSP (Momentum reads local zones from `maps/zones/local/`):

- The main course is `climb_startzone`/`climb_endzone` (`trigger_multiple`) and `climb_startbutton`/`climb_endbutton`
  (`func_button`), bonus X `climb_bonusX_startzone` etc. Several start or end entities become regions of one zone.
- Zone triggers become zones of the same shape: one region per brush, its outline clipped by the brush's vertical sides.
- Buttons become box zones reaching 32 units past the button sideways and up, and 64 down.
- Each start zone teleports to the course's `climb_start`/`climb_bonusX_start`. Without one, the main course uses the
  map's first spawn point and bonuses the bottom middle of their start zone.
- Bonuses are numbered without gaps, as Momentum requires, and at most 25 are kept.

Not reproducible, and listed as warnings:

- Timer buttons: the timer starts on leaving the zone around the start button and stops on touching the end button's
  zone, without pressing anything.
- `climb_teleport_reorient_player` and `climb_teleport_relative`: those triggers teleport to the destination as is.
- Sequential bhops act as single bhops per trigger; they don't share which triggers of the set were used.
- Courses with no start or end aren't zoned; without a main course, no zone file is written.

## Splitting brush entities

`BrushModelEditor.SplitModel` gives each brush of a brush model its own model with a one node, one leaf collision
tree. The new models share the original's faces and bounds, so each still draws the whole original entity (e.g. with
`showtriggers`). That's fine for triggers, but visible brush entities would need their faces divided up as well.
