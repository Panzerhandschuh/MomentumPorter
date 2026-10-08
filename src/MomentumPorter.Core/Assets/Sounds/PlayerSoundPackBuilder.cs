namespace MomentumPorter.Assets.Sounds;

using System.Globalization;
using System.Text;
using MomentumPorter.Assets.Common;
using MomentumPorter.Assets.Output;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

/// <summary>
/// What a player sound pack build produced, beyond the files.
/// </summary>
/// <param name="ExecCommand">The console command that applies the pack's cfg (e.g. <c>exec q3_sounds_ranger</c>), or null if it has none</param>
public sealed record PlayerSoundPackResult(string PackName, string Character, string? ExecCommand);

/// <summary>
/// Builds a custom pack that gives the player Quake 3's movement sounds. Like the hand-made overrides it automates,
/// the pack ships a full copy of Momentum's scripts/game_sounds_momentum_player.txt with the movement and footstep
/// entries changed (the manifest that lists the file lives in the base game, so the file can only be replaced
/// whole), the WAVs those entries play, and optionally a cfg.
/// </summary>
public sealed class PlayerSoundPackBuilder(Quake3SoundSource quake3, IPortLog log, PortReport report)
{
    /// <summary>The soundscript with Momentum's movement sounds and every surface's footsteps.</summary>
    public const string SoundscriptPath = "scripts/game_sounds_momentum_player.txt";

    public const string Jump = "MomPlayer.Jump";
    public const string LandSoft = "MomPlayer.LandSoft";
    public const string LandHard = "MomPlayer.LandHard";

    /// <summary>Momentum-only movement sounds Quake 3 has nothing like, muted by Pure Quake 3.</summary>
    public static readonly IReadOnlyList<string> NonQuake3Sounds = ["MomPlayer.AirJump", "MomPlayer.SprintStart", "MomPlayer.PowerSlide", "MomPlayer.WallRun"];

    // Pack-relative folders. Shared Q3 sounds get their own folders so they never replace a mounted game's files.
    private const string SharedFolder = "player/q3";
    private const string FootstepFolder = "player/footsteps/q3";
    private const string SilentWave = SharedFolder + "/silent.wav";

    /// <summary>
    /// Adds the pack's files to <paramref name="pack"/>.
    /// </summary>
    /// <param name="soundscript">Momentum's game_sounds_momentum_player.txt; changed in place</param>
    /// <param name="surfaceFootsteps">The stepleft/stepright sounds surfaceproperties*.txt names, so footsteps it uses but the soundscript doesn't define get one too</param>
    public PlayerSoundPackResult Build(PlayerSoundPackOptions options, SoundscriptFile soundscript, CustomPackWriter pack, IEnumerable<string>? surfaceFootsteps = null)
    {
        string character = options.Character.Trim().ToLowerInvariant();
        if (!quake3.HasCharacter(character))
            throw new PortStepException($"{quake3.Path} has no character \"{options.Character}\". Characters: {string.Join(", ", quake3.Characters)}");

        string volume = options.Volume.ToString("0.###", CultureInfo.InvariantCulture);

        if (options.JumpSound == JumpSoundMode.Quake3)
        {
            string? wave = AddQuake3Sound($"{Quake3SoundSource.PlayerSoundFolder}/{character}/jump1.wav", $"player/{character}/jump1.wav", pack);
            if (wave is not null)
                SetMovementSound(soundscript, Jump, wave, "CHAN_VOICE", volume);
        }

        switch (options.LandSound)
        {
            case LandSoundMode.Quake3:
                if (AddQuake3Sound($"{Quake3SoundSource.PlayerSoundFolder}/land1.wav", $"{SharedFolder}/land1.wav", pack) is { } soft)
                    SetMovementSound(soundscript, LandSoft, soft, "CHAN_AUTO", volume);
                if (AddQuake3Sound($"{Quake3SoundSource.PlayerSoundFolder}/{character}/fall1.wav", $"player/{character}/fall1.wav", pack) is { } hard)
                    SetMovementSound(soundscript, LandHard, hard, "CHAN_AUTO", volume);
                break;
            case LandSoundMode.None:
                Mute(soundscript, LandSoft, pack);
                Mute(soundscript, LandHard, pack);
                break;
        }

        string? execCommand = null;
        switch (options.Footsteps)
        {
            case FootstepMode.Quake3:
                SetQuake3Footsteps(options, character, soundscript, pack, surfaceFootsteps);
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

                // The game's own footstep volume too, so footsteps stay silent whatever soundscript ends up loaded
                string packName = pack.Name;
                pack.AddText($"cfg/{packName}.cfg", CfgText(packName));
                execCommand = $"exec {packName}";
                report.Change($"cfg/{packName}.cfg sets snd_volume_footsteps 0; run \"{execCommand}\" or add it to cfg/autoexec.cfg");
                break;
            }
        }

        if (options.PureQuake3)
        {
            foreach (string name in NonQuake3Sounds)
                Mute(soundscript, name, pack);
        }

        pack.AddText(SoundscriptPath, soundscript.ToText());
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

