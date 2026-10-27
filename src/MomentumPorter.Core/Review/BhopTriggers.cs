namespace MomentumPorter.Review;

using LibBSP;
using MomentumPorter.Porting;

/// <summary>
/// The "mpbhop" bhop triggers of CS:S bhop maps (and CS:GO ones made the same way), which the entity review points at
/// the entity tools' Bhop Trigger Fix.
/// </summary>
/// <remarks>
/// A bhop block has a <c>trigger_multiple</c> on top that renames the player (<c>AddOutput targetname X</c>) a moment
/// after they touch it and back to default a little later, and the <c>trigger_teleport</c> around the block only
/// accepts players with that name (<c>filter_activator_name</c>). Where a trigger overhangs its block, or lies on a
/// slope, that can teleport a player who isn't standing on the block, which the entity tools fix in game where a map
/// needs it. Detection is MomEntityUtils::IsBhopTrigger (mom_entity_utils.cpp), which Momentum's KZ checkpoints and
/// the entity tools use too.
/// </remarks>
public static class BhopTriggers
{
    // A rename more than this long after touching isn't a bhop trigger
    private const float MaxDelay = 1f;

    private static readonly string[] TriggerOutputs = ["OnStartTouch", "OnTrigger"];

    /// <param name="Targetname">The name the player is given</param>
    /// <param name="Delay">How long after touching</param>
    public sealed record RenameOutput(string Targetname, float Delay);

    /// <param name="Entity">The trigger_multiple</param>
    /// <param name="Outputs">Its two renames, the earlier one (to the name the teleport filters for) first</param>
    /// <param name="FilterName">The targetname of the filter_activator_name the teleports use</param>
    public sealed record BhopTrigger(Entity Entity, RenameOutput[] Outputs, string FilterName);

    /// <summary>
    /// The map's bhop triggers that still rename the player on touch.
    /// </summary>
    public static List<BhopTrigger> Find(PortContext context) => Find(context.Entities);

    public static List<BhopTrigger> Find(IReadOnlyList<Entity> entities)
    {
        // filtername of each filter_activator_name -> its targetname, for filters some trigger_teleport uses
        var teleportFilters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedFilters = new HashSet<string>(
            entities.Where(x => x.ClassName.Equals("trigger_teleport", StringComparison.OrdinalIgnoreCase)).Select(x => x["filtername"]).Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        foreach (Entity filter in entities.Where(x => x.ClassName.Equals("filter_activator_name", StringComparison.OrdinalIgnoreCase)))
        {
            if (filter.Name.Length > 0 && usedFilters.Contains(filter.Name) && filter["filtername"] is { Length: > 0 } playerName)
                teleportFilters.TryAdd(playerName, filter.Name);
        }

        var triggers = new List<BhopTrigger>();
        if (teleportFilters.Count == 0)
            return triggers;

        foreach (Entity entity in entities.Where(x => x.ClassName.Equals("trigger_multiple", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string output in TriggerOutputs)
            {
                RenameOutput[]? renames = GetRenames(entity, output);
                if (renames is null || !teleportFilters.TryGetValue(renames[0].Targetname, out string? filterName))
                    continue;

                triggers.Add(new BhopTrigger(entity, renames, filterName));
                break;
            }
        }

        return triggers;
    }

    // TryGetBhopTriggerOutput: exactly two renames on the output, neither late, the earlier one first
    private static RenameOutput[]? GetRenames(Entity entity, string output)
    {
        var renames = new List<RenameOutput>();
        foreach (Entity.EntityConnection connection in entity.connections)
        {
            if (!connection.name.Equals(output, StringComparison.OrdinalIgnoreCase))
                continue;

            string[] parts = (connection.param ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !parts[0].Equals("targetname", StringComparison.OrdinalIgnoreCase))
                continue;

            if (connection.delay > MaxDelay)
                return null;

            renames.Add(new RenameOutput(parts[1], connection.delay));
        }

        if (renames.Count != 2)
            return null;

        return renames[0].Delay <= renames[1].Delay ? [renames[0], renames[1]] : [renames[1], renames[0]];
    }

    /// <summary>
    /// Whether an entity is a bhop trigger the entity tools fixed: a trigger_momentum_bhop they made from one, or a
    /// trigger_multiple whose renames their older fix moved to OnLand.
    /// </summary>
    public static bool IsFixedTrigger(Entity entity)
    {
        if (entity.ClassName.Equals("trigger_momentum_bhop", StringComparison.OrdinalIgnoreCase))
            return EntityTools.IsTagged(entity);

        return entity.ClassName.Equals("trigger_multiple", StringComparison.OrdinalIgnoreCase)
            && entity.connections.Any(c => c.name.Equals("OnLand", StringComparison.OrdinalIgnoreCase) && (c.param ?? "").StartsWith("targetname", StringComparison.OrdinalIgnoreCase));
    }
}
