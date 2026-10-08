# Engine patches

Changes to Momentum's engine (strata/engine) that ported assets need. They aren't applied anywhere; review them and
apply with `git apply <patch>` in an engine checkout. They apply cleanly to the engine as of October 2026 and compile, but
haven't been tested in game.

## weapon-overrides.patch

Fixes client-side weapon overrides (weapon scripts in `momentum/custom/*/scripts/weapon_momentum_*.txt`), found by
porting the CS:GO Desert Eagle over Momentum's pistol, which loaded but had no fire animation, no fire sound and no arms.

- **Fire animation.** The client picks view model sequences by index on the networked reference model (Momentum's
  `v_mom_pistol.mdl`) and remaps them to the override model by activity. Two places went wrong when the override's
  sequence order differs:
  - `RecvProxy_MomentumViewmodelSequenceNum` compared the networked index with the override's *local* index, so a
    change was skipped whenever they happened to be equal (the reference's fire sequence and the deagle's idle are
    both 0). It now compares with the last networked index.
  - Predicted animations (`SendWeaponAnim` on the client) passed the reference model's index straight to the
    override model. `CMomentumViewModel::SendViewModelMatchingSequence` now remaps it through the activity too.
- **Arms.** `attach_to_hands 1` always bone-merged Momentum's arms, whose bones (`hand.L`, ...) no foreign view model
  has, so nothing showed. A weapon script can now name its arms with `ModelData { "arms" "..." }` (the porter writes
  CS:GO's `models/weapons/ct_arms.mdl`, which shares 47 of 48 bones with CS:GO view models). Arms are also swapped
  when switching to a weapon that wants different ones, instead of keeping whichever were attached first.
- **Shot sounds.** `SoundData { Overrides }` is applied per sound source entity, but bullet weapons (pistol, machine
  gun, shotgun, sniper) play their shot from the player in `FX_FireBullets`, so overrides never reached it.
  `C_MomentumPlayer::GetEffectiveSoundEntry` now applies the active weapon's overrides. Until this ships, the porter
  redefines the shot's soundscript entry in the pack instead.

## weapon-reload-all.patch

- **`weapon_reload_all`** (client command, needs a map loaded): reloads everything a weapon pack touches, so packs can
  be tested without restarting.
  - Mounts folders and VPKs added to `momentum/custom/` since the game started (`gameinfo.txt`'s `custom/*` is only
    scanned at boot), ahead of the packs already mounted. It unmounts the ones that were removed, and does nothing
    with `-nocustommount`.
  - Then runs `sv_soundemitter_flush`, `sv_particles_reload`, `cl_particles_reload`, `weapon_reload_scripts` and
    `flush` (models and waves), and reloads textures and materials. Those commands are cheats only because they're
    for development, so it dispatches them directly rather than through the console's cheat check.
  - `weapon_reload_scripts` on its own still only re-reads the weapon scripts.
- **Defrag grenade trail.** `CMomDFGrenade` derives from `CMomExplosive`, not `CMomDFProjectile`, so it never made trail
  particles and its `GrenadeTrail` key went unread. It now makes the script's `GrenadeTrail` at the model's `trail`
  attachment, like the other Defrag projectiles. Momentum's own script has no `GrenadeTrail`, so its grenade is
  unchanged; the porter's Quake 3 grenade launcher uses it for Quake 3's smoke.
