# Engine patches

Changes to Momentum's engine (strata/engine) that ported assets need. They aren't applied anywhere; review them and
apply with `git apply <patch>` in an engine checkout. They apply cleanly to the engine as of October 2026 and compile,
but haven't been tested in game. Each is a fix in an existing feature, except `gamemode-weapon-scripts.patch`, which
is the one small feature the porter can't do content-side; the porter works around everything else, so a Momentum
build without them only misses what's listed under each patch.

## weapon-overrides.patch

Fixes client-side weapon overrides (weapon scripts in `momentum/custom/*/scripts/weapon_momentum_*.txt`), found by
porting the CS:GO Desert Eagle over Momentum's pistol, which loaded but had no fire animation and no arms.

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
  when switching to a weapon that wants different ones, instead of keeping whichever were attached first. Leaving
  the key out keeps Momentum's arms.

Without it: weapons from mounted games (CS:GO, CS:S, TF2, HL2) may play the wrong fire animation, and CS:GO weapons
show no arms. Converted weapons (Quake 3, GoldSrc) aren't affected by the animation bugs when their sequences are in
Momentum's order.

Shot sounds of weapons used in every mode need no engine change: bullet weapons play their shot from the player,
where a weapon's `SoundData Overrides` don't apply, so the porter redefines the shot's soundscript entry in the pack
instead. Weapons for some modes can't (see `gamemode-weapon-scripts.patch`).

## gamemode-weapon-scripts.patch

Lets a game mode have its own weapons, so Climb (1.6) can use a CS 1.6 pistol and Climb (KZT) a CS:GO one.

- **Per-mode client scripts.** `CWeaponDef::ParseWeaponScript` reads a client weapon script from
  `scripts/<mode>/<weapon>.txt` when that file exists, where `<mode>` is the current mode's cfg name without its
  extension (`kz16`, `kzt`, `df_cpm`, ...), and from `scripts/<weapon>.txt` as before otherwise. Server scripts
  (`scripts/server_weapon/`) are untouched. The definitions are parsed again in `LevelInitPreEntity`, just before the
  existing `PrecacheAll()`: the mode is set from the map name at `game_init`, before either DLL's level init, and the
  game mode system is one engine object both DLLs query, so client and server agree without a cvar, a cfg line or a
  reload, and the mode's models precache normally. `weapon_reload_scripts` keeps working.
- **Player-emitted sounds use the weapon's overrides.** Bullet weapons' shots are emitted by the server from the
  player (`FX_FireBullets`, the server script's `single_shot` entry), so the client weapon's `SoundData Overrides` never
  reached them and the porter had to redefine the entry in the soundscript, which every mode shares. The client player
  now routes its sound entries through its active weapon's `GetEffectiveSoundEntry` (which returns names it doesn't
  override unchanged), so a per-mode script's `Overrides { "Weapon_Pistol.Single" "Porter_cs16.usp.single_shot" }` is
  what plays.

Without it: `scripts/<mode>/` is ignored, so weapons built for some modes don't appear and those modes use the pack's
shared script or Momentum's weapon; weapons for every mode are unaffected (their shot is redefined in the soundscript).

## bonemerge-flush-crash.patch

`InvalidateMdlCache()` deleted an entity's `CStudioHdr` but left its `CBoneMergeCache`, which holds a copy of that
pointer and only rebuilds when the model's raw header moves. A flushed model usually reloads at the same address, so
the next bone merge (`MergeMatchingBones`, bone_merge_cache.cpp:208) read the deleted header and crashed. The cache is
now deleted with the header, on client and server. Pre-existing: `flush` and `r_flushlod` crash without it whenever
anything is bone-merged (a held weapon, arms on the view model).

Without it: the `flush` line in a pack's `cfg/porter_reload.cfg` crashes the game; remove it and reload the map to
pick up changed models instead.

## particles-reload-precache.patch

`cl_particles_reload` / `sv_particles_reload` call `UncacheAllParticleSystems()`, which threw away the precache
lookups (string table index to system), then re-read the manifest. The string table outlives the reload and effects
dispatched by index (bullet impacts, networked effects) look up through those tables, so every one of them found
nothing afterwards: `Attempted to create unknown particle system type "impact_concrete"` and the red-cross error
effect on every hit until the next map. Walking the string table isn't enough, because client-side precaches (the
impacts) live in its client-side part. The lookups are now kept through an uncache (they resolve by name, which
survives the re-read) and `PrecacheLookedUpParticleSystems()` precaches the re-read definitions they point at.

Without it: the `sv_particles_reload` / `cl_particles_reload` lines in `cfg/porter_reload.cfg` break bullet impacts
for the rest of the map; remove them and restart to pick up particle changes instead.

## weapon-reload-scripts-model.patch

`weapon_reload_scripts` (client side) set every weapon's model to its world model. A held weapon's model is its view
model, and that differing from the world model is what makes the client create the world model clone that stands in
for it in third person and hides it in first person; with the two equal the clone goes away on the next update and
the entity draws its world model in first person, floating in front of the camera. Each weapon now keeps whichever
of the two models it had.

Without it: after `weapon_reload_scripts` (so after `cfg/porter_reload.cfg`) the held weapon's world model shows in
first person until the next map.

## viewmodel-muzzle-flash.patch

Momentum's own view models have no muzzle flash events, but models from other games carry `CL_EVENT_MUZZLEFLASH0`
(5001) on their fire sequences: CS:GO's name the attachment in the options (`"1"`), CS:S's and HL2's give the sprite
flash type and imply the attachment from the event. `C_BaseViewModel::FireEvent` hands the event to the weapon, and
the base class falls through to `C_BaseAnimating::FireObsoleteEvent`, which draws the flash on the *view entity* (the
player model) rather than the view model, so nothing visible comes of it. `CWeaponBase::OnFireEvent` now handles it on
the view model: it draws the weapon script's `ParticleData "MuzzleFlashEffect"` at the attachment when the script has
one (the porter sets CS:GO's `weapon_muzzle_flash_*` from `items_game.txt`), else the engine's sprite flash.

Without it: ported weapons from CS:GO, CS:S and HL2 have no first-person muzzle flash.

## df-grenade-trail.patch

`CMomDFGrenade` derives from `CMomExplosive`, not `CMomDFProjectile`, so it never made trail particles and its
`GrenadeTrail` key went unread. It now makes the script's `GrenadeTrail` at the model's `trail` attachment, like the
other Defrag projectiles. Momentum's own script has no `GrenadeTrail`, so its grenade is unchanged.

Without it: the Quake 3 grenade launcher's grenades have no smoke trail.
