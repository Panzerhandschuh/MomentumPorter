namespace MomentumPorter.Steps;

using MomentumPorter.Assets.Vpk;
using MomentumPorter.Detection;
using MomentumPorter.Games.GoldSrc;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Review;
using MomentumPorter.Settings;
using MomentumPorter.Steam;

/// <summary>
/// Runs the game profile's conversions on the loaded BSP, then the fixes every Source map gets: the bhop trigger fix,
/// and the bhop block fix for maps Momentum's own block fix would run on (named bhop_* or kz_*), baked in so the
/// blocks are func_bhops in the BSP.
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

        PorterSettings settings = context.TryGet<PorterSettings>() ?? new PorterSettings();
        SteamLocator steam = context.TryGet<SteamLocator>() ?? new SteamLocator();

        // A KZ map from outside KZ's maps folder still gets KZ's files (e.g. its shared soundscapes)
        if (state.GameDirectory is null && profile.Name == "kzmod")
            state.GameDirectory = settings.ResolveKreedzClimbingContentDirectory(steam);

        var portContext = new PortContext(bsp, state.MapName, state.GameDirectory, context.Report);
        GameContent? gameContent = OpenGameContent(profile, state, settings, steam, context.Log);
        try
        {
            portContext.GameContent = gameContent;
            profile.Apply(portContext);

            int bhopTriggers = BhopTriggers.Fix(portContext);
            if (bhopTriggers > 0)
                context.Log.Info($"Moved {bhopTriggers} bhop trigger{(bhopTriggers == 1 ? "'s" : "s'")} renames to landing");

            if (EntityReview.HasBlockFixPrefix(state.MapName))
            {
                int blocks = BhopBlocks.Convert(portContext);
                if (blocks > 0)
                    context.Log.Info($"Turned {blocks} dropping bhop block{(blocks == 1 ? "" : "s")} into func_bhop");
            }
        }
        finally
        {
            portContext.GameContent = null;
            gameContent?.Dispose();
        }

        portContext.CheckLimits();

        if (!Path.GetFileNameWithoutExtension(state.InputPath).Equals(state.MapName, StringComparison.OrdinalIgnoreCase))
            context.Report.Warn("packed files named after the map (e.g. its cubemap materials) keep the old map name; save from Lumper with Update filename-specific Content to rename them");

        state.Profile = profile;
        state.PortContext = portContext;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The source game's files for conversions that look up models the map uses but doesn't pack (the model scale fix
    /// checks for collision models). Only CS:S needs them so far; a missing game is logged, not fatal.
    /// </summary>
    private static GameContent? OpenGameContent(IGameProfile profile, MapPortState state, PorterSettings settings, SteamLocator steam, IPortLog log)
    {
        if (profile.Name != "css")
            return null;

        string? contentDirectory = state.GameDirectory is { } game && File.Exists(Path.Combine(game, "cstrike_pak_dir.vpk"))
            ? game
            : settings.ResolveGameContentDirectory(SourceGames.CounterStrikeSource);
        try
        {
            return SourceGames.Open(SourceGames.CounterStrikeSource, steam, settings.ResolveMomentumContentDirectory(steam), contentDirectory);
        }
        catch (DirectoryNotFoundException ex)
        {
            log.Warn($"Counter-Strike: Source's files aren't available ({ex.Message}), so scaled props' collision models can't be checked");
            return null;
        }
    }
}
