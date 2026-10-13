namespace MomentumPorter.Review;

using MomentumPorter.Pipeline;

/// <summary>
/// One thing to do before a ported map can go on Momentum Mod's website, with the docs page that explains it.
/// </summary>
public sealed record NextStep(string Title, string Text, string? Url)
{
    public Uri? Uri => Url is null ? null : new Uri(Url);
}

/// <summary>
/// What's left to do by hand after the porter is done with a map, from Momentum's map submission guides: entity fixes
/// the porter couldn't make (in Lumper or the in-game entity tools), the Lumper checks, zoning, screenshots and the
/// submission form.
/// </summary>
public static class SubmissionSteps
{
    /// <param name="mapName">The ported map's name (its file name in Momentum's maps folder)</param>
    /// <param name="review">The map's entity review, or null when there wasn't one (Quake 3 and GoldSrc maps)</param>
    /// <param name="zoneFileWritten">The porter wrote a zone file for the map's timer</param>
    /// <param name="mountedGame">The game Momentum mounts that the map's assets come from (CS:S, CS:GO), for the form's required games; null for a game whose assets are packed (KZ) or unknown</param>
    public static IReadOnlyList<NextStep> For(string mapName, EntityReview? review, bool zoneFileWritten, string? mountedGame)
    {
        var steps = new List<NextStep>();

        List<ReviewItem> pending = review?.Pending.Where(x => x.Severity != ReviewSeverity.Note).ToList() ?? [];
        List<ReviewItem> notes = review?.Pending.Where(x => x.Severity == ReviewSeverity.Note).ToList() ?? [];
        // Fixes made by porting again or in Lumper come first, so the in-game pass sees the final entities
        foreach (ReviewItem item in pending.Where(x => !x.InGame))
            steps.Add(new NextStep($"{item.Tool}: {item.Count(item.Pending)}", Capitalize(item.Message), item.Url));

        if (pending.Any(x => x.InGame))
        {
            steps.Add(new NextStep(
                "Fix entities in game",
                $"Load the map in Momentum (map {mapName}), enter sv_cheats 1 and mom_lumper_sync 1, connect Lumper with Connect to Game Sync, "
                + "and open the entity tools with devui_show entitytools. Go through the tools below, then Export To Lumper, run the job it adds and save the BSP.",
                DocsLinks.EntityToolsSetup));

            foreach (ReviewItem item in pending.Where(x => x.InGame))
                steps.Add(new NextStep($"{item.Tool}: {item.Count(item.Pending)}", Capitalize(item.Message), item.Url));
        }
        else if (review is not null)
        {
            steps.Add(new NextStep(
                "Check the entities in game",
                "Nothing for the entity tools was found. Load the map in Momentum, open devui_show entitytools once to confirm, and play through it looking for teleports that should reset speed and boosts that can be exploited.",
                DocsLinks.EntityToolsSetup));
        }

        foreach (ReviewItem item in notes)
            steps.Add(new NextStep($"{item.Tool}: {item.Count(item.Pending)}", Capitalize(item.Message), item.Url));

        steps.Add(new NextStep(
            "Review the rest in Lumper",
            "Open the map in Lumper. Delete the entities the Entity Review tab marks invalid, and check its comments on the remaining ones.",
            DocsLinks.FixEntitiesInLumper));

        steps.Add(new NextStep(
            "Check the packed assets",
            (mountedGame is null
                ? "The porter removed packed files Momentum mounts from the games installed here (see the report); run the Remove Game Assets job for the rest. "
                : $"Momentum mounts {mountedGame}, so the porter packed nothing and removed the packed files the installed mounted games have (see the report); run the Remove Game Assets job to catch assets from games that aren't installed here. ")
            + "Delete copyrighted music and move the rest into sound/music, remove explicit or copyrighted textures, and if the map was renamed, use Update filename-specific Content when saving so its cubemaps follow.",
            DocsLinks.RemoveValveAssets));

        steps.Add(new NextStep(
            "Compress and check the map",
            "Save from Lumper with Save Compressed and Update filename-specific Content ticked, then look over Tools > Map Summary (Ctrl+H).",
            DocsLinks.CompressTheMap));

        steps.Add(new NextStep(
            "Zone the map",
            zoneFileWritten
                ? $"The porter wrote maps/zones/local/{mapName}.json from the map's timer entities. Load the map, enter sv_cheats 1 and mom_zoning_enable 1 (or press Tab and the bottom-left button) to check the zones, add checkpoints and bonuses, and set each start zone's teleport destination and the max velocity."
                : $"Load the map, enter sv_cheats 1 and mom_zoning_enable 1 (or press Tab and the bottom-left button). Create the main track with its start and end zones, then its stages or checkpoints, bonuses, and Allow Bhop regions where surf maps need them. Zones save to momentum/maps/zones/local/{mapName}.json.",
            DocsLinks.ZoningSetup));

        steps.Add(new NextStep(
            "Take screenshots",
            "Find up to five views with noclip (one thumbnail and four gallery images) and take each with mom_official_screenshot, which sets the official settings. They save to momentum/screenshots.",
            DocsLinks.TakeScreenshots));

        steps.Add(new NextStep(
            "Submit on the dashboard",
            $"Sign in at dashboard.momentum-mod.org, open Your Maps and press Submit a Map. Upload the compressed BSP and the zones JSON. The name must be {mapName} with no version suffix; "
            + "pick Port as the submission type, the map's original creation date, "
            + (mountedGame is null ? "the games it still needs mounted, " : $"{mountedGame} under required games, ")
            + "the author's description, the credits from where the map was released, and list what the porter changed (see the report) in the porting changelog.",
            DocsLinks.SubmissionFiles));

        steps.Add(new NextStep(
            "Go through the review checklist",
            "Before submitting, check the map against the reviewers' own checklist: zones, the entity tools, in-game checks, the Lumper review and the submission page.",
            DocsLinks.ReviewChecklist));

        return steps;
    }

    /// <summary>
    /// Writes the steps to a log, numbered, with their links.
    /// </summary>
    public static void Print(IPortLog log, string mapName, IReadOnlyList<NextStep> steps)
    {
        log.Info($"Next steps to submit {mapName} to Momentum Mod's website:");
        for (int i = 0; i < steps.Count; i++)
        {
            NextStep step = steps[i];
            log.Info($"{i + 1,3}. {step.Title}: {step.Text}");
            if (step.Url is not null)
                log.Info($"     {step.Url}");
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
