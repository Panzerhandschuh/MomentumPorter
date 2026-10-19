namespace MomentumPorter.Cli;

using System.Text.Json;
using MomentumPorter.BspConvert;
using MomentumPorter.Detection;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.Steam;

/// <summary>
/// Converts Quake 3 and GoldSrc maps with BSPConvert, like BSPConv.exe: <c>MomentumPorter map.pk3 outputGameDir [options]</c>.
/// </summary>
internal static class BspConvertCommand
{
    public static bool Handles(CliOptions cli)
    {
        if (cli.GoldSrcHulls is not null)
            return true;

        return File.Exists(cli.Input) && InputDetector.Detect(cli.Input) is InputKind.Quake3Bsp or InputKind.GoldSrcBsp or InputKind.Pk3Archive or InputKind.ZipArchive;
    }

    public static async Task<int> RunAsync(CliOptions cli)
    {
        BspConvertOptions options;
        try
        {
            options = ToOptions(cli);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var log = new Program.CliLog();
        foreach (string filled in BspConvertDefaults.Fill(options, new SteamLocator(), cli.Input, output: false))
            log.Info($"Using {filled}");

        (PortJob job, BspConvertState state) = BspConvertJobs.Create(cli.Input, options);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            // Let BSPConvert finish the map it's on, so nothing is left half written. A second Ctrl+C quits.
            if (cancellation.IsCancellationRequested)
                return;

            e.Cancel = true;
            cancellation.Cancel();
            log.Warn("Stopping after the current map...");
        };

        string workDirectory = PortJob.CreateWorkDirectory(job.Name);
        try
        {
            var context = new PortJobContext { Log = log, WorkDirectory = workDirectory, Cancellation = cancellation.Token };
            context.Set(state);
            if (LoadSettings(log) is { } settings)
                context.Set(settings);

            PortJobResult result = await job.RunAsync(context);
            result.Report.Print(Console.Out);
            if (!result.Succeeded && result.Error == "Cancelled")
                Console.Error.WriteLine("Cancelled");

            return result.Succeeded ? 0 : 1;
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

    // The app's settings, for the Quake 3 folder set on its Settings page
    private static PorterSettings? LoadSettings(IPortLog log)
    {
        try
        {
            return PorterSettings.Load(PorterSettings.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.Warn($"Couldn't read {PorterSettings.DefaultPath}, so the folders set in the app aren't used: {ex.Message}");
            return null;
        }
    }

    private static BspConvertOptions ToOptions(CliOptions cli)
    {
        var options = new BspConvertOptions
        {
            OutputDirectory = cli.Output,
            // Like BSPConv.exe, replace what's there
            Overwrite = true,
            Compress = cli.Compress,
            NoPak = cli.NoPak,
            Prefix = cli.Prefix,
            Maps = cli.Maps,
            NoToolDisplacements = cli.NoToolDisps,
            PatchDisplacements = cli.PatchDisps,
            LavaTriggers = cli.LavaTriggers,
            NoZones = cli.NoZones,
            FogObb = cli.FogObb,
            NoFogOverlay = cli.NoFogOverlay,
            ClampOverbright = cli.ClampOverbright,
            SetUpQuake3Content = !cli.NoQ3Content,
            Quake3Mod = cli.Q3Mod,
            NoAnim = cli.NoAnim,
            Wads = cli.Wads,
            Studiomdl = cli.Studiomdl,
            GoldSrcHulls = cli.GoldSrcHulls,
            PointSample = cli.PointSample,
            Overbright = !cli.NoOverbright,
            FixBhopBlocks = !cli.NoBlockFix,
        };

        options.Subdiv = cli.Subdiv ?? options.Subdiv;
        options.MinDamage = cli.MinDmg ?? options.MinDamage;
        options.Scale = cli.Scale ?? options.Scale;
        options.MinLight = cli.MinLight ?? options.MinLight;
        options.Quake3Gamma = cli.Q3Gamma ?? options.Quake3Gamma;
        options.MapOverBrightBits = cli.MapOverBrightBits ?? options.MapOverBrightBits;
        options.Gamma = cli.Gamma ?? options.Gamma;
        options.Brightness = cli.Brightness ?? options.Brightness;
        options.LightGamma = cli.LightGamma ?? options.LightGamma;
        options.TexGamma = cli.TexGamma ?? options.TexGamma;
        options.FogMinHeight = cli.FogMinHeight ?? options.FogMinHeight;
        options.AnimBudget = cli.AnimBudget ?? options.AnimBudget;
        options.AnimMaxRes = cli.AnimMaxRes ?? options.AnimMaxRes;
        options.AnimFps = cli.AnimFps ?? options.AnimFps;
        options.AnimMaxFrames = cli.AnimMaxFrames ?? options.AnimMaxFrames;
        options.AnimAlpha = cli.AnimAlpha ?? options.AnimAlpha;

        if (cli.OffModeEnts is not null)
        {
            options.OffModeEntities = cli.OffModeEnts.ToLowerInvariant() switch
            {
                "cpm" => OffModeEntities.CPM,
                "vq3" => OffModeEntities.VQ3,
                _ => throw new ArgumentException("Default entity state must be either 'cpm' or 'vq3'."),
            };
        }

        return options;
    }
}
