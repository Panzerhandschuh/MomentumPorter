namespace MomentumPorter.Steps;

using MomentumPorter.Fixes;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;

/// <summary>
/// The submission docs' Sounds step: the map's music goes into sound/music/ so Momentum's music volume slider controls
/// it (see <see cref="MapMusic"/>). Moves what's ticked in the map's Sounds review (a <see cref="MapMusicChoice"/> in
/// the job's context; without one, the sounds long enough to be music) and removes what the review took out.
/// </summary>
public sealed class MoveMusicStep : IPortStep
{
    public string Name => "Move music";

    public double Weight => 0.5;

    public Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        PortContext portContext = state.PortContext ?? throw new InvalidOperationException("The map's entities haven't been converted");

        MapSounds sounds = MapMusic.Scan(portContext);
        if (sounds.Sounds.Count == 0)
            return Task.CompletedTask;

        MapMusicChoice? choice = context.TryGet<MapMusicChoice>();
        (List<string> move, List<string> remove) = MapMusic.Decide(sounds, choice);
        MusicChanges changes = MapMusic.Apply(portContext, move, remove);

        foreach ((string from, string to) in changes.Moved)
            context.Log.Info($"Moved {from} to {to}");
        foreach (string path in changes.Removed)
            context.Log.Info($"Removed {path}");
        foreach (string skipped in changes.Skipped)
            context.Log.Warn($"Left {skipped}");

        if (changes.Moved.Count > 0)
        {
            portContext.Report.Change("packed sounds moved into sound/music/ for the music volume slider", changes.Moved.Count);
            context.Log.Info($"Moved {changes.Moved.Count} sound{(changes.Moved.Count == 1 ? "" : "s")} into sound/music/ and updated {changes.References} reference{(changes.References == 1 ? "" : "s")} to {(changes.Moved.Count == 1 ? "it" : "them")}");
        }

        if (changes.Removed.Count > 0)
            portContext.Report.Change("packed sounds removed in the Sounds review", changes.Removed.Count);

        foreach (string skipped in changes.Skipped)
            portContext.Report.Warn($"Sound not moved into sound/music/: {skipped}");

        int toCheck = sounds.Maybe.Count(x => choice?.Decided(x.Path) != true);
        if (toCheck > 0)
            context.Log.Info($"{toCheck} more sound{(toCheck == 1 ? "" : "s")} might be music; listen to {(toCheck == 1 ? "it" : "them")} in the map's Sounds review");

        return Task.CompletedTask;
    }
}
