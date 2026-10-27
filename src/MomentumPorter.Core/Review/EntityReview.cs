namespace MomentumPorter.Review;

using System.Numerics;
using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Games.GoldSrc;
using MomentumPorter.Porting;

public enum ReviewSeverity
{
    /// <summary>The docs say to always apply the fix when the map has these entities.</summary>
    Required,

    /// <summary>Someone has to look at each entity in game and decide.</summary>
    Review,

    /// <summary>Worth knowing while reviewing; nothing to do for most maps.</summary>
    Note,
}

/// <summary>
/// What a map has for one of the entity fixes or checks and how much of it has been dealt with.
/// </summary>
/// <param name="Tool">The fix's name; for the ones done with the in-game entity tools, the tool's name as their list shows it</param>
/// <param name="Noun">What's counted, plural ("boost triggers"), for summaries</param>
/// <param name="Found">Entities the fix applies to</param>
/// <param name="Done">Of those, the ones already changed (by the tools or the porter) or fine as they are</param>
/// <param name="Message">What to do about the rest</param>
/// <param name="Url">The docs section for it</param>
/// <param name="Singular">The noun for one, when it isn't the plural minus its s</param>
/// <param name="InGame">The rest are dealt with in the in-game entity tools, rather than in Lumper or by porting again</param>
public sealed record ReviewItem(string Tool, ReviewSeverity Severity, string Noun, int Found, int Done, string Message, string Url, string? Singular = null, bool InGame = false)
{
    public int Pending => Math.Max(Found - Done, 0);

    public bool IsPending => Pending > 0;

    /// <summary>"3 boost triggers", "1 boost trigger"</summary>
    public string Count(int count) => count == 1 ? $"1 {Singular ?? Singularize(Noun)}" : $"{count} {Noun}";

    private static string Singularize(string plural)
    {
        if (plural.EndsWith("ies", StringComparison.Ordinal))
            return plural[..^3] + "y";
        if (plural.EndsWith("shes", StringComparison.Ordinal) || plural.EndsWith("ches", StringComparison.Ordinal) || plural.EndsWith("xes", StringComparison.Ordinal) || plural.EndsWith("sses", StringComparison.Ordinal))
            return plural[..^2];
        return plural.EndsWith('s') ? plural[..^1] : plural;
    }
}

/// <summary>
/// Goes through a Source map the way the in-game entity tools (and the review checklist) would, to tell which entities
/// still need changing by hand and which a reviewer has already dealt with. Run on a freshly ported map it says what
/// to fix in game; run on a map that has been through the tools it says whether anything was left out.
/// </summary>
public sealed class EntityReview
{
    private EntityReview(string mapName, IReadOnlyList<ReviewItem> items, int taggedEntities)
    {
        MapName = mapName;
        Items = items;
        TaggedEntities = taggedEntities;
    }

    public string MapName { get; }

    public IReadOnlyList<ReviewItem> Items { get; }

    /// <summary>Entities tagged by the entity tools (or the porter's own fixes).</summary>
    public int TaggedEntities { get; }

    /// <summary>The map has been through the entity tools (or the porter's fixes) at least once.</summary>
    public bool ThroughEntityTools => TaggedEntities > 0;

    public IEnumerable<ReviewItem> Pending => Items.Where(x => x.IsPending);

    /// <summary>Something has to be looked at in game before the map can be submitted.</summary>
    public bool NeedsManualChanges => Pending.Any(x => x.Severity != ReviewSeverity.Note);

    /// <summary>A fix the docs say to always apply hasn't been.</summary>
    public bool HasMissingRequiredFixes => Pending.Any(x => x.Severity == ReviewSeverity.Required);

    /// <summary>
    /// One line for a queue item: "Needs manual entity changes: 3 boost triggers, 2 gravity triggers", or
    /// null when nothing but notes is pending.
    /// </summary>
    public string? Summary
    {
        get
        {
            List<ReviewItem> pending = Pending.Where(x => x.Severity != ReviewSeverity.Note).ToList();
            if (pending.Count == 0)
                return null;

            return "Needs manual entity changes: " + string.Join(", ", pending.Select(x => x.Count(x.Pending)));
        }
    }

    /// <summary>
    /// The review as lines of text, for logs and the command line.
    /// </summary>
    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string> { $"Entity review of {MapName}:" };
        lines.Add(ThroughEntityTools
            ? $"  {(TaggedEntities == 1 ? "1 entity was" : $"{TaggedEntities} entities were")} changed by Momentum's entity tools or the porter's fixes"
            : "  no entity has been changed by Momentum's entity tools yet");

