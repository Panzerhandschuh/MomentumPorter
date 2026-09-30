namespace MomentumPorter.Games.KreedzClimbing;

using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

internal sealed partial class KreedzClimbingProfile
{
    private static void ConvertFuncBrushes(PortContext context, AllowList allowList)
    {
        foreach (Entity entity in context.OfClass("func_brush_kzmod").ToList())
        {
            int flags = entity.GetInt("spawnflags", 0);
            // Without 4096, KZ hides the brush from each player until they're allowed
            bool startsEnabled = (flags & KzAllPlayersByDefault) != 0;

            entity.ClassName = "func_brush";
            // 2 (ignore +USE) means the same on func_brush
            entity.Set("spawnflags", flags & ~KzAllPlayersByDefault);
            entity.Set("StartDisabled", startsEnabled ? 0 : 1);

            // KZ uses -1 for never solid, func_brush uses 1. 0 is solid while visible for both.
            if (entity.GetInt("Solidity", 0) == -1)
                entity.Set("Solidity", 1);

            allowList.Add(entity, startsEnabled);
            context.Report.Change("func_brush_kzmod -> func_brush");
        }
    }
}
