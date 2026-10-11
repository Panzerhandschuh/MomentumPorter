namespace MomentumPorter.Assets.Sounds;

using System.Globalization;
using System.Text;
using MomentumPorter.Assets.Common;
using MomentumPorter.Assets.Output;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

/// <summary>
/// What a player sound pack build produced, beyond the files.
/// </summary>
/// <param name="Character">The Quake 3 character or CS:GO team, or null for games with one set of sounds</param>
/// <param name="ExecCommand">The console command that applies the pack's cfg (e.g. <c>exec q3_sounds_ranger</c>), or null if it has none</param>
public sealed record PlayerSoundPackResult(string PackName, string? Character, string? ExecCommand);

/// <summary>
/// Builds a custom pack that gives the player another game's movement sounds. Like the hand-made overrides it
/// automates, the pack ships a full copy of Momentum's scripts/game_sounds_momentum_player.txt with the movement and
/// footstep entries changed (the manifest that lists the file lives in the base game, so the file can only be replaced
/// whole), the WAVs those entries play, and optionally a cfg. A pack for some game modes instead ships only the changed
/// entries as each mode's overrides (<see cref="ModeSoundscriptPath"/>).
/// </summary>
public sealed class PlayerSoundPackBuilder(PlayerSoundSource source, IPortLog log, PortReport report)
{
    /// <summary>The soundscript with Momentum's movement sounds and every surface's footsteps.</summary>
    public const string SoundscriptPath = "scripts/game_sounds_momentum_player.txt";

    public const string Jump = "MomPlayer.Jump";
    public const string LandSoft = "MomPlayer.LandSoft";
    public const string LandHard = "MomPlayer.LandHard";
    public const string Sprint = "MomPlayer.SprintStart";

    /// <summary>
    /// A mode's overrides of <see cref="SoundscriptPath"/>, which the engine loads at level init in that mode.
    /// </summary>
    public static string ModeSoundscriptPath(GameMode mode) => $"scripts/{mode.Id}/game_sounds_momentum_player.txt";

    /// <summary>Momentum-only movement sounds the other games have nothing like, muted by Pure.</summary>
    public static readonly IReadOnlyList<string> MomentumOnlySounds = ["MomPlayer.AirJump", "MomPlayer.SprintStart", "MomPlayer.PowerSlide", "MomPlayer.WallRun"];

    // Pack-relative, in a folder of its own so it never replaces a mounted game's file
    private const string SilentWave = "player/q3/silent.wav";

