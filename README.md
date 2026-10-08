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
