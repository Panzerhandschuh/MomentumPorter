namespace MomentumPorter.Games.Gokz;

using System.Numerics;
using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

public sealed partial class GokzProfile
{
    private const string ContextFilterClass = "filter_activator_context";

    // trigger_momentum_limitmovement SF_LIMIT_CROUCH
    private const int LimitCrouch = 1 << 18;

    // How far apart an anti-crouch teleport's top and its trigger's bottom can be
    private const float AntiCrouchGap = 1;

    // Keyvalues a trigger_teleport has that trigger_momentum_limitmovement doesn't
    private static readonly string[] TeleportKeys = ["target", "filtername", "landmark", "UseLandmarkAngles", "CheckDestIfClearForPlayer"];

    /// <param name="Name">The context it checks</param>
    /// <param name="Negated">Whether it passes entities that don't have the context set</param>
    private sealed record ContextFilter(Entity Entity, string Name, bool Negated);

    /// <summary>
    /// CS:GO's filter_activator_context checks one context name (its ResponseContext) and passes when the activator's
    /// value for it is above 0, so maps keep progress as <c>AddContext cpA2:1</c> and clear it with <c>cpA2:0</c>.
    /// Momentum's filter_activator_context takes <c>name:value</c> pairs and never passes a bare name, so a teleport
    /// filtered on one never fires (kz_armored_core's checkpoint teleports, double hop blocks, anti-crouch). The filter
    /// gets the one value above 0 the map's AddContext outputs give that context.
    /// </summary>
    private static void FixContextFilters(PortContext context)
    {
        Dictionary<string, HashSet<string>> values = ReadContextValues(context);
        foreach (ContextFilter filter in ReadContextFilters(context))
        {
            List<string> positive = values.GetValueOrDefault(filter.Name)?.Where(x => Atoi(x) > 0).Order(StringComparer.Ordinal).ToList() ?? [];
            if (positive.Count > 1)
            {
                context.Report.Warn($"{Describe(filter.Entity)} wasn't converted: the map sets \"{filter.Name}\" to {string.Join(", ", positive)}, and Momentum's filter matches one value");
                continue;
            }

            // A context nothing sets above 0 never passes either way
            filter.Entity["ResponseContext"] = $"{filter.Name}:{(positive.Count == 1 ? positive[0] : "1")}";
            context.Report.Change($"{ContextFilterClass} context name -> name:value");
        }
    }