    /// <summary>
    /// Adds the pack's files to <paramref name="pack"/>.
    /// </summary>
    /// <param name="soundscript">Momentum's game_sounds_momentum_player.txt; changed in place</param>
    /// <param name="surfaceFootsteps">The stepleft/stepright sounds surfaceproperties*.txt names, so footsteps it uses but the soundscript doesn't define get one too</param>
    /// <param name="halfLife2">Half-Life 2's sounds, for its sprint sound when the game isn't HL2 (<see cref="SourceGameSoundSource.OpenMomentumHalfLife2"/>)</param>
    public PlayerSoundPackResult Build(PlayerSoundPackOptions options, SoundscriptFile soundscript, CustomPackWriter pack, IEnumerable<string>? surfaceFootsteps = null, PlayerSoundSource? halfLife2 = null)
    {
        string? character = ResolveCharacter(options);
        string volume = options.Volume.ToString("0.###", CultureInfo.InvariantCulture);

        switch (options.JumpSound)
        {
            case JumpSoundMode.Game:
                if (source.GetJump(character) is { } jump)
                {
                    if (AddSound(jump, pack) is { } waves)
                        SetMovementSound(soundscript, Jump, waves, jump, "CHAN_VOICE", volume);
                }
                else if (options.Pure)
                {
                    Mute(soundscript, Jump, pack);
                }
                else
                {
                    report.Change($"{source.Game.Name} has no jump sound (it plays the footstep, as Momentum does), so {Jump} is Momentum's");
                }

                break;
            case JumpSoundMode.None:
                Mute(soundscript, Jump, pack);
                break;
        }

        switch (options.LandSound)
        {
            case LandSoundMode.Game:
            {
                (GameSound? soft, GameSound? hard) = source.GetLand(character);
                if (soft is not null && AddSound(soft, pack) is { } softWaves)
                    SetMovementSound(soundscript, LandSoft, softWaves, soft, "CHAN_AUTO", volume);
                if (hard is not null && AddSound(hard, pack) is { } hardWaves)
                    SetMovementSound(soundscript, LandHard, hardWaves, hard, "CHAN_AUTO", volume);
                if (soft is null && hard is null)
                    report.Change($"{source.Game.Name} has no landing sounds, so Momentum's are kept");
                break;
            }
            case LandSoundMode.None:
                Mute(soundscript, LandSoft, pack);
                Mute(soundscript, LandHard, pack);
                break;
        }

        bool sprintSet = SetSprint(options, soundscript, pack, halfLife2);

        IReadOnlyList<GameMode> modes = options.ResolveGameModes();
        string? execCommand = null;
        switch (options.Footsteps)
        {
            case FootstepMode.Game:
                SetFootsteps(options, character, soundscript, pack, surfaceFootsteps);
                break;
            case FootstepMode.Silent:
            {
                int count = 0;
                foreach (SoundscriptEntry entry in FootstepEntries(soundscript, surfaceFootsteps))
                {
                    entry.SetWaves([SilentWave]);
                    count++;
                }

                AddSilentWave(pack);
                report.Change("footstep sounds muted", count);

                // snd_volume_footsteps is saved in the player's config, so it would mute every mode
                if (modes.Count > 0)
                    break;

                // The game's own footstep volume too, so footsteps stay silent whatever soundscript ends up loaded
                string packName = pack.Name;
                pack.AddText($"cfg/{packName}.cfg", CfgText(packName));
                execCommand = $"exec {packName}";
                report.Change($"cfg/{packName}.cfg sets snd_volume_footsteps 0; run \"{execCommand}\" or add it to cfg/autoexec.cfg");
                break;
            }
        }

        if (options.Pure)
        {
            // The sprint sound only when the game has none and it wasn't chosen
            foreach (string name in MomentumOnlySounds.Where(x => x != Sprint || (!sprintSet && options.SprintSound == SprintSoundMode.Game)))
                Mute(soundscript, name, pack);
        }

        if (modes.Count == 0)
        {
            pack.AddText(SoundscriptPath, soundscript.ToText());
        }
        else
        {
            // Only the changed entries, so the ones left as Momentum's keep following Momentum's own file when it's updated
            string overrides = soundscript.ToChangedText(
            [
                $"Made by MomentumPorter: {source.Game.Name}'s player sounds for {GameModes.Describe(modes)}.",
                $"Loaded over {SoundscriptPath} in this mode only. Each entry replaces Momentum's whole entry.",
            ]);
            foreach (GameMode mode in modes)
                pack.AddText(ModeSoundscriptPath(mode), overrides);
            report.Change($"sounds only for {GameModes.Describe(modes)}: {string.Join(", ", modes.Select(ModeSoundscriptPath))}");
        }

        log.Info($"Built {pack.Name}: {pack.Files.Count} files");
        return new PlayerSoundPackResult(pack.Name, character, execCommand);
    }

    /// <summary>
    /// Every footstep sound: the soundscript's <c>*.StepLeft</c>/<c>*.StepRight</c> entries (and the like, e.g.
    /// <c>Ladder.WoodStepLeft</c>), plus entries added for any the surfaces use that it doesn't define.
    /// </summary>
    public IEnumerable<SoundscriptEntry> FootstepEntries(SoundscriptFile soundscript, IEnumerable<string>? surfaceFootsteps)
    {
        var entries = soundscript.Entries.Where(x => IsFootstep(x.Name) && soundscript.Find(x.Name) == x).ToList();
        if (surfaceFootsteps is not null)
        {
            SoundscriptEntry? template = soundscript.Find("Default.StepLeft");
            foreach (string name in surfaceFootsteps.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (soundscript.Find(name) is not null)
                    continue;

                SoundscriptEntry added = template is null ? soundscript.Add(name) : soundscript.AddCopy(name, template);
                report.Change("footstep sound a surface uses but Momentum doesn't define, added");
                entries.Add(added);
            }
        }

        return entries;
    }

