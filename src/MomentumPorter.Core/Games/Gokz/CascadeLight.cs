namespace MomentumPorter.Games.Gokz;

using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

public sealed partial class GokzProfile
{
    // env_cascade_light's own defaults, written out
    private static readonly (string Key, string Value)[] CascadeLightKeys =
    [
        ("uselightenvangles", "1"),
        ("startdisabled", "0"),
        ("maxshadowdistance", "400"),
        ("color", "255 255 255 1"),
    ];

    /// <summary>
    /// Adds the env_cascade_light cascaded shadow maps need, (the CSM Fix). CS:GO maps
    /// mostly leave it out. With uselightenvangles the light takes its direction and colour from the map's
    /// light_environment; without one it would cast shadows from a built-in sun the lightmaps weren't baked for, so
    /// such maps are left alone.
    /// </summary>
    private static void AddCascadeLight(PortContext context)
    {
        if (context.OfClass("env_cascade_light").Any())
            return;

        if (!context.OfClass("light_environment").Any())
        {
            context.Report.Warn("no env_cascade_light added for cascaded shadows: the map has no light_environment to take the sun from");
            return;
        }

        var light = new Entity("env_cascade_light", context.EntityLump);
        foreach ((string key, string value) in CascadeLightKeys)
            light[key] = value;
        light.Set(PortedByToolsKey, EntityToolsVersion);
        context.AddEntity(light);
        context.Report.Change("env_cascade_light added for cascaded shadows");
    }
}
