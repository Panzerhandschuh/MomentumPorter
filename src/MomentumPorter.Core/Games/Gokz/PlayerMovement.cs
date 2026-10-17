namespace MomentumPorter.Games.Gokz;

using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

public sealed partial class GokzProfile
{
    // trigger_playermovement spawnflags (CS:GO's base.fgd). Momentum's trigger_playermovement has no "Disable jump".
    private const int PlayerMovementObsolete = 16;
    private const int PlayerMovementAutoDisable = 128;
    private const int PlayerMovementAutoDuck = 2048;
    private const int PlayerMovementAutoWalk = 4096;
    private const int PlayerMovementDisableJump = 8192;
    private const int PlayerMovementFlags = PlayerMovementObsolete | PlayerMovementAutoDisable | PlayerMovementAutoDuck | PlayerMovementAutoWalk | PlayerMovementDisableJump;

    // What a twin limiting jumps for a trigger_playermovement that keeps its other job copies from it
    private static readonly string[] TwinKeys = ["model", "origin", "angles", "targetname", "parentname", "filtername", "StartDisabled"];

    /// <summary>
    /// CS:GO's trigger_playermovement can stop jumping (spawnflag 8192, kz_dontjump's no-jump zones), which Momentum's
    /// can't, so it becomes a trigger_momentum_limitmovement that limits jumping. One that also ducks or walks the
    /// player, which Momentum's does, stays as it is for that, and a limitmovement on the same brushes, with its name
    /// and filter, limits the jumps.
    /// </summary>
    private static void ConvertNoJumpTriggers(PortContext context)
    {
        foreach (Entity trigger in context.OfClass("trigger_playermovement").ToList())
        {
            int spawnflags = trigger.GetInt("spawnflags", 0);
            if ((spawnflags & PlayerMovementDisableJump) == 0 || trigger.ModelNumber <= 0)
                continue;

            int limitFlags = spawnflags & ~PlayerMovementFlags | LimitJump;
            if ((spawnflags & (PlayerMovementAutoDuck | PlayerMovementAutoWalk)) == 0)
            {
                trigger.ClassName = "trigger_momentum_limitmovement";
                trigger.Set("spawnflags", limitFlags);
                trigger.Set(PortedByToolsKey, EntityToolsVersion);
                context.Report.Change("trigger_playermovement with jumping disabled -> trigger_momentum_limitmovement");
                continue;
            }

            trigger.Set("spawnflags", spawnflags & ~PlayerMovementDisableJump);
            var twin = new Entity("trigger_momentum_limitmovement", context.EntityLump);
            foreach (string key in TwinKeys.Where(trigger.ContainsKey))
                twin[key] = trigger[key];
            twin.Set("spawnflags", limitFlags);
            twin.Set(PortedByToolsKey, EntityToolsVersion);
            context.AddEntity(twin);
            context.Report.Change("trigger_playermovement with jumping disabled -> trigger_momentum_limitmovement beside it for the jumps");
        }
    }
}
