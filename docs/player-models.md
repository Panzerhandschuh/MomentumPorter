# Player models from other games (Phase 6a spike)

Can bodies from CS:S / CS:GO / TF2 / HL2, CS 1.6 / HL1 and Quake 3 become the Momentum player body (third person,
ghosts, replays)? This is the write-up of the spike: what Momentum's model is made of, what was tried, what compiled,
what it looks like when Momentum's animations play on it, and what to build next.

## Recommendation

1. **ValveBiped games (CS:S, HL2, and very likely CS:GO and GMod-style models): feasible as an automatic full-body port.**
   Don't rename bones. Instead, *fit* the source mesh onto Momentum's skeleton: pose it so its joints land on
   Momentum's joints, then bind it to Momentum's exact 61-bone skeleton and `$includemodel` Momentum's animation model.
   CS:S `ct_urban` and HL2 `male_07` compile cleanly with Momentum's studiomdl on the first try. The compiled skeleton
   matches `mom_player.mdl` to 0.0001 units, and in software renders they play Momentum's idle, run, crouch and hop.
2. **GoldSrc (CS 1.6, HL1): feasible with the same pipeline.** BSPConvert's reader gives the SMD, and a second bone map
   covers both Biped namings. CS 1.6 `urban` (T-pose) and HL1 `gordon` fit, compile and animate. Textures go through
   BSPConvert's GoldSrc material converter as they already do for map models.
3. **Quake 3: no full-body port.** MD3 is three rigid, vertex-animated parts with no skeleton. The MVP is a
   *rigid three-part* body: legs, torso and head bound to Momentum's hip, chest and head bones. It turns and bobs
   with Momentum's animations, but the legs don't walk. Proper legs need auto-rigging, which is out of scope.
4. **TF2: probably feasible (custom `bip_*` rig, same fit approach, a third bone map), but untested.** This machine's
   TF2 and CS:GO installs have no `_dir.vpk` (only a few chunk files), so no TF2 or CS:GO model could be extracted.
5. **Next step before building 6b:** the in-game check. Copy one compiled port into `momentum/custom/<pack>/` (steps
   below) and look at it as a ghost and in third person. It wasn't done here because the spike can't run the game or
   hlmv.

The "mechanical" approach from the plan (rename ValveBiped bones to Momentum names and recompile) compiles, but the
result is garbage, with or without Momentum's `$definebone` list. The evidence is under
[Step 2](#step-2-valvebiped-cs-s-ct_urban-and-hl2-male_07).

## Step 1: how Momentum's player model is built

### Files and sources

- Compiled: `momentum/models/player/mom_player.mdl` (+ `.vvd`, `.dx90.vtx`) and `mom_player_animations.mdl`
  (animation only, no meshes). Both are MDL version 49 (Strata).
- **Sources exist** in the Playtest install: `momentum/modelsrc/mom_player/` has `mom_player.qc`,
  `mom_player_animations.qc`, `player_bones.qci` (61 `$definebone` lines), `player_weightlists.qci`, `*_anims.qci`,
  `mom_player_body.SMD` (Blender export) and `anims/*.SMD`. There's also `mom_player_old/` and `urban/` (Valve's
  CS:S `urban_sample` source). `E:\MomentumDev\game\momentum\modelsrc` only holds compile logs. There's nothing
  relevant in `E:\hl2_content`. There is no `.blend`.
- The installed models **don't match modelsrc**: the compiled animation model has 188 sequences, including `bfg_*`
  ones that modelsrc lacks, and uses `ACT_IDLE`/`ACT_JUMP` where modelsrc's QC says
  `ACT_MP_STAND_IDLE`/`ACT_MP_JUMP`. The `qc_path`
  keyvalue points at the artist's machine (`C:\Users\vxpdx\...`). **The pipeline therefore uses only the compiled
  `mom_player.mdl`** (skeleton, attachments, includes), read with
  `src/MomentumPorter.Core/Assets/Models/StudioModelHeader.cs`. modelsrc was used only to check the simulation.
