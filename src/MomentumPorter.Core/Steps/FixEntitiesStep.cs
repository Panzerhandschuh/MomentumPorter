namespace MomentumPorter.Steps;

using MomentumPorter.Fixes;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

/// <summary>
/// The fixes every ported Source map gets after its game profile, which Lumper's entity review and the entity tools
/// would otherwise ask for by hand: dead entities and env_globals go, and trigger_gravity persists for games where it
/// did (<see cref="IGameProfile.GravityPersists"/>). Everything here is mechanical; what needs a decision is left to
/// <see cref="ReviewEntitiesStep"/>.
/// </summary>
public sealed class FixEntitiesStep : IPortStep
{
    public string Name => "Fix entities";

    public double Weight => 0.5;

    public Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        PortContext portContext = state.PortContext ?? throw new InvalidOperationException("The map's entities haven't been converted");
        IGameProfile profile = state.Profile ?? throw new InvalidOperationException("The map's game profile hasn't been applied");

        int removed = DeadEntities.Remove(portContext) + GlobalStates.Remove(portContext);
        if (removed > 0)
            context.Log.Info($"Removed {removed} entit{(removed == 1 ? "y" : "ies")} that do nothing in Momentum");

        if (profile.GravityPersists)
        {
            int gravity = GravityPersistence.Apply(portContext);
            if (gravity > 0)
                context.Log.Info($"Set {gravity} gravity trigger{(gravity == 1 ? "" : "s")} to persist, as {profile.Description} did");
        }

        return Task.CompletedTask;
    }
}
