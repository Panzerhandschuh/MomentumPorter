namespace MomentumPorter.Cli;

using CommandLine;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;

[Verb("weapon", HelpText = "Port a weapon from another game into a Momentum weapon slot as a custom pack")]
internal sealed class WeaponCliOptions
{
    [Option('g', "game", HelpText = "Game the weapon comes from: css, csgo, tf2, hl2 (mounted, scripts only), cs16, hl1 (GoldSrc) or q3")]
    public string? Game { get; set; }

    [Option('w', "weapon", HelpText = "Weapon id from the catalog (see --list)")]
    public string? Weapon { get; set; }

    [Option("all", HelpText = "Port every weapon of --game, each into the Momentum weapon it fits best (see --list), instead of --weapon")]
    public bool All { get; set; }

    [Option('s', "slot", HelpText = "Momentum weapon it replaces (pistol, machinegun, shotgun, sniper, knife, grenade, rocketlauncher, stickylauncher, concgrenade, df_*). Defaults to the weapon's best fit.")]
    public string? Slot { get; set; }

    [Option('m', "modes", HelpText = "Game modes the weapon is used in (surf, bhop, bhophl, kz, kzt, kz16, rj, sj, ahop, conc, df_cpm, df_vq3, df_vtg), comma separated. Every mode by default. Needs the gamemode-weapon-scripts engine change (docs/engine-changes.md).")]
    public string? Modes { get; set; }

    [Option('o', "output", HelpText = "Folder to write the pack folder into")]
    public string? Output { get; set; }

    [Option("name", HelpText = "Pack folder name. Defaults to momentumporter, shared by every weapon pack so their soundscript and particle manifest merge.")]
    public string? Name { get; set; }

    [Option("no-sounds", HelpText = "Keep Momentum's sounds")]
    public bool NoSounds { get; set; }

    [Option("arms", HelpText = "auto, momentum (draw Momentum's arms) or none", Default = "auto")]
    public string Arms { get; set; } = "auto";

    [Option("no-projectile", HelpText = "Keep Momentum's projectile model")]
    public bool NoProjectile { get; set; }

    [Option("no-particles", HelpText = "Keep Momentum's particles")]
    public bool NoParticles { get; set; }

    [Option("no-world-fit", HelpText = "Use the source game's world model as it is instead of fitting it to Momentum's player's hand")]
    public bool NoWorldFit { get; set; }

    [Option("gun-fov", HelpText = "Quake 3: the FOV its gun is drawn with (Quake 3 uses cg_fov). Defaults to your fov_desired.")]
    public int? GunFov { get; set; }

    [Option("no-defrag-configs", HelpText = "Quake 3: don't add the view model block to the modes' cfg files (cfg/df_*.cfg by default) and cfg/game.cfg")]
    public bool NoDefragConfigs { get; set; }

    [Option("momentum-dir", HelpText = "Momentum Mod's install folder. Found through Steam by default.")]
    public string? MomentumDirectory { get; set; }

    [Option("game-dir", HelpText = "The source game's content folder (cstrike, tf, baseq3...). Found through Steam by default.")]
    public string? GameDirectory { get; set; }

    [Option("studiomdl", HelpText = "studiomdl.exe for converted models. Defaults to Momentum's.")]
    public string? StudioMdl { get; set; }

    [Option("restore", HelpText = "Give --slot back to Momentum in --modes (every mode by default): removes the pack's weapon there instead of porting one")]
    public bool Restore { get; set; }

    [Option("list", HelpText = "List the catalog's weapons (of --game, if given) and exit")]
    public bool List { get; set; }
}

/// <summary>
/// <c>MomentumPorter weapon ...</c>: ports a weapon into a Momentum weapon slot.
/// </summary>
internal static class WeaponCommand
{
    public const string Verb = "weapon";

    public static int Run(string[] args)
    {
        return new Parser(with =>
            {
                with.HelpWriter = Console.Error;
                with.CaseSensitive = false;
            })
            .ParseArguments(args, typeof(WeaponCliOptions))
            .MapResult((WeaponCliOptions options) => Run(options).GetAwaiter().GetResult(), _ => 1);
    }