- The game code hard-codes the model: `server.dll` contains `models/player/mom_player.mdl`. There's no player-model
  cvar. `mom_ghost_bodygroup` (0-14) is a leftover of the old shape ghosts, and `mom_player.mdl` has no appearance
  body groups. **A port replaces `models/player/mom_player.mdl` through `momentum/custom/<pack>/`**, which changes the
  local player and every ghost (replays and online) for that user only.
- Momentum mounts CS:S (`cstrike_pak_dir`), TF2 (`tf2_misc_dir`, `tf2_textures_dir`), CS:GO (`pak01`) and Portal 2
  (`gameinfo.txt` `mount` block). A ported CS:S body can therefore reference CS:S materials directly for a user who
  owns CS:S. A pack meant for sharing must ship them.

### mom_player.qc (installed source)

```
$modelname "player/mom_player"
$cdmaterials "models/mom_player_base"
$includemodel player/mom_player_animations.mdl
$origin 0 0 0 -90            // = no rotation: studiomdl's default turn is +90
$include player_weightlists.qci
$include player_bones.qci    // 61 $definebone lines (with fixups for the Blender bone axes)
$model "mom_player_body" "mom_player_body.SMD"
$bonemerge weapon_bone.R
$attachment player_forward forward 0 0 0 rigid rotate 0 0 -90
$sequence ref mom_player_body.smd
```

`mom_player_animations.qc` has the same `$definebone`/`$weightlist`/`$bonemerge` includes plus every sequence.
Both compiled models have **the identical 61-bone list** (checked bone by bone with `info`). The engine maps an
included model's animation onto the body by bone name, and the animation model's bone positions win for every bone
both models share.

### Compiled facts (from `PlayerModelSpike info`, full dump in `player-models/mom_player_info.txt`)

