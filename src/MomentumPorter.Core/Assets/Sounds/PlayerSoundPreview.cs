namespace MomentumPorter.Assets.Sounds;

/// <summary>
/// One of Momentum's movement sounds and the game's sound a pack would give it, to listen to before building.
/// </summary>
/// <param name="Name">The Momentum sound: Jump, Soft landing, Footsteps...</param>
/// <param name="Detail">More about where it's played: the surfaces footsteps are for, or null</param>
public sealed record PreviewSound(string Name, string? Detail, GameSound Sound);

/// <summary>
/// Lists the sounds a player sound pack would use, resolved the way <see cref="PlayerSoundPackBuilder"/> resolves them
/// (the first of the game's candidates whose files can be read), so what's heard is what the pack gets.
/// </summary>
public static class PlayerSoundPreview
{
    /// <summary>Surfaces whose footsteps the preview lists, with Momentum's footstep entry for each.</summary>
    public static readonly IReadOnlyList<(string Surface, string Entry)> FootstepSurfaces =
    [
        ("concrete", "Concrete.StepLeft"),
        ("default", "Default.StepLeft"),
        ("metal", "SolidMetal.StepLeft"),
        ("metal grate", "Metalgrate.StepLeft"),
        ("vent", "MetalVent.StepLeft"),
        ("chain-link", "ChainLink.StepLeft"),
        ("wood", "Wood.StepLeft"),
        ("tile", "Tile.StepLeft"),
        ("glass", "Glass.StepLeft"),
        ("dirt", "Dirt.StepLeft"),
        ("mud", "Mud.StepLeft"),
        ("grass", "Grass.StepLeft"),
        ("gravel", "Gravel.StepLeft"),
        ("sand", "Sand.StepLeft"),
        ("snow", "Snow.StepLeft"),
        ("slime", "SlipperySlime.StepLeft"),
        ("shallow water", "Water.StepLeft"),
        ("wading", "Wade.StepLeft"),
        ("ladder", "Ladder.StepLeft"),
        ("wooden ladder", "Ladder.WoodStepLeft"),
    ];

    /// <summary>
    /// The game's jump, landing, sprint and footstep sounds for the options, in that order. Footsteps that sound the
    /// same on several surfaces are one entry listing them. Sounds the game doesn't have, or whose files can't be
    /// read, are left out.
    /// </summary>
    public static IReadOnlyList<PreviewSound> List(PlayerSoundSource source, string? character, PlayerSoundPackOptions options)
    {
        var sounds = new List<PreviewSound>();
        if (source.Characters.Count > 0)
            character ??= source.DefaultCharacter ?? source.Characters[0];

        if (options.JumpSound == JumpSoundMode.Game && source.GetJump(character) is { } jump && CanRead(source, jump))
            sounds.Add(new PreviewSound("Jump", null, jump));

        if (options.LandSound == LandSoundMode.Game)
        {
            (GameSound? soft, GameSound? hard) = source.GetLand(character);
            if (soft is not null && CanRead(source, soft))
                sounds.Add(new PreviewSound("Soft landing", null, soft));
            if (hard is not null && CanRead(source, hard))
                sounds.Add(new PreviewSound("Hard landing", null, hard));
        }

        if (options.SprintSound == SprintSoundMode.Game && source.GetSprint() is { } sprint && CanRead(source, sprint))
            sounds.Add(new PreviewSound("Sprint", "Accelerated Hop", sprint));

        if (options.Footsteps == FootstepMode.Game)
        {
            // One entry per distinct set of files, in the order the surfaces first use it
            var footsteps = new List<(GameSound Sound, List<string> Surfaces)>();
            foreach ((string surface, string entry) in FootstepSurfaces)
            {
                GameSound? sound = source.GetFootsteps(entry, character, options).FirstOrDefault(x => CanRead(source, x));
                if (sound is null)
                    continue;

                string key = FilesKey(sound);
                int existing = footsteps.FindIndex(x => FilesKey(x.Sound) == key);
                if (existing >= 0)
                    footsteps[existing].Surfaces.Add(surface);
                else
                    footsteps.Add((sound, [surface]));
            }

            foreach ((GameSound sound, List<string> surfaces) in footsteps)
                sounds.Add(new PreviewSound("Footsteps", char.ToUpperInvariant(surfaces[0][0]) + string.Join(", ", surfaces)[1..], sound));
        }

        return sounds;
    }

    private static bool CanRead(PlayerSoundSource source, GameSound sound) => sound.Files.Any(x => source.ReadSound(x.Source, out _) is not null);

    private static string FilesKey(GameSound sound) => string.Join('|', sound.Files.Select(x => x.Source.ToLowerInvariant()));
}
