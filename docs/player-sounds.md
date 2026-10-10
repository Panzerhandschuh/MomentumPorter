# Player sounds

Packs that give the player another game's jump, landing, footstep and sprint sounds. See the [README](../README.md) for an overview.

Builds a pack for Momentum's `custom` folder that gives the player another game's movement sounds:

```
MomentumPorter sounds [--game q3|css|csgo|tf2|hl2|cs16|hl1] [--character <name>] [--jump game|default|none]
                      [--land game|default|none] [--footsteps game|default|silent] [--sprint game|default|hl2|none] [--pure]
                      [--modes <mode>[,<mode>...]] [--output <folder>|auto] [--vpk|--folder] [--name <pack>] [--list]
```

| `--game` | Jump | Land | Footsteps |
|---|---|---|---|
| `q3` (default) | the character's `jump1.wav` | `land1.wav` soft, the character's `fall1.wav` hard | the character's set; metal clanks, water splashes |
| `css`, `tf2`, `hl2` | none | the default footsteps | the game's soundscript entries |
| `csgo` | none | `Land_Default.StepLeft` (`player/land*.wav`) | the team's (`--character ct` or `t`) |
| `cs16`, `hl1` | none | the concrete footsteps | the `pl_<type>` sets |

- **Jump** (`MomPlayer.Jump`) and **Land** (`MomPlayer.LandSoft`/`LandHard`): Momentum only plays these in Defrag modes
  unless `mom_movement_sound_jump`/`mom_movement_sound_land` are on; otherwise jumps and landings play the footstep, as
  in the Valve games. Games without a jump sound keep Momentum's (`--pure` mutes it). The Valve games play the footstep
  on landing, so `game` landings play their (concrete) footsteps.
- **Footsteps**: every surface's. Source games are matched by entry name (they share most of Momentum's, e.g.
  `SolidMetal.StepLeft`; CS:GO's are `CT_`/`T_` prefixed), then through the surfaces that use the entry in Momentum's
  `surfaceproperties` and what the same surface uses in the game's, then the game's `Default.StepLeft`/`StepRight`; the
  game's pitch and sound level come along. GoldSrc has no soundscripts: surfaces are matched by name to the
  `sound/player/pl_<type>N.wav` sets (step, metal, dirt, duct, grate, tile, slosh, wade, ladder, CS 1.6's snow), one foot
  playing 1 and 3 and the other 2 and 4, as GoldSrc does. For Quake 3 the set comes from the character's
  `animation.cfg` (normal, boot, energy, flesh or mech; `--footstep-type` overrides it). `--no-surface-footsteps` plays
  the default footsteps everywhere. `silent` mutes them and also writes `cfg/<pack>.cfg` with `snd_volume_footsteps 0`;
  run `exec <pack>` or add it to `cfg/autoexec.cfg`. The cvar is saved in your config, so set it back to 1 after removing
  the pack. Footsteps play at the volume Momentum's movement gives them (the engine ignores the entry's volume).
- **Sprint** (`MomPlayer.SprintStart`, played when sprint starts in Accelerated Hop; `mom_ahop_sprint_sound` turns it
  off): `game` gives HL2 its suit sprint sound (`HL2Player.SprintStart`, `player/suit_sprint.wav`) and keeps Momentum's
  for the other games; `hl2` uses HL2's with any game's sounds. HL2's comes from Momentum's own copy of HL2
  (`mount/hl2_dir.vpk` and its `scripts/game_sounds_player.txt`), so HL2 needn't be installed for it.
- **`--pure`**: also mutes what the game doesn't have: `MomPlayer.AirJump`, `PowerSlide`, `WallRun`, and the jump and
  sprint sounds for games without them (unless `--sprint` picks one).