    /// <summary>
    /// An anti-crouch section is a context filtered teleport with a trigger right above it whose <c>OnEndTouch</c>
    /// sets the context for a moment. A player standing on the floor touches the trigger with the top of their hull,
    /// so crouching leaves it and the teleport takes them. That hangs on the hull's height and on whether touching a
    /// trigger's face counts, so the teleport becomes a trigger_momentum_limitmovement that stops crouching instead,
    /// without the teleport's outputs, and the trigger goes.
    /// </summary>
    private static void ConvertAntiCrouchTeleports(PortContext context)
    {
        Dictionary<string, ContextFilter> filters = ReadContextFilters(context)
            .Where(x => !x.Negated && x.Entity.Name.Length > 0)
            .GroupBy(x => x.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        if (filters.Count == 0)
            return;

        // What sets each context above 0, and whether it's all end-touch triggers like those
        var setters = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
        var otherSetters = new HashSet<string>(StringComparer.Ordinal);
        foreach (Entity entity in context.Entities)
        {
            foreach (Entity.EntityConnection connection in entity.connections)
            {
                foreach ((string name, string value) in ReadAddContext(connection).Where(x => Atoi(x.Value) > 0))
                {
                    if (IsEndTouchContextTrigger(entity))
                        (setters.TryGetValue(name, out List<Entity>? list) ? list : setters[name] = []).Add(entity);
                    else
                        otherSetters.Add(name);
                }
            }
        }

        var converted = new HashSet<Entity>();
        foreach (Entity teleport in context.OfClass("trigger_teleport").ToList())
        {
            if (!filters.TryGetValue(teleport["filtername"], out ContextFilter? filter))
                continue;

            if (otherSetters.Contains(filter.Name) || !setters.TryGetValue(filter.Name, out List<Entity>? triggers))
                continue;

            if (ZoneShapes.GetBounds(context.Bsp, teleport) is not (Vector3 mins, Vector3 maxs))
                continue;

            if (!triggers.Any(x => IsRightAbove(context.Bsp, x, mins, maxs)))
                continue;

            teleport.ClassName = "trigger_momentum_limitmovement";
            foreach (string key in TeleportKeys)
                teleport.Remove(key);

            // Its outputs ran on the players it teleported, which crouching no longer gets them
            teleport.connections.Clear();

            teleport.Set("spawnflags", teleport.GetInt("spawnflags", TriggerAllowClients) | LimitCrouch);
            teleport.Set(PortedByToolsKey, EntityToolsVersion);
            converted.Add(teleport);
            context.Report.Change("anti-crouch trigger_teleport -> trigger_momentum_limitmovement with crouching disabled");
        }

        if (converted.Count == 0)
            return;

        // The triggers only set contexts. Once no filter on those contexts is used, they do nothing.
        var used = new HashSet<string>(
            context.Entities.Select(x => x["filtername"]).Where(x => x.Length > 0)
                .Concat(context.Entities.SelectMany(x => x.connections).Select(x => x.target)),
            StringComparer.OrdinalIgnoreCase);
        var unused = new HashSet<string>(
            ReadContextFilters(context).GroupBy(x => x.Name).Where(x => x.All(f => !used.Contains(f.Entity.Name))).Select(x => x.Key),
            StringComparer.Ordinal);
        foreach (Entity trigger in setters.Values.SelectMany(x => x).Distinct().ToList())
        {
            if (!trigger.connections.SelectMany(ReadAddContext).All(x => unused.Contains(x.Name)))
                continue;

            context.EntityLump.Remove(trigger);
            context.Report.Change("removed the end touch trigger of an anti-crouch section");
        }
    }

    // A brush trigger_multiple whose only outputs set its activator's contexts when they stop touching it
    private static bool IsEndTouchContextTrigger(Entity entity)
    {
        return entity.ClassName == "trigger_multiple" && entity.ModelNumber > 0 && entity.connections.All(x =>
            x.name.Equals("OnEndTouch", StringComparison.OrdinalIgnoreCase)
            && x.target.Equals("!activator", StringComparison.OrdinalIgnoreCase)
            && x.action.Equals("AddContext", StringComparison.OrdinalIgnoreCase));
    }

    // Whether the trigger's bottom is the teleport's top, over some of it
    private static bool IsRightAbove(BSP bsp, Entity trigger, Vector3 mins, Vector3 maxs)
    {
        if (ZoneShapes.GetBounds(bsp, trigger) is not (Vector3 triggerMins, Vector3 triggerMaxs))
            return false;

        return MathF.Abs(triggerMins.Z - maxs.Z) <= AntiCrouchGap
            && triggerMins.X < maxs.X && triggerMaxs.X > mins.X
            && triggerMins.Y < maxs.Y && triggerMaxs.Y > mins.Y;
    }

    // Context filters written the CS:GO way: one name, no value
    private static List<ContextFilter> ReadContextFilters(PortContext context)
    {
        return context.OfClass(ContextFilterClass)
            .Select(x => (Entity: x, Name: x["ResponseContext"].Trim()))
            .Where(x => x.Name.Length > 0 && !x.Name.Contains(':') && !x.Name.Contains(','))
            .Select(x => new ContextFilter(x.Entity, x.Name, Atoi(x.Entity["Negated"]) != 0))
            .ToList();
    }

    // Every value the map's AddContext outputs give each context
    private static Dictionary<string, HashSet<string>> ReadContextValues(PortContext context)
    {
        var values = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (Entity.EntityConnection connection in context.Entities.SelectMany(x => x.connections))
        {
            foreach ((string name, string value) in ReadAddContext(connection))
                (values.TryGetValue(name, out HashSet<string>? set) ? set : values[name] = new HashSet<string>(StringComparer.Ordinal)).Add(value);
        }

        return values;
    }

    // AddContext's parameter: name:value[:duration] pairs separated by commas
    private static IEnumerable<(string Name, string Value)> ReadAddContext(Entity.EntityConnection connection)
    {
        if (!connection.action.Equals("AddContext", StringComparison.OrdinalIgnoreCase))
            yield break;

        foreach (string pair in (connection.param ?? "").Split(','))
        {
            string[] parts = pair.Split(':');
            if (parts.Length >= 2 && parts[0].Trim().Length > 0)
                yield return (parts[0].Trim(), parts[1].Trim());
        }
    }

    // C's atoi, which is how CS:GO reads a context's value and Source reads a boolean keyvalue
    private static int Atoi(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan().TrimStart();
        int sign = 1;
        if (span.Length > 0 && span[0] is '+' or '-')
        {
            sign = span[0] == '-' ? -1 : 1;
            span = span[1..];
        }

        long result = 0;
        foreach (char c in span)
        {
            if (!char.IsAsciiDigit(c))
                break;

            result = Math.Min(result * 10 + (c - '0'), int.MaxValue);
        }

        return (int)(sign * result);
    }
}
