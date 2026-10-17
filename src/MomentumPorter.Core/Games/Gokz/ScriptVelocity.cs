namespace MomentumPorter.Games.Gokz;

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
}
