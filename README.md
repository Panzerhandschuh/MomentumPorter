# MomentumPorter

Ports maps and assets from other games to Momentum Mod, as a desktop app (`MomentumPorterUI`) and a command line
(`MomentumPorter`):

- **Maps** made for other Source games (Kreedz Climbing Mod, CS:GO with the GOKZ mapping API): game-specific entities
  become their Momentum equivalents, brush entities are restructured where Momentum's work differently, timer entities
  become zone files, then bspPack packs the assets and the map is installed into Momentum.
- **Quake 3 and GoldSrc (Half-Life, CS 1.6) maps**, through [BSPConvert](https://github.com/Panzerhandschuh/BSPConvert) in process.
- **Player sounds**: packs that give the player Quake 3's jump, landing and footstep sounds.
- **Weapons**: packs that put weapons from CS:S, CS:GO, TF2, HL2, CS 1.6, HL1 and Quake 3 into Momentum's weapon slots.
- **Player models**: a feasibility study so far, see `docs/player-models.md`.

Packs go into `momentum/custom/`; delete a pack to get Momentum's own assets back.

## Building

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

## Command line

The verbs `sounds` and `weapon` build asset packs (see below). Without a verb, the arguments are a map to port:

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
file there. An installed map of the same name is replaced; turn off "Overwrite existing maps" to be asked first. The
options panel only shows the options for the kinds of map in the queue. Maps that aren't installed are saved to the output folder instead (Documents/MomentumPorter/maps by default).
Files the conversion writes next to the map, such as GOKZ's `zones/local/<map>.json`, go along with it.

- **Maps**: the queue (status per map, detected kind and game), the options form (generated from the
  `[PortOption]` attributes on `MapPortOptions`), and the log and report of the selected map. Options can be saved and
  loaded as JSON presets. Quake 3 and GoldSrc maps in the queue go through BSPConvert instead (its options sit under
  the Source map options) into Momentum's game folder, or the output folder laid out like one.
- **Settings**: Momentum's folder (found in your Steam libraries unless overridden), bspPack's location, an optional
  pre-pack command, the output and work folders, whether to install by default, and which known games were found.
  Saved to `%AppData%/MomentumPorter/settings.json`.
- **Weapons**: pick a game, a weapon and the Momentum weapon it replaces, then build the pack (see Weapons below).
- **Player Sounds**: pick a Quake 3 character and the jump/land/footstep options, then build the pack (see Player sounds).
- **Player Models**: placeholder; see `docs/player-models.md` for the feasibility study.

bspPack reads its `config.ini` (the game folder it packs assets from) and `ResourceConfig.ini` from its own folder, so
set those up for the source game, e.g. a stub game folder that only mounts KZ's content. The pre-pack command runs
through the shell before every bspPack run; `{resourceconfig}`, `{bsppack}`, `{bsppackdir}` and `{bsp}` are replaced
with paths, so the bat's ResourceConfig update becomes
`python "<path>\kz_shadowed_excludes.py" "{resourceconfig}"`.

## Quake 3 and GoldSrc maps

Quake 3 (`.bsp`/`.pk3`) and GoldSrc (Half-Life, CS 1.6; `.bsp`/`.zip`) maps are converted in process with
[BSPConvert](https://github.com/momentum-mod/BSPConvert) (the `external/BSPConvert` submodule, see Building). The CLI
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

## Weapons

```
MomentumPorter weapon --game css --weapon ak47 [--slot machinegun] --output <folder> [--name <pack>]
                      [--no-sounds] [--arms auto|momentum|none] [--no-projectile] [--no-particles]
                      [--no-world-fit] [--gun-fov <degrees>] [--no-defrag-configs]
                      [--momentum-dir <install>] [--game-dir <content folder>] [--studiomdl <exe>]
MomentumPorter weapon --list [--game css]
```

Ports a weapon from another game into one of Momentum's weapons as a pack folder (`<output>/momentumporter` by
default: every weapon goes into the same folder, where their shared files merge, because packs in separate folders
each replace `game_sounds_momentum_weapons.txt` and the particle manifest and Momentum only takes one; `--name` makes
a separate folder anyway). Copy the folder into `momentum/custom/` and restart Momentum: it only scans `custom/` at startup. After
that, rebuilding the pack into the same folder doesn't need a restart: run `exec porter_reload` in game (with
`sv_cheats 1`). The pack's `cfg/porter_reload.cfg` reloads soundscripts, particles, weapon scripts, models and the
pack's materials with Momentum's own commands; the model step (`flush`) crashes engine builds without
`docs/engine-patches/bonemerge-flush-crash.patch`, so on those remove that line and reload the map instead. A pack in
a VPK needs a restart for every change. Removing the pack brings Momentum's weapon back. Nothing is written into the
Momentum install (except the Quake 3 config blocks below); models are compiled against a stub game folder in a temp work folder.

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
  The game's own view model paths are used. `attach_to_hands` is 0 (the view models have arms), except CS:GO, whose view
  models have none: the script names CS:GO's `ct_arms.mdl` as `ModelData` `arms`, which needs the engine change in
  `docs/engine-patches` (without it Momentum's arms are attached, and they share no bones with CS:GO's view models). CS:S
  and CS 1.6 models are left handed (`BuiltRightHanded 0`).
