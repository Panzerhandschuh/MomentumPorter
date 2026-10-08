namespace MomentumPorter.BspConvert;

using LibBSP;
using MomentumPorter.Detection;
using MomentumPorter.Games.GoldSrc;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

/// <summary>
/// Turns the dropping bhop blocks of the GoldSrc maps <see cref="BspConvertStep"/> wrote into func_bhops
/// (see <see cref="BhopBlocks"/>), rewriting those maps in the staging folder. Quake 3 maps are left alone.
/// </summary>
public sealed class BhopBlockFixStep : IPortStep
{
    public string Name => "Fix bhop blocks";

    public Task RunAsync(PortJobContext context)
    {
        BspConvertState state = context.Get<BspConvertState>();
        HashSet<string> goldSrcMaps = GetGoldSrcMapNames(state);

        foreach (string map in state.MapPaths)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            string name = Path.GetFileNameWithoutExtension(map);
            if (!goldSrcMaps.Contains(name))
                continue;

            var bsp = new BSP(new FileInfo(map));
            int converted = BhopBlocks.Convert(new PortContext(bsp, name, gameDirectory: null, context.Report));
            if (converted == 0)
                continue;

            new BSPWriter(bsp).WriteBSP(map, state.Options.Compress);
            context.Log.Info($"Turned {converted} dropping bhop block{(converted == 1 ? "" : "s")} in {name} into func_bhop");
        }

        return Task.CompletedTask;
    }

    // The names BSPConvert gave the maps it converted from GoldSrc ones
    private static HashSet<string> GetGoldSrcMapNames(BspConvertState state)
    {
        string prefix = state.Options.Prefix ?? "";
        return Pk3Inspector.ListMaps(state.InputPath)
            .Where(x => x.Kind == InputKind.GoldSrcBsp)
            .Select(x => prefix + x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
