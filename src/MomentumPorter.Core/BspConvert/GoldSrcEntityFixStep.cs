namespace MomentumPorter.BspConvert;

using LibBSP;
using MomentumPorter.Detection;
using MomentumPorter.Fixes;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

/// <summary>
/// The entity fixes Source ports get (<see cref="Steps.FixEntitiesStep"/>) for the GoldSrc maps <see cref="BspConvertStep"/>
/// wrote: the env_globals BSPConvert leaves behind once their states are logic_branches, other dead entities, and
/// persisting gravity triggers (GoldSrc kept a gravity change after the trigger). Rewrites the maps in the staging
/// folder; Quake 3 maps are left alone.
/// </summary>
public sealed class GoldSrcEntityFixStep : IPortStep
{
    public string Name => "Fix entities";

    public double Weight => 0.5;

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
            var portContext = new PortContext(bsp, name, gameDirectory: null, context.Report);
            int changed = DeadEntities.Remove(portContext) + GlobalStates.Remove(portContext) + GravityPersistence.Apply(portContext);
            if (changed == 0)
                continue;

            new BSPWriter(bsp).WriteBSP(map, state.Options.Compress);
            context.Log.Info($"Fixed {changed} entit{(changed == 1 ? "y" : "ies")} in {name} that Lumper's entity review would flag");
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