- **Muzzle flashes**: Momentum's view models have no muzzle flash events, and the ones on other games' models
  (`CL_EVENT_MUZZLEFLASH0`, which CS:GO, CS:S and HL2 models all carry) are drawn by the base engine on the player
  entity rather than the view model, so nothing shows. With `docs/engine-patches/viewmodel-muzzle-flash.patch` the
  engine draws the weapon script's `ParticleData MuzzleFlashEffect` at the attachment the event names (CS:GO), or its
  sprite flash on the view model (CS:S, HL2). The catalog gives each CS:GO weapon its muzzle flash from CS:GO's
  `items_game.txt`, and the pack ships Momentum's particle manifest with `particles/weapons/cs_weapon_fx.pcf` added.
- **World models** (third person, replays, spawners): held weapons are bone-merged onto the player by bone name, and
  other games' world models are rigged to their own player skeletons, so they'd sit at the player's feet. The porter
  recompiles them onto Momentum's `weapon_bone.R` (`models/porter/<game>/`), placed like the Momentum model they replace.
  Momentum's world models are all laid out level in model space (barrel -y, top +z), which gives the target; the source
  model's barrel, top and tilt come from its muzzle, shell ejection, slide and top line, and the two triggers are lined
  up when both models have one. CS:GO, CS:S, TF2, HL2 and Quake 3 world models are fitted; GoldSrc's are ground pickups
  with nothing marking where they're held and stay as they are. "Fit world model" turns this off.
- **Sounds**: Momentum only loads the soundscripts in its `game_sounds_manifest.txt`, and the first definition of a
  name wins. Bullet weapons (pistol, machine gun, shotgun, sniper) play their shot from the player, where a weapon
  script's `Overrides` don't apply, so for those the pack redefines the shot's entry (e.g. `Weapon_Pistol.Single`) in place. Overrides point at the game's entries where Momentum loads them (CS:S, TF2, HL2). Entries Momentum doesn't
  load (CS:GO), or that a Momentum entry of the same name hides (Momentum's `Weapon_Pistol.Single`, `Weapon_Scout.Single`,
  `Weapon_Shotgun.Single`, `Weapon_Knife.*`), are copied (wave, channel, volume, pitch, level; no operator stacks), hidden
  ones as `Porter_<game>.<name>`. The manifest isn't overridden, so the pack ships a full copy of
  `scripts/game_sounds_momentum_weapons.txt` with the entries appended: only one such pack works at a time, and it hides
  Momentum's updates to that file until it's rebuilt. View model animation events play entries by name, so names a
  Momentum entry hides play Momentum's sound (reported).
- **GoldSrc** (BSPConvert's `GoldSrcModel` and material converter): v_ models keep their sequences, which get the
  activities Momentum plays from their names (idle, shoot, reload, draw, slash/stab, pullpin/throw), and their sound
  events, which play the waves copied to `sound/porter/<game>/`. Unsilenced sequences are used for the M4A1 and USP.
  Chrome textures get GoldSrc's sphere-map coordinates from the vertex normals (fixed against the model, so no
  shimmer). Muzzle flash events are dropped, `p_` models aren't used (Momentum's player rig differs) and the view
  model FOV differs.
- **Quake 3**: the weapon and its barrel become one model. The view model hangs it from the hand model's
  `tag_weapon`, and the hand's frames give it idle, fire (recoil) and draw sequences; the barrel rides its own bone,
  which the fire sequence spins a full turn (Quake 3 spins it while firing), and `<weapon>_flash.md3` is a body group
  the fire sequence shows for a frame (the whole swing for the gauntlet and lightning gun). Shaders become plain
  materials from their first stage (additive if it's blended so); an `animmap` stage becomes a multi-frame VTF with
  an `AnimatedTexture` proxy, which is the gauntlet's glow. No flash light, scrolling or environment maps.
- **Quake 3 effects**: rocket, grenade, plasma and BFG explosions, the rocket and grenade smoke trails and the plasma
  ball are rebuilt as particle systems in `particles/porter_q3.pcf` from Quake 3's own textures, timed as cgame draws
  them (explosion sprites 16 units off the wall growing from 30 to 72 units through the animmap frames, a smoke puff
  every 50 ms, a 16 unit spinning plasma sprite on an invisible projectile). The file is written as keyvalues2 text and
  converted to binary with Momentum's `dmxconvert`. Particle files only load through `particles/particles_manifest.txt`,
  so the pack ships Momentum's manifest with the file added (every Quake 3 pack writes the same files). The railgun,
  machine gun and shotgun hit with Momentum's bullet impacts: those aren't in the weapon script.
- **Quake 3 view model FOV**: Quake 3 draws its gun with the world's FOV (`cg_fov`) and lowers it 0.2 units per degree
  above 90; Momentum draws view models at `fov_viewmodel` 54, which crops most of a Quake 3 gun. The pack's
  `cfg/porter_q3_viewmodel.cfg` sets `fov_viewmodel` (your `fov_desired`, or `--gun-fov`) and `viewmodel_offset_z`.
  For a `df_` weapon written into `momentum/custom/`, the porter adds a marked block to `cfg/df_cpm.cfg`, `df_vq3.cfg`
  and `df_vtg.cfg` that runs it, and one to `cfg/game.cfg` that sets your own `fov_viewmodel` and offsets back
  (Momentum runs `game.cfg` on every map before the mode's config). Delete the blocks to undo.
- Every file written and every limitation is in the report.

## Splitting brush entities

`BrushModelEditor.SplitModel` gives each brush of a brush model its own model with a one node, one leaf collision
tree. The new models share the original's faces and bounds, so each still draws the whole original entity (e.g. with
`showtriggers`). That's fine for triggers, but visible brush entities would need their faces divided up as well.
