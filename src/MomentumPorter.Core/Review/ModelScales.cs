namespace MomentumPorter.Review;

using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

/// <summary>
/// The entity tools' "Model Scale Fix". Older Source games (CS:S) squared a prop's <c>ModelScale</c>, so a prop scaled
/// for them comes out too small in Momentum unless its <c>ScaleType</c> is set to non-hierarchical (1), which brings the
/// old behaviour back. Like the tool, only props without a collision model are touched: the ones with one never had
/// the problem.
/// </summary>
public static class ModelScales
{
    // ModelScaleType_t::NONHIERARCHICAL_MODEL_SCALE
    public const int NonHierarchical = 1;

    private static readonly HashSet<string> PropClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "prop_dynamic", "prop_dynamic_override", "prop_dynamic_glow",
    };

    /// <param name="HasCollision">Whether the model has a collision model, or null when it can't be told</param>
    public sealed record ScaledProp(Entity Entity, float Scale, bool? HasCollision)
    {
        public bool IsFixed => Entity.GetInt("ScaleType", 0) == NonHierarchical;

        /// <summary>Props with a collision model are fine as they are; the rest (and the unknown ones) need the fix.</summary>
        public bool NeedsFix => HasCollision != true;
    }

    /// <summary>
    /// The map's scaled dynamic props, fixed or not.
    /// </summary>
    public static List<ScaledProp> Find(PortContext context)
    {
        var props = new List<ScaledProp>();
        var collisions = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
        foreach (Entity entity in context.Entities.Where(x => PropClasses.Contains(x.ClassName)))
        {
            // A prop without the key is at the datadesc default of 1
            float scale = entity.GetFloat("ModelScale", 1f);
            if (scale == 1f || entity.Model.Length == 0 || entity.Model.StartsWith('*'))
                continue;

            if (!collisions.TryGetValue(entity.Model, out bool? hasCollision))
                collisions[entity.Model] = hasCollision = context.ModelHasCollision(entity.Model);

            props.Add(new ScaledProp(entity, scale, hasCollision));
        }

        return props;
    }

    /// <summary>
    /// Applies the fix to every scaled prop without a collision model ("Fix All Model Scales"). Returns how many.
    /// </summary>
    public static int Fix(PortContext context)
    {
        List<ScaledProp> props = Find(context);
        int fixedProps = 0;
        int unknown = 0;
        foreach (ScaledProp prop in props.Where(x => x.NeedsFix && !x.IsFixed))
        {
            prop.Entity.Set("ScaleType", NonHierarchical);
            EntityTools.Tag(prop.Entity);
            fixedProps++;
            if (prop.HasCollision is null)
                unknown++;
        }

        if (fixedProps > 0)
            context.Report.Change("scaled prop_dynamic set to non-hierarchical scale (model scale fix)", fixedProps);

        if (unknown > 0)
            context.Report.Warn($"{unknown} scaled props were fixed without checking for a collision model (the game's files weren't available); props with one don't need the fix");

        return fixedProps;
    }
}
