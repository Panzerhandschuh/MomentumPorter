namespace MomentumPorter.Games.KreedzClimbing;

using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

internal sealed partial class KreedzClimbingProfile
{
    // KZ-only trigger_teleport spawnflags. Most of these bits mean something else on Momentum's triggers.
    private const int KzResetTimer = 128;
    private const int KzFreezeUntilGrounded = 256;
    private const int KzTeams = 1024 | 2048;
    private const int KzRedirectVelocity = 8192;

    private const int KzTeleportFlags =
        KzKeepAngles
        | KzKeepVelocity
        | KzResetTimer
        | KzFreezeUntilGrounded
        | KzOnlyAllowedPlayers
        | KzTeams
        | KzAllPlayersByDefault
        | KzRedirectVelocity;

    private static void ConvertTeleports(PortContext context, AllowList allowList)
    {
        foreach (Entity entity in context.OfClass("trigger_teleport").ToList())
        {
            int flags = entity.GetInt("spawnflags", 0);

            // KZ resets velocity unless told otherwise, Momentum keeps it by default
            if (!entity.ContainsKey("VelocityMode"))
            {
                if ((flags & KzKeepVelocity) != 0)
                {
                    entity.Set("VelocityMode", 0);
                }
                else if ((flags & KzRedirectVelocity) != 0)
                {
                    entity.Set("VelocityMode", 3);
                    // Keep the player's speed
                    entity.Set("SetSpeed", 0);
                }
                else
                {
                    entity.Set("VelocityMode", 1);
                    context.Report.Change("trigger_teleport set to reset velocity");
                }
            }

            if ((flags & KzOnlyAllowedPlayers) != 0)
            {
                bool startsEnabled = (flags & KzAllPlayersByDefault) != 0 && entity.GetInt("StartDisabled", 0) == 0;
                entity.Set("StartDisabled", startsEnabled ? 0 : 1);
                allowList.Add(entity, startsEnabled);
                context.Report.Change("per-player trigger_teleport -> enabled/disabled trigger_teleport");
            }

            if ((flags & KzKeepAngles) != 0)
                context.Report.Warn("trigger_teleport \"keep angles\" spawnflag isn't converted");

            if ((flags & KzResetTimer) != 0)
                context.Report.Warn("trigger_teleport \"reset timer\" spawnflag isn't converted");

            if ((flags & KzFreezeUntilGrounded) != 0)
                context.Report.Warn("trigger_teleport \"prevent movement until grounded\" spawnflag isn't converted");

            if ((flags & KzTeams) != 0)
                context.Report.Warn("trigger_teleport team spawnflags aren't converted");

            entity.Set("spawnflags", flags & ~KzTeleportFlags);
        }
    }
}
