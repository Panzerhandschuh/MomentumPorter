namespace MomentumPorter.Fixes;

using LibBSP;
using MomentumPorter.Porting;

/// <summary>
/// env_global carries a named state across level transitions, which Momentum doesn't have, so Lumper's entity rules
/// deny it. Within one map the state can still gate a logic_auto (it only fires while its <c>globalstate</c> is on),
/// which is the one use that has to stay. GoldSrc maps converted with BSPConvert are the usual source: it turns the
/// states their multisources read into logic_branches and leaves the env_globals behind as harmless extra targets.
/// </summary>
public static class GlobalStates
{
    public static List<Entity> Find(IReadOnlyList<Entity> entities)
    {
        return entities.Where(x => x.ClassName.Equals("env_global", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// The env_globals nothing reads any more, and the ones a logic_auto depends on.
    /// </summary>
    public static (List<Entity> Removable, List<Entity> Kept) Sort(IReadOnlyList<Entity> entities)
    {
        var read = new HashSet<string>(
            entities
                .Where(x => x.ClassName.Equals("logic_auto", StringComparison.OrdinalIgnoreCase))
                .Select(x => x["globalstate"])
                .Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var removable = new List<Entity>();
        var kept = new List<Entity>();
        foreach (Entity entity in Find(entities))
            (read.Contains(entity["globalstate"]) ? kept : removable).Add(entity);

        return (removable, kept);
    }

    /// <summary>
    /// Removes the env_globals nothing reads and warns about the rest. Returns how many were removed.
    /// </summary>
    public static int Remove(PortContext context)
    {
        (List<Entity> removable, List<Entity> kept) = Sort(context.Entities);
        foreach (Entity entity in removable)
            context.EntityLump.Remove(entity);

        if (removable.Count > 0)
            context.Report.Change("env_global removed: its global state was only for level transitions", removable.Count);

        foreach (Entity entity in kept)
            context.Report.Warn($"env_global \"{entity["globalstate"]}\" kept: a logic_auto only fires while that state is on; Lumper flags env_global, so check it");

        return removable.Count;
    }
}
