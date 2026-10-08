namespace MomentumPorter.Steps;

using MomentumPorter.Detection;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

/// <summary>
/// Runs the game profile's conversions on the loaded BSP.
/// </summary>
public sealed class ApplyProfileStep : IPortStep
{
    public string Name => "Convert entities";

    public Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        LibBSP.BSP bsp = state.Bsp ?? throw new InvalidOperationException("The map hasn't been loaded");

        string? profileName = state.Options.Game;
        SourceGameSniffer.Detection? detection = null;
        if (profileName is null)
        {
            detection = SourceGameSniffer.Detect(bsp, state.GameDirectory, Path.GetFileNameWithoutExtension(state.InputPath));
            profileName = detection?.Profile
                ?? throw new PortStepException("Couldn't tell which game the map was made for, pick one. Known games: " + string.Join(", ", GameProfiles.All.Select(x => x.Name)));
        }

        IGameProfile profile = GameProfiles.Find(profileName)
            ?? throw new PortStepException($"Unknown game \"{profileName}\". Known games: {string.Join(", ", GameProfiles.All.Select(x => x.Name))}");

        if (detection is not null)
        {
            context.Log.Info($"Treating the map as {profile.Description}, going by {detection.Reason}");
            if (!detection.Certain)
                context.Report.Warn($"guessed the game from {detection.Reason}; pass the game explicitly if that's wrong");
        }

        var portContext = new PortContext(bsp, state.MapName, state.GameDirectory, context.Report);
        profile.Apply(portContext);
        portContext.CheckLimits();

        if (!Path.GetFileNameWithoutExtension(state.InputPath).Equals(state.MapName, StringComparison.OrdinalIgnoreCase))
            context.Report.Warn("packed files named after the map (e.g. its cubemap materials) keep the old map name");

        state.Profile = profile;
        state.PortContext = portContext;
        return Task.CompletedTask;
    }
}
