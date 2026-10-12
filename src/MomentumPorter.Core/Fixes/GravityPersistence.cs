namespace MomentumPorter.Fixes;

using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

/// <summary>
/// The entity tools' "Gravity Triggers" fix as a rule: in every Source game before CS:GO, and in GoldSrc, a
/// trigger_gravity's change stayed with the player after leaving the trigger, while Momentum (like CS:GO) reverts it
/// unless the trigger says <c>persist 1</c>. Lumper's entity rules and the review checklist both say ports from those
/// games need it, so it's applied for them (<see cref="IGameProfile.GravityPersists"/>) and tagged like the tools do.
/// </summary>
public static class GravityPersistence
{
    public static List<Entity> Find(IReadOnlyList<Entity> entities)
    {
        return entities.Where(x => x.ClassName.Equals("trigger_gravity", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Sets persist on every trigger_gravity that doesn't say either way. Returns how many were changed.
    /// </summary>
    public static int Apply(PortContext context)
    {
        int changed = 0;
        foreach (Entity entity in Find(context.Entities).Where(x => !x.ContainsKey("persist")))
        {
            entity.Set("persist", 1);
            EntityTools.Tag(entity);
            changed++;
        }

        if (changed > 0)
            context.Report.Change("trigger_gravity set to persist after leaving, as the game it came from did", changed);

        return changed;
    }
}
