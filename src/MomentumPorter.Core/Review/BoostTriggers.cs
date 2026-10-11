namespace MomentumPorter.Review;

using System.Globalization;
using System.Numerics;
using LibBSP;
using MomentumPorter.Bsp;

/// <summary>
/// The triggers the entity tools' "Boost Triggers" tool lists: the ones that speed the player up, which other games'
/// physics let players exploit (re-triggering a push in the air, jumping on a basevelocity trigger). Which fix each one
/// needs depends on how the map uses it, so they're only found here, for the reviewer to go through in game.
/// Mirrors CMomentumEntityTools::ScanEntities and IsBaseVelocityTrigger / IsGravityTrigger / IsActivatingTriggerPush.
/// </summary>
public static class BoostTriggers
{
    private static readonly string[] BaseVelocityOutputs = ["OnStartTouch", "OnEndTouch", "OnTrigger", "OnJump"];
    private static readonly string[] GravityOutputs = ["OnEndTouch", "OnJump"];

    public enum BoostKind
    {
        /// <summary>trigger_push with a horizontal component (vertical boosts generally aren't exploitable).</summary>
        Push,

        /// <summary>trigger_setspeed, which the tools turn pushes into.</summary>
        SetSpeed,

        /// <summary>trigger_multiple adding basevelocity to the player.</summary>
        BaseVelocity,

        /// <summary>trigger_multiple lowering the player's gravity for a moment.</summary>
        Gravity,

        /// <summary>trigger_multiple renaming the player so a filtered trigger_push takes them.</summary>
        ActivatesPush,
    }

    public sealed record BoostTrigger(Entity Entity, BoostKind Kind, string Description);

    public static List<BoostTrigger> Find(IReadOnlyList<Entity> entities)
    {
        var boosts = new List<BoostTrigger>();

        // filter_activator_name filtername (the player's name) -> filter targetname, for filters a trigger_push uses
        var pushFilters = new HashSet<string>(
            entities.Where(x => x.ClassName.Equals("trigger_push", StringComparison.OrdinalIgnoreCase)).Select(x => x["filtername"]).Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var pushPlayerNames = new HashSet<string>(
            entities
                .Where(x => x.ClassName.Equals("filter_activator_name", StringComparison.OrdinalIgnoreCase) && pushFilters.Contains(x.Name))
                .Select(x => x["filtername"])
                .Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        foreach (Entity entity in entities)
        {
            switch (entity.ClassName.ToLowerInvariant())
            {
                case "trigger_push":
                {
                    Vector3 pushDir = ToVector(entity["pushdir"]);
                    Vector3 direction = SourceAngles.Rotate(SourceAngles.Forward(pushDir), entity.Angles);
                    if (MathF.Abs(direction.X) > 0.0001f || MathF.Abs(direction.Y) > 0.0001f)
                        boosts.Add(new BoostTrigger(entity, BoostKind.Push, $"push {entity.GetFloat("speed", 0):0} @ {pushDir.Y:0} H, {pushDir.X:0} V"));
                    break;
                }
                case "trigger_setspeed":
                    boosts.Add(new BoostTrigger(entity, BoostKind.SetSpeed, $"setspeed {entity.GetFloat("HorizontalSpeed", 0):0} H, {entity.GetFloat("VerticalSpeed", 0):0} V"));
                    break;
                case "trigger_multiple":
                {
                    if (FindBaseVelocityOutput(entity) is { } baseVelocity)
                        boosts.Add(new BoostTrigger(entity, BoostKind.BaseVelocity, $"basevelocity {baseVelocity.Param} on {baseVelocity.Output}"));
                    else if (FindGravityOutput(entity) is { } gravity)
                        boosts.Add(new BoostTrigger(entity, BoostKind.Gravity, $"gravity {gravity.Param} on {gravity.Output}"));
                    else if (FindPushActivation(entity, pushPlayerNames) is { } activation)
                        boosts.Add(new BoostTrigger(entity, BoostKind.ActivatesPush, $"activates a filtered trigger_push on {activation}"));
                    break;
                }
            }
        }

        return boosts;
    }

    // An output giving the player basevelocity ("basevelocity x y z")
    private static (string Output, string Param)? FindBaseVelocityOutput(Entity entity)
    {
        foreach (string output in BaseVelocityOutputs)
        {
            foreach (Entity.EntityConnection connection in Connections(entity, output))
            {
                string[] parts = Split(connection.param);
                if (parts.Length == 4 && parts[0].Equals("basevelocity", StringComparison.OrdinalIgnoreCase))
                    return (output, string.Join(' ', parts[1..]));
            }
        }

        return null;
    }

    // One output lowering gravity at once and one putting it back to 1 after a delay (TryGetGravityOutput)
    private static (string Output, string Param)? FindGravityOutput(Entity entity)
    {
        foreach (string output in GravityOutputs)
        {
            float? reduced = null;
            float? resetDelay = null;
            bool invalid = false;
            foreach (Entity.EntityConnection connection in Connections(entity, output))
            {
                string[] parts = Split(connection.param);
                if (parts.Length != 2 || !parts[0].Equals("gravity", StringComparison.OrdinalIgnoreCase)
                    || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float gravity))
                {
                    continue;
                }

                if (gravity == 1f)
                {
                    if (resetDelay is not null || connection.delay == 0f)
                        invalid = true;
                    resetDelay = connection.delay;
                }
                else
                {
                    if (reduced is not null || gravity >= 1f || connection.delay != 0f)
                        invalid = true;
                    reduced = gravity;
                }
            }

            if (!invalid && reduced is not null && resetDelay is not null)
                return (output, $"{reduced.Value.ToString(CultureInfo.InvariantCulture)} for {resetDelay.Value.ToString(CultureInfo.InvariantCulture)}s");
        }

        return null;
    }

    // An output renaming the player to a name a trigger_push's filter_activator_name lets through
    private static string? FindPushActivation(Entity entity, HashSet<string> pushPlayerNames)
    {
        if (pushPlayerNames.Count == 0)
            return null;

        foreach (string output in BaseVelocityOutputs)
        {
            string? name = null;
            float lowest = float.MaxValue;
            foreach (Entity.EntityConnection connection in Connections(entity, output))
            {
                if (!connection.action.Equals("AddOutput", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] parts = Split(connection.param);
                if (parts.Length >= 2 && parts[0].Equals("targetname", StringComparison.OrdinalIgnoreCase) && connection.delay < lowest)
                {
                    lowest = connection.delay;
                    name = parts[1];
                }
            }

            if (name is not null && pushPlayerNames.Contains(name))
                return output;
        }

        return null;
    }

    private static IEnumerable<Entity.EntityConnection> Connections(Entity entity, string output)
    {
        return entity.connections.Where(x => x.name.Equals(output, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] Split(string? param) => (param ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static Vector3 ToVector(string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float Part(int i) => parts.Length > i && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
        return new Vector3(Part(0), Part(1), Part(2));
    }
}
