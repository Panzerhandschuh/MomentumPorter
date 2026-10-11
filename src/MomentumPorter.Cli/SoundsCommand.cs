namespace MomentumPorter.Cli;

using CommandLine;
using MomentumPorter.Assets.Output;
using MomentumPorter.Assets.Sounds;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;

[Verb("sounds", HelpText = "Build a custom pack with another game's player movement sounds (jump, land, footsteps)")]
internal sealed class SoundsCliOptions
{
    [Option('g', "game", HelpText = "Game whose sounds to use: q3, css, csgo, tf2, hl2, cs16 or hl1", Default = "q3")]
    public string Game { get; set; } = "q3";

    [Option("character", HelpText = "Quake 3 character whose sounds to use (ranger, sarge, ...; ranger by default), or CS:GO team (ct or t; ct by default). See --list.")]
    public string? Character { get; set; }

    [Option("jump", HelpText = "game (Quake 3: the character's jump1.wav; the other games have none and keep Momentum's), default (Momentum's) or none", Default = "game")]
    public string Jump { get; set; } = "game";

    [Option("land", HelpText = "game (Quake 3: land1.wav soft, the character's fall1.wav hard; CS:GO: its landing sound; the others: their footsteps), default or none", Default = "game")]
    public string Land { get; set; } = "game";

    [Option("footsteps", HelpText = "game (the game's on every surface), default or silent (also writes a cfg setting snd_volume_footsteps 0)", Default = "game")]
    public string Footsteps { get; set; } = "game";

    [Option("sprint", HelpText = "Accelerated Hop's sprint sound: game (HL2: its suit sprint; the others keep Momentum's), default (Momentum's), hl2 (HL2's with any game, from Momentum's copy of HL2) or none", Default = "game")]
    public string Sprint { get; set; } = "game";

    [Option("footstep-type", HelpText = "Quake 3: character (the character's own), normal, boot, energy, flesh or mech", Default = "character")]
    public string FootstepType { get; set; } = "character";

    [Option("no-surface-footsteps", HelpText = "Play the game's default (concrete) footsteps on every surface instead of its metal, dirt, water... ones")]
    public bool NoSurfaceFootsteps { get; set; }

    [Option("pure", HelpText = "Also mute the sounds the game doesn't have: air jump, power slide, wall run, and the jump and sprint sounds for games without them")]
    public bool Pure { get; set; }

    [Option('m', "modes", HelpText = "Game modes the sounds are used in (surf, bhop, bhophl, kz, kzt, kz16, rj, sj, ahop, conc, df_cpm, df_vq3, df_vtg), comma separated. Every mode by default.")]
    public string? Modes { get; set; }

    [Option("volume", HelpText = "Volume of the game's jump and land sounds, 0 to 1", Default = 0.35f)]
    public float Volume { get; set; } = 0.35f;

    [Option("name", HelpText = "Pack name. Defaults to <game>_sounds, plus the character or team (q3_sounds_ranger, csgo_sounds_ct), or with --modes to <modes>_sounds (kz16_sounds, defrag_sounds).")]
    public string? Name { get; set; }

    [Option('o', "output", HelpText = "Folder to write the pack to, or auto for Momentum's custom folder", Default = "auto")]
    public string Output { get; set; } = "auto";

    [Option("vpk", HelpText = "Write a .vpk (the default)")]
    public bool Vpk { get; set; }

    [Option("folder", HelpText = "Write a folder instead of a .vpk")]
    public bool Folder { get; set; }

    [Option("q3-dir", HelpText = "Quake 3's baseq3 folder or a pk3. Found through Steam by default (Quake 3, then Quake Live).")]
    public string? Quake3Path { get; set; }

    [Option("game-dir", HelpText = "The game's content folder (cstrike, csgo, tf, hl2, valve; for Quake 3, like --q3-dir). Found through Steam by default.")]
    public string? GameDirectory { get; set; }

    [Option("momentum-dir", HelpText = "Momentum Mod's install or momentum folder. Found through Steam by default.")]
    public string? MomentumDirectory { get; set; }

    [Option("list", HelpText = "List the games, and the game's characters or teams, and exit")]
    public bool List { get; set; }

    /// <summary>The folder given for the game's sounds: --q3-dir or --game-dir for Quake 3, --game-dir for the others.</summary>
    public string? GetGameDirectory(SourceGameInfo game) => game.Engine == ContentEngine.Quake3 ? Quake3Path ?? GameDirectory : GameDirectory;
}

/// <summary>
/// <c>MomentumPorter sounds ...</c>: builds a player sound pack.
/// </summary>
internal static class SoundsCommand
{
    public const string Verb = "sounds";

    public static int Run(string[] args)
    {
        return new Parser(with =>
            {
                with.HelpWriter = Console.Error;
                with.CaseSensitive = false;
            })
            .ParseArguments(args, typeof(SoundsCliOptions))
            .MapResult((SoundsCliOptions options) => RunAsync(options).GetAwaiter().GetResult(), _ => 1);
    }

