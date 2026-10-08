namespace MomentumPorter.Cli;

using CommandLine;
using MomentumPorter.Assets.Output;
using MomentumPorter.Assets.Sounds;
using MomentumPorter.Pipeline;

[Verb("sounds", HelpText = "Build a custom pack with Quake 3's player movement sounds (jump, land, footsteps)")]
internal sealed class SoundsCliOptions
{
    [Option("character", HelpText = "Quake 3 character whose sounds to use (ranger, sarge, ...). See --list.", Default = "ranger")]
    public string Character { get; set; } = "ranger";

    [Option("jump", HelpText = "q3 (character's jump1.wav) or default (Momentum's)", Default = "q3")]
    public string Jump { get; set; } = "q3";

    [Option("land", HelpText = "q3 (land1.wav soft, character's fall1.wav hard), default or none", Default = "q3")]
    public string Land { get; set; } = "q3";

    [Option("footsteps", HelpText = "q3 (Quake 3's on every surface), default or silent (also writes a cfg setting snd_volume_footsteps 0)", Default = "q3")]
    public string Footsteps { get; set; } = "q3";

    [Option("footstep-type", HelpText = "character (the character's own), normal, boot, energy, flesh or mech", Default = "character")]
    public string FootstepType { get; set; } = "character";

    [Option("no-surface-footsteps", HelpText = "Don't use Quake 3's metal (clank) and water (splash) footsteps on metal and water surfaces")]
    public bool NoSurfaceFootsteps { get; set; }

    [Option("pure", HelpText = "Pure Quake 3: also mute air jump, sprint, power slide and wall run sounds")]
    public bool Pure { get; set; }

    [Option("volume", HelpText = "Volume of the Quake 3 jump and land sounds, 0 to 1", Default = 0.35f)]
    public float Volume { get; set; } = 0.35f;

    [Option("name", HelpText = "Pack name. Defaults to q3_sounds_<character>.")]
    public string? Name { get; set; }

    [Option('o', "output", HelpText = "Folder to write the pack to, or auto for Momentum's custom folder", Default = "auto")]
    public string Output { get; set; } = "auto";

    [Option("vpk", HelpText = "Write a .vpk (the default)")]
    public bool Vpk { get; set; }

    [Option("folder", HelpText = "Write a folder instead of a .vpk")]
    public bool Folder { get; set; }

    [Option("q3-dir", HelpText = "Quake 3's baseq3 folder or a pk3. Found through Steam by default (Quake 3, then Quake Live).")]
    public string? Quake3Path { get; set; }

    [Option("momentum-dir", HelpText = "Momentum Mod's install or momentum folder. Found through Steam by default.")]
    public string? MomentumDirectory { get; set; }

    [Option("list", HelpText = "List the Quake 3 characters and exit")]
    public bool List { get; set; }
}

/// <summary>
/// <c>MomentumPorter sounds ...</c>: builds a Quake 3 player sound pack.
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
            return ListCharacters(cli.Quake3Path);

        if (cli.Vpk && cli.Folder)
        {
            Console.Error.WriteLine("Use either --vpk or --folder");
            return 1;
        }

        PlayerSoundPackOptions options;
        try
        {
            options = new PlayerSoundPackOptions
            {
                Character = cli.Character,
                JumpSound = ParseMode(cli.Jump, "jump", ("q3", JumpSoundMode.Quake3), ("default", JumpSoundMode.MomentumDefault)),
                LandSound = ParseMode(cli.Land, "land", ("q3", LandSoundMode.Quake3), ("default", LandSoundMode.MomentumDefault), ("none", LandSoundMode.None)),
                Footsteps = ParseMode(cli.Footsteps, "footsteps", ("q3", FootstepMode.Quake3), ("default", FootstepMode.MomentumDefault), ("silent", FootstepMode.Silent)),
                FootstepType = Enum.TryParse(cli.FootstepType, ignoreCase: true, out Quake3FootstepType type)
                    ? type
                    : throw new ArgumentException($"Unknown --footstep-type \"{cli.FootstepType}\""),
                SurfaceFootsteps = !cli.NoSurfaceFootsteps,
                PureQuake3 = cli.Pure,
                Volume = Math.Clamp(cli.Volume, 0, 1),
                PackName = cli.Name,
                OutputAsVpk = !cli.Folder,
                OutputDirectory = cli.Output.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : cli.Output,
                Quake3Path = cli.Quake3Path,
                MomentumDirectory = cli.MomentumDirectory,
            };
        }
        catch (ArgumentException ex)
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

    private static int ListCharacters(string? quake3Path)
    {
        string? path = quake3Path ?? Quake3SoundSource.FindInstalled();
        if (path is null)
        {
            Console.Error.WriteLine("Quake 3 wasn't found through Steam; pass --q3-dir");
            return 1;
        }

        using Quake3SoundSource source = Quake3SoundSource.Open(path);
        Console.WriteLine($"Characters in {source.Path}:");
        foreach (string character in source.Characters)
            Console.WriteLine($"  {character,-10} footsteps: {PlayerSoundPackBuilder.FootstepSetName(source.GetFootstepType(character))}");

        return 0;
    }

    private static T ParseMode<T>(string value, string option, params (string Name, T Mode)[] modes)
    {
        string name = value.Trim().ToLowerInvariant() switch
        {
            "quake3" or "q3" => "q3",
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
