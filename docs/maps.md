# Maps

Porting maps made for other games: Source maps (Kreedz Climbing Mod, CS:GO with GOKZ, Counter-Strike: Source) in the porter itself, Quake 3 and GoldSrc maps through BSPConvert. See the [README](../README.md) for an overview.

## Command line

The verbs `sounds` and `weapon` build asset packs (see [weapons.md](weapons.md) and [player-sounds.md](player-sounds.md)), and `review` checks a map against Momentum's entity tools (see [Entity review](#entity-review-and-next-steps)). Without a verb, the arguments are a map to port:

```
MomentumPorter-CLI <input.bsp> <output.bsp> [--game kzmod|gokz|css] [--compress] [--bhop-trigger-fix bhop|on|off] [--game-dir <folder>]
MomentumPorter-CLI review <map.bsp> [--steps]
```

`--bhop-trigger-fix` picks the maps that get the bhop trigger fix (see [Automatic fixes](#automatic-fixes)): `bhop`
(the default) for maps named `bhop_*`, `on` for every map, e.g. bhop maps named otherwise (`kz_bhop_*` climb/bhop
hybrids such as `kz_bhop_yonkoma`, or a map missing its prefix), `off` for maps the fix breaks. KZ Mod maps never get it, since their
bhop blocks are their own entities. In the app it's the **Fix bhop triggers** dropdown (bhop_ maps only, On, Off),
shown while the queue has a Source map that isn't a KZ Mod one.

`--game-dir` is the source game's folder (the one with `gameinfo.txt`), for game files the map relies on, such as
shared soundscapes. It defaults to the folder above the input's `maps` folder, or for KZ maps from elsewhere, Kreedz
Climbing's folder in your Steam libraries.

The output is written as a new file, uncompressed unless `--compress` is given, and the tool prints what it converted
and anything it couldn't, then the entity review and the steps left to submit the map. It doesn't pack assets; the
desktop app does. Packed files named after the map, such as cubemap materials, aren't renamed, so keep the map's name
or rename it with Lumper afterwards.

## Desktop app

`src/MomentumPorter.UI` (`MomentumPorter-UI.exe`) does what `pack_for_momentum.bat` did, with a window: drop maps on it
(or pass them on the command line), pick options, Run. Each queued Source map is ported, packed with bspPack
(`-M -C`) if it's a Kreedz Climbing map (always, since Momentum doesn't mount KZ; the games it mounts, CS:S, CS:GO,
TF2 and Portal 2, need no packing) and copied into Momentum's `momentum/maps` folder, one map at a time. Everything happens in a temp work folder
and the map is only moved into `maps/` once every step succeeded, so a failed or cancelled map never leaves a broken
file there. An installed map of the same name is replaced. The game a Source map was made for is detected from its
entities. The options panel only shows the options for the kinds of map in the queue, and the rarely needed ones only
with its **Advanced** toggle on. Files the conversion writes next to the map, such as GOKZ's `zones/local/<map>.json`,
go along with it.

- **Maps**: the queue (status per map, detected kind and game), the options form (generated from the
  `[PortOption]` attributes on `MapPortOptions` and `BspConvertOptions`), and the log, report and next steps of the
  selected map. A ported map whose entities still need changing by hand in game gets a warning line in the queue
  (see [Entity review](#entity-review-and-next-steps)); the clipboard button on a Source map checks it as it is,
  without porting. Options can be saved and loaded as JSON presets. Quake 3 and GoldSrc maps in the queue go through
  BSPConvert instead (its options follow the Source map options in the same list) into Momentum's game folder.
- **Settings**: Momentum's and Kreedz Climbing's folders (found in your Steam libraries unless overridden), the
  studiomdl executable (Momentum's own unless overridden), the work folder, the output folder packs fall back to when
  Momentum isn't found, and which known games were found. Saved to `%AppData%/MomentumPorter/settings.json`.
- **Weapons**: pick a game mode and the Momentum weapon to replace, then the game and the weapon that goes in its
  place, and build the pack (see [weapons.md](weapons.md)).
- **Player Sounds**: pick every game mode or one, a game (and a Quake 3 character or CS:GO team), then build the
  pack (see [player-sounds.md](player-sounds.md)).
- **Player Models**: placeholder; see [player-models.md](player-models.md) for the feasibility study.

## Packing assets

A Windows build of bspPack with the fixes these ports need comes with the porter (`bspPack/` next to the app, from
[tools/bspPack](../tools/bspPack)), and the porter sets it up for each map's game. bspPack reads the game folder it
packs from in its `config.ini` and runs bspzip from `<game folder>/../bin/win64`, so before each pack the porter
writes, in `%LocalAppData%/MomentumPorter/bspPack`:

- `bin/win64`, a junction to Momentum's `bin/win64` for its bspzip.
- `<game>/gameinfo.txt`, a stub game folder that searches only the game's own content, so nothing Momentum already has
  (its own content, HL2's, the games it mounts) gets packed.
- `<game>/vpk`, the game's VPKs extracted, since bspPack only packs loose files. This happens on the first pack (KZ's
  are 2.3 GB and take a few minutes) and again when the VPKs change.

It then writes bspPack's `config.ini`, pointing at the stub, and its `ResourceConfig.ini`, excluding the game's loose
files that a VPK searched before them shadows (the game never loads them, and Momentum has the VPK's copy):

| Game | Searches | Excludes loose files shadowed by |
|---|---|---|
| Kreedz Climbing | `kz_textures`, `kz_sounds` and `kz_misc` extracted, then the loose `kz` folder | KZ's HL2 VPKs |

Only Kreedz Climbing maps are packed (`IGameProfile.PacksAssets`): Momentum mounts CS:S and CS:GO, so their maps keep
whatever their authors packed and the pack step just logs that. Kreedz Climbing is found in your Steam libraries unless
its folder is set in Settings. KZ maps from outside KZ's `maps` folder also get their shared soundscapes from that KZ
folder.

## Quake 3 and GoldSrc maps

Quake 3 (`.bsp`/`.pk3`) and GoldSrc (Half-Life, CS 1.6; `.bsp`/`.zip`) maps are converted in process with
[BSPConvert](https://github.com/momentum-mod/BSPConvert) (the `external/BSPConvert` submodule, see [building.md](building.md)). The CLI
takes BSPConvert's options under the
same names (`--wads`, `--subdiv`, `--scale`, `--nopak`, `--prefix`, `--maps`...; see `--help` or BSPConvert's README):

```
MomentumPorter-CLI <map.pk3|map.bsp|maps.zip> <outputGameDir> [BSPConvert options]
MomentumPorter-CLI "...aseq3\pak0.pk3" "...\Momentum Mod Playtest\momentum" --maps q3dm1
MomentumPorter-CLI "...\Half-Life\cstrike\maps\kz_persia.bsp" "...\Momentum Mod Playtest\momentum"
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
  `Q3Content` folder next to the program, and mods' and expansions' from `Q3Mods/<mod folder>`, and packs what a map
  uses from them into the map. The porter extracts Quake 3's `baseq3/pak*.pk3` into `Q3Content` first (about 700 MB,
  once), and the pk3s of every mod folder next to `baseq3` into `Q3Mods` (Team Arena's `missionpack` is about 600 MB;
  `defrag` is skipped, since maps don't use its content). The install is the Quake 3 folder set on the Settings page,
  or else Steam's, and the folders are extracted again when the pk3s change. Folders filled by hand are left as they
  are. `--noq3content` turns that off.
- The Quake 3 game (`--q3mod`, **Game content** in the app's Quake 3 options): BSPConvert picks the mod a map needs
  from the textures it uses that neither it nor Quake 3 has, and logs which one ("Using Team Arena's content..."). The
  dropdown (or `--q3mod missionpack`, `--q3mod baseq3`) overrides that. Files are looked up in the order Defrag's
  engine (oDFe) searches them: the mod's before Quake 3's, and the map's pk3 before or after Quake 3's paks depending
  on how its name sorts against `pak0.pk3`; where two shader scripts define a shader, the one searched first wins.
- Textures a map draws that none of that has come from other maps' pk3s. BSPConvert lists them in a warning; download
  the map's dependencies from [ws.q3df.org](https://ws.q3df.org/) and extract them into `CustomContent` as described
  in its `readme.txt`.
- In the app, the output folder defaults to Momentum's `momentum` folder.

GoldSrc bhop maps make their blocks touch-activated `func_door`s (or `func_button`s) that drop the player into a
teleport. After converting a GoldSrc map, the porter turns those into `func_bhop`s that teleport the player to the same
destination once they've stood on the block too long, keeping the teleport's angle and velocity settings (`Fix bhop
blocks` in the app's GoldSrc options, `--noblockfix` to turn it off). A block counts when the player can fall from it
into a `trigger_teleport` without landing on anything solid first, so this also finds blocks over a pit, which
Momentum's runtime block fix misses because it only looks for teleports the block dips into as it drops. Named blocks lose their name and outputs, which the report warns about.

GoldSrc maps are lit and their textures brightened as Half-Life's 25th anniversary update draws them with the video
settings in the app's GoldSrc options: **Gamma** and **Brightness** (`--gamma`, `--brightness`), plus **Light gamma**,
**Texture gamma** and **Overbright lighting** among the advanced ones (`--lightgamma`, `--texgamma`, `--nooverbright`).
They default to the game's own (gamma 2.2, brightness 0, overbright on). A map made with an older config (gamma 2.5,
brightness 1) has much brighter shadows with those, so set them to match. Leave overbright on unless matching
`gl_use_shaders 0`: Half-Life's shaders draw it whatever `gl_overbright` says, so an old config's `gl_overbright 0`
changes nothing.

GoldSrc filters every texture unless texture filtering is turned off in its video options, which some maps are made for,
with pixelated or dithered textures that look blurry filtered (kz_lain). **Point sampled textures** in the app's GoldSrc
options (`--pointsample`) draws them unfiltered and without mips, as GoldSrc does with filtering off.

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
| `css` | Counter-Strike: Source |

Each game is an `IGameProfile` in `src/MomentumPorter/Games/<Game>/`, registered in `GameProfiles.All`. Profiles work on
a `PortContext`, which provides the entities and generic operations such as `SplitByBrush` and `RewriteOutputs`.

The game is detected from the map (`SourceGameSniffer`): KZ Mod's own entity classes, GOKZ's `climb_*` names, and
Counter-Strike entities (spawns, buy zones...) in a version 19/20 BSP for CS:S or a version 21 one for CS:GO (a CS:GO
map without GOKZ entities goes through the GOKZ profile, which only adds the cascaded shadow light and zones nothing).
Failing those, the game folder the map sits in, then its name (`kz_` KZ Mod, `bhop_`/`surf_` CS:S), with a warning.

Whatever the game (but KZ Mod), a Source map named `bhop_*` gets the bhop trigger fix by default (see
[Automatic fixes](#automatic-fixes)), and
one named `bhop_*` or `kz_*` gets the bhop block fix baked in after the profile ran
(dropping `func_door`/`func_button` blocks over a teleport become `func_bhop`s, see
[Quake 3 and GoldSrc maps](#quake-3-and-goldsrc-maps)), since those are the maps Momentum's own runtime block fix
would run on.

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
- An `env_cascade_light` is added when the map has none (the CSM Fix), so cascaded shadow maps work. It follows the map's `light_environment`; a map without one is left alone, with a warning.

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

### Counter-Strike: Source

CS:S surf, bhop and kz maps use stock entities Momentum already runs, so the profile only bakes in the model scale
fix every CS:S map needs, tagging the props `_ported_by_tools 2`: older Source games squared a prop's `ModelScale`, so
`prop_dynamic`, `prop_dynamic_override` and `prop_dynamic_glow` with a scale other than 1 get `ScaleType 1`
(non-hierarchical), which brings that behaviour back. Props with a collision model are skipped; the porter looks for
the model's `.phy` in the map's pak file and then in CS:S's files (found through Steam or the Settings page). Without
CS:S the fix is applied anyway, with a warning. CS:GO maps don't need it, since CS:GO doesn't square the scale.

The bhop fixes apply to Source maps by name and `persist` on gravity triggers by game (see [Automatic fixes](#automatic-fixes)).
Everything else needs someone in game and is left to the entity review below.

## Automatic fixes

After the game profile, `ApplyProfileStep` applies the bhop fixes, and `FixEntitiesStep` and `RemoveMountedAssetsStep`
(`src/MomentumPorter.Core/Fixes/`) apply what Lumper's Entity Review tab would otherwise ask for by hand, where the
answer is the same for every map. GoldSrc maps converted with BSPConvert get the entity fixes too
(`GoldSrcEntityFixStep`).

- **Bhop triggers** (`BhopTriggers`, Source maps named `bhop_*` by default, or every Source map or none as
  **Fix bhop triggers** / `--bhop-trigger-fix` says, since the fix breaks some maps; never KZ Mod maps): a bhop block's `trigger_multiple` renames the player
  (`AddOutput targetname X`) a moment after touching and back to `default` a little later, and the `trigger_teleport`
  around the block only takes players with that name through a `filter_activator_name`, so jumping up a slope or onto
  a ledge that touches the trigger can teleport the player. The fix makes the renames happen `OnLand`, marks the
  player as off the ground with a `_mom_leftground` context `OnJump`/`OnEndTouch`, and points the teleports at a
  `filter_multi` (`momentum_tools__filter_bhop_multi`, then `_2`, `_3`... for any further name filter) of the name
  filter and a `filter_activator_context` that rejects players with that context. Detection is the engine's
  `IsBhopTrigger` (exactly two renames within a second on `OnStartTouch` or `OnTrigger`, the earlier one a name some
  teleport's filter accepts). Fixed entities are tagged `_ported_by_tools 2`.
- **Bhop blocks** (`BhopBlocks`, Source maps named `bhop_*` or `kz_*`, the ones Momentum's runtime block fix runs
  on): touch-activated `func_door`s and `func_button`s that drop the player into a teleport become `func_bhop`s in the
  BSP, as described for GoldSrc maps above.

- **Dead entities** (`DeadEntities`): entities Lumper's rules (`resources/EntityRules_Momentum.json`) deny because
  they do nothing in Momentum, and whose removal can't change gameplay: level transitions (`trigger_changelevel`,
  `trigger_transition`; not `info_landmark`, which landmark teleports use), `point_servercommand` (CS:S surf maps set `sv_airaccelerate` with it;
  Momentum's cvars are per mode and the max velocity comes from the zones), `game_ui`, `logic_console`, `logic_convar`,
  the event listeners, achievements, commentary, gamestats and test entities, `weapon_cubemap`, the flashbang and smoke
  grenade, and the Counter-Strike round entities Momentum's server doesn't link (`func_buyzone`, `func_bomb_target`,
  `func_hostage_rescue`, `func_vip_safetyzone`, `hostage_entity`, `info_map_parameters`). Denied entities that need
  *replacing* (the deprecated Momentum triggers, `env_surface_teleport`) and the Warn list (weapons, jails, timers)
  are left to the review.
- **env_global** (`GlobalStates`): carries a state across level transitions, so Lumper denies it. Within a map it can
  still gate a `logic_auto` (which only fires while its `globalstate` is on), so those are kept with a warning and the
  rest removed. BSPConvert's GoldSrc ports are the usual source: it turns the states multisources read into
  `logic_branch`es (`__goldsrc_global_<state>`) and leaves the `env_global`s as spare targets.
- **Gravity triggers** (`GravityPersistence`): every Source game before CS:GO, and GoldSrc, kept a `trigger_gravity`'s
  change after leaving the trigger; Momentum reverts it unless the trigger says `persist 1`. Lumper's rule and the
  review checklist say ports from those games need it, so profiles whose `GravityPersists` is true (all but GOKZ) set
  it on triggers that don't say either way and tag them `_ported_by_tools`.
- **Mounted assets** (`MountedAssets`): Lumper's Remove Game Assets job. A packed file that Counter-Strike: Source,
  CS:GO, Team Fortress 2, Portal 2 or Half-Life 2 (Momentum's own copy) has at the same path with the same bytes is
  removed; one that differs is a deliberate replacement and is kept with a warning. Only installed games are checked
  (found through Steam or the Settings page), the others are logged. `materials/maps/`, `maps/` and
  `resource/overviews/` are the map's own and skipped.

Not automated, because each needs a look at the map: teleport velocity modes (Reset, Keep Negative Z), boost trigger
fixes (which scenario a boost is), landmark teleport angles, jail `logic_timer`s, weapons and `game_player_equip` in
jails, slow `func_button`s (spawnflag 1), music into `sound/music`, textures to shrink or remove, and the cubemap
folder of a renamed map (its materials' names live in the BSP's texture data as well as the pak file, which Lumper's
Update filename-specific Content rewrites). The review below lists them.

## Entity review and next steps

After the profile, `ReviewEntitiesStep` goes through the map the way Momentum's
[entity tools](https://docs.momentum-mod.org/guide/map_submission/fixing_entities/) and
[review checklist](https://docs.momentum-mod.org/guide/map_submission/review_checklist/) would
(`EntityReview.Analyze`, `src/MomentumPorter.Core/Review/`). Each fix or check becomes a `ReviewItem` with how many
entities it applies to, how many are already dealt with (tagged by the tools or the porter, or fine as they are), where
the rest are fixed (the in-game tools for teleports and boosts; Lumper or porting again for the others) and a docs
link. Pending items that aren't notes go in the report as warnings, into the queue item's warning line ("Needs manual
entity changes: 3 boost triggers, ...") and into the **Next steps** tab:

| Tool | Severity | Found | Done when |
|---|---|---|---|
| Invalid entities | required | entities Lumper denies for doing nothing (`DeadEntities`) | removed |
| env_global | required | `env_global`s | removed (ones a `logic_auto` reads need that `logic_auto` changed first) |
| Bhop Trigger Fix | review on `bhop_` maps, note otherwise | `trigger_multiple`s matching `IsBhopTrigger` | the renames are on `OnLand` |
| Bhop Block Fix | required for `bhop_`/`kz_` maps | dropping blocks over a teleport (`BhopBlocks.Find`) | `func_bhop` |
| Model Scale Fix | required, maps before version 21 | scaled dynamic props without a collision model | `ScaleType 1` |
| CSM Fix | required for version 21 (CS:GO) maps | the map | it has an `env_cascade_light` |
| Teleport Velocity Mode | review on `surf_` maps, note otherwise | destinations inside `playerclip` brushes (surf cages, jails), which the docs say should keep only downward velocity; bhop maps box their fail destinations in clip too | every teleport to it has a `VelocityMode` |
| Teleport Velocity Mode | note | every other destination | same |
| Landmark Teleports | review | `trigger_teleport`s with a `landmark` | tagged |
| Boost Triggers | review | horizontal `trigger_push`es, `trigger_setspeed`s, `trigger_multiple`s adding basevelocity, lowering gravity for a moment or renaming the player for a filtered push (`BoostTriggers.Find`) | tagged |
| Gravity Triggers | review | `trigger_gravity` | has a `persist` key (set by `GravityPersistence` for games before CS:GO) |
| Weapon strip | review | `player_weaponstrip`, `game_player_equip` | never |
| logic_timer | note | `logic_timer` | never |
| func_button | note | `func_button` | spawnflag 1 |

Review items are judgement calls the tools can't make for you, so a map that went through the tools still shows the
boosts and gravity triggers the reviewer decided to leave alone; the verdict line says so ("every required fix is in;
still to confirm in game, since they were left unchanged: ..."). Required items decide the exit code of
`MomentumPorter-CLI review <map.bsp>` (1 when one is missing), which is the way to validate a map saved from Lumper after
the in-game review, as is the clipboard button on a Source map in the app's queue (it reviews the file as it is).

`SubmissionSteps.For` turns the review into the **Next steps** list (only there, so the log stays the tools' output;
the tab shows a dot until the selected map's steps have been looked at; the CLI prints them after the report): one step
per pending fix made in Lumper or by porting again, then the entity tools setup and one step per pending in-game tool
(only when there is one), zoning (noting the zone file the
porter wrote for GOKZ and Quake 3 maps), screenshots with `mom_official_screenshot`, the submission form and the review
checklist, each linked to its section of the docs (`DocsLinks`).

## Splitting brush entities

`BrushModelEditor.SplitModel` gives each brush of a brush model its own model with a one node, one leaf collision
tree. The new models share the original's faces and bounds, so each still draws the whole original entity (e.g. with
`showtriggers`). That's fine for triggers, but visible brush entities would need their faces divided up as well.
