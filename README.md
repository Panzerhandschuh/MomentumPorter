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

## Player sounds

Builds a pack for Momentum's `custom` folder that gives the player Quake 3's movement sounds:

```
MomentumPorter sounds [--character ranger] [--jump q3|default] [--land q3|default|none] [--footsteps q3|default|silent]
                      [--pure] [--output <folder>|auto] [--vpk|--folder] [--name <pack>] [--list]
```

- **Jump**: the character's `jump1.wav` (`MomPlayer.Jump`).
- **Land**: `land1.wav` for soft landings, the character's `fall1.wav` for hard ones (`MomPlayer.LandSoft`/`LandHard`), or none.
- **Footsteps**: Quake 3's on every surface. Like Quake 3, the set comes from the character's `animation.cfg` (normal,
  boot, energy, flesh or mech; `--footstep-type` overrides it), metal surfaces clank and water splashes
  (`--no-surface-footsteps` turns that off). `silent` mutes them and also writes `cfg/<pack>.cfg` with
  `snd_volume_footsteps 0`; run `exec <pack>` or add it to `cfg/autoexec.cfg`. The cvar is saved in your config, so
  set it back to 1 after removing the pack.
- **`--pure`**: also mutes what Quake 3 doesn't have: `MomPlayer.AirJump`, `SprintStart`, `PowerSlide`, `WallRun`.

Quake 3 and Momentum are found through Steam (`--q3-dir`, `--momentum-dir` override that); `--list` shows the characters.
Quake Live works too, but its footsteps are Ogg Vorbis, which Momentum can't play, so those stay Momentum's. The pack
goes to `momentum/custom/q3_sounds_<character>.vpk` (built with Momentum's `vpk.exe`) unless `--output` names another
folder; delete it to get the default sounds back.

The pack holds a full copy of Momentum's `scripts/game_sounds_momentum_player.txt` (the soundscript manifest lives in the
base game, so the file is replaced whole) with only those entries changed, the WAVs they play under
`sound/player/<character>/`, `sound/player/q3/` and `sound/player/footsteps/q3/` (own folders, so no mounted game's
sounds are replaced), and the cfg. WAVs Source can't play (rates other than 11025/22050/44100 Hz, more than 16 bits,
float) are re-encoded. Only one pack can replace the soundscript, so the tool warns about other `custom` folders that
have their own copy (e.g. `custom/overrides/scripts/game_sounds_momentum_player.txt`). Folder packs are only replaced if
MomentumPorter wrote them (`momentumporter_pack.txt`).

In code, `PlayerSoundPackJob.Create(PlayerSoundPackOptions)` builds the job; `CustomPackWriter` (folder or VPK) and
`SoundscriptFile` (edit soundscripts keeping comments) are reusable for other asset packs.

## Splitting brush entities

`BrushModelEditor.SplitModel` gives each brush of a brush model its own model with a one node, one leaf collision
tree. The new models share the original's faces and bounds, so each still draws the whole original entity (e.g. with
`showtriggers`). That's fine for triggers, but visible brush entities would need their faces divided up as well.
