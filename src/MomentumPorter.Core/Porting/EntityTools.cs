namespace MomentumPorter.Porting;

using LibBSP;
using MomentumPorter.Bsp;

/// <summary>
/// What Momentum's in-game entity tools (mom_entity_tools.cpp, <c>devui_show entitytools</c>) leave on the entities
/// they change, so a map can be told to have been through them. The porter tags the entities it fixes the same way,
/// so the tools (and <see cref="Review.EntityReview"/>) treat them as done.
/// </summary>
public static class EntityTools
{
    /// <summary>Set to <see cref="Version"/> on every entity the tools change or add.</summary>
    public const string PortedByToolsKey = "_ported_by_tools";

    /// <summary>ENTITY_TOOLS_VERSION. Version 1 maps exist too; any value counts as tagged.</summary>
    public const int Version = 2;

    /// <summary>The bhop trigger fix's filters (<see cref="Review.BhopTriggers"/>); further teleport filters get _2, _3... multis.</summary>
    public const string BhopFilterContextName = "momentum_tools__filter_bhop_context";

    public const string BhopFilterMultiName = "momentum_tools__filter_bhop_multi";

    /// <summary>The "Only activate when standing on the ground" filter of the Boost Triggers tool.</summary>
    public const string StandableFilterName = "momentum_tools__filter_standable";

    /// <summary>The response context the bhop trigger fix sets while the player is off the ground.</summary>
    public const string LeftGroundContext = "_mom_leftground";

    public static bool IsTagged(Entity entity) => entity.ContainsKey(PortedByToolsKey);

    public static void Tag(Entity entity) => entity.Set(PortedByToolsKey, Version);

    /// <summary>Whether an entity is one the tools added themselves (their filters, the CSM light).</summary>
    public static bool IsToolsEntity(Entity entity)
    {
        return entity.Name.StartsWith("momentum_tools__", StringComparison.OrdinalIgnoreCase)
            || (IsTagged(entity) && entity.ClassName.Equals("env_cascade_light", StringComparison.OrdinalIgnoreCase));
    }
}
