# Engine patches

Changes to Momentum's engine (strata/engine) that ported assets need. They aren't applied anywhere; review them and
apply with `git apply <patch>` in an engine checkout. They apply cleanly to the engine as of October 2026 but haven't
been compiled or tested in game.

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