    private static async Task<int> Run(WeaponCliOptions cli)
    {
        WeaponCatalog catalog = WeaponCatalog.Default;
        if (cli.List)
            return List(catalog, cli.Game);

        if (cli.Restore)
        {
            if (cli.Slot is null || cli.Output is null)
            {
                Console.Error.WriteLine("weapon --restore needs --slot and --output");
                return 1;
            }

            var restore = new WeaponPackOptions
            {
                Restore = true,
                Slot = cli.Slot,
                GameModes = cli.Modes,
                OutputDirectory = cli.Output,
                PackName = cli.Name,
                MomentumDirectory = cli.MomentumDirectory,
            };
            PortJobResult restored = await WeaponPackBuilder.RunAsync(restore, ConsoleLog.Instance);
            restored.Report.Print(Console.Out);
            return restored.Succeeded ? 0 : 1;
        }

        if (cli.Game is null || (cli.Weapon is null && !cli.All) || cli.Output is null)
        {
            Console.Error.WriteLine("weapon needs --game, --weapon (or --all) and --output (or --list)");
            return 1;
        }

        if (cli.All && (cli.Weapon is not null || cli.Slot is not null))
        {
            Console.Error.WriteLine("--all picks the weapons and their Momentum weapons itself; leave out --weapon and --slot");
            return 1;
        }

        if (!Enum.TryParse(cli.Arms, ignoreCase: true, out WeaponArms arms))
        {
            Console.Error.WriteLine($"Unknown --arms \"{cli.Arms}\"; use auto, momentum or none");
            return 1;
        }

        var options = new WeaponPackOptions
        {
            Game = cli.Game,
            Weapon = cli.Weapon ?? "",
            Slot = cli.Slot,
            GameModes = cli.Modes,
            OutputDirectory = cli.Output,
            PackName = cli.Name,
            Sounds = !cli.NoSounds,
            Arms = arms,
            Projectile = !cli.NoProjectile,
            Particles = !cli.NoParticles,
            FitWorldModel = !cli.NoWorldFit,
            GunFov = cli.GunFov,
            SetInDefragModes = !cli.NoDefragConfigs,
            MomentumDirectory = cli.MomentumDirectory,
            GameDirectory = cli.GameDirectory,
            StudioMdlPath = cli.StudioMdl,
        };

        if (!cli.All)
        {
            PortJobResult result = await WeaponPackBuilder.RunAsync(options, ConsoleLog.Instance);
            result.Report.Print(Console.Out);
            return result.Succeeded ? 0 : 1;
        }

        if (catalog.FindGame(cli.Game) is not { } game)
        {
            Console.Error.WriteLine($"Unknown game \"{cli.Game}\". Known games: {string.Join(", ", catalog.Games.Select(x => x.Id))}");
            return 1;
        }

        // One after another: they share the pack folder, whose soundscript and particle manifest each build merges into
        var failed = new List<string>();
        foreach (LoadoutEntry entry in game.FullLoadout())
        {
            Console.WriteLine($"== {entry.Weapon.Name} as {entry.Slot.Name} ==");
            WeaponPackOptions weaponOptions = OptionsPreset.Clone(options);
            weaponOptions.Weapon = entry.Weapon.Id;
            weaponOptions.Slot = entry.Slot.Id;
            PortJobResult result = await WeaponPackBuilder.RunAsync(weaponOptions, ConsoleLog.Instance);
            result.Report.Print(Console.Out);
            if (!result.Succeeded)
                failed.Add($"{entry.Weapon.Name} ({result.FailedStep}: {result.Error})");
        }

        if (failed.Count > 0)
        {
            Console.Error.WriteLine($"Failed: {string.Join("; ", failed)}");
            return 1;
        }

        return 0;
    }

    private static int List(WeaponCatalog catalog, string? gameId)
    {
        IEnumerable<CatalogGame> games = catalog.Games;
        if (gameId is not null)
        {
            CatalogGame? game = catalog.FindGame(gameId);
            if (game is null)
            {
                Console.Error.WriteLine($"Unknown game \"{gameId}\". Known games: {string.Join(", ", catalog.Games.Select(x => x.Id))}");
                return 1;
            }

            games = [game];
        }

        foreach (CatalogGame game in games)
        {
            SourceGameInfo? info = SourceGames.Find(game.Id);
            string how = info?.MountedByMomentum == true ? "mounted by Momentum, scripts only" : "models and sounds are converted";
            Console.WriteLine($"{game.Id}: {game.Name} ({how})");
            foreach (CatalogWeapon weapon in game.Weapons)
                Console.WriteLine($"  {weapon.Id,-20} {weapon.Name,-24} {string.Join(", ", weapon.Slots)}");

            Console.WriteLine($"  --all builds: {string.Join(", ", game.FullLoadout().Select(x => $"{x.Weapon.Id} as {x.Slot.Id}"))}");
        }

        return 0;
    }
}
