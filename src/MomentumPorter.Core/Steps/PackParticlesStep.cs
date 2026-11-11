namespace MomentumPorter.Steps;

using MomentumPorter.Fixes;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;
using MomentumPorter.Steam;

/// <summary>
/// Packs a <c>maps/&lt;map&gt;_particles.txt</c> listing the particle files the map needs and Momentum doesn't load
/// (see <see cref="MapParticles"/>). Needs Momentum's install, found through Steam or the <see cref="PorterSettings"/>.
/// </summary>
public sealed class PackParticlesStep : IPortStep
{
    public string Name => "Pack particle manifest";

    public double Weight => 0.5;

    public Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        PortContext portContext = state.PortContext ?? throw new InvalidOperationException("The map's entities haven't been converted");

        PorterSettings settings = context.TryGet<PorterSettings>() ?? new PorterSettings();
        SteamLocator steam = context.TryGet<SteamLocator>() ?? new SteamLocator();
        if (settings.ResolveMomentumContentDirectory(steam) is not { } momentum)
        {
            context.Report.Warn("Momentum Mod wasn't found, so the map's particle systems weren't checked; set Momentum's folder in Settings");
            return Task.CompletedTask;
        }

        List<MapParticles.SearchPath> searchPaths = MapParticles.OpenSearchPaths(momentum, steam, settings, context.Log);
        try
        {
            string inputName = Path.GetFileNameWithoutExtension(state.InputPath);
            MapParticles.Result result = MapParticles.Apply(portContext, searchPaths, inputName, context.Cancellation);
            if (result.AddedFiles.Count > 0)
            {
                context.Log.Info(
                    $"Listed {string.Join(", ", result.AddedFiles)} in {MapParticles.MapManifestPath(portContext.MapName)} for {result.FixedSystems.Count} particle system{(result.FixedSystems.Count == 1 ? "" : "s")} Momentum doesn't load");
            }
        }
        finally
        {
            foreach (MapParticles.SearchPath searchPath in searchPaths)
                searchPath.Dispose();
        }

        return Task.CompletedTask;
    }
}
