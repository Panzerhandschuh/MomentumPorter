# Engine changes

Changes to Momentum's engine (strata/engine) that ported assets need. The engine is closed source, so the patches
aren't published here; each section describes one change. None are in Momentum yet. They're kept to what the porter
can't do with content: two fixes of existing features (`weapon-overrides`, `explosion-sound-position`) and three small
features (`weapon-arms`, `gamemode-weapon-scripts`, `viewmodel-muzzle-flash`). A Momentum build without them only misses
what's listed under each change. Changes that were [set aside](#set-aside) to keep the review small are listed at the
end.

## weapon-overrides

Fixes the view model animations of client-side weapon overrides (weapon scripts in
`momentum/custom/*/scripts/weapon_momentum_*.txt`), found by porting the CS:GO Desert Eagle over Momentum's pistol,
which loaded but had no fire animation, and the CS:S knife, which played the wrong animation on every other swing.

The client picks view model sequences by index on the networked reference model (Momentum's `v_mom_pistol.mdl`) and
remaps them to the override model by activity. That went wrong when the override's sequences are in another order:

- `RecvProxy_MomentumViewmodelSequenceNum` compared the networked index with the override's *local* index, so a change
  was skipped whenever they happened to be equal (the reference's fire sequence and the deagle's idle are both 0). It
  now compares with the last networked index.
- Predicted animations (`SendWeaponAnim` on the client) passed the reference model's index straight to the override
  model. `CMomentumViewModel::SendViewModelMatchingSequence` now remaps it through the activity too.
- When the override had no sequence for the activity, the reference's index was kept, which is a different animation
  on the override or out of its range (`Bad sequence (-1 out of 6 max)` for every swing of the CS:S knife, which has
  `ACT_VM_HITCENTER`/`MISSCENTER` but not the `ACT_VM_SWINGHIT`/`SWINGMISS` Momentum's knife alternates with). It now
  tries the nearest activities (swing and stab either way, secondary attack to the primary attack) and otherwise keeps
  the sequence playing.

Without it: weapons from mounted games (CS:GO, CS:S, TF2, HL2) may play the wrong fire animation, and the CS:S knife
the wrong swing. Converted weapons (Quake 3, GoldSrc) aren't affected when their sequences are in Momentum's order.

## explosion-sound-position

Explosion sounds of every explosive (rocket launcher, stickies, conc and all Defrag weapons) played at the shooter
instead of at the explosion. `TE_TFExplosion` emitted the sound on the server with the firing weapon as its source
entity and the explosion as its origin, but the client re-reads a sound's position from its source entity every frame
(`SND_Spatialize`, `bUpdatePositions`), and server sounds can't turn that off, so the sound was moved onto the weapon in
the player's hands as soon as it started. The tempent now carries the sound (as its soundscript hash, since only the
server's `scripts/server_weapon/` scripts name it) and the weapon, and the client plays it, still from the weapon so its
sound overrides, the `mom_*_sound_explosion` convars and the local/ghost mix groups apply, but with a new
`EmitSound_t::m_bUpdatePositions = false` so it stays at the explosion. It also stops a crash when an explosive went off
after its owner lost the weapon that fired it (`GetWeapon( iWeaponID )` was dereferenced unchecked); that explosion
now plays from the world.

Verified in game on 2026-10-08 (`snd_show` puts the channel at the impact, 3,500 units from the player). Note that
Source explosion soundlevels barely fall off with distance (Momentum's own are 95-140 dB), so a far explosion is still
loud even when positioned right; the porter gives wave-built explosions `SNDLVL_80dB` for that reason.

Without it: every explosion sounds like it went off at the player, which is most noticeable on long rocket and plasma
shots. Not specific to ported weapons; Momentum's own explosives have it too.

## weapon-arms

`attach_to_hands 1` always bone-merged Momentum's arms, whose bones (`hand.L`, ...) no foreign view model has, so no
arms showed. A weapon script can now name its arms with `ModelData { "arms" "..." }` (the porter writes CS:GO's
`models/weapons/ct_arms.mdl`, which shares 47 of 48 bones with CS:GO view models). Arms are also swapped when switching
to a weapon that wants different ones, instead of keeping whichever were attached first. Leaving the key out keeps
Momentum's arms.

Without it: CS:GO weapons show no arms.

## gamemode-weapon-scripts

Lets a game mode have its own weapons and sounds, so Climb (1.6) can use a CS 1.6 pistol and its player sounds and
Climb (KZT) CS:GO's.

- **Per-mode client scripts.** `CWeaponDef::ParseWeaponScript` reads a client weapon script from
  `scripts/<mode>/<weapon>.txt` when that file exists, where `<mode>` is the current mode's cfg name without its
  extension (`kz16`, `kzt`, `df_cpm`, ...), and from `scripts/<weapon>.txt` as before otherwise. Server scripts
  (`scripts/server_weapon/`) are untouched. The definitions are parsed again in `LevelInitPreEntity`, just before the
  existing `PrecacheAll()`: the mode is set from the map name at `game_init`, before either DLL's level init, and the
  game mode system is one engine object both DLLs query, so client and server agree without a cvar, a cfg line or a
  reload, and the mode's models precache normally.
- **Per-mode soundscripts.** `CSoundEmitterSystem::PreloadSounds` (game/shared/SoundEmitterSystem.cpp, run at level
  init on client and server) loads every `scripts/<mode>/game_sounds_*.txt` as sound overrides, through the same
  `AddSoundOverrides` call it already makes for a map's `maps/<map>_level_sounds.txt`. Each entry replaces the whole
  entry of the same name until the level shuts down (`ClearSoundOverrides`), so a mode's file only needs the entries it
  changes. The files are found with `FindFirstEx`, which looks inside the VPKs in `custom/` too, so each pack can bring
  its own: player sound packs write `game_sounds_momentum_player.txt`, weapon packs `game_sounds_momentum_weapons.txt`
  (the shot of a per-mode bullet weapon, which is played from the player where the weapon script's `Overrides` don't
  apply). They're loaded before the map's file, so a map's own overrides still win, and loading them from both DLLs is
  harmless (`AddSoundOverrides` skips a file it already has). The sound emitter's level init runs before the weapon
  definitions' (`CWeaponDef` is an auto game system, added after the ones the DLLs add), so their precache picks up the
  overridden waves. A mode changed mid-map with `mom_gamemode` takes effect at the next map load, as the weapon scripts
  do.

Without it: `scripts/<mode>/` is ignored, so weapons and player sounds built for some modes don't appear and those modes
use the packs' every-mode files or Momentum's own. Weapons for every mode are unaffected (their shot is redefined in the
pack's copy of the shared soundscript).

## viewmodel-muzzle-flash

Momentum's own view models have no muzzle flash events, but models from other games carry `CL_EVENT_MUZZLEFLASH0`-`3`
(5001 + 10 per attachment) on their fire sequences: CS:GO's name the attachment in the options (`"1"`), CS:S's and
HL2's imply it from the event. `C_BaseViewModel::FireEvent` hands the event to the weapon, and the base class falls
through to `C_BaseAnimating::FireObsoleteEvent`, which draws the flash on the *view entity* (the player model) rather
than the view model, so nothing visible comes of it. `CWeaponBase::OnFireEvent` now draws the weapon script's
`ParticleData "MuzzleFlashEffect"` at that attachment on the view model when the script has one; without one nothing
changes. The porter gives every gun one (CS:GO's flash for its slot when CS:GO is mounted, else the assault rifle flash
Momentum ships), and keeps the events of converted GoldSrc models without options.

Without it: ported weapons from CS:GO, CS:S, HL2 and GoldSrc games have no first-person muzzle flash.

## Set aside

Kept as commits on the engine's `backup/feat/weapon-pack-fixes-removed` branch (on top of `feat/weapon-pack-fixes`),
because the porter does without them:

- **Reloading packs in game** (bone merge cache freed with the model on `flush`, which crashed; particle precache lookups
  kept through `cl_/sv_particles_reload`, which broke bullet impacts; `weapon_reload_scripts` keeping held weapons on
  their view model). Genuine upstream bugs, but only the porter's old `cfg/porter_reload.cfg` hit them. Packs now need a
  Momentum restart after a rebuild, and rebuilding removes that cfg.
- **Defrag grenade trails from the weapon script** (`CMomDFGrenade` never reads `GrenadeTrail`). The porter's grenade
  model draws its trail from its own `particles` keyvalues instead.
- **Sprite muzzle flashes** for view model events without a `MuzzleFlashEffect`, and drawing the first-person sprite
  flashes at all (`SetDrawBeforeViewModel` took them out of the leaf system for a pass nothing draws). The porter gives
  every gun a particle flash instead.
- **Weapon sound overrides for player-emitted sounds** (`C_MomentumPlayer::GetEffectiveSoundEntry`). A per-mode
  weapon's shot is redefined in the mode's soundscript instead.
- **Dry fire falling back to the primary attack** on override view models: no Momentum weapon plays `ACT_VM_DRYFIRE`.
