namespace MomentumPorter.Games.Gokz;

using System.Globalization;
using System.Text.RegularExpressions;
using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

public sealed partial class GokzProfile
{
    // A delayed reset still stops the player, a moment after they come out of the teleport
    private const float ScriptResetMaxDelay = 0.1f;

    // trigger_teleport VelocityMode Reset (TeleportVelocityMode_t)
    private const int TeleportVelocityReset = 1;

    // "self.SetVelocity(Vector(0,0,0))" or "activator.SetVelocity(activator.GetVelocity() * 0)", spaces removed
    [GeneratedRegex(@"^(self|activator)\.SetVelocity\((Vector\(0(\.0*)?,0(\.0*)?,0(\.0*)?\)|\1\.GetVelocity\(\)\*0(\.0*)?)\);?$")]
    private static partial Regex StopPlayerScript();

    /// <summary>
    /// Momentum has no VScript. CS:GO maps stop the player coming out of a teleport with a RunScriptCode output that
    /// sets the activator's velocity to 0, which Momentum's trigger_teleport does itself with VelocityMode Reset.
    /// Other scripts are left for the entity review's VScript item.
    /// </summary>
    private static void ConvertScriptVelocityResets(PortContext context)
    {
        foreach (Entity teleport in context.OfClass("trigger_teleport"))
        {
            List<Entity.EntityConnection> resets = teleport.connections.Where(IsScriptVelocityReset).ToList();
            if (resets.Count == 0 || teleport.GetInt("VelocityMode", 0) != 0)
                continue;

            teleport.connections.RemoveAll(IsScriptVelocityReset);
            teleport.Set("VelocityMode", TeleportVelocityReset);
            context.Report.Change("trigger_teleport stopping the player with RunScriptCode -> VelocityMode 1 (reset)");
        }
    }

    // The teleport's own touch outputs, run on whoever it teleported, soon after
    private static bool IsScriptVelocityReset(Entity.EntityConnection connection)
    {
        return connection.name is "OnStartTouch" or "OnEndTouch" or "OnTrigger" or "OnStartTouchAll"
            && connection.target.Equals("!activator", StringComparison.OrdinalIgnoreCase)
            && connection.action.Equals("RunScriptCode", StringComparison.OrdinalIgnoreCase)
            && connection.delay <= ScriptResetMaxDelay
            && StopPlayerScript().IsMatch(Regex.Replace(connection.param ?? "", @"\s", ""));
    }

    // player_speed spawnflags (Momentum's port of Quake 3's target_speed): with "launcher" and one axis, Fire sets the
    // player's velocity on that axis to the speed and leaves the other two alone
    private const int PlayerSpeedLauncher = 1 << 8;
    private static readonly int[] PlayerSpeedAxis = [1 << 2, 1 << 4, 1 << 6];
    private static readonly string[] AxisNames = ["x", "y", "z"];

    // Trigger spawnflags letting anything but players through: NPCs, pushables, physics objects, ally NPCs, everything.
    // player_speed always launches the player, whoever touched the trigger.
    private const int TriggerNonPlayerFlags = 0x02 | 0x04 | 0x08 | 0x10 | 0x40;

    // "self.SetVelocity(Vector(self.GetVelocity().x, self.GetVelocity().y, 500))", spaces removed: each axis a number
    // or the axis's own current velocity
    [GeneratedRegex(@"^(?<who>self|activator)\.SetVelocity\(Vector\((?<a>-?\d+(\.\d+)?|\k<who>\.GetVelocity\(\)\.[xyz]),(?<a>-?\d+(\.\d+)?|\k<who>\.GetVelocity\(\)\.[xyz]),(?<a>-?\d+(\.\d+)?|\k<who>\.GetVelocity\(\)\.[xyz])\)\);?$")]
    private static partial Regex SetVelocityScript();

    /// <summary>
    /// CS:GO maps set the velocity of whoever touches a trigger with RunScriptCode, mostly as jump pads that keep the
    /// player's horizontal speed and set the vertical one once. Momentum's player_speed does the same per axis when an
    /// output fires it, so each output fires one per axis it sets instead, keeping its delay.
    /// </summary>
    private static void ConvertScriptVelocities(PortContext context)
    {
        var speeds = new Dictionary<(int Axis, float Speed), string>();
        var names = new HashSet<string>(context.Entities.Select(x => x["targetname"]), StringComparer.OrdinalIgnoreCase);

        foreach (Entity trigger in context.Entities.Where(x => x.ClassName.StartsWith("trigger_", StringComparison.OrdinalIgnoreCase)))
        {
            int spawnflags = trigger.GetInt("spawnflags", 0);
            if ((spawnflags & 1) == 0 || (spawnflags & TriggerNonPlayerFlags) != 0 || trigger.GetInt("VelocityMode", 0) != 0)
                continue;

            for (int i = trigger.connections.Count - 1; i >= 0; i--)
            {
                Entity.EntityConnection connection = trigger.connections[i];
                if (ScriptVelocity(connection) is not { } velocity)
                    continue;

                trigger.connections.RemoveAt(i);
                foreach ((int axis, float speed) in velocity)
                {
                    if (!speeds.TryGetValue((axis, speed), out string? name))
                    {
                        name = UniqueName($"porter_speed_{AxisNames[axis]}_{speed.ToString(CultureInfo.InvariantCulture)}", names);
                        speeds[(axis, speed)] = name;
                        AddPlayerSpeed(context, name, axis, speed);
                    }

                    trigger.connections.Insert(i, connection with { target = name, action = "Fire", param = "" });
                }

                context.Report.Change("RunScriptCode setting the player's velocity -> player_speed");
            }
        }
    }

    // The axes a trigger's touch output sets on the player who touched it, or null if it isn't one
    private static List<(int Axis, float Speed)>? ScriptVelocity(Entity.EntityConnection connection)
    {
        if (connection.name is not ("OnStartTouch" or "OnEndTouch" or "OnTrigger" or "OnStartTouchAll" or "OnEndTouchAll")
            || !connection.target.Equals("!activator", StringComparison.OrdinalIgnoreCase)
            || !connection.action.Equals("RunScriptCode", StringComparison.OrdinalIgnoreCase))
            return null;

        Match match = SetVelocityScript().Match(Regex.Replace(connection.param ?? "", @"\s", ""));
        if (!match.Success)
            return null;

        var velocity = new List<(int, float)>();
        for (int axis = 0; axis < 3; axis++)
        {
            string value = match.Groups["a"].Captures[axis].Value;
            if (value.Contains("GetVelocity"))
            {
                // Another axis's velocity moves speed between axes, which player_speed can't do
                if (!value.EndsWith("." + AxisNames[axis], StringComparison.Ordinal))
                    return null;
                continue;
            }

            velocity.Add((axis, float.Parse(value, CultureInfo.InvariantCulture)));
        }

        return velocity.Count > 0 ? velocity : null;
    }

    private static void AddPlayerSpeed(PortContext context, string name, int axis, float speed)
    {
        var entity = new Entity("player_speed", context.EntityLump);
        entity["targetname"] = name;
        entity["origin"] = "0 0 0";
        entity.Set("spawnflags", PlayerSpeedLauncher | PlayerSpeedAxis[axis]);
        entity["speed"] = speed.ToString(CultureInfo.InvariantCulture);
        entity.Set(PortedByToolsKey, EntityToolsVersion);
        context.AddEntity(entity);
    }

    private static string UniqueName(string name, HashSet<string> names)
    {
        string unique = name;
        for (int i = 2; !names.Add(unique); i++)
            unique = $"{name}_{i}";
        return unique;
    }
}
