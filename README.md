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

## Weapons

```
MomentumPorter weapon --game css --weapon ak47 [--slot machinegun] --output <folder> [--name <pack>]
                      [--no-sounds] [--arms auto|momentum|none] [--no-projectile] [--no-particles]
                      [--momentum-dir <install>] [--game-dir <content folder>] [--studiomdl <exe>]
MomentumPorter weapon --list [--game css]
```

Ports a weapon from another game into one of Momentum's weapons as a pack folder (`<output>/<game>_<weapon>_<slot>`
by default). Copy the folder into `momentum/custom/` (or pack it into a VPK) and run `weapon_reload_scripts`; new
soundscript entries need a restart. Removing the pack brings Momentum's weapon back. Nothing is written into the
Momentum install; models are compiled against a stub game folder in a temp work folder.

The weapons come from `src/MomentumPorter.Core/Assets/Weapons/weapons.json` (embedded in Core): per game, each
weapon's models, the Momentum weapons it fits (best first) and its sounds keyed by the role Momentum gives them in
`scripts/server_weapon/weapon_momentum_<slot>.txt` (`single_shot`, `explosion`, `deploy`, `swish`, `stab`, `hit`,
`hit_wall`, `zoom`, `bounce`, ...). Add weapons there; the tests check every path against the installed games.

| `--game` | Game | How |
|---|---|---|
| `css` | Counter-Strike: Source | Mounted: script only |
| `csgo` | CS:GO (legacy, app 4465480) | Mounted: script, plus soundscript entries Momentum doesn't load |
| `tf2` | Team Fortress 2 | Mounted: script only (TF2 particles for the launchers) |
| `hl2` | Half-Life 2 | Mounted (Momentum's `mount/hl2_dir.vpk`): script only |
| `cs16` | Counter-Strike 1.6 | GoldSrc: models, textures and sounds converted |
| `hl1` | Half-Life | GoldSrc: models, textures and sounds converted |
| `q3` | Quake III Arena | MD3: models, textures and sounds converted, into the `df_*` weapons |

Slots: `pistol`, `machinegun`, `shotgun`, `sniper`, `knife`, `grenade`, `rocketlauncher`, `stickylauncher`,
`concgrenade`, `df_bfg`, `df_grenadelauncher`, `df_knife`, `df_lightninggun`, `df_machinegun`, `df_plasmagun`,
`df_railgun`, `df_rocketlauncher`, `df_shotgun`.

The pack's `scripts/weapon_momentum_<slot>.txt` is Momentum's script with its `ModelData` (view, view_side, world,
spawner and the projectile), `attach_to_hands`, `BuiltRightHanded`, `SoundData/Overrides` and `ParticleData` replaced.

- **Mounted games**: every model is checked in the VPKs Momentum's `gameinfo.txt` mounts (not loose files of the game).
  The game's own paths are used. `attach_to_hands` is 0 (the view models have arms), except CS:GO, whose view models
  have none, so Momentum's are bone-merged on (1). CS:S and CS 1.6 models are left handed (`BuiltRightHanded 0`).
- **Sounds**: Momentum only loads the soundscripts in its `game_sounds_manifest.txt`, and the first definition of a
  name wins. Overrides point at the game's entries where Momentum loads them (CS:S, TF2, HL2). Entries Momentum doesn't
  load (CS:GO), or that a Momentum entry of the same name hides (Momentum's `Weapon_Pistol.Single`, `Weapon_Scout.Single`,
  `Weapon_Shotgun.Single`, `Weapon_Knife.*`), are copied (wave, channel, volume, pitch, level; no operator stacks), hidden
  ones as `Porter_<game>.<name>`. The manifest isn't overridden, so the pack ships a full copy of
  `scripts/game_sounds_momentum_weapons.txt` with the entries appended: only one such pack works at a time, and it hides
  Momentum's updates to that file until it's rebuilt. View model animation events play entries by name, so names a
  Momentum entry hides play Momentum's sound (reported).
- **GoldSrc** (BSPConvert's `GoldSrcModel` and material converter): v_ models keep their sequences, which get the
  activities Momentum plays from their names (idle, shoot, reload, draw, slash/stab, pullpin/throw), and their sound
  events, which play the waves copied to `sound/porter/<game>/`. Unsilenced sequences are used for the M4A1 and USP.
  Muzzle flash events are dropped, `p_` models aren't used (Momentum's player rig differs) and the view model FOV differs.
- **Quake 3**: the weapon and its barrel become a one-bone model. The view model hangs it from the hand model's
  `tag_weapon`, and the hand's frames give it idle, fire (recoil) and draw sequences. Barrels don't spin, the muzzle
  flash isn't drawn, and shaders become plain materials from their first stage (additive if it's blended so).
- Every file written and every limitation is in the report.

## Splitting brush entities

`BrushModelEditor.SplitModel` gives each brush of a brush model its own model with a one node, one leaf collision
tree. The new models share the original's faces and bounds, so each still draws the whole original entity (e.g. with
`showtriggers`). That's fine for triggers, but visible brush entities would need their faces divided up as well.
