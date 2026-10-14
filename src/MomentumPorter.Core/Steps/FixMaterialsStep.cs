namespace MomentumPorter.Steps;

using MomentumPorter.Fixes;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

/// <summary>
/// The packed material fixes the submission docs' Rare Issues ask for in Lumper's Pakfile Explorer, where the answer is
/// the same for every map: sky materials Momentum would draw as the error texture (<see cref="SkyMaterials"/>).
/// </summary>
public sealed class FixMaterialsStep : IPortStep
{
    public string Name => "Fix materials";

    public double Weight => 0.5;

    public Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        PortContext portContext = state.PortContext ?? throw new InvalidOperationException("The map's entities haven't been converted");

        int skies = SkyMaterials.Apply(portContext);
        if (skies > 0)
            context.Log.Info($"Switched {skies} sky material{(skies == 1 ? "" : "s")} to Sky_SDR, since Momentum draws them as the error texture without HDR textures");

        return Task.CompletedTask;
    }
}