    private static async Task<int> RunAsync(SoundsCliOptions cli)
    {
        if (cli.List)
            return List(cli);

        if (cli.Vpk && cli.Folder)
        {
            Console.Error.WriteLine("Use either --vpk or --folder");
            return 1;
        }

        PlayerSoundPackOptions options;
        try
        {
            SourceGameInfo game = PlayerSoundSources.Find(cli.Game)
                ?? throw new ArgumentException($"Unknown --game \"{cli.Game}\"; use {string.Join(", ", PlayerSoundSources.Games.Select(x => x.Id))}");
            options = new PlayerSoundPackOptions
            {
                Game = game.Id,
                Character = cli.Character,
                JumpSound = ParseMode(cli.Jump, "jump", ("game", JumpSoundMode.Game), ("default", JumpSoundMode.MomentumDefault), ("none", JumpSoundMode.None)),
                LandSound = ParseMode(cli.Land, "land", ("game", LandSoundMode.Game), ("default", LandSoundMode.MomentumDefault), ("none", LandSoundMode.None)),
                SprintSound = ParseMode(cli.Sprint, "sprint", ("game", SprintSoundMode.Game), ("default", SprintSoundMode.MomentumDefault), ("hl2", SprintSoundMode.HalfLife2), ("none", SprintSoundMode.None)),
                Footsteps = ParseMode(cli.Footsteps, "footsteps", ("game", FootstepMode.Game), ("default", FootstepMode.MomentumDefault), ("silent", FootstepMode.Silent)),
                FootstepType = Enum.TryParse(cli.FootstepType, ignoreCase: true, out Quake3FootstepType type)
                    ? type
                    : throw new ArgumentException($"Unknown --footstep-type \"{cli.FootstepType}\""),
                SurfaceFootsteps = !cli.NoSurfaceFootsteps,
                Pure = cli.Pure,
                GameModes = GameModes.ToList(GameModes.Parse(cli.Modes)),
                Volume = Math.Clamp(cli.Volume, 0, 1),
                PackName = cli.Name,
                OutputAsVpk = !cli.Folder,
                OutputDirectory = cli.Output.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : cli.Output,
                Quake3Path = game.Engine == ContentEngine.Quake3 ? cli.GetGameDirectory(game) : null,
                GameDirectory = game.Engine == ContentEngine.Quake3 ? null : cli.GameDirectory,
                MomentumDirectory = cli.MomentumDirectory,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        (PortJob job, PlayerSoundPackState state) = PlayerSoundPackJob.Create(options);
        string workDirectory = PortJob.CreateWorkDirectory(job.Name);
        try
        {
            var context = new PortJobContext { Log = ConsoleLog.Instance, WorkDirectory = workDirectory };
            context.Set(state);

            PortJobResult result = await job.RunAsync(context);
            result.Report.Print(Console.Out);
            if (!result.Succeeded)
                return 1;

            Console.WriteLine($"Pack written to {context.Get<CustomPackOutput>().Path}");
            if (state.Result?.ExecCommand is { } exec)
                Console.WriteLine($"Run \"{exec}\" in Momentum's console, or add it to cfg/autoexec.cfg, for silent footsteps");

            return 0;
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Leave it for the OS to clean up
            }
        }
    }

    private static int List(SoundsCliOptions cli)
    {
        Console.WriteLine("Games (--game):");
        foreach (SourceGameInfo each in PlayerSoundSources.Games)
            Console.WriteLine($"  {each.Id,-5} {each.Name}");

        SourceGameInfo? game = PlayerSoundSources.Find(cli.Game);
        if (game is null)
        {
            Console.Error.WriteLine($"Unknown --game \"{cli.Game}\"");
            return 1;
        }

        var steam = new SteamLocator();
        string? path = PlayerSoundSources.Locate(game, cli.GetGameDirectory(game), steam);
        if (path is null)
        {
            Console.Error.WriteLine($"{game.Name} wasn't found through Steam; pass --game-dir");
            return 1;
        }

        using PlayerSoundSource source = PlayerSoundSources.Open(game, path, steam);
        if (source.Characters.Count == 0)
        {
            Console.WriteLine($"{game.Name} ({source.Path}) has one set of sounds");
            return 0;
        }

        Console.WriteLine($"{char.ToUpperInvariant(source.CharacterKind[0])}{source.CharacterKind[1..]}s (--character) in {source.Path}:");
        foreach (string character in source.Characters)
        {
            Console.WriteLine(source is Quake3SoundSource quake3
                ? $"  {character,-10} footsteps: {Quake3SoundSource.FootstepSetName(quake3.GetFootstepType(character))}"
                : $"  {character}");
        }

        return 0;
    }

    private static T ParseMode<T>(string value, string option, params (string Name, T Mode)[] modes)
    {
        string name = value.Trim().ToLowerInvariant() switch
        {
            "game" or "quake3" or "q3" => "game",
            "momentum" or "momentumdefault" or "default" => "default",
            var other => other,
        };
        foreach ((string modeName, T mode) in modes)
        {
            if (modeName == name)
                return mode;
        }

        throw new ArgumentException($"Unknown --{option} \"{value}\"; use {string.Join(", ", modes.Select(x => x.Name))}");
    }
}