| | `mom_player.mdl` |
|---|---|
| Bones | 61, Blender names. Every bone except `weapon_bone.R` carries vertex weights (`BONE_USED_BY_VERTEX`) |
| Hitboxes | 1 set, 29 boxes, auto-generated (there's no `$hbox` in the QC) |
| Attachments | `player_forward` on `spine.006` at (0, 3.88, 0). Its source bone `forward` was collapsed |
| `$includemodel` | `models/player/mom_player_animations.mdl` |
| Body parts | `mom_player_body` and `clamped1`. Strata studiomdl split the 34k-vertex mesh because of its vertex limit ("model has too many verts (34013/21845), cutting into multiple models"). They aren't body groups |
| Materials | `models/mom_player_base/mom_player_body`, `mom_player_arms`: PBR shader with `MomPlayerColorPrimaryBody`, `MomPlayerSpeedBody` and `MomPlayerColorSecondaryBody` proxies (player colours and speed glow) |
| Rest pose | Faces +x, relaxed A-pose (hands at z 37.7, shoulders at 57.9), about 71 units tall |
| Animation model | 249 animations, 188 sequences, pose parameters `body_yaw`, `body_pitch`, `move_x`, `move_y` |

Skeleton (parent -> children, model-space z of the joint):

```
rootTransform (0)
└ spine (35.6, hips)
  ├ spine.001 (42.5) └ spine.002 (46.4) └ spine.003 (52.3, chest)
  │   ├ spine.004 (59.1, neck base) └ spine.005 (60.1) └ spine.006 (62.0, head; player_forward)
  │   ├ shoulder.L └ shoulder_helper.L └ upper_arm.L (56.8) └ forearm.L (45.9) └ hand.L (37.7)
  │   │     hand.L ├ palm.02.L ├ f_index.01-03.L, f_middle.01-03.L, f_ring.01-03.L, f_pinky.01-03.L
  │   │            └ thumb.01.L └ thumb.01.helper.L └ thumb.02.L └ thumb.03.L
  │   └ shoulder.R ... (mirror) ... hand.R ├ weapon_bone.R ($bonemerge) ├ palm.02.R ... └ thumb.01.R ...
  ├ thigh.L (37.9) └ shin.L (20.5) └ foot.L (4.2) └ toe.L
  └ thigh.R ... (mirror)
```

The Blender rig's bone axes differ from the `$definebone` frames. Momentum's QC compensates with `$definebone`
fixups. When I replayed modelsrc's `anims/*.smd` straight onto the compiled skeleton, the limbs flew off. The
`bake-anim` command derives the per-bone fixup (compiled rest x inverse of the rig rest) and converts the animations
first. With that, Momentum's own body plays its anims cleanly, which is the baseline below.

## Tooling built (`tools/player-model-spike`)

A console project, not in the solution (`dotnet build -c Release` in that folder). It links `StudioModelHeader.cs`
from Core and `GoldSrcModel.cs` from BSPConvert, and uses the ValvePak and ImageSharp packages.

| Command | What it does |
|---|---|
| `info <mdl>` | Header dump: bones with model-space joints and flags, hitboxes, attachments, body parts, materials, sequences, pose parameters, includes |
| `extract <dir.vpk> <out> <prefix>...` | VPK extraction (ValvePak) |
| `decompile <mdl> <out.smd>` | Reference mesh from `.mdl`+`.vvd`+`.dx90.vtx` (LOD 0, first model of each body part, weights as compiled). Handles v44-49, vvd fixups, and both vtx strip-group layouts (25 bytes for SDK 2013, 33 for Strata/CS:GO). **Crowbar 0.74 has no headless decompile mode** (the only switch-like strings in its exe are arguments it passes to studiomdl/hlmv), so this replaces it for meshes |
| `goldsrc <mdl> <outdir>` | GoldSrc v10 -> reference SMD + first frame of a few sequences, via BSPConvert's `GoldSrcModel` |
| `retarget <src.smd> <map.txt> <target.mdl> <out.smd> rename\|fit [--scale auto]` | The two approaches below |
| `qc <target.mdl> <mesh.smd> <out.qc> [--cdmaterials d] [--modelname m] [--no-definebone] [--static]` | QC for a fitted body: Momentum's includes, `$definebone` for all 61 bones, `$bonemerge`, attachments copied from the compiled model |
| `compare <reference.mdl> <port.mdl>` | Bone-by-bone skeleton, bind pose and attachment check of a compiled port against `mom_player.mdl` |
| `bake-anim <compiled.mdl> <rig-rest.smd> <anim.smd> <out.smd>` | Converts a source-rig animation SMD into the compiled bone frames (fixups), for simulation |
| `pose <mesh.smd> <anim.smd> <frame> <out.smd>` | Plays one frame on a mesh the way an `$includemodel` animation plays: bones matched by name, the animation's local transforms win, linear blend skinning |
| `stats <rest.smd> <posed.smd>` | Edge-length distortion of a pose, and which bones the stretched edges belong to |
| `render <out.png> <smd>...` | Z-buffered software render, front/side/three-quarter, one row per SMD, all at one scale |
| `smdbones <smd>` | Skeleton of an SMD with model-space joints and which bones are weighted |

Work folders: `%TEMP%\mp-player-spike\game` (a minimal `gameinfo.txt`, which is the `-game` folder for studiomdl),
`...\src\<variant>\` (SMD, QC, `studiomdl.log`), `...\game\models\player\spike\*.mdl` (compiled) and `...\renders\`
(the PNGs referred to below). Nothing was written into Momentum or any game folder.

## Step 2: ValveBiped (CS:S ct_urban and HL2 male_07)

```powershell
PlayerModelSpike extract "...\Counter-Strike Source\cstrike\cstrike_pak_dir.vpk" extracted models/player/ct_urban materials/models/player/ct_urban
PlayerModelSpike extract "...\Half-Life 2\hl2\hl2_misc_dir.vpk" extracted models/humans/group01/male_07
PlayerModelSpike decompile extracted\models\player\ct_urban.mdl ct_urban_ref.smd
#   vvd: 6672 LOD 0 vertices, 9 fixups / vtx strip group header size: 25
#   body part ct_urban: model 0 (ct_urban_reference.smd), 5024 triangles
#   winding check: 2000/2000 triangles face along their vertex normals
PlayerModelSpike decompile ...\male_07.mdl male_07_ref.smd     # 4540 triangles, 56 bones
```

`ct_urban.mdl` is v44 with 50 bones. Besides the biped there are twist helpers (`L/R_Wrist`, `L/R_Ulna`), weapon
bones (`weapon_bone*`, unweighted) and `forward`. It has 3 fingers per hand, 19 hitboxes and 12 attachments
(grenades, holsters...). It includes `cs_player_shared.mdl`/`ak_anims_ct.mdl`. `male_07` has 5 fingers and no
helpers. **Both rest poses face -y and hold the arms out at about 45 degrees**, while Momentum faces +x with its arms
down (see `renders/source_refs.png`). ValveBiped bones run along +x and Blender bones along +y, so the bone axes
differ as well as the proportions.

### Bone map

`docs/player-models/valvebiped_to_momentum.txt` (format: `source target [aim=child] [place=target]`; unlisted
bones fold into their parent):

| ValveBiped | Momentum | Note |
|---|---|---|
| `Bip01` | (fold) | unweighted root, dropped by compiled v44+ models |
| `Bip01_Pelvis` | `spine` | `aim=-`: Spine's joint sits behind the pelvis, so aiming at it would tilt the hips 49 degrees |
| `Bip01_Spine`, `Spine1`, `Spine2` | `spine.001`, `spine.002`, `spine.003` | |
| `Bip01_Spine4` | `spine.003`, `place=spine.004` | Clavicles hang off Spine4 (z 61) in ValveBiped but off spine.003 (z 52) in Momentum: the weights go to the chest bone, the joint to the neck base |
| `Bip01_Neck1`, `Head1` | `spine.005`, `spine.006` | `aim=-` on the neck keeps the face upright (Momentum's neck leans 17-24 degrees further forward) |
| `L/R_Thigh`, `Calf`, `Foot`, `Toe0` | `thigh`, `shin`, `foot`, `toe` `.L/.R` | |
| `L/R_Clavicle`, `UpperArm`, `Forearm`, `Hand` | `shoulder`, `upper_arm`, `forearm`, `hand` | `shoulder_helper` stays unweighted; `Hand` aims at `Finger2` |
| `Finger0/01/02` | `thumb.01/02/03` | |
| `Finger1x`, `Finger2x`, `Finger3x`, `Finger4x` | `f_index`, `f_middle`, `f_ring`, `f_pinky` | CS:S only has Finger0-2 |
| `L/R_Wrist`, `L/R_Ulna`, `weapon_bone*`, `forward`, `Anim_Attachment_*` | (fold) | twist helpers -> forearm |

The Momentum bones that get no weights are `rootTransform`, `shoulder_helper`, `palm.02`, `spine.004`,
`thumb.01.helper`, `weapon_bone.R`, and ring/pinky on CS:S.

### Attempt A: mechanical rename (as the plan proposed)

`retarget ... rename` renames the mapped bones, folds the rest into their parents and keeps the source rest pose.
Two QCs (`qc --no-definebone`, and with Momentum's 61 `$definebone`s):

| Variant | studiomdl | Compiled skeleton vs mom_player | Result |
|---|---|---|---|
| `css_rename` (no `$definebone`) | OK, 0 warnings, 40 bones | 31 missing/reparented bones, rest pose off by 8.7 units / 180 degrees | Rest pose is CS:S (facing -y, arms out). Under Momentum's run it's a tangle (`renders/posed_run.png`, row 3): 20-23% of edges stretched > 25% in every pose |
| `css_rename_def` (`$definebone`) | OK, 0 warnings, 61 bones | identical | studiomdl keeps each vertex fixed to its source bone and moves the bones to the defined rest pose, so the mesh is mangled before any animation (`renders/compiled_rest.png`, row 3: twisted head, folded shoulders) |

Why: Momentum's animations are local rotations about *Momentum's* bone axes, relative to *Momentum's* rest pose. Under
a renamed ValveBiped skeleton the same rotations turn the limbs about the wrong axes. `$definebone` fixes the skeleton
but not the mesh. `$bonemerge` doesn't help here either: it only makes bones follow another entity's bones of the
same name, it doesn't remap a mesh.

### Attempt B: fit onto Momentum's skeleton (the proposal)

`retarget ... fit --scale auto`:

1. **Turn** the source about z so its left-right axis (mapped `.L` minus `.R` joints) matches Momentum's: 90 degrees for
   CS:S/HL2/GoldSrc.
2. **Scale** uniformly by the median target/source segment ratio: CS:S 0.939, HL2 0.977, CS 1.6 1.052, HL1 1.002.
3. For each mapped bone, **turn** its vertices about its joint so the bone points where the Momentum bone points (at the
   joint of its mapped child, or `aim=`). Bones with no child turn with their parent. Then **move** the joint onto the
   Momentum joint (or `place=`). Blend by the source weights, which is linear blend skinning from the source rest pose
   to a pose matching Momentum's rest.
4. **Bind** the moved mesh to Momentum's exact skeleton (built from the compiled `mom_player.mdl`), with the weights
   remapped and merged (at most 3 per vertex).

Segment fit for CS:S (full table in `player-models/retarget_css_ct_urban.log`). The arms bend 21-23 degrees from
"out" to "down", the legs about 6:

```
segment                         source len  target len  ratio  bend (deg)
Thigh -> Calf                        17.43      17.43   1.00      6.1
Calf -> Foot                         16.14      16.45   1.02      0.5
UpperArm -> Forearm                  11.42      11.38   1.00     23.4
Forearm -> Hand                      11.21       8.73   0.78     21.4
Clavicle -> UpperArm                  5.89       7.40   1.26     12.3
Spine2 -> Spine4                      8.71       6.82   0.78      7.9
```

Compile (`qc` output copied to `player-models/css_ct_urban_fit.qc`, log in
`player-models/studiomdl_css_ct_urban_fit.log`):

```
"...\Momentum Mod Playtest\bin\win64\studiomdl.exe" -game %TEMP%\mp-player-spike\game -nop4 -nox360 css_fit2.qc
SMD MODEL body.smd
Processing LOD for material: ct_urban
Processing LOD for material: ct_urban_glass
bones                  16320 bytes (61)
Completed "...\src\css_fit2\css_fit2.qc"          (exit 0, no warnings)
```

No errors needed resolving: every variant (CS:S, HL2, CS 1.6, static) compiled first time. The things that had to be
right are all in the generated QC:

- `$origin 0 0 0 -90`, because the SMDs are in compiled model space and studiomdl would otherwise turn them 90 degrees.
- `$definebone` angles in QAngle order (pitch = SMD y, yaw = z, roll = x). The generated lines equal
  `player_bones.qci`, e.g. `spine ... 0 0 96.894531`.
- `$includemodel` without the `models/` prefix.

`compare` against `mom_player.mdl` (`player-models/compare_css_ct_urban_fit.txt`):

```
bones: reference 61, port 61
rest pose: max position difference 0.0000, max rotation difference 0.040 deg, max poseToBone difference 0.0001
parent/missing problems: 0
includemodels: reference [models/player/mom_player_animations.mdl], port [models/player/mom_player_animations.mdl]
  attachment player_forward: bone spine.006 (reference spine.006), max matrix difference 0.0000
```

The compiled port was decompiled again and posed. It gives the same numbers as the pre-compile simulation, so the
renders show what studiomdl actually produced.

### How good is it? (no in-game check yet; see "Not verified")

`stats` measures edges stretched by more than 25%, and the p95 of edge stretch, for each port posed with Momentum's
animations (converted with `bake-anim`). Momentum's own body under the same frames is the baseline:

| Body | idle f0 | run_N f5 | crouch_walk f4 | hop f3 |
|---|---|---|---|---|
| Momentum `mom_player` (baseline) | 0.6% / 2.9% | 2.3% / 9.9% | 1.2% / 4.7% | 1.2% / 5.7% |
| CS:S `ct_urban`, **fit** | 0.9% / 9.5% | 7.8% / 35% | 5.9% / 29% | 2.9% / 19% |
| HL2 `male_07`, **fit** | 1.3% / 11% | 8.7% / 36% | 7.6% / 34% | 3.2% / 18% |
| CS:S `ct_urban`, rename | 20.5% / 128% | 22.7% / 137% | 22.5% / 135% | 20.6% / 130% |
| CS 1.6 `urban`, fit | 2.1% / 14% | 10.7% / 53% | 8.4% / 52% | |
| HL1 `gordon`, fit | 1.3% / 14% | 9.8% / 63% | 9.2% / 68% | |

The fit bodies stretch 3-5x more than Momentum's body, mostly at the chest/shoulders (`spine.003`), hips/thighs and
hands. Low-poly meshes look worse on this metric because their long edges cross joints. Visually (`renders/fit_v2.png`,
`posed_fit.png`, `hl2_fit.png`, `goldsrc_fit.png`) they read as the source character running, crouching and jumping
correctly.

Known shortcomings and their fixes, in order of value:

1. **Proportions become Momentum's.** Joints are moved, segments stay rigid, so where segment lengths differ the mesh
   overlaps or gaps at the joint. CS:S forearm 11.2 vs 8.7 means the sleeve slides into the hand; the neck is
   compressed to about half. Fixes: (a) scale each bone's vertices along its axis by the segment ratio, which is cheap
   and keeps the skeleton exact; or (b) keep the source proportions with the GMod "proportion trick": bind to
   Momentum's bone *orientations* at the source's joint positions and add
   `$animation a_prop prop.smd subtract a_ref 0` / `$sequence proportions a_prop predelta autoplay`, so the bone
   lengths are corrected at runtime. (b) is more faithful but depends on Momentum's client running autoplay
   sequences on players and ghosts, so it needs the in-game test.
2. **Limb twist isn't matched** (only the bone direction is), so palms/feet can be a few degrees off. Fix: also align
   a secondary axis (e.g. the elbow/knee bend plane) per chain.
3. **Fingers:** Momentum's anims curl the fingers, and the source rest finger shapes differ. Folding the fingers into
   the hand (delete their lines from the map) gives rigid mitts, which is a valid option for low-poly bodies.
4. **Materials:** the port keeps the source game's VMTs (`VertexLitGeneric`), so it won't get Momentum's player
   colour/speed proxies or ghost tint. The porter should write VMTs for the ported textures with the
   `MomPlayerColor*`/`MomPlayerSpeedBody` proxies (needs checking which shader parameters those proxies drive).

## Step 3: GoldSrc (CS 1.6, Half-Life 1)

```powershell
PlayerModelSpike goldsrc "...\Half-Life\cstrike\models\player\urban\urban.mdl" gs_urban
#   body part studio: 1 models, first has 790 triangles / 53 bones / 111 sequences
PlayerModelSpike goldsrc "...\Half-Life\valve\models\player\gordon\gordon.mdl" gs_gordon
#   362 triangles / 42 bones / 77 sequences
PlayerModelSpike retarget gs_urban\reference.smd docs\player-models\goldsrc_biped_to_momentum.txt mom_player.mdl gs_urban_fit.smd fit --scale auto
```

- The skeleton is 3ds Max Character Studio `Bip01 ...` with spaces in the names, which the map file quotes. There are
  **two naming generations**: CS 1.6 uses `Thigh/Calf`, `Clavicle/UpperArm/Forearm` plus 20 `-- L Knee`/`-- R Butt`
  style helper bones; HL1 uses `Leg/Leg1`, `Arm/Arm1/Arm2` plus `DummyNN`/`BoneNN` head helpers. One map file
  (`docs/player-models/goldsrc_biped_to_momentum.txt`) covers both. The helpers fold into their parents
  (`player-models/retarget_cs16_urban.log` lists each).
- GoldSrc vertices have exactly one bone, so the port is rigidly skinned, as it was in GoldSrc. CS 1.6 rests in a
  T-pose. The fit bends its arms about 70 degrees down without trouble (`renders/goldsrc_fit.png`).
- `cs16_fit` compiled cleanly: 61 bones, skeleton identical to Momentum's.
- Remaining work for 6c: textures. Use `GoldSrcMaterialConverter` (8-bit BMP with palette -> VTF/VMT, masked/additive
  flags) as the BSPConvert GoldSrc path already does. Also body parts: CS 1.6 `backpack` and HL1 heads become extra
  models or are dropped. GoldSrc's 111 sequences aren't needed, since Momentum's animations replace them.

## Quake 3

From `baseq3/pak0.pk3`:

| `sarge` | frames | tags | vertices | triangles |
|---|---|---|---|---|
| `lower.md3` | 191 | `tag_torso` | 363 | 336 |
| `upper.md3` | 153 | `tag_head`, `tag_weapon`, `tag_torso` | 328 | 342 |
| `head.md3` | 1 | `tag_head` | 108 | 117 |

`animation.cfg` maps frame ranges to `LEGS_RUN`, `TORSO_STAND` and so on. Each frame stores every vertex position,
there are no bones or weights, and the parts are joined at runtime by tags. Options:

- **Static ghost (MVP):** assemble `lower` frame 166 (`LEGS_IDLE`: cfg frame 229 minus the 63 torso-only frames
  that `lower.md3` skips) + `upper` frame 151 (`TORSO_STAND`) + `head` via the tags. Scale to about 71 units and turn to face +x. Bind every vertex rigidly to `rootTransform`, but keep
  Momentum's `$includemodel` and 61 `$definebone`s so every sequence and pose parameter the game asks for still exists.
- **Rigid three-part (recommended Q3 MVP):** the same assembly, but `lower` -> `spine`, `upper` -> `spine.003`,
  `head` -> `spine.006`. Torso lean, look and body yaw follow Momentum's animations; the legs stay a statue.
- **Full legs:** needs per-vertex weights the MD3 doesn't have. A proximity auto-rig (after the rigid fit, assign each
  `lower` vertex to the nearest of `thigh/shin/foot .L/.R` by side and height) would probably work on Q3's simple
  legs, but it's guesswork, so it's out of scope for now.
- Playing the MD3's own frames isn't an option: Source only does vertex animation through flexes, and Momentum's code
  drives sequences, not frames.

What's needed: an `Md3Model` reader (already planned for Phase 5's weapons) plus a tag-assembly step; the rest is the
same QC/compile path. TGA skins -> VTF.

## Games not tested

- **TF2** (`bip_pelvis`, `bip_spine_0..3`, `bip_hip_L`, `bip_collar_L`, `bip_upperArm_L`, ... plus many `prp_`/
  jiggle helpers, facial flexes): same fit approach with a third bone map. Flexes and `$proceduralbones` are lost.
  Couldn't be tried because the install has only `tf2_misc_000/003/018.vpk` and no `tf2_misc_dir.vpk`. The runtime is
  fine: Momentum mounts TF2.
- **CS:GO** (ValveBiped with extra helper/twist bones, v49): should take the ValveBiped map, with the extra helpers
  folding into their parents. Its install also lacks `pak01_dir.vpk`.

## Proposed pipeline (Phase 6b/6c)

```
input (.mdl from a VPK or loose, GoldSrc .mdl, Q3 .pk3)
  -> reference mesh SMD           Source: SourceModelDecompiler (mdl+vvd+vtx); GoldSrc: GoldSrcModel; Q3: Md3Model + tags
  -> bone map by skeleton family  ValveBiped | GoldSrc Biped | TF2 bip_ | (Q3: rigid parts) - detected from bone names
  -> fit onto mom_player.mdl      Retargeter.Fit (turn, scale, aim, place, bind, <= 3 weights)
  -> materials                    copy/convert textures, write VMTs with Momentum's player colour proxies
  -> QC                           QcWriter: $origin 0 0 0 -90, $includemodel, 61 $definebone, $bonemerge, attachments
  -> studiomdl (temp game dir)    Momentum's bin/win64/studiomdl.exe -game <temp> -nop4
  -> verify                       compare vs mom_player.mdl (skeleton/attachments) + render/stats report
  -> output                       momentum/custom/<pack>/models/player/mom_player.{mdl,vvd,dx90.vtx} + materials
```

Core pieces to move out of the spike: `StudioModelHeader` (done, in Core), `SourceModelDecompiler`, `Smd`, `BoneMap`,
`Retargeter`, `QcWriter`, and `compare` as a post-compile check. `Renderer`/`stats` can become the "preview" on the
Player Models page.

Manual steps that remain:

- Writing a bone map once per skeleton family (done for ValveBiped and GoldSrc; TF2 to do).
- Picking which body-group model to use (CS 1.6 backpack, HL2 heads).
- Judging the result in game.
- For odd custom rigs: adding `aim=`/`place=` lines.

## Not verified / open questions

- **No in-game or hlmv check.** The spike can't see the game window. To check, copy
  `%TEMP%\mp-player-spike\game\models\player\spike\css_fit2.{mdl,vvd,dx90.vtx}` to
  `momentum\custom\spike\models\player\mom_player.{mdl,vvd,dx90.vtx}` (with CS:S installed, the CS:S materials come
  from the mount). Then load a map, watch a replay ghost and `thirdperson`. Delete the folder to undo. `hlmv` can open
  the same files directly.
- Whether Momentum enforces model consistency for `models/player` in online lobbies, and whether online ghosts use the
  local override (the expectation: yes, client side).
- Whether the static variant without `$includemodel` (`css_static`, compiled OK) upsets game code that looks up
  sequences or pose parameters. The recommendation above keeps `$includemodel` even for static bodies to avoid that.
- Whether Momentum's client runs autoplay/predelta sequences on players and ghosts (only matters for the proportion
  trick).

## Reproduce

```powershell
cd tools\player-model-spike; dotnet build -c Release
$P = "tools\player-model-spike\bin\Release\net10.0\PlayerModelSpike.exe"
$M = "C:\Program Files (x86)\Steam\steamapps\common\Momentum Mod Playtest\momentum\models\player\mom_player.mdl"
$S = "C:\Program Files (x86)\Steam\steamapps\common\Momentum Mod Playtest\momentum\modelsrc\mom_player"
& $P extract "...\cstrike\cstrike_pak_dir.vpk" extracted models/player/ct_urban
& $P decompile extracted\models\player\ct_urban.mdl ct_urban_ref.smd
& $P retarget ct_urban_ref.smd docs\player-models\valvebiped_to_momentum.txt $M ct_urban_fit.smd fit --scale auto
& $P qc $M body.smd css_fit.qc --cdmaterials models/player/ct_urban/ --modelname player/spike/css_fit.mdl
& "...\bin\win64\studiomdl.exe" -game $env:TEMP\mp-player-spike\game -nop4 -nox360 css_fit.qc
& $P compare $M $env:TEMP\mp-player-spike\game\models\player\spike\css_fit.mdl
# simulation: Momentum's run as compiled, played on the port
& $P bake-anim $M "$S\mom_player_body.SMD" "$S\anims\run_N.smd" baked_run_N.smd
& $P pose ct_urban_fit.smd baked_run_N.smd 5 fit_run5.smd
& $P stats ct_urban_fit.smd fit_run5.smd
& $P render fit.png ct_urban_fit.smd fit_run5.smd
```