    public static bool IsFootstep(string name)
    {
        return name.EndsWith("StepLeft", StringComparison.OrdinalIgnoreCase) || name.EndsWith("StepRight", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sets Accelerated Hop's sprint sound. Returns whether it was set to something other than Momentum's.</summary>
    private bool SetSprint(PlayerSoundPackOptions options, SoundscriptFile soundscript, CustomPackWriter pack, PlayerSoundSource? halfLife2)
    {
        PlayerSoundSource? from;
        switch (options.SprintSound)
        {
            case SprintSoundMode.None:
                Mute(soundscript, Sprint, pack);
                return true;
            case SprintSoundMode.Game:
                from = source;
                break;
            case SprintSoundMode.HalfLife2:
                from = source.Game.Id == SourceGames.HalfLife2.Id ? source : halfLife2;
                if (from is null)
                {
                    report.Warn($"Half-Life 2's sprint sound wasn't found (Momentum's mount/hl2_dir.vpk), so {Sprint} is Momentum's");
                    return false;
                }

                break;
            default:
                return false;
        }

        if (from.GetSprint() is not { } sprint)
        {
            if (options.SprintSound == SprintSoundMode.HalfLife2)
                report.Warn($"{from.Path} has no HL2Player.SprintStart, so {Sprint} is Momentum's");
            return false;
        }

        if (AddSound(sprint, pack, from) is not { } waves)
            return false;

        // At the game's volume: the option's volume is for jumps and landings
        SetMovementSound(soundscript, Sprint, waves, sprint, "CHAN_VOICE", sprint.Volume ?? "0.9");
        if (sprint.SoundLevel is not null)
            soundscript.Find(Sprint)!.SoundLevel = sprint.SoundLevel;
        return true;
    }

    private string? ResolveCharacter(PlayerSoundPackOptions options)
    {
        if (source.Characters.Count == 0)
            return null;

        string character = options.ResolveCharacter() ?? source.DefaultCharacter ?? source.Characters[0];
        if (!source.Characters.Contains(character, StringComparer.OrdinalIgnoreCase))
            throw new PortStepException($"{source.Path} has no {source.CharacterKind} \"{options.Character}\". {char.ToUpperInvariant(source.CharacterKind[0])}{source.CharacterKind[1..]}s: {string.Join(", ", source.Characters)}");

        return character.ToLowerInvariant();
    }

    private void SetFootsteps(PlayerSoundPackOptions options, string? character, SoundscriptFile soundscript, CustomPackWriter pack, IEnumerable<string>? surfaceFootsteps)
    {
        if (source.DescribeFootsteps(character, options) is { } description)
            log.Info(description);

        // A sound whose files couldn't be read isn't tried (and warned about) again for the next entry
        var added = new Dictionary<string, IReadOnlyList<string>?>();
        foreach (SoundscriptEntry entry in FootstepEntries(soundscript, surfaceFootsteps))
        {
            GameSound? used = null;
            IReadOnlyList<string>? waves = null;
            foreach (GameSound sound in source.GetFootsteps(entry.Name, character, options))
            {
                string key = string.Join('|', sound.Files.Select(x => x.Source));
                if (!added.TryGetValue(key, out waves))
                    added[key] = waves = AddSound(sound, pack);

                if (waves is not null)
                {
                    used = sound;
                    break;
                }
            }

            if (used is null || waves is null)
            {
                report.Warn($"no {source.Game.Name} footsteps found for {entry.Name}, left as Momentum's");
                continue;
            }

            entry.SetWaves(waves);
            // The engine plays footsteps at the volume the movement gives them; the pitch and level are the game's
            entry.Volume = "1.0";
            entry.Pitch = used.Pitch ?? "PITCH_NORM";
            if (used.SoundLevel is not null)
                entry.SoundLevel = used.SoundLevel;
            // Most Source game footsteps have Momentum's names (CS:GO's with a team prefix); only the others are listed
            bool sameName = used.Label.EndsWith(entry.Name, StringComparison.OrdinalIgnoreCase)
                && (used.Label.Length == entry.Name.Length || used.Label[^(entry.Name.Length + 1)] == '_');
            report.Change(sameName ? $"footstep sounds -> {source.Game.Name}'s of the same name" : $"footstep sounds -> {source.Game.Name} {used.Label}");
        }
    }

    private void SetMovementSound(SoundscriptFile soundscript, string name, IReadOnlyList<string> waves, GameSound sound, string channel, string volume)
    {
        SoundscriptEntry entry = FindOrAdd(soundscript, name);
        entry.Channel = channel;
        entry.Volume = volume;
        entry.SoundLevel = "SNDLVL_60dB";
        entry.Pitch = sound.Pitch;
        entry.SetWaves(waves);
        report.Change($"{name} -> {(waves.Count == 1 ? waves[0] : $"{source.Game.Name} {sound.Label}")}");
    }

    private void Mute(SoundscriptFile soundscript, string name, CustomPackWriter pack)
    {
        SoundscriptEntry entry = FindOrAdd(soundscript, name);
        entry.SetWaves([SilentWave]);
        AddSilentWave(pack);
        report.Change($"{name} muted");
    }

    private SoundscriptEntry FindOrAdd(SoundscriptFile soundscript, string name)
    {
        SoundscriptEntry? entry = soundscript.Find(name);
        if (entry is not null)
            return entry;

        report.Warn($"Momentum's soundscript has no {name}; added it");
        entry = soundscript.Add(name);
        entry.Channel = "CHAN_AUTO";
        entry.SoundLevel = "SNDLVL_NORM";
        return entry;
    }

    private static void AddSilentWave(CustomPackWriter pack)
    {
        if (!pack.Contains("sound/" + SilentWave))
            pack.AddFile("sound/" + SilentWave, SilentWav.Bytes);
    }

    /// <summary>
    /// Copies a sound's files into the pack. Returns the waves for soundscripts, or null if none of its files could be
    /// read (each one that couldn't is warned about).
    /// </summary>
    private IReadOnlyList<string>? AddSound(GameSound sound, CustomPackWriter pack, PlayerSoundSource? from = null)
    {
        var waves = new List<string>();
        foreach (GameSoundFile file in sound.Files)
        {
            if (AddFile(file, pack, from ?? source))
                waves.Add(file.Wave);
        }

        return waves.Count > 0 ? waves : null;
    }

    /// <summary>
    /// Copies a game's sound into the pack as sound/&lt;wave&gt;, re-encoding it if Source can't play it. Returns false
    /// (with a warning) if it's missing or unreadable.
    /// </summary>
    private bool AddFile(GameSoundFile file, CustomPackWriter pack, PlayerSoundSource from)
    {
        string packPath = "sound/" + file.Wave;
        if (pack.Contains(packPath))
            return true;

        byte[]? bytes = from.ReadSound(file.Source, out string? problem);
        if (bytes is null)
        {
            report.Warn(problem ?? $"{file.Source} isn't in {System.IO.Path.GetFileName(from.Path.TrimEnd('/', '\\'))}");
            return false;
        }

        if (!WavFile.TryParse(bytes, out WavFile? wav, out string? error))
        {
            // A Source game's own sounds play in Momentum whatever they are (ADPCM WAVs, MP3s)
            if (from.Game.Engine != ContentEngine.Source)
            {
                report.Warn($"{file.Source} can't be read ({error})");
                return false;
            }

            log.Info($"Copied {file.Source} as it is ({error})");
        }
        else if (!wav.IsSourceCompatible)
        {
            WavFile converted = wav.ToSourceCompatible();
            report.Change($"re-encoded for Source: {wav.Describe()} -> {converted.Describe()}");
            log.Info($"Re-encoded {file.Source} ({wav.Describe()} -> {converted.Describe()})");
            bytes = converted.ToBytes();
        }

        pack.AddFile(packPath, bytes);
        return true;
    }

    private static string CfgText(string packName)
    {
        var text = new StringBuilder();
        text.AppendLine($"// Made by MomentumPorter for the {packName} pack: silent footsteps.");
        text.AppendLine($"// Run \"exec {packName}\" in the console or add that line to cfg/autoexec.cfg.");
        text.AppendLine("// snd_volume_footsteps is saved in your config, so set it back (default 1) after removing the pack.");
        text.AppendLine("snd_volume_footsteps 0");
        return text.ToString();
    }
}
