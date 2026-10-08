namespace MomentumPorter.Games.KreedzClimbing;

using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

public sealed partial class KreedzClimbingProfile
{
    // trigger_momentum_bhop spawnflags
    private const int BhopResetOnActivate = 1;
    private const int BhopResetOnLandNotBhop = 2;
    private const int BhopResetOnLandAnywhereElse = 4;

    private static void ConvertBhopTeleports(PortContext context)
    {
        foreach (Entity entity in context.OfClass("trigger_bhop_teleport").ToList())
        {
            int kzFlags = entity.GetInt("spawnflags", 0);
            // An empty value means no restrictions in KZ too
            int restrictions = entity.GetInt("mbhoprestrictions", 0);

            // KZ fires these for the player even without the clients flag.
            // On trigger_momentum_bhop, 1 is both the clients flag and "reset the landing count when activated".
            int flags = BhopResetOnActivate;
            int landingLimit = 0;
            switch (restrictions)
            {
                case 0:
                    break;
                // Can't land in the same area twice in a row
                case 1:
                    landingLimit = 1;
                    flags |= BhopResetOnLandAnywhereElse;
                    break;
                // Can't land in any area of the trigger twice until landing on something else. KZ also resets this
                // when landing in a different trigger, which Momentum can't do yet.
                case 2:
                    landingLimit = 1;
                    flags |= BhopResetOnLandNotBhop;
                    break;
                // Can't land in the trigger twice
                case 3:
                    landingLimit = 1;
                    flags |= BhopResetOnLandAnywhereElse;
                    break;
                default:
                    context.Report.Warn($"trigger_bhop_teleport with unknown mbhoprestrictions {restrictions} has no landing limit");
                    break;
            }

            entity.ClassName = "trigger_momentum_bhop";
            entity.Set("spawnflags", flags);
            entity.Set("LandingLimit", landingLimit);
            entity.Set("UseDestinationAngles", (kzFlags & KzKeepAngles) == 0 ? 1 : 0);
            entity.Set("ResetVelocityOnTeleport", (kzFlags & KzKeepVelocity) == 0 ? 1 : 0);
            float time = entity.GetFloat("allowedtime", 0);
            if (time > 0)
                entity.Set("TimeLimit", time);

            entity.Remove("allowedtime");
            entity.Remove("mbhoprestrictions");

            if (entity.ContainsKey("landmark"))
            {
                context.Report.Warn("trigger_bhop_teleport landmark isn't supported and was removed");
                entity.Remove("landmark");
            }

            if ((kzFlags & ~(1 | KzKeepAngles | KzKeepVelocity)) != 0)
                context.Report.Warn($"trigger_bhop_teleport spawnflags {kzFlags} are only partly converted");

            context.Report.Change("trigger_bhop_teleport -> trigger_momentum_bhop");

            // KZ tracks restrictions 1 and 2 for each brush ("area") of the trigger, Momentum for each entity
            if (restrictions is 1 or 2)
            {
                int areas = context.SplitByBrush(entity).Count;
                if (areas > 1)
                    context.Report.Change("extra trigger_momentum_bhop for each area of a trigger", areas - 1);
            }
        }
    }
}
