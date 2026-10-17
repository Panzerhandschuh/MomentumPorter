namespace MomentumPorter.Review;

using System.Numerics;
using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Options;
using MomentumPorter.Porting;

/// <summary>
/// The "mpbhop" bhop triggers of CS:S bhop maps (and CS:GO ones made the same way) and the Bhop Trigger Fix for them.
/// </summary>
/// <remarks>
/// A bhop block has a <c>trigger_multiple</c> on top that renames the player (<c>AddOutput targetname X</c>) a moment
/// after they touch it and back to default a little later, and the <c>trigger_teleport</c> around the block only
/// accepts players with that name (<c>filter_activator_name</c>), so jumping up slopes or onto ledges that touch the
/// trigger can teleport them. The fix turns the trigger into a <c>trigger_momentum_bhop</c>, which teleports the
/// player to where that teleport would have once they've stood on the block as long as the rename took, and only
/// while they're on the ground. Parts of a trigger over teleports to different places become separate entities.
/// Where it can't do what the map's teleport did (a landmark, a teleport the map toggles, no teleport under it but a
/// push the name lets the player into...), that part stays the trigger_multiple it was, tagged so the review counts
/// it as looked at, with a warning. Momentum's entity tools moved the renames to <c>OnLand</c>, which
/// <see cref="IsFixedTrigger"/> also accepts. Detection is MomEntityUtils::IsBhopTrigger (mom_entity_utils.cpp), which
/// Momentum's KZ checkpoints use too, except for triggers that remove themselves.
/// </remarks>
public static class BhopTriggers
{
    // A rename more than this long after touching isn't a bhop trigger
    private const float MaxDelay = 1f;

    private static readonly string[] TriggerOutputs = ["OnStartTouch", "OnTrigger"];

    // What the fix keeps of a bhop trigger_multiple
    private static readonly HashSet<string> KeptTriggerKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "model", "targetname", "StartDisabled", "origin", "filtername", "classname", "hammerid",
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
            // Unlike the engine: a trigger that removes itself is a one-time teleporter, not a block
            // (bhop_quarry's teleporter_N). A tagged one is one the fix left as it was.
            if (KillsItself(entity) || EntityTools.IsTagged(entity))
                continue;

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

