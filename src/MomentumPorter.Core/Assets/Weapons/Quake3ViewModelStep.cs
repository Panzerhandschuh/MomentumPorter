namespace MomentumPorter.Assets.Weapons;

using System.Globalization;
using System.Text;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;

/// <summary>
/// Draws Quake 3 view models as Quake 3 does. Quake 3 renders its gun with the world's FOV (cg_fov, 90 by default) and
/// drops it 0.2 units for each degree above 90 (cg_weapons.c CG_AddViewWeapon), where Momentum draws view models at
/// fov_viewmodel (54), which crops most of a Quake 3 gun off the screen. Both FOVs are horizontal at 4:3, widened for
/// wider screens. The pack gets a config with those settings, the weapon's modes' configs run it (the Defrag modes'
/// for a df_ weapon used in every mode), and game.cfg puts the player's own settings back for the other modes.
/// </summary>
public sealed class Quake3ViewModelStep : IPortStep
{
    public const string PackConfig = "porter_q3_viewmodel";
    private const string BlockId = "Quake 3 view model";

    public string Name => "Quake 3 view model";

    public Task RunAsync(PortJobContext context)
    {
        WeaponPackState state = context.Get<WeaponPackState>();
        if (state.GameInfo!.Engine != ContentEngine.Quake3)
            return Task.CompletedTask;

        MomentumInstall momentum = state.Momentum!;
        string cfgFolder = Path.Combine(momentum.GameDirectory, "cfg");
        string? playerConfig = ReadConfig(cfgFolder, "config.cfg");
        int fov = state.Options.GunFov ?? (int)MathF.Round(GameModeConfigs.ReadFloat(playerConfig ?? "", "fov_desired") ?? 90);
        fov = Math.Clamp(fov, 1, 179);
        float drop = fov > 90 ? -0.2f * (fov - 90) : 0;

        var pack = new StringBuilder();
        pack.AppendLine($"// Quake 3's view model, written by MomentumPorter for {state.PackName}.");
        pack.AppendLine("// Quake 3 draws its gun with the world's FOV (cg_fov) and lowers it 0.2 units per degree above 90.");
        pack.AppendLine(Invariant($"fov_viewmodel {fov}"));
        pack.AppendLine("viewmodel_offset_x 0");
        pack.AppendLine("viewmodel_offset_y 0");
        pack.AppendLine(Invariant($"viewmodel_offset_z {drop:0.##}"));
        state.Sink!.AddFile($"cfg/{PackConfig}.cfg", Encoding.ASCII.GetBytes(pack.ToString()));
        context.Log.Info(Invariant($"Quake 3's gun is drawn at fov_viewmodel {fov}, {-drop:0.##} units lower ({PackConfig}.cfg)"));

        // The configs of the modes the weapon is used in: a df_ weapon used in every mode is a Defrag weapon
        bool defrag = state.Slot!.Id.StartsWith("df_", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<string> configs = state.GameModes.Count > 0
            ? state.GameModes.Select(x => x.ConfigFile).ToList()
            : defrag ? GameModeConfigs.DefragConfigs : [];
        if (configs.Count == 0)
        {
            context.Report.Warn($"The {state.Slot.Name} is used in every mode, so the Quake 3 view model FOV isn't set for you: run \"exec {PackConfig}\" to use it, and set fov_viewmodel back (54 by default) for other weapons");
            return Task.CompletedTask;
        }

        if (!state.Options.SetInDefragModes || !state.PackIsInMomentumCustom)
        {
            context.Report.Warn($"Momentum draws view models at fov_viewmodel 54, which hides most of a Quake 3 gun. Add \"execifexists {PackConfig}\" to cfg/{string.Join(", cfg/", configs)} and your own fov_viewmodel and viewmodel_offset_x/y/z to cfg/{GameModeConfigs.GameConfig}");
            return Task.CompletedTask;
        }

        // game.cfg runs first on every map, then the mode's config: reset there, Quake 3's settings in the Defrag modes.
        // The reset keeps what an earlier pack recorded, else the player's current settings.
        string gameConfig = ReadConfig(cfgFolder, GameModeConfigs.GameConfig) ?? "";
        IReadOnlyList<string> reset = GameModeConfigs.GetBlock(gameConfig, BlockId) is { Count: > 0 } existing
            ? existing
            :
            [
                $"fov_viewmodel {Setting(playerConfig, "fov_viewmodel", "54")}",
                $"viewmodel_offset_x {Setting(playerConfig, "viewmodel_offset_x", "0")}",
                $"viewmodel_offset_y {Setting(playerConfig, "viewmodel_offset_y", "0")}",
                $"viewmodel_offset_z {Setting(playerConfig, "viewmodel_offset_z", "0")}",
            ];
        WriteConfig(cfgFolder, GameModeConfigs.GameConfig, GameModeConfigs.SetBlock(gameConfig, BlockId, reset));

        foreach (string config in configs)
        {
            string text = ReadConfig(cfgFolder, config) ?? "";
            WriteConfig(cfgFolder, config, GameModeConfigs.SetBlock(text, BlockId, [$"execifexists {PackConfig}"]));
        }

        string modes = state.GameModes.Count > 0 ? GameModes.Describe(state.GameModes) : "The Defrag modes";
        context.Report.Change($"{modes} draw Quake 3's gun at fov_viewmodel {fov}");
        context.Log.Info($"Added a Momentum Porter block to cfg/{string.Join(", cfg/", configs)} (runs {PackConfig}.cfg) and cfg/{GameModeConfigs.GameConfig} ({string.Join("; ", reset)}). Every weapon in those modes is drawn at that FOV.");
        return Task.CompletedTask;
    }

    private static string Setting(string? config, string name, string fallback) =>
        config is not null && GameModeConfigs.ReadCvar(config, name) is { } value ? value : fallback;

    private static string? ReadConfig(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static void WriteConfig(string folder, string name, string text)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), text);
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
