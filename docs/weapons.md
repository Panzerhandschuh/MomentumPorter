# Weapons

Packs that put weapons from other games into Momentum's weapon slots. See the [README](../README.md) for an overview.

```
MomentumPorter weapon --game css --weapon ak47 [--slot machinegun] [--modes kzt,kz16] --output <folder> [--name <pack>]
                      [--no-sounds] [--arms auto|momentum|none] [--no-projectile] [--no-particles]
                      [--no-world-fit] [--gun-fov <degrees>] [--no-defrag-configs]
                      [--momentum-dir <install>] [--game-dir <content folder>] [--studiomdl <exe>]
MomentumPorter weapon --game q3 --all [--modes ...] --output <folder> [options as above]
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

`--all` (**Build all weapons** in the app) ports every weapon of a game, one after another into the same pack, each
into the Momentum weapon it fits best: all of Quake 3's go into the `df_*` weapons, so one run gives the Defrag modes
Quake 3's whole arsenal. Where several weapons fit a Momentum weapon best (CS:S has six pistols), the game's `loadout`
in `weapons.json` picks one (USP, AK-47, AWP for the CS games), else the first listed wins; the loadout can also fill a
weapon none fits best (the flashbang as the concussion grenade). `--list` shows what `--all` builds for each game.

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

- **Game modes**: `--modes` (the Game modes list in the app) limits a weapon to some modes (`surf`, `bhop`, `bhophl`,
  `kz`, `kzt`, `kz16`, `rj`, `sj`, `ahop`, `conc`, `df_cpm`, `df_vq3`, `df_vtg`: the modes' cfg names). Its script is
  then written as `scripts/<mode>/weapon_momentum_<slot>.txt`, which the engine reads in that mode with
  `docs/engine-patches/gamemode-weapon-scripts.patch` (falling back to the shared script, or Momentum's), so the Climb
  (1.6) pistol can be CS 1.6's USP and the Climb (KZT) one CS:GO's Desert Eagle. A weapon for every mode keeps the plain
  path and leaves the modes' own scripts in place. Bullet weapons' shots play from the player, so a per-mode weapon's
  shot sound needs that patch too (it applies the weapon's `Overrides` to the player's sounds); a weapon for every mode
  also redefines the shot entry, which works without it. The app shows what the pack holds for the slot per mode.

- **Mounted games**: every model is checked in the VPKs Momentum's `gameinfo.txt` mounts (not loose files of the game).
  The game's own view model paths are used. `attach_to_hands` is 0 (the view models have arms), except CS:GO, whose view
  models have none: the script names CS:GO's `ct_arms.mdl` as `ModelData` `arms`, which needs the engine change in
  `docs/engine-patches` (without it Momentum's arms are attached, and they share no bones with CS:GO's view models). CS:S
  and CS 1.6 models are left handed (`BuiltRightHanded 0`). Momentum mounts these games together, and a path two of
  them ship (CS:S's and CS:GO's `models/weapons/v_pist_deagle.mdl`) is one game's file for every script that names it,
  so a model another mounted game ships too is copied into the pack as `models/porter/<game>/...` (reported; its
  materials keep their paths, with a warning when the other game ships those too).
- **Muzzle flashes**: a gun whose catalog entry names no `MuzzleFlashEffect` (CS:S, HL2, CS 1.6, HL1) gets CS:GO's
  flash for its slot when CS:GO is mounted (pistol, assault rifle, auto shotgun or AWP, with CS:GO's particle file added
  to the manifest), else the assault rifle flash Momentum ships in `particles/weapons.pcf`; the engine's sprite flash is
  only the fallback for scripts without one. Momentum's view models have no muzzle flash events, and the ones on other games' models
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
  script's `Overrides` only apply with `docs/engine-patches/gamemode-weapon-scripts.patch`, so for those the pack also
  redefines the shot's entry (e.g. `Weapon_Pistol.Single`) in place when the weapon is for every mode. Overrides point at the game's entries where Momentum loads them (CS:S, TF2, HL2). Entries Momentum doesn't
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
  shimmer). The model's attachments come along and its muzzle flash events (5001 + 10 per attachment) stay, drawing
  Momentum's sprite flash for the slot (pistol, SMG or shotgun) with `docs/engine-patches/viewmodel-muzzle-flash.patch`
  and `viewmodel-sprite-flash-sequences.patch` (without the second, the engine never draws those sprite flashes).
  `p_` models aren't used (Momentum's player rig differs) and the view model FOV differs.
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
  For a `df_` weapon written into `momentum/custom/` (or any weapon with `--modes`), the porter adds a marked block to
  the modes' configs (`cfg/df_cpm.cfg`, `df_vq3.cfg` and `df_vtg.cfg` for a Defrag weapon) that runs it, and one to
  `cfg/game.cfg` that sets your own `fov_viewmodel` and offsets back (Momentum runs `game.cfg` on every map before the
  mode's config). Delete the blocks to undo.
- Every file written and every limitation is in the report.