        foreach (ReviewItem item in Items.Where(x => x.Found > 0))
        {
            string state = item.Done == item.Found ? (item.Found == 1 ? "done" : "all done") : item.Done == 0 ? "none done" : $"{item.Done} done";
            lines.Add($"  [{item.Severity}] {item.Tool}: {item.Count(item.Found)}, {state}");
            if (item.IsPending)
            {
                lines.Add($"      {item.Message}");
                lines.Add($"      {item.Url}");
            }
        }

        lines.Add("  " + Verdict);
        return lines;
    }

    /// <summary>
    /// One sentence on where the map stands.
    /// </summary>
    public string Verdict
    {
        get
        {
            List<ReviewItem> required = Pending.Where(x => x.Severity == ReviewSeverity.Required).ToList();
            List<ReviewItem> review = Pending.Where(x => x.Severity == ReviewSeverity.Review).ToList();
            if (required.Count > 0)
                return "Verdict: required fixes are missing: " + string.Join(", ", required.Select(x => $"{x.Tool} ({x.Count(x.Pending)})"));

            if (review.Count > 0)
            {
                return "Verdict: every required fix is in; still to confirm in game, since they were left unchanged: "
                    + string.Join(", ", review.Select(x => x.Count(x.Pending)));
            }

            return ThroughEntityTools
                ? "Verdict: the map has been through the entity tools and nothing is left to change"
                : "Verdict: nothing for the entity tools to change was found";
        }
    }

    public void Print(TextWriter writer)
    {
        foreach (string line in Lines())
            writer.WriteLine(line);
    }

    /// <summary>
    /// Adds the pending items to a port report as warnings.
    /// </summary>
    public void Report(PortReport report)
    {
        foreach (ReviewItem item in Pending.Where(x => x.Severity != ReviewSeverity.Note))
            report.Warn($"{item.Count(item.Pending)} need{(item.Pending == 1 ? "s" : "")} changing by hand ({item.Tool}): {item.Message}");
    }

    /// <summary>
    /// Reviews a Source map file as it is, e.g. a ported map saved from Lumper after the in-game review.
    /// </summary>
    public static EntityReview AnalyzeFile(string path)
    {
        var bsp = new BSP(new FileInfo(path));
        if (!bsp.MapType.IsSubtypeOf(MapType.Source))
            throw new InvalidDataException($"{Path.GetFileName(path)} isn't a Source engine map ({bsp.MapType})");

        string? gameDirectory = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(path)));
        if (gameDirectory is not null && !File.Exists(Path.Combine(gameDirectory, "gameinfo.txt")))
            gameDirectory = null;

        return Analyze(new PortContext(bsp, Path.GetFileNameWithoutExtension(path), gameDirectory));
    }

    /// <summary>
    /// Reviews a map's entities. The map's name decides whether Momentum's own block fix would run for it.
    /// </summary>
    public static EntityReview Analyze(PortContext context)
    {
        List<Entity> entities = context.Entities;
        var items = new List<ReviewItem>();

        items.AddRange(ReviewDeadEntities(entities));
        items.Add(ReviewGlobalStates(entities));
        items.Add(ReviewBhopTriggers(context));
        items.Add(ReviewBhopBlocks(context));
        if (ReviewModelScales(context) is { } modelScales)
            items.Add(modelScales);
        if (ReviewCascadeLight(context) is { } csm)
            items.Add(csm);
        items.AddRange(ReviewTeleportVelocity(context));
        items.Add(ReviewLandmarkTeleports(entities));
        items.Add(ReviewBoostTriggers(entities));
        items.Add(ReviewGravityTriggers(entities));
        items.Add(ReviewWeaponStrips(entities));
        items.Add(ReviewLogicTimers(entities));
        items.Add(ReviewScripts(entities));
        items.Add(ReviewButtons(entities));

        int tagged = entities.Count(x => EntityTools.IsTagged(x) || EntityTools.IsToolsEntity(x));
        return new EntityReview(context.MapName, items, tagged);
    }

    // Entities the porter removes itself (Fixes.DeadEntities): the ones Lumper's Entity Review tab marks invalid, and
    // the Counter-Strike ones it doesn't know, which reviewers leave in since they only fail to spawn
    private static IEnumerable<ReviewItem> ReviewDeadEntities(List<Entity> entities)
    {
        (List<Entity> denied, List<Entity> unknown) = Fixes.DeadEntities.Sort(entities);
        static string Classes(List<Entity> list) => string.Join(", ", list.Select(x => x.ClassName.ToLowerInvariant()).Distinct().Order());

        yield return new ReviewItem(
            "Invalid entities",
            ReviewSeverity.Required,
            "entities Lumper marks invalid",
            denied.Count,
            0,
            $"delete them in Lumper's Entity Review tab, they do nothing in Momentum ({Classes(denied)})",
            DocsLinks.FixEntitiesInLumper,
            Singular: "entity Lumper marks invalid");

        yield return new ReviewItem(
            "Unknown entities",
            ReviewSeverity.Note,
            "Counter-Strike entities Momentum doesn't have",
            unknown.Count,
            0,
            $"they fail to spawn, which is harmless; delete them in Lumper to quiet the console ({Classes(unknown)})",
            DocsLinks.FixEntitiesInLumper,
            Singular: "Counter-Strike entity Momentum doesn't have");
    }

    private static ReviewItem ReviewGlobalStates(List<Entity> entities)
    {
        (List<Entity> removable, List<Entity> kept) = Fixes.GlobalStates.Sort(entities);
        return new ReviewItem(
            "env_global",
            ReviewSeverity.Required,
            "env_globals",
            removable.Count + kept.Count,
            0,
            kept.Count > 0
                ? "delete them in Lumper; the ones a logic_auto reads through its globalstate need that logic_auto changed to fire on its own first"
                : "delete them in Lumper, nothing reads their state (it was only for level transitions)",
            DocsLinks.FixEntitiesInLumper);
    }

    // Only some maps need them fixed, and only in game, so it's a note
    private static ReviewItem ReviewBhopTriggers(PortContext context)
    {
        List<Entity> entities = context.Entities;
        int unfixed = BhopTriggers.Find(entities).Count;
        int done = entities.Count(BhopTriggers.IsFixedTrigger);
        return new ReviewItem(
            "Bhop Trigger Fix",
            ReviewSeverity.Note,
            "bhop triggers",
            unfixed + done,
            done,
            "these trigger_multiples rename the player on touch for a filtered teleport, which only matters where a trigger overhangs its block and catches players next to or below it, or lies on a slope and teleports players sliding up it; if this map has those, fix them with the entity tools' Bhop Trigger Fix",
            DocsLinks.BhopTriggerFix,
            InGame: true);
    }

    private static ReviewItem ReviewBhopBlocks(PortContext context)
    {
        int pending = BhopBlocks.Find(context).Count;
        int done = context.OfClass("func_bhop").Count();
        bool blockFixMap = HasBlockFixPrefix(context.MapName);
        return new ReviewItem(
            "Bhop Block Fix",
            blockFixMap ? ReviewSeverity.Required : ReviewSeverity.Review,
            "dropping bhop blocks",
            pending + done,
            done,
            blockFixMap
                ? "these func_doors/func_buttons drop the player into a teleport; port the map with the porter, which turns them into func_bhops in the BSP"
                : "these func_doors/func_buttons drop the player into a teleport, but Momentum's block fix only runs for maps named bhop_* or kz_*; if they're bhop blocks, port the map again under a bhop_ or kz_ name so the porter turns them into func_bhops",
            DocsLinks.BhopBlockFix);
    }

    // Only games before CS:GO (BSP version 21) squared the scale
    private static ReviewItem? ReviewModelScales(PortContext context)
    {
        if (context.Bsp.MapType == MapType.Source21)
            return null;

        List<ModelScales.ScaledProp> props = ModelScales.Find(context).Where(x => x.NeedsFix).ToList();
        bool unknown = props.Any(x => x.HasCollision is null && !x.IsFixed);
        return new ReviewItem(
            "Model Scale Fix",
            ReviewSeverity.Required,
            "scaled props",
            props.Count,
            props.Count(x => x.IsFixed),
            "these prop_dynamics have a ModelScale that older Source games squared; port the map as a Counter-Strike: Source map, which sets their ScaleType to non-hierarchical (1), or set it in Lumper"
                + (unknown ? ". Their collision models couldn't be checked, and props with one don't need the fix" : ""),
            DocsLinks.ModelScaleFix);
    }

    // CS:GO maps (BSP version 21) use cascaded shadows, which need an env_cascade_light in Momentum
    private static ReviewItem? ReviewCascadeLight(PortContext context)
    {
        if (context.Bsp.MapType != MapType.Source21)
            return null;

        bool hasLight = context.OfClass("env_cascade_light").Any();
        return new ReviewItem(
            "CSM Fix",
            ReviewSeverity.Required,
            "CS:GO maps without an env_cascade_light",
            1,
            hasLight ? 1 : 0,
            context.OfClass("light_environment").Any()
                ? "the map is a CS:GO one with no env_cascade_light; port the map with the porter, which adds one following its light_environment"
                : "the map is a CS:GO one with no env_cascade_light or light_environment to take the sun from; if its shadows look wrong next to CS:GO's, add an env_cascade_light in Lumper and set its angles to the sun's",
            DocsLinks.CsmFix,
            Singular: "CS:GO map without an env_cascade_light");
    }

    private static IEnumerable<ReviewItem> ReviewTeleportVelocity(PortContext context)
    {
        // The tool lists each destination once, with every teleport to it
        var destinations = new Dictionary<string, List<Entity>>(StringComparer.OrdinalIgnoreCase);
        foreach (Entity teleport in context.Entities.Where(x => x.ClassName is "trigger_teleport" or "trigger_momentum_teleport"))
        {
            if (teleport["target"].Length == 0 || teleport["landmark"].Length > 0)
                continue;

            if (!destinations.TryGetValue(teleport["target"], out List<Entity>? teleports))
                destinations[teleport["target"]] = teleports = [];

            teleports.Add(teleport);
        }

        if (destinations.Count == 0)
            yield break;

        var byName = context.Entities.Where(x => x.Name.Length > 0).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        List<ClipBrush> clips = GetClipBrushes(context.Bsp);

        int inClips = 0;
        int inClipsDone = 0;
        int others = 0;
        int othersDone = 0;
        foreach ((string name, List<Entity> teleports) in destinations)
        {
            // A velocity mode written out, by the tools or the porter, means someone decided
            bool done = teleports.All(x => x.ContainsKey("VelocityMode"));
            bool inClip = byName.TryGetValue(name, out Entity? destination)
                && clips.Any(x => x.Bounds.Contains(destination.Origin) && BrushGeometry.Contains(context.Bsp, x.Brush, destination.Origin));
            if (inClip)
            {
                inClips++;
                if (done)
                    inClipsDone++;
            }
            else
            {
                others++;
                if (done)
                    othersDone++;
            }
        }

        // The docs' rule of thumb for Keep Negative Z, which is mostly a surf thing (cages over the start platform);
        // bhop maps box their fail destinations in clip too and are usually fine keeping velocity
        bool surf = context.MapName.StartsWith("surf_", StringComparison.OrdinalIgnoreCase);
        yield return new ReviewItem(
            "Teleport Velocity Mode",
            surf ? ReviewSeverity.Review : ReviewSeverity.Note,
            "teleport destinations inside clip brushes",
            inClips,
            inClipsDone,
            "these destinations sit inside clip brushes (a surf cage, a jail): their teleports should keep only downward velocity (Keep Negative Z) if the player isn't meant to carry speed through",
            DocsLinks.KeepNegativeZ,
            Singular: "teleport destination inside a clip brush",
            InGame: true);

        yield return new ReviewItem(
            "Teleport Velocity Mode",
            ReviewSeverity.Note,
            "teleport destinations",
            others,
            othersDone,
            "their teleports keep the player's velocity, Momentum's default; check whether any should reset it (fail teleports that launch the player, RJ/SJ/conc/ahop fails) or keep only downward velocity",
            DocsLinks.TeleportVelocityMode,
            InGame: true);
    }

    private static ReviewItem ReviewLandmarkTeleports(List<Entity> entities)
    {
        List<Entity> landmarks = entities.Where(x => x.ClassName.Equals("trigger_teleport", StringComparison.OrdinalIgnoreCase) && x["landmark"].Length > 0).ToList();
        return new ReviewItem(
            "Landmark Teleports",
            ReviewSeverity.Review,
            "landmark teleports",
            landmarks.Count,
            landmarks.Count(EntityTools.IsTagged),
            "these teleport relative to a landmark; check in game that the player comes out facing the right way (Fix Destination Angles)",
            DocsLinks.EntityToolsList,
            InGame: true);
    }

    private static ReviewItem ReviewBoostTriggers(List<Entity> entities)
    {
        List<BoostTriggers.BoostTrigger> boosts = BoostTriggers.Find(entities);
        return new ReviewItem(
            "Boost Triggers",
            ReviewSeverity.Review,
            "boost triggers",
            boosts.Count,
            boosts.Count(x => EntityTools.IsTagged(x.Entity)),
            "walk into each boost in game and apply the fix for how it's used: a cooldown for boosts taken in the air, OnJump for ones jumped on, trigger_setspeed for ones walked into, a ground filter for ones stood on",
            DocsLinks.BoostTriggers,
            InGame: true);
    }

    private static ReviewItem ReviewGravityTriggers(List<Entity> entities)
    {
        List<Entity> gravity = entities.Where(x => x.ClassName.Equals("trigger_gravity", StringComparison.OrdinalIgnoreCase)).ToList();
        return new ReviewItem(
            "Gravity Triggers",
            ReviewSeverity.Review,
            "gravity triggers",
            gravity.Count,
            gravity.Count(x => x.ContainsKey("persist")),
            "set persist to 1 in Lumper on the ones meant to change gravity for good: every game before CS:GO kept the change after the trigger, so ports from those need it (the porter sets it for them)",
            DocsLinks.GravityTriggers);
    }

    private static ReviewItem ReviewWeaponStrips(List<Entity> entities)
    {
        int strips = entities.Count(x => x.ClassName is "player_weaponstrip" or "game_player_equip");
        return new ReviewItem(
            "Weapon strip",
            ReviewSeverity.Review,
            "weapon strip entities",
            strips,
            0,
            "remove player_weaponstrip/game_player_equip in Lumper unless changing weapons is part of the map",
            DocsLinks.ChecklistLumper);
    }

    private static ReviewItem ReviewLogicTimers(List<Entity> entities)
    {
        int timers = entities.Count(x => x.ClassName.Equals("logic_timer", StringComparison.OrdinalIgnoreCase));
        return new ReviewItem(
            "logic_timer",
            ReviewSeverity.Note,
            "logic_timers",
            timers,
            0,
            "old surf maps use one to send players to a jail after a while; remove it in Lumper if so (timers driving a display can stay)",
            DocsLinks.LogicTimer);
    }

    // Momentum's server has no VScript, so a script, and any input that runs one, does nothing
    private static readonly HashSet<string> ScriptInputs = new(StringComparer.OrdinalIgnoreCase) { "RunScriptCode", "RunScriptFile", "CallScriptFunction", "RunScriptCodeQuotable" };

    private static ReviewItem ReviewScripts(List<Entity> entities)
    {
        int scripts = entities.Count(x =>
            x.ClassName.Equals("logic_script", StringComparison.OrdinalIgnoreCase)
            || x["vscripts"].Length > 0
            || x.connections.Any(c => ScriptInputs.Contains(c.action)));
        return new ReviewItem(
            "VScript",
            ReviewSeverity.Review,
            "entities using VScript",
            scripts,
            0,
            "Momentum doesn't run VScript, so their scripts and RunScriptCode outputs do nothing; find what each did in game and rebuild what the map needs in Lumper (a teleport's VelocityMode, trigger_setspeed...)",
            DocsLinks.ChecklistLumper,
            Singular: "entity using VScript");
    }

    private static ReviewItem ReviewButtons(List<Entity> entities)
    {
        List<Entity> buttons = entities.Where(x => x.ClassName.Equals("func_button", StringComparison.OrdinalIgnoreCase)).ToList();
        return new ReviewItem(
            "func_button",
            ReviewSeverity.Note,
            "buttons",
            buttons.Count,
            buttons.Count(x => (x.GetInt("spawnflags", 0) & 1) != 0),
            "buttons reset much slower in Momentum; if one holds players up, add 1 (Don't move) to its spawnflags in Lumper",
            DocsLinks.FuncButton);
    }

    /// <summary>Momentum's runtime block fix (and the tools' export of it) only runs for these maps.</summary>
    public static bool HasBlockFixPrefix(string mapName)
    {
        return mapName.StartsWith("bhop_", StringComparison.OrdinalIgnoreCase) || mapName.StartsWith("kz_", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct Box(Vector3 Mins, Vector3 Maxs)
    {
        public bool Contains(Vector3 point) =>
            point.X >= Mins.X && point.X <= Maxs.X && point.Y >= Mins.Y && point.Y <= Maxs.Y && point.Z >= Mins.Z && point.Z <= Maxs.Z;
    }

    // A world brush with its bounds, to rule out most brushes before the exact plane test
    private readonly record struct ClipBrush(int Brush, Box Bounds);

    // CONTENTS_PLAYERCLIP brushes of the world
    private static List<ClipBrush> GetClipBrushes(BSP bsp)
    {
        const int PlayerClip = 0x10000;
        var brushes = new List<ClipBrush>();
        if (bsp.Models is null || bsp.Models.Count == 0 || bsp.Brushes is null)
            return brushes;

        foreach ((int brush, _) in BrushGeometry.GetBrushes(bsp, 0))
        {
            if ((bsp.Brushes[brush].Contents & PlayerClip) == 0)
                continue;

            if (BrushGeometry.GetBrushBounds(bsp, brush) is (Vector3 mins, Vector3 maxs))
                brushes.Add(new ClipBrush(brush, new Box(mins, maxs)));
        }

        return brushes;
    }
}