- **`--modes`**: only use the sounds in these game modes (`surf`, `bhop`, `bhophl`, `kz`, `kzt`, `kz16`, `rj`, `sj`,
  `ahop`, `conc`, `df_cpm`, `df_vq3`, `df_vtg`), e.g. CS 1.6's in Climb (1.6) and Quake 3's in Defrag. Instead of
  replacing Momentum's soundscript, the pack has `scripts/<mode>/game_sounds_momentum_player.txt` for each mode with only
  the entries it changes, which the engine loads over Momentum's in that mode. That needs the
  `gamemode-weapon-scripts` engine change ([engine-changes.md](engine-changes.md)); other builds ignore it. The pack is
  named after the modes (`kz16_sounds`, `defrag_sounds` for all three Defrag modes), so building a mode's sounds again
  from another game replaces it. `--footsteps silent` writes no cfg for some modes, since `snd_volume_footsteps` is
  saved in your config and would mute every mode; the soundscript mutes them on its own.

The app always builds the pack that sounds most like the game: every sound set to `game`, surface footsteps on (the
Quake 3 character's own footstep type), `--pure` on, and a VPK named as above in Momentum's `custom` folder that
replaces the last build. It only asks for the game mode, the game and the Quake 3 character or CS:GO team: the game is
found through Steam and Momentum's folder comes from the Settings page. The game mode is **Every mode** or one mode as the
Weapons page offers them (the Defrag modes as one); picking a mode switches to the game it comes from (CS 1.6 for Climb
(1.6), Quake 3 for Defrag, TF2 for Rocket and Sticky Jump...), which can still be changed. The command line keeps every option above, including the
folder overrides.

The app's **Preview** lists what the pack would play for the chosen game and character: the jump, soft and hard
landings, sprint and the footsteps, with the surfaces that share each footstep set. Each click plays the next of a
sound's files (the files a footstep or landing picks from at random in game). Playback uses Windows' own player, so a
few Source game sounds stored in formats it lacks a codec for (e.g. MP3) can't be previewed, though Momentum plays them.

The games and Momentum are found through Steam (`--q3-dir`, `--game-dir`, `--momentum-dir` override that); `--list`
lists the games and the chosen game's characters or teams. HL2's footsteps are defined in the HL2 install's loose
`hl2/scripts` (Momentum's own HL2 mount has the WAVs but not those definitions), so HL2 has to be installed; CS:S and TF2
read HL2's footstep WAVs from their own `hl2` folder. Quake Live works too, but its footsteps are Ogg Vorbis, which
Momentum can't play, so those stay Momentum's. The pack goes to `momentum/custom/<game>_sounds[_<character>].vpk` (e.g.
`q3_sounds_ranger`, `csgo_sounds_ct`, `css_sounds`; built with Momentum's `vpk.exe`) unless `--output` names another
folder; delete it to get the default sounds back.

The pack holds a full copy of Momentum's `scripts/game_sounds_momentum_player.txt` (the soundscript manifest lives in the
base game, so the file is replaced whole) with only those entries changed, the WAVs they play, and the cfg. Quake 3's go
under `sound/player/<character>/`, `sound/player/q3/` and `sound/player/footsteps/q3/`, the other games' under
`sound/porter/<game>/` with their own paths (own folders, so no mounted game's sounds are replaced, and HL2's and CS:S's
same-named footsteps can't stand in for each other). WAVs Source can't play (rates other than 11025/22050/44100 Hz, more
than 16 bits, float) are re-encoded. A mode with its own pack uses it, and the every-mode pack (or Momentum's sounds)
for any entry it doesn't change. Only one pack can replace the soundscript (or a mode's), so the tool warns about other
`custom` folders that have their own copy (e.g. `custom/overrides/scripts/game_sounds_momentum_player.txt`). Folder packs are only
replaced if MomentumPorter wrote them (`momentumporter_pack.txt`).

In code, `PlayerSoundPackJob.Create(PlayerSoundPackOptions)` builds the job; `CustomPackWriter` (folder or VPK) and
`SoundscriptFile` (edit soundscripts keeping comments) are reusable for other asset packs.
