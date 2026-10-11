namespace MomentumPorter.Steps;

using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Review;

/// <summary>
/// Goes through the converted map like Momentum's in-game entity tools would and leaves an <see cref="EntityReview"/>
/// in the job's context saying which entities still need changing by hand before the map can be submitted. The
/// pending ones go in the report as warnings too.
/// </summary>
public sealed class ReviewEntitiesStep : IPortStep
{
    public string Name => "Check entities";

    public double Weight => 0.5;

    public Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        PortContext portContext = state.PortContext ?? throw new InvalidOperationException("The map's entities haven't been converted");

        EntityReview review = EntityReview.Analyze(portContext);
        review.Report(context.Report);
        context.Set(review);

        context.Log.Info(review.Summary is { } summary
            ? $"{summary}; see the next steps and the report"
            : "No entities that need changing by hand in game were found");
        return Task.CompletedTask;
    }
}
