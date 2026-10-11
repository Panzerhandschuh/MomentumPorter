namespace MomentumPorter.Games.CounterStrikeSource;

using MomentumPorter.Porting;
using MomentumPorter.Review;

/// <summary>
/// Counter-Strike: Source maps (surf, bhop, kz...), which use stock entities Momentum already understands. What's
/// baked in is what Momentum's in-game entity tools apply to every CS:S map: the bhop trigger fix on the mpbhop
/// triggers of bhop maps and the model scale fix on scaled props. Dropping bhop blocks are converted by
/// <see cref="Steps.ApplyProfileStep"/> for maps named bhop_* or kz_*, like Momentum's own block fix. Everything else
/// (teleport velocity modes, boost triggers, gravity triggers) needs someone in game; <see cref="EntityReview"/> lists it.
/// </summary>
public sealed class CounterStrikeSourceProfile : IGameProfile
{
    public string Name => "css";

    public string Description => "Counter-Strike: Source";

    public string? MountedGame => "Counter-Strike: Source";

    public void Apply(PortContext context)
    {
        BhopTriggers.Fix(context);
        ModelScales.Fix(context);
    }
}
