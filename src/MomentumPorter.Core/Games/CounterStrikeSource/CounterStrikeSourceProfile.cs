namespace MomentumPorter.Games.CounterStrikeSource;

using MomentumPorter.Porting;
using MomentumPorter.Review;

/// <summary>
/// Counter-Strike: Source maps (surf, bhop, kz...), which use stock entities Momentum already understands. What's
/// baked in is the model scale fix every CS:S map needs. The bhop trigger fix and dropping bhop blocks are handled by
/// <see cref="Steps.ApplyProfileStep"/> for Source maps named after their kind. Everything else (teleport velocity
/// modes, boost triggers) needs someone in game; <see cref="EntityReview"/> lists it.
/// </summary>
public sealed class CounterStrikeSourceProfile : IGameProfile
{
    public string Name => "css";

    public string Description => "Counter-Strike: Source";

    public string? MountedGame => "Counter-Strike: Source";

    public void Apply(PortContext context)
    {
        ModelScales.Fix(context);
    }
}
