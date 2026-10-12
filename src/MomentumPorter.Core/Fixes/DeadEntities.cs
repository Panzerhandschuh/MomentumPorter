namespace MomentumPorter.Fixes;

using LibBSP;
using MomentumPorter.Porting;

/// <summary>
/// Entities that do nothing in Momentum and whose removal can't change how a map plays: the ones Lumper's entity rules
/// (resources/EntityRules_Momentum.json) deny for that reason, and the Counter-Strike round entities Momentum's server
/// doesn't link (Lumper shows them as unknown; they fail to spawn and only add console noise). Denied entities that
/// need <em>replacing</em> (deprecated Momentum triggers, env_surface_teleport) and the ones that are judgement calls
/// (weapons, jails, timers) aren't here; the entity review lists those.
/// </summary>
public static class DeadEntities
{
    /// <param name="Reason">Why the entity goes</param>
    /// <param name="DeniedByLumper">Lumper's Entity Review marks it invalid (the rest it doesn't know)</param>
    public sealed record Rule(string Reason, bool DeniedByLumper);

    private static readonly Rule Transitions = new("level transitions, which Momentum doesn't have", true);
    private static readonly Rule NotRelevant = new("does nothing in Momentum (Lumper's entity rules deny it)", true);
    private static readonly Rule CounterStrike = new("a Counter-Strike round entity Momentum doesn't have, which fails to spawn", false);

    /// <summary>Classname to the rule that removes it.</summary>
    public static readonly IReadOnlyDictionary<string, Rule> Rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase)
    {
        // Not info_landmark: landmark teleports (trigger_teleport with a "landmark") use it too
        ["trigger_changelevel"] = Transitions,
        ["trigger_transition"] = Transitions,
        ["point_servercommand"] = new("server commands; Momentum sets movement cvars per game mode and the max velocity comes from the zones", true),
        ["game_ui"] = NotRelevant,
        ["logic_console"] = NotRelevant,
        ["logic_convar"] = NotRelevant,
        ["logic_keyfield"] = NotRelevant,
        ["logic_eventlistener"] = NotRelevant,
        ["logic_eventlistener_itemequip"] = NotRelevant,
        ["logic_achievement"] = NotRelevant,
        ["logic_playmovie"] = NotRelevant,
        ["game_globalvars"] = NotRelevant,
        ["info_game_event_proxy"] = NotRelevant,
        ["point_gamestats_counter"] = NotRelevant,
        ["env_particle_performance_monitor"] = NotRelevant,
        ["commentary_auto"] = NotRelevant,
        ["point_commentary_node"] = NotRelevant,
        ["test_sidelist"] = NotRelevant,
        ["test_traceline"] = NotRelevant,
        ["weapon_cubemap"] = new("gives the player the cubemap weapon", true),
        ["weapon_flashbang"] = new("a Counter-Strike weapon Momentum doesn't have", true),
        ["weapon_smokegrenade"] = new("a Counter-Strike weapon Momentum doesn't have", true),
        ["func_buyzone"] = CounterStrike,
        ["func_bomb_target"] = CounterStrike,
        ["func_hostage_rescue"] = CounterStrike,
        ["func_vip_safetyzone"] = CounterStrike,
        ["hostage_entity"] = CounterStrike,
        ["info_map_parameters"] = CounterStrike,
    };

    public static bool IsDead(Entity entity) => Rules.ContainsKey(entity.ClassName);

    public static List<Entity> Find(IReadOnlyList<Entity> entities) => entities.Where(IsDead).ToList();

    /// <summary>The dead entities Lumper marks invalid, and the ones it merely doesn't know.</summary>
    public static (List<Entity> Denied, List<Entity> Unknown) Sort(IReadOnlyList<Entity> entities)
    {
        List<Entity> dead = Find(entities);
        return (dead.Where(x => Rules[x.ClassName].DeniedByLumper).ToList(), dead.Where(x => !Rules[x.ClassName].DeniedByLumper).ToList());
    }

    /// <summary>
    /// Removes the dead entities, one report line per class. Returns how many were removed.
    /// </summary>
    public static int Remove(PortContext context)
    {
        List<Entity> entities = context.Entities;
        List<Entity> dead = Find(entities);

        // The commands a map ran are worth knowing before they go: surf maps set their max velocity that way
        foreach (string command in FindServerCommands(entities, dead))
            context.Report.Warn($"the map ran \"{command}\" through a point_servercommand, which was removed; set the max velocity in the zones if that's what it was for");

        foreach (Entity entity in dead)
            context.EntityLump.Remove(entity);

        foreach (IGrouping<string, Entity> group in dead.GroupBy(x => x.ClassName.ToLowerInvariant()))
            context.Report.Change($"{group.Key} removed: {Rules[group.Key].Reason}", group.Count());

        return dead.Count;
    }

    /// <summary>
    /// The commands sent to the map's point_servercommands (Command inputs from other entities' outputs), once each.
    /// </summary>
    public static List<string> FindServerCommands(IReadOnlyList<Entity> entities, IReadOnlyList<Entity>? dead = null)
    {
        var names = new HashSet<string>(
            (dead ?? Find(entities)).Where(x => x.ClassName.Equals("point_servercommand", StringComparison.OrdinalIgnoreCase)).Select(x => x.Name).Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
            return [];

        return entities
            .SelectMany(x => x.connections)
            .Where(x => names.Contains(x.target) && x.action.Equals("Command", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.param))
            .Select(x => x.param.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