    private static bool KillsItself(Entity entity)
    {
        return entity.connections.Any(x => x.action.Equals("Kill", StringComparison.OrdinalIgnoreCase)
            && (x.target.Equals("!self", StringComparison.OrdinalIgnoreCase) || (entity.Name.Length > 0 && x.target.Equals(entity.Name, StringComparison.OrdinalIgnoreCase))));
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
    /// Whether a map is a bhop or climb map by name, which gets the fix by default. Elsewhere it's only applied when asked,
    /// since other maps using the pattern may not be bhop blocks.
    /// </summary>
    public static bool AppliesTo(string mapName) =>
        mapName.StartsWith("bhop_", StringComparison.OrdinalIgnoreCase) || mapName.StartsWith("kz_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the porter applies the fix to a map of a game, as the options say.</summary>
    public static bool ShouldFix(BhopTriggerFix option, string mapName, IGameProfile profile)
    {
        if (!profile.HasBhopTriggers)
            return false;

        return option switch
        {
            BhopTriggerFix.BhopAndKzMaps => AppliesTo(mapName),
            BhopTriggerFix.On => true,
            _ => false,
        };
    }

    /// <summary>
    /// Applies the Bhop Trigger Fix to every bhop trigger. Returns how many trigger_multiples became
    /// trigger_momentum_bhops (or partly did).
    /// </summary>
    public static int Fix(PortContext context)
    {
        List<BhopTrigger> triggers = Find(context);
        if (triggers.Count == 0)
            return 0;

        // Teleports the map's outputs act on (Enable, Disable, SetRemoteDestination...)
        var targeted = new HashSet<string>(context.Entities.SelectMany(x => x.connections).Select(x => x.target), StringComparer.OrdinalIgnoreCase);

        // The teleports each name filter lets the player through, with their brushes
        var teleports = new Dictionary<string, List<Teleport>>(StringComparer.OrdinalIgnoreCase);
        foreach (string filterName in triggers.Select(x => x.FilterName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            teleports[filterName] = context.OfClass("trigger_teleport")
                .Where(x => x["filtername"].Equals(filterName, StringComparison.OrdinalIgnoreCase) && x.ModelNumber > 0)
                .Select(x => CreateTeleport(context, x, targeted))
                .ToList();
        }

        int converted = 0, extra = 0, left = 0;
        foreach (BhopTrigger trigger in triggers)
        {
            // Brushes that send the player to the same place stay one entity, and so do the ones left as they were
            var groups = new List<(Destination? Destination, List<int> Brushes)>();
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            foreach ((int brush, Box bounds) in GetBrushes(context.Bsp, trigger.Entity))
            {
                (Destination? destination, string? reason) = FindDestination(bounds, teleports[trigger.FilterName]);
                if (reason is not null)
                    reasons.Add(reason);

                int group = groups.FindIndex(x => x.Destination == destination);
                if (group < 0)
                    groups.Add((destination, [brush]));
                else
                    groups[group].Brushes.Add(brush);
            }

            List<Entity> entities = context.SplitByBrushes(trigger.Entity, groups.Select(x => (IReadOnlyList<int>)x.Brushes).ToList());
            int pieces = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                if (groups.Count > 0 && groups[i].Destination is { } destination)
                {
                    Convert(context, entities[i], trigger, destination);
                    pieces++;
                }
                else
                {
                    // Left as the map had it, renames and all; tagged so it counts as looked at
                    EntityTools.Tag(entities[i]);
                }
            }

            if (pieces > 0)
            {
                converted++;
                extra += pieces - 1;
            }

            if (pieces < entities.Count)
            {
                left++;
                string which = pieces > 0 ? "part of the bhop trigger" : "the bhop trigger";
                context.Report.Warn($"bhop trigger fix: left {which} {Describe(trigger.Entity)} as it was, since {(reasons.Count > 0 ? string.Join("; ", reasons) : "it has no brushes")}");
            }
        }

        if (converted > 0)
            context.Report.Change("bhop trigger fix: trigger_multiple -> trigger_momentum_bhop", converted);
        if (extra > 0)
            context.Report.Change("bhop trigger fix: extra trigger_momentum_bhop for parts of a trigger that teleport elsewhere", extra);
        if (left > 0)
            context.Report.Change("bhop trigger fix: bhop triggers left (partly) as they were", left);

        return converted;
    }

    /// <param name="Target">The teleport's destination</param>
    /// <param name="UseDestinationAngles">Whether the teleport turns the player to the destination's angles</param>
    /// <param name="ResetVelocity">Whether it stops the player</param>
    private sealed record Destination(string Target, bool UseDestinationAngles, bool ResetVelocity);

    /// <param name="Destination">Where it sends the player, or null when trigger_momentum_bhop can't do what it does</param>
    /// <param name="Unsupported">What trigger_momentum_bhop can't do, when it can't</param>
    private sealed record Teleport(Entity Entity, List<Box> Brushes, Destination? Destination, string? Unsupported);

    private readonly record struct Box(Vector3 Mins, Vector3 Maxs)
    {
        public Box Grow(Vector3 mins, Vector3 maxs) => new(Mins - mins, Maxs + maxs);

        // How much of this box is inside the other, or 0 when they only touch
        public float Overlap(Box other)
        {
            Vector3 size = Vector3.Min(Maxs, other.Maxs) - Vector3.Max(Mins, other.Mins);
            return size.X > OverlapTolerance && size.Y > OverlapTolerance && size.Z > OverlapTolerance ? size.X * size.Y * size.Z : 0;
        }
    }

    // Brushes that only share a face (neighbouring teleports under a row of blocks) don't overlap. Converted brush
    // bounds are only float-accurate.
    private const float OverlapTolerance = 0.1f;

    // A brush is inside teleports to different places when another overlaps it at least this much of the most
    private const float AmbiguousOverlap = 0.5f;

    // A player touching the trigger is up to their height (the hull's 72 units) above or below it. Many maps put the
    // teleport as a thin slab on the block, just under the trigger.
    private static readonly Vector3 PlayerHeight = new(0, 0, 72);

    // TeleportVelocityMode_t
    private const int VelocityReset = 1;

    // trigger_teleport's "Preserve angles even when a local landmark is not specified" (SF_TELEPORT_PRESERVE_ANGLES)
    private const int SF_TELEPORT_PRESERVE_ANGLES = 32;

    // trigger_momentum_bhop spawnflags: reset the landing count when it activates, and when the player lands on
    // anything that isn't a bhop trigger. 1 is also SF_TRIGGER_ALLOW_CLIENTS.
    private const int TriggerBhopFlags = 1 | 2;

    // A TimeLimit of 0 is sv_bhop_time_limit's; this teleports on the next tick, as a rename without delay did
    private const float MinTimeLimit = 0.001f;

    // Where the player goes from a brush of the trigger: the teleport it overlaps most, as the player stands in that
    // teleport while on the block. Failing any, the one above or below it within the player's height that it
    // overlaps most. Where trigger_momentum_bhop can't do what the map did, no destination and why: the brush stays
    // as it was.
    private static (Destination? Destination, string? Reason) FindDestination(Box brush, List<Teleport> teleports)
    {
        List<(Teleport Teleport, float Overlap)> overlaps = Overlaps(brush, teleports);
        if (overlaps.Count == 0)
            overlaps = Overlaps(brush.Grow(PlayerHeight, PlayerHeight), teleports);

        // Something else the name lets the player into (kz_bhop_lj pushes them off the block), a teleport somewhere
        // else, or nothing
        if (overlaps.Count == 0)
            return (null, "no teleport its renames are for is under it");

        Teleport teleport = overlaps[0].Teleport;
        if (teleport.Destination is null)
            return (null, $"its teleport {teleport.Unsupported}");

        // Where the player stands decides which teleport they're in. A sliver of a neighbouring one doesn't count.
        if (overlaps.Any(x => x.Overlap >= overlaps[0].Overlap * AmbiguousOverlap && x.Teleport.Destination != teleport.Destination))
            return (null, "it's inside teleports to different places");

        return (teleport.Destination, null);
    }

    // The original map's model, which Lumper shows, and Hammer's id when the map kept it
    private static string Describe(Entity trigger)
    {
        string model = trigger.Model is { Length: > 0 } name ? name : "with no model";
        return trigger["hammerid"].Length > 0 ? $"{model} (hammerid {trigger["hammerid"]})" : model;
    }

    private static List<(Teleport Teleport, float Overlap)> Overlaps(Box brush, List<Teleport> teleports)
    {
        return teleports
            .Select(x => (Teleport: x, Overlap: x.Brushes.Sum(brush.Overlap)))
            .Where(x => x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .ToList();
    }

    private static Teleport CreateTeleport(PortContext context, Entity teleport, HashSet<string> targeted)
    {
        List<Box> brushes = GetBrushes(context.Bsp, teleport).Select(b => b.Bounds).ToList();
        int velocityMode = teleport.GetInt("VelocityMode", 0);
        string? unsupported =
            teleport["target"].Length == 0 ? "has no destination"
            : teleport["landmark"].Length > 0 ? "has a landmark"
            : velocityMode > VelocityReset ? $"has VelocityMode {velocityMode}"
            : teleport.GetInt("TeleportOnEndTouch", 0) != 0 ? "teleports on end touch"
            : teleport.GetInt("StartDisabled", 0) != 0 ? "starts disabled"
            : teleport.Name.Length > 0 && targeted.Contains(teleport.Name) ? "is changed by the map's outputs"
            : null;

        if (unsupported is not null)
            return new Teleport(teleport, brushes, null, unsupported);

        // Old maps leave UseLandmarkAngles out and go by the spawnflag (CTriggerTeleport::Spawn)
        int useAngles = teleport.GetInt("UseLandmarkAngles", -1);
        if (useAngles == -1)
            useAngles = (teleport.GetInt("spawnflags", 0) & SF_TELEPORT_PRESERVE_ANGLES) != 0 ? 0 : 1;

        return new Teleport(teleport, brushes, new Destination(teleport["target"], useAngles != 0, velocityMode == VelocityReset), null);
    }

    // The trigger teleports the player itself once they've stood in it as long as the map took to rename them, and
    // only while they're on the ground. Everything else the trigger did stays.
    private static void Convert(PortContext context, Entity entity, BhopTrigger trigger, Destination destination)
    {
        foreach (string key in entity.Keys.Where(x => !KeptTriggerKeys.Contains(x)).ToList())
            entity.Remove(key);

        entity.ClassName = "trigger_momentum_bhop";
        entity.Set("spawnflags", TriggerBhopFlags);
        entity.Set("ProgressTeleport", 0);
        entity["target"] = destination.Target;
        entity.Set("UseDestinationAngles", destination.UseDestinationAngles ? 1 : 0);
        entity.Set("ResetVelocityOnTeleport", destination.ResetVelocity ? 1 : 0);

        // Bhop mode has no landing limit, the others teleport on the second landing, as Momentum's func_bhops do
        entity.Set("LandingLimit", -1);
        entity.Set("TimeLimit", MathF.Max(trigger.Outputs[0].Delay, MinTimeLimit));

        entity.connections.RemoveAll(x => (x.param ?? "").StartsWith("targetname", StringComparison.OrdinalIgnoreCase));

        // OnTrigger is trigger_multiple's
        int moved = 0;
        for (int i = 0; i < entity.connections.Count; i++)
        {
            if (!entity.connections[i].name.Equals("OnTrigger", StringComparison.OrdinalIgnoreCase))
                continue;

            Entity.EntityConnection connection = entity.connections[i];
            connection.name = "OnStartTouch";
            entity.connections[i] = connection;
            moved++;
        }

        if (moved > 0)
            context.Report.Change("bhop trigger fix: OnTrigger outputs -> OnStartTouch", moved);

        EntityTools.Tag(entity);
    }

    // Each brush of an entity's model, placed at the entity's origin
    private static List<(int Brush, Box Bounds)> GetBrushes(BSP bsp, Entity entity)
    {
        var brushes = new List<(int, Box)>();
        int model = entity.ModelNumber;
        if (model <= 0 || model >= bsp.Models.Count)
            return brushes;

        foreach ((int brush, _) in BrushGeometry.GetBrushes(bsp, model))
        {
            if (BrushGeometry.GetBrushBounds(bsp, brush) is (Vector3 mins, Vector3 maxs))
                brushes.Add((brush, new Box(mins + entity.Origin, maxs + entity.Origin)));
        }

        return brushes;
    }

    /// <summary>
    /// Whether the fix has been applied to the map: the porter's trigger_momentum_bhops, or the filters Momentum's
    /// entity tools added.
    /// </summary>
    public static bool IsFixed(IReadOnlyList<Entity> entities)
    {
        return entities.Any(x => IsFixedTrigger(x) || x.Name.Equals(EntityTools.BhopFilterMultiName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether an entity is a bhop trigger the fix has been applied to: a trigger_momentum_bhop the porter made, a
    /// bhop trigger it left as it was on purpose, or a trigger_multiple Momentum's entity tools moved the renames of to
    /// OnLand.
    /// </summary>
    public static bool IsFixedTrigger(Entity entity)
    {
        if (entity.ClassName.Equals("trigger_momentum_bhop", StringComparison.OrdinalIgnoreCase))
            return EntityTools.IsTagged(entity);

        if (!entity.ClassName.Equals("trigger_multiple", StringComparison.OrdinalIgnoreCase))
            return false;

        if (EntityTools.IsTagged(entity) && TriggerOutputs.Any(x => GetRenames(entity, x) is not null))
            return true;

        return entity.connections.Any(c => c.name.Equals("OnLand", StringComparison.OrdinalIgnoreCase) && (c.param ?? "").StartsWith("targetname", StringComparison.OrdinalIgnoreCase));
    }
}