    /// <summary>
    /// The Quake 3 footstep set a Momentum footstep sound gets: metal clanks and water splashes (Quake 3's surface
    /// footsteps), flesh is flesh, and everything else uses the character's set.
    /// </summary>
    public static string ClassifyFootstep(string soundName, string characterSet, bool surfaceFootsteps)
    {
        if (!surfaceFootsteps)
            return characterSet;

        string surface = soundName.Split('.')[0].ToLowerInvariant();
        bool woodLadder = soundName.Contains("wood", StringComparison.OrdinalIgnoreCase);
        if (surface.Contains("metal") || surface.Contains("grate") || surface.Contains("vent") || surface.Contains("chainlink")
            || surface is "grenade" or "weapon" || (surface == "ladder" && !woodLadder))
            return "clank";

        if (surface.Contains("water") || surface.Contains("wade") || surface.Contains("slosh"))
            return "splash";

        if (surface.Contains("flesh"))
            return "flesh";

        return characterSet;
    }

    public static string FootstepSetName(Quake3FootstepType type)
    {
        return type switch
        {
            Quake3FootstepType.Boot => "boot",
            Quake3FootstepType.Energy => "energy",
            Quake3FootstepType.Flesh => "flesh",
            Quake3FootstepType.Mech => "mech",
            _ => "step",
        };
    }

    private void SetQuake3Footsteps(PlayerSoundPackOptions options, string character, SoundscriptFile soundscript, CustomPackWriter pack, IEnumerable<string>? surfaceFootsteps)
    {
        Quake3FootstepType type = options.FootstepType == Quake3FootstepType.Character ? quake3.GetFootstepType(character) : options.FootstepType;
        string characterSet = FootstepSetName(type);
        log.Info($"Footsteps: {characterSet}{(options.FootstepType == Quake3FootstepType.Character ? $" ({character}'s)" : "")}");

        // Each set is four files (step1..step4); the engine picks one at random each step, as Quake 3 does
        var sets = new Dictionary<string, List<string>?>();
        List<string>? GetSet(string set)
        {
            if (sets.TryGetValue(set, out List<string>? waves))
                return waves;

            waves = [];
            for (int i = 1; i <= 4; i++)
            {
                if (AddQuake3Sound($"{Quake3SoundSource.FootstepFolder}/{set}{i}.wav", $"{FootstepFolder}/{set}{i}.wav", pack) is { } wave)
                    waves.Add(wave);
            }

            sets[set] = waves.Count > 0 ? waves : null;
            return sets[set];
        }

        foreach (SoundscriptEntry entry in FootstepEntries(soundscript, surfaceFootsteps))
        {
            string set = ClassifyFootstep(entry.Name, characterSet, options.SurfaceFootsteps);
            List<string>? waves = GetSet(set) ?? GetSet(characterSet);
            if (waves is null)
            {
                report.Warn($"no Quake 3 \"{set}\" footsteps found, {entry.Name} left as Momentum's");
                continue;
            }

            entry.SetWaves(waves);
            // Quake 3's footsteps are the same loudness and pitch everywhere
            entry.Volume = "1.0";
            entry.Pitch = "PITCH_NORM";
            report.Change($"footstep sounds -> Quake 3 {set}");
        }
    }

    private void SetMovementSound(SoundscriptFile soundscript, string name, string wave, string channel, string volume)
    {
        SoundscriptEntry entry = FindOrAdd(soundscript, name);
        entry.Channel = channel;
        entry.Volume = volume;
        entry.SoundLevel = "SNDLVL_60dB";
        entry.Pitch = null;
        entry.SetWaves([wave]);
        report.Change($"{name} -> {wave}");
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
    /// Copies a Quake 3 sound into the pack as sound/<paramref name="wave"/>, re-encoding it if Source can't play it.
    /// Returns the wave path for soundscripts, or null (with a warning) if it's missing or unreadable.
    /// </summary>
    private string? AddQuake3Sound(string quake3Path, string wave, CustomPackWriter pack)
    {
        string packPath = "sound/" + wave;
        if (pack.Contains(packPath))
            return wave;

        byte[]? bytes = quake3.ReadSound(quake3Path, out bool foundOgg);
        if (bytes is null)
        {
            report.Warn(foundOgg
                ? $"{quake3Path} is only there as Ogg Vorbis, which Momentum can't play (Quake Live); use Quake 3's pk3s for it"
                : $"{quake3Path} isn't in {System.IO.Path.GetFileName(quake3.Path)}");
            return null;
        }

        if (!WavFile.TryParse(bytes, out WavFile? wav, out string? error))
        {
            report.Warn($"{quake3Path} can't be read ({error})");
            return null;
        }

        if (!wav.IsSourceCompatible)
        {
            WavFile converted = wav.ToSourceCompatible();
            report.Change($"re-encoded for Source: {wav.Describe()} -> {converted.Describe()}");
            log.Info($"Re-encoded {quake3Path} ({wav.Describe()} -> {converted.Describe()})");
            bytes = converted.ToBytes();
        }

        pack.AddFile(packPath, bytes);
        return wave;
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
