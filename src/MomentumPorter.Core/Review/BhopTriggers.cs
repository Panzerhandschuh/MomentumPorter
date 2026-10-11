namespace MomentumPorter.Review;

using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

/// <summary>
/// The "mpbhop" bhop triggers of CS:S bhop maps and the entity tools' "Bhop Trigger Fix" for them.
/// </summary>
/// <remarks>
/// A bhop block has a <c>trigger_multiple</c> on top that renames the player (<c>AddOutput targetname X</c>) a moment
/// after they touch it and back to default a little later, and the <c>trigger_teleport</c> around the block only
/// accepts players with that name (<c>filter_activator_name</c>). Momentum's fix makes the rename happen on landing
/// (<c>OnLand</c>) and stops the teleport while the player is off the ground, so jumping up slopes or onto ledges that
/// touch the trigger doesn't teleport them. Detection is MomEntityUtils::IsBhopTrigger and the fix is the tools'
/// TrySubstituteBhopTrigger*Change (mom_entity_tools.cpp), key for key.
/// </remarks>
public static class BhopTriggers
{
    // A rename more than this long after touching isn't a bhop trigger
    private const float MaxDelay = 1f;

    private static readonly string[] TriggerOutputs = ["OnStartTouch", "OnTrigger"];

    // What the fix keeps of a bhop trigger_multiple and of a filtered trigger_teleport (CopyEntityData lists)
    private static readonly HashSet<string> KeptTriggerKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "model", "wait", "targetname", "StartDisabled", "spawnflags", "solid", "origin", "filtername", "classname", "hammerid",
    };

    private static readonly HashSet<string> KeptTeleportKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "model", "UseLandmarkAngles", "TeleportOnEndTouch", "targetname", "target", "StartDisabled", "spawnflags", "solid",
        "SetSpeed", "origin", "landmark", "VelocityMode", "classname", "hammerid",
    };

    /// <param name="Targetname">The name the player is given</param>
    /// <param name="Delay">How long after touching</param>
    public sealed record RenameOutput(string Targetname, float Delay);

    /// <param name="Entity">The trigger_multiple</param>
    /// <param name="Outputs">Its two renames, the earlier one (to the name the teleport filters for) first</param>
    /// <param name="FilterName">The targetname of the filter_activator_name the teleports use</param>
    public sealed record BhopTrigger(Entity Entity, RenameOutput[] Outputs, string FilterName);

    /// <summary>
    /// The map's bhop triggers that haven't been fixed yet.
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
    /// Applies the Bhop Trigger Fix to every bhop trigger, like the tools' "Fix Bhop Triggers" button followed by an
    /// export. Returns how many triggers were fixed.
    /// </summary>
    public static int Fix(PortContext context)
    {
        List<BhopTrigger> triggers = Find(context);
        if (triggers.Count == 0)
            return 0;

        // The tools take the filter of the first trigger for all of them; maps use a single one
        string filterName = triggers[0].FilterName;
        List<BhopTrigger> others = triggers.Where(x => !x.FilterName.Equals(filterName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (others.Count > 0)
            context.Report.Warn($"{others.Count} bhop triggers use another teleport filter ({others[0].FilterName}) than the rest ({filterName}) and weren't fixed; fix them in game");

        AddFilters(context, filterName);

        int teleports = 0;
        foreach (Entity teleport in context.OfClass("trigger_teleport").Where(x => x["filtername"].Equals(filterName, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string key in teleport.Keys.Where(x => !KeptTeleportKeys.Contains(x)).ToList())
                teleport.Remove(key);

            teleport["filtername"] = EntityTools.BhopFilterMultiName;
            EntityTools.Tag(teleport);
            teleports++;
        }

        int fixedTriggers = 0;
        foreach (BhopTrigger trigger in triggers.Except(others))
        {
            Entity entity = trigger.Entity;
            foreach (string key in entity.Keys.Where(x => !KeptTriggerKeys.Contains(x)).ToList())
                entity.Remove(key);

            // The renames move to OnLand; everything else the trigger did stays
            entity.connections.RemoveAll(x => (x.param ?? "").StartsWith("targetname", StringComparison.OrdinalIgnoreCase));
            foreach (RenameOutput rename in trigger.Outputs)
                entity.connections.Add(Output("OnLand", "!activator", "AddOutput", $"targetname {rename.Targetname}", rename.Delay));

            entity.connections.Add(Output("OnJump", "!activator", "AddContext", $"{EntityTools.LeftGroundContext}:1", 0));
            entity.connections.Add(Output("OnEndTouch", "!activator", "AddContext", $"{EntityTools.LeftGroundContext}:1", 0));
            entity.connections.Add(Output("OnLand", "!activator", "RemoveContext", EntityTools.LeftGroundContext, 0));
            EntityTools.Tag(entity);
            fixedTriggers++;
        }

        context.Report.Change("bhop trigger fix: trigger_multiple renames moved to OnLand", fixedTriggers);
        context.Report.Change("bhop trigger fix: trigger_teleport filtered to grounded players", teleports);
        return fixedTriggers;
    }

    // The filters the fix adds: "the player is on the ground" (no _mom_leftground context) and "has the bhop name"
    private static void AddFilters(PortContext context, string filterName)
    {
        if (!context.Entities.Any(x => x.Name.Equals(EntityTools.BhopFilterContextName, StringComparison.OrdinalIgnoreCase)))
        {
            var contextFilter = new Entity("filter_activator_context", context.EntityLump) { Name = EntityTools.BhopFilterContextName };
            contextFilter["ResponseContext"] = $"{EntityTools.LeftGroundContext}:*";
            contextFilter["negated"] = "1";
            EntityTools.Tag(contextFilter);
            context.AddEntity(contextFilter);
        }

        if (!context.Entities.Any(x => x.Name.Equals(EntityTools.BhopFilterMultiName, StringComparison.OrdinalIgnoreCase)))
        {
            var multi = new Entity("filter_multi", context.EntityLump) { Name = EntityTools.BhopFilterMultiName };
            multi["filter01"] = filterName;
            multi["filter02"] = EntityTools.BhopFilterContextName;
            multi["filtertype"] = "0";
            multi["negated"] = "0";
            EntityTools.Tag(multi);
            context.AddEntity(multi);
        }

        context.Report.Change("bhop trigger fix: filter_activator_context and filter_multi added");
    }

    private static Entity.EntityConnection Output(string name, string target, string action, string param, float delay)
    {
        return new Entity.EntityConnection { name = name, target = target, action = action, param = param, delay = delay, fireOnce = -1 };
    }

    /// <summary>
    /// Whether the fix has been applied to the map (its filters are there).
    /// </summary>
    public static bool IsFixed(IReadOnlyList<Entity> entities)
    {
        return entities.Any(x => x.Name.Equals(EntityTools.BhopFilterMultiName, StringComparison.OrdinalIgnoreCase));
    }
}
