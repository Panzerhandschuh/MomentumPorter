namespace MomentumPorter.Steps;

using MomentumPorter.Fixes;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;
using MomentumPorter.Steam;

/// <summary>
/// Lumper's "Remove Game Assets" job, the submission docs' first step: packed files that Momentum mounts from CS:S,
/// CS:GO, TF2, Portal 2 or HL2 are taken out of the map (see <see cref="MountedAssets"/>). Needs those games installed;
/// the ones that aren't are logged and their files left alone.
/// </summary>
public sealed class RemoveMountedAssetsStep : IPortStep
{
    public string Name => "Remove mounted assets";

    public Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        PortContext portContext = state.PortContext ?? throw new InvalidOperationException("The map's entities haven't been converted");
        if (portContext.ListPakFiles().Count == 0)
            return Task.CompletedTask;

        PorterSettings settings = context.TryGet<PorterSettings>() ?? new PorterSettings();
        SteamLocator steam = context.TryGet<SteamLocator>() ?? new SteamLocator();

        List<MountedAssets.MountedGame> games = MountedAssets.OpenMountedGames(steam, settings, context.Log);
        try
        {
            int removed = MountedAssets.Remove(portContext, games, context.Cancellation);
            context.Log.Info(removed > 0
                ? $"Removed {removed} packed file{(removed == 1 ? "" : "s")} Momentum mounts from {string.Join(", ", games.Select(x => x.Name))}"
                : $"No packed files Momentum mounts from {string.Join(", ", games.Select(x => x.Name))} to remove");
        }
        finally
        {
            foreach (MountedAssets.MountedGame game in games)
                game.Dispose();
        }

        return Task.CompletedTask;
    }
}
