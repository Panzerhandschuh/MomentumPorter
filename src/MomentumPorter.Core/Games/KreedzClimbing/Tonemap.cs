namespace MomentumPorter.Games.KreedzClimbing;

using System.Globalization;
using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

public sealed partial class KreedzClimbingProfile
{
    // LUMP_LIGHTING_HDR
    private const int HdrLightingLump = 53;

    private const int TonemapMaster = 1;
    private const string TonemapName = "kz_tonemap";

    // Momentum's engine_post halves the bloom texture, Source 2013's doesn't
    private const float BloomScaleMultiplier = 2;

    // KZ's (Source 2013's) defaults where Momentum's differ. mat_accelerate_adjust_exposure_down (3 vs 40) can't be set per map.
    private static readonly (string Input, string Value)[] KzTonemapDefaults =
    [
        ("SetAutoExposureMin", "0.5"),
        ("SetAutoExposureMax", "2"),
        ("SetBloomScale", "2"),
        ("SetTonemapPercentTarget", "60"),
        ("SetTonemapRate", "1"),
    ];

    private static void ConvertTonemap(PortContext context)
    {
        if (context.Bsp[HdrLightingLump].length == 0)
            return;

        List<Entity> controllers = context.OfClass("env_tonemap_controller").ToList();
        if (controllers.Count == 0)
        {
            var controller = new Entity("env_tonemap_controller", context.EntityLump) { Name = TonemapName };
            controller.Set("spawnflags", TonemapMaster);
            context.AddEntity(controller);
            controllers.Add(controller);
            context.Report.Change("env_tonemap_controller added for KZ's HDR defaults");
        }

        var controllerNames = new HashSet<string>(controllers.Select(x => x.Name).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
        context.RewriteOutputs(
            (_, output) =>
            {
                if (!controllerNames.Contains(output.target) || !output.action.Equals("SetBloomScale", StringComparison.OrdinalIgnoreCase))
                    return output;

                if (!float.TryParse(output.param, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
                {
                    context.Report.Warn($"SetBloomScale parameter \"{output.param}\" isn't a number");
                    return output;
                }

                output.param = (scale * BloomScaleMultiplier).ToString(CultureInfo.InvariantCulture);
                context.Report.Change("SetBloomScale doubled for Momentum's bloom");
                return output;
            }
        );

        List<Entity.EntityConnection> spawnOutputs = context
            .OfClass("logic_auto")
            .SelectMany(x => x.connections)
            .ToList();

        var logicAuto = new Entity("logic_auto", context.EntityLump);
        logicAuto.Set("spawnflags", 1);
        foreach (Entity controller in controllers)
        {
            if (controller.Name is not { Length: > 0 })
                controller.Name = TonemapName;

            foreach ((string input, string value) in KzTonemapDefaults)
            {
                // The map's own settings at spawn win
                if (spawnOutputs.Any(x => x.target.Equals(controller.Name, StringComparison.OrdinalIgnoreCase) && x.action.Equals(input, StringComparison.OrdinalIgnoreCase)))
                    continue;

                logicAuto.connections.Add(
                    new Entity.EntityConnection
                    {
                        name = "OnMapSpawn",
                        target = controller.Name,
                        action = input,
                        param = value,
                        delay = 0,
                        fireOnce = -1,
                    }
                );
            }
        }

        if (logicAuto.connections.Count == 0)
            return;

        context.AddEntity(logicAuto);
        context.Report.Change("KZ HDR tonemap defaults set on map spawn");
    }
}
