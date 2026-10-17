namespace MomentumPorter.Tests;

using System.Numerics;
using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Detection;
using MomentumPorter.Games.CounterStrikeSource;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Review;
using MomentumPorter.Steam;
using MomentumPorter.Steps;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// The entity review and the CS:S fixes. The pattern matching runs on hand-made entities; the rest needs
/// Counter-Strike: Source's stock bhop maps, or Momentum's installed maps that have been through the in-game entity
/// tools, and does nothing when they aren't installed.
/// </summary>
public class EntityReviewTests(ITestOutputHelper output)
{
    private static Entity Make(string className, params (string Key, string Value)[] keys)
    {
        var entity = new Entity(className);
        foreach ((string key, string value) in keys)
            entity[key] = value;
        return entity;
    }

    private static Entity.EntityConnection Output(string name, string action, string param, float delay, string target = "!activator")
    {
        return new Entity.EntityConnection { name = name, target = target, action = action, param = param, delay = delay, fireOnce = -1 };
    }

    // The mpbhop pattern of CS:S bhop maps (bhop_arcane_v1 and friends). Giving the trigger and the teleport the same
    // model puts the trigger inside the teleport.
    private static List<Entity> MpBhopEntities(string filterName = "filter_teleport", string playerName = "teleport", string triggerModel = "*354", string teleportModel = "*160", string target = "stage2")
    {
        Entity trigger = Make("trigger_multiple", ("model", triggerModel), ("wait", "0"), ("spawnflags", "1"), ("angles", "0 0 0"));
        trigger.connections.Add(Output("OnTrigger", "AddOutput", "targetname default", 0.1f));
        trigger.connections.Add(Output("OnTrigger", "AddOutput", $"targetname {playerName}", 0.09f));
        return
        [
            Make("filter_activator_name", ("targetname", filterName), ("filtername", playerName), ("Negated", "0")),
            Make("trigger_teleport", ("model", teleportModel), ("target", target), ("filtername", filterName), ("angles", "0 0 0"), ("spawnflags", "1")),
            trigger,
        ];
    }

    [Fact]
    public void FindsMpBhopTriggers()
    {
        List<Entity> entities = MpBhopEntities();

        BhopTriggers.BhopTrigger found = Assert.Single(BhopTriggers.Find(entities));

        Assert.Same(entities[2], found.Entity);
        Assert.Equal("filter_teleport", found.FilterName);
        Assert.Equal("teleport", found.Outputs[0].Targetname);
        Assert.Equal(0.09f, found.Outputs[0].Delay);
        Assert.Equal("default", found.Outputs[1].Targetname);
    }

    [Fact]
    public void IgnoresTriggersThatArentBhopTriggers()
    {
        // A rename that comes too late
        List<Entity> late = MpBhopEntities();
        late[2].connections[1] = Output("OnTrigger", "AddOutput", "targetname teleport", 2f);
        Assert.Empty(BhopTriggers.Find(late));

        // Only one rename
        List<Entity> single = MpBhopEntities();
        single[2].connections.RemoveAt(0);
        Assert.Empty(BhopTriggers.Find(single));

        // No teleport uses the filter
        List<Entity> unfiltered = MpBhopEntities();
        unfiltered[1].Remove("filtername");
        Assert.Empty(BhopTriggers.Find(unfiltered));

        // The filter lets another name through
        List<Entity> otherName = MpBhopEntities();
        otherName[0]["filtername"] = "somebody";
        Assert.Empty(BhopTriggers.Find(otherName));

        // A one-time teleporter that removes itself (bhop_quarry's teleporter_N)
        List<Entity> teleporter = MpBhopEntities();
        teleporter[2].connections.Add(Output("OnTrigger", "Kill", "", 0, "!self"));
        Assert.Empty(BhopTriggers.Find(teleporter));
        List<Entity> namedTeleporter = MpBhopEntities();
        namedTeleporter[2]["targetname"] = "teleporter_1";
        namedTeleporter[2].connections.Add(Output("OnUser1", "Kill", "", 4, "teleporter_1"));
        Assert.Empty(BhopTriggers.Find(namedTeleporter));
    }

    [Fact]
    public void FindsBoostTriggers()
    {
        Entity baseVelocity = Make("trigger_multiple", ("model", "*1"));
        baseVelocity.connections.Add(Output("OnStartTouch", "AddOutput", "basevelocity 0 0 500", 0));
        baseVelocity.connections.Add(Output("OnEndTouch", "AddOutput", "basevelocity 0 50 80", 0));

        // Straight up only, like kz_bounce's nudges under its jump pads
        Entity vertical = Make("trigger_multiple", ("model", "*10"));
        vertical.connections.Add(Output("OnStartTouch", "AddOutput", "basevelocity 0 0 5", 0));

        Entity gravity = Make("trigger_multiple", ("model", "*2"));
        gravity.connections.Add(Output("OnEndTouch", "AddOutput", "gravity 0.5", 0));
        gravity.connections.Add(Output("OnEndTouch", "AddOutput", "gravity 1", 2));

        // A gravity change with no reset isn't a boost
        Entity permanentGravity = Make("trigger_multiple", ("model", "*3"));
        permanentGravity.connections.Add(Output("OnEndTouch", "AddOutput", "gravity 0.5", 0));

        Entity activator = Make("trigger_multiple", ("model", "*4"));
        activator.connections.Add(Output("OnStartTouch", "AddOutput", "targetname boosted", 0));

        List<Entity> entities =
        [
            Make("trigger_push", ("model", "*5"), ("pushdir", "0 315 0"), ("speed", "500")),
            Make("trigger_push", ("model", "*6"), ("pushdir", "-90 0 0"), ("speed", "500")),
            // Straight up, but the entity is tilted
            Make("trigger_push", ("model", "*7"), ("pushdir", "-90 0 0"), ("angles", "45 0 0"), ("speed", "500")),
            Make("trigger_setspeed", ("model", "*8"), ("HorizontalSpeed", "1000")),
            baseVelocity,
            vertical,
            gravity,
            permanentGravity,
            activator,
            Make("filter_activator_name", ("targetname", "boost_filter"), ("filtername", "boosted")),
            Make("trigger_push", ("model", "*9"), ("pushdir", "-90 0 0"), ("filtername", "boost_filter"), ("speed", "900")),
        ];

        List<BoostTriggers.BoostTrigger> boosts = BoostTriggers.Find(entities);

        Assert.Equal(
            [BoostTriggers.BoostKind.Push, BoostTriggers.BoostKind.Push, BoostTriggers.BoostKind.SetSpeed, BoostTriggers.BoostKind.BaseVelocity, BoostTriggers.BoostKind.Gravity, BoostTriggers.BoostKind.ActivatesPush],
            boosts.Select(x => x.Kind));
        Assert.Equal(["*5", "*7", "*8", "*1", "*2", "*4"], boosts.Select(x => x.Entity.Model));
        Assert.Equal("basevelocity 0 50 80 on OnEndTouch", boosts[3].Description);
    }

    [Fact]
    public void SourceAnglesFollowTheEngine()
    {
        Assert.Equal(new Vector3(1, 0, 0), SourceAngles.Forward(Vector3.Zero));
        Vector3 up = SourceAngles.Forward(new Vector3(-90, 0, 0));
        Assert.True(Vector3.Distance(up, new Vector3(0, 0, 1)) < 0.001f);
        Vector3 east = SourceAngles.Forward(new Vector3(0, 90, 0));
        Assert.True(Vector3.Distance(east, new Vector3(0, 1, 0)) < 0.001f);

        // Yawing the entity 90 degrees turns its forward from +X to +Y
        Vector3 rotated = SourceAngles.Rotate(new Vector3(1, 0, 0), new Vector3(0, 90, 0));
        Assert.True(Vector3.Distance(rotated, new Vector3(0, 1, 0)) < 0.001f);
        Assert.Equal(new Vector3(1, 2, 3), SourceAngles.Rotate(new Vector3(1, 2, 3), Vector3.Zero));
    }

    [Fact]
    public void SubmissionStepsLinkEachStepToTheDocs()
    {
        IReadOnlyList<NextStep> steps = SubmissionSteps.For("bhop_test", review: null, zoneFileWritten: false, mountedGame: "Counter-Strike: Source");

        Assert.All(steps, x => Assert.StartsWith(DocsLinks.Root, x.Url));
        Assert.All(steps, x => Assert.NotNull(x.Uri));
        NextStep zoning = Assert.Single(steps, x => x.Title == "Zone the map");
        Assert.Contains("zones/local/bhop_test.json", zoning.Text);
        Assert.Equal(DocsLinks.ZoningSetup, zoning.Url);
        NextStep screenshots = Assert.Single(steps, x => x.Title == "Take screenshots");
        Assert.Contains("mom_official_screenshot", screenshots.Text);
        NextStep submit = Assert.Single(steps, x => x.Title == "Submit on the dashboard");
        Assert.Contains("Counter-Strike: Source under required games", submit.Text);
        Assert.Equal(DocsLinks.ReviewChecklist, steps[^1].Url);
    }

    [Fact]
    public void SubmissionStepsOnlySendToTheEntityToolsForInGameFixes()
    {
        if (FindCssMap("de_dust") is not { } path)
            return;

        var context = new PortContext(new BSP(new FileInfo(path)), "de_dust", null);
        foreach (Entity entity in context.Entities.Where(x => x.ClassName is "trigger_teleport" or "trigger_push").ToList())
            context.Bsp.Entities.Remove(entity);
        context.Bsp.Entities.Add(Make("trigger_gravity", ("model", "*1"), ("gravity", "0.5")));

        EntityReview review = EntityReview.Analyze(context);
        Assert.DoesNotContain(review.Pending, x => x.InGame);
        Assert.True(review.Items.Single(x => x.Tool == "Gravity Triggers").IsPending);
        IReadOnlyList<NextStep> steps = SubmissionSteps.For("de_dust", review, zoneFileWritten: false, mountedGame: null);
        Assert.DoesNotContain(steps, x => x.Title == "Fix entities in game");
        Assert.Contains(steps, x => x.Title == "Check the entities in game");
        NextStep gravity = Assert.Single(steps, x => x.Title.StartsWith("Gravity Triggers"));
        Assert.Contains("in Lumper", gravity.Text);

        context.Bsp.Entities.Add(Make("trigger_push", ("model", "*2"), ("pushdir", "0 90 0"), ("speed", "800")));
        review = EntityReview.Analyze(context);
        steps = SubmissionSteps.For("de_dust", review, zoneFileWritten: false, mountedGame: null);
        int setup = steps.ToList().FindIndex(x => x.Title == "Fix entities in game");
        Assert.True(setup >= 0);
        Assert.True(steps.ToList().FindIndex(x => x.Title.StartsWith("Gravity Triggers")) < setup);
        Assert.True(steps.ToList().FindIndex(x => x.Title.StartsWith("Boost Triggers")) > setup);
    }

    // Two brush models of a map, far enough apart that a player standing in one isn't near the other
    private static (string First, string Second) TwoBrushModels(BSP bsp)
    {
        var models = new List<(string Model, Vector3 Mins, Vector3 Maxs)>();
        for (int i = 1; i < bsp.Models.Count; i++)
        {
            var bounds = BrushGeometry.GetBrushes(bsp, i).Select(x => BrushGeometry.GetBrushBounds(bsp, x.Brush)).OfType<(Vector3 Mins, Vector3 Maxs)>().ToList();
            if (bounds.Count > 0)
                models.Add(($"*{i}", bounds.Select(x => x.Mins).Aggregate(Vector3.Min), bounds.Select(x => x.Maxs).Aggregate(Vector3.Max)));
        }

        var first = models[0];
        var second = models.First(x => Vector3.Max(Vector3.Zero, Vector3.Max(first.Mins - x.Maxs, x.Mins - first.Maxs)).Length() > 200);
        return (first.Model, second.Model);
    }

    private static string ReportText(PortContext context)
    {
        using var writer = new StringWriter();
        context.Report.Print(writer);
        return writer.ToString();
    }

    [Fact]
    public void FixesBhopTriggersOfEveryTeleportFilter()
    {
        if (FindCssMap("de_dust") is not { } path)
            return;

        var context = new PortContext(new BSP(new FileInfo(path)), "bhop_test", null);
        (string a, string b) = TwoBrushModels(context.Bsp);
        List<Entity> first = MpBhopEntities("filter_a", "teleport_a", a, a, "stage2");
        first[1]["UseLandmarkAngles"] = "1";
        first[1]["VelocityMode"] = "1";
        first[2].connections.Add(Output("OnTrigger", "Open", "", 0, "door"));
        // An old map's teleport that keeps the player's angles, and a rename without a delay
        List<Entity> second = MpBhopEntities("filter_b", "teleport_b", b, b, "stage3");
        second[1]["spawnflags"] = "33";
        second[2].connections[1] = Output("OnTrigger", "AddOutput", "targetname teleport_b", 0);
        foreach (Entity entity in first.Concat(second))
            context.Bsp.Entities.Add(entity);

        Assert.Equal(2, BhopTriggers.Fix(context));

        Assert.Empty(BhopTriggers.Find(context));
        Entity fixedA = first[2];
        Assert.Equal("trigger_momentum_bhop", fixedA.ClassName);
        Assert.Equal("stage2", fixedA["target"]);
        Assert.Equal(0.09f, fixedA.GetFloat("TimeLimit", 0), 3);
        Assert.Equal(-1, fixedA.GetInt("LandingLimit", 0));
        Assert.Equal(0, fixedA.GetInt("ProgressTeleport", 1));
        Assert.Equal(1, fixedA.GetInt("UseDestinationAngles", 0));
        Assert.Equal(1, fixedA.GetInt("ResetVelocityOnTeleport", 0));
        Assert.Equal(3, fixedA.GetInt("spawnflags", 0));
        Assert.False(fixedA.ContainsKey("wait"));
        Assert.False(fixedA.ContainsKey("angles"));
        Assert.True(EntityTools.IsTagged(fixedA));
        // The renames are gone, the trigger's other outputs stay
        Entity.EntityConnection open = Assert.Single(fixedA.connections);
        Assert.Equal(("OnStartTouch", "door", "Open"), (open.name, open.target, open.action));

        Entity fixedB = second[2];
        Assert.Equal("stage3", fixedB["target"]);
        Assert.Equal(0, fixedB.GetInt("UseDestinationAngles", 1));
        Assert.Equal(0, fixedB.GetInt("ResetVelocityOnTeleport", 1));
        Assert.True(fixedB.GetFloat("TimeLimit", 0) is > 0 and < 0.01f);
        Assert.Empty(fixedB.connections);

        // The teleports stay as they were; nothing renames the player for them any more
        Assert.Equal("filter_a", first[1]["filtername"]);
        Assert.False(EntityTools.IsTagged(first[1]));
        Assert.True(BhopTriggers.IsFixed(context.Entities));
        Assert.False(EntityReview.Analyze(context).Items.Single(x => x.Tool == "Bhop Trigger Fix").IsPending);
        Assert.DoesNotContain("warning", ReportText(context));
    }

    // Where trigger_momentum_bhop can't do what the map's teleport did, the trigger stays as the map had it
    [Theory]
    [InlineData("different places", "it's inside teleports to different places")]
    [InlineData("away", "no teleport its renames are for is under it")]
    [InlineData("landmark", "its teleport has a landmark")]
    [InlineData("velocity", "its teleport has VelocityMode 2")]
    [InlineData("disabled", "its teleport starts disabled")]
    [InlineData("toggled", "its teleport is changed by the map's outputs")]
    public void LeavesBhopTriggersWhoseTeleportCantBeCopied(string scenario, string reason)
    {
        if (FindCssMap("de_dust") is not { } path)
            return;

        var context = new PortContext(new BSP(new FileInfo(path)), "bhop_test", null);
        (string a, string b) = TwoBrushModels(context.Bsp);
        List<Entity> entities = MpBhopEntities(triggerModel: a, teleportModel: scenario == "away" ? b : a);
        Entity teleport = entities[1];
        switch (scenario)
        {
            case "different places":
                entities.Add(Make("trigger_teleport", ("model", a), ("target", "elsewhere"), ("filtername", "filter_teleport"), ("spawnflags", "1")));
                break;
            case "landmark":
                teleport["landmark"] = "landmark_1";
                break;
            case "velocity":
                teleport["VelocityMode"] = "2";
                break;
            case "disabled":
                teleport["StartDisabled"] = "1";
                break;
            case "toggled":
                teleport["targetname"] = "stage2_teleport";
                Entity relay = Make("logic_relay", ("targetname", "open_stage2"));
                relay.connections.Add(Output("OnTrigger", "Disable", "", 0, "stage2_teleport"));
                entities.Add(relay);
                break;
        }

        foreach (Entity entity in entities)
            context.Bsp.Entities.Add(entity);

        BhopTriggers.Fix(context);

        Entity trigger = entities[2];
        Assert.Equal("trigger_multiple", trigger.ClassName);
        Assert.Equal("0", trigger["wait"]);
        Assert.Equal(["targetname default", "targetname teleport"], trigger.connections.Where(x => x.name == "OnTrigger").Select(x => x.param));
        Assert.True(EntityTools.IsTagged(trigger));
        Assert.Empty(context.OfClass("trigger_momentum_bhop"));
        Assert.Contains($"warning: bhop trigger fix: left the bhop trigger {a} as it was, since {reason}", ReportText(context));

        // Done as far as the review goes, and the fix leaves it alone when run again
        Assert.Empty(BhopTriggers.Find(context));
        Assert.Equal(0, BhopTriggers.Fix(context));
        Assert.False(EntityReview.Analyze(context).Items.Single(x => x.Tool == "Bhop Trigger Fix").IsPending);
    }

    [Fact]
    public void SplitsABrushModelIntoGroups()
    {
        if (FindCssMap("de_dust") is not { } path)
            return;

        var bsp = new BSP(new FileInfo(path));
        int model = Enumerable.Range(1, bsp.Models.Count - 1).First(x => BrushGeometry.GetBrushes(bsp, x).Count >= 3);
        List<int> brushes = BrushGeometry.GetBrushes(bsp, model).Select(x => x.Brush).ToList();
        List<IReadOnlyList<int>> groups = [[brushes[0], brushes[2]], brushes.Where((_, i) => i != 0 && i != 2).ToList()];

        List<int> models = new BrushModelEditor(bsp).SplitModel(model, groups);

        Assert.Equal(model, models[0]);
        Assert.Equal(bsp.Models.Count - 1, models[1]);
        for (int i = 0; i < groups.Count; i++)
            Assert.Equal(groups[i].Order(), BrushGeometry.GetBrushes(bsp, models[i]).Select(x => x.Brush));
    }

    // ---- Counter-Strike: Source's own bhop maps ----

    private static string? FindCssMap(string name)
    {
        GameLocation? css = new SteamLocator().Find(KnownGame.CounterStrikeSource);
        if (css?.ContentDirectory is null)
            return null;

        string path = Path.Combine(css.ContentDirectory, "maps", name + ".bsp");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void DetectsCounterStrikeSourceMaps()
    {
        if (FindCssMap("bhop_arcane_v1") is not { } path)
            return;

        var bsp = new BSP(new FileInfo(path));
        SourceGameSniffer.Detection? detection = SourceGameSniffer.Detect(bsp, null, "bhop_arcane_v1");
        Assert.NotNull(detection);
        Assert.Equal("css", detection.Profile);
        Assert.True(detection.Certain);

        // Without its CS entities the name is all there is to go by
        foreach (Entity entity in bsp.Entities.Where(x => x.ClassName.StartsWith("info_player_", StringComparison.OrdinalIgnoreCase) || x.ClassName.StartsWith("func_buyzone", StringComparison.OrdinalIgnoreCase)).ToList())
            bsp.Entities.Remove(entity);
        Assert.Equal(("css", false), SourceGameSniffer.Detect(bsp, null, "surf_test") is { } byName ? (byName.Profile, byName.Certain) : (null, true));
        Assert.Equal("kzmod", SourceGameSniffer.Detect(bsp, null, "kz_test")?.Profile);
        Assert.Null(SourceGameSniffer.Detect(bsp, null, "de_test"));
    }

    [Fact]
    public void FixesTheBhopTriggersOfACssBhopMap()
    {
        if (FindCssMap("bhop_arcane_v1") is not { } path)
            return;

        var bsp = new BSP(new FileInfo(path));
        var context = new PortContext(bsp, "bhop_arcane_v1", null);
        List<BhopTriggers.BhopTrigger> before = BhopTriggers.Find(context);
        Assert.NotEmpty(before);
        int brushes = before.Sum(x => BrushGeometry.GetBrushes(bsp, x.Entity.ModelNumber).Count);
        string filterName = before[0].FilterName;
        int filteredTeleports = context.OfClass("trigger_teleport").Count(x => x["filtername"] == filterName);
        Assert.True(filteredTeleports > 0);

        // As the Convert entities step does after the profile, then its Fix entities step
        new CounterStrikeSourceProfile().Apply(context);
        BhopTriggers.Fix(context);
        MomentumPorter.Fixes.DeadEntities.Remove(context);
        MomentumPorter.Fixes.GlobalStates.Remove(context);

        Assert.Empty(BhopTriggers.Find(context));
        Assert.True(BhopTriggers.IsFixed(context.Entities));

        // Each trigger covers a stage of blocks over teleports to different places, so it's split by destination
        List<Entity> bhops = context.OfClass("trigger_momentum_bhop").ToList();
        Assert.True(bhops.Count > before.Count);
        Assert.All(bhops, x => Assert.True(EntityTools.IsTagged(x)));
        Assert.Equal(brushes, bhops.Sum(x => BrushGeometry.GetBrushes(bsp, x.ModelNumber).Count));
        var targets = context.OfClass("trigger_teleport").Where(x => x["filtername"] == filterName).Select(x => x["target"]).ToHashSet();
        Assert.All(bhops, x => Assert.Contains(x["target"], targets));
        Assert.All(bhops.Where(x => x["hammerid"].Length > 0).GroupBy(x => x["hammerid"]), x => Assert.Equal(x.Count(), x.Select(y => y["target"]).Distinct().Count()));
        Assert.All(bhops, x => Assert.DoesNotContain(x.connections, c => (c.param ?? "").StartsWith("targetname")));

        // The teleports stay as they were
        Assert.Equal(filteredTeleports, context.OfClass("trigger_teleport").Count(x => x["filtername"] == filterName && !EntityTools.IsTagged(x)));

        // The review sees the fix as done and the pushes as still to be looked at
        EntityReview review = EntityReview.Analyze(context);
        output.WriteLine(string.Join(Environment.NewLine, review.Lines()));
        ReviewItem bhop = Assert.Single(review.Items, x => x.Tool == "Bhop Trigger Fix");
        Assert.Equal(bhops.Count, bhop.Found);
        Assert.False(bhop.IsPending);
        ReviewItem boosts = Assert.Single(review.Items, x => x.Tool == "Boost Triggers");
        Assert.True(boosts.Pending > 0);
        Assert.True(review.NeedsManualChanges);
        Assert.False(review.HasMissingRequiredFixes);
        Assert.Contains("boost triggers", review.Summary);
        Assert.True(review.ThroughEntityTools);

        using var writer = new StringWriter();
        context.Report.Print(writer);
        Assert.Contains("bhop trigger fix: trigger_multiple -> trigger_momentum_bhop", writer.ToString());
        Assert.DoesNotContain("warning: bhop trigger fix", writer.ToString());
    }

    [Fact]
    public void ReviewsAnUnfixedCssMap()
    {
        if (FindCssMap("bhop_monster_jam") is not { } path)
            return;

        EntityReview review = EntityReview.AnalyzeFile(path);
        output.WriteLine(string.Join(Environment.NewLine, review.Lines()));

        Assert.False(review.ThroughEntityTools);
        ReviewItem gravity = Assert.Single(review.Items, x => x.Tool == "Gravity Triggers");
        Assert.Equal(3, gravity.Found);
        Assert.Equal(3, gravity.Pending);
        ReviewItem timers = Assert.Single(review.Items, x => x.Tool == "logic_timer");
        Assert.Equal(3, timers.Found);
        Assert.Equal(ReviewSeverity.Note, timers.Severity);
        Assert.Contains("gravity triggers", review.Summary);
        Assert.Contains(review.Lines(), x => x.Contains(DocsLinks.GravityTriggers));

        var report = new PortReport();
        review.Report(report);
        Assert.True(report.HasWarnings);
    }

    [Fact]
    public async Task PortsACssMapAndReviewsIt()
    {
        if (FindCssMap("bhop_arcane_v1") is not { } input)
            return;

        using var temp = new TempFolder();
        string outputPath = temp.Combine("bhop_arcane_v1.bsp");
        (PortJob job, MapPortState state) = PortJobFactory.CreateSourceMapJob(input, outputPath, new MapPortOptions { Pack = false, Install = false });
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = temp.Path };
        context.Set(state);

        PortJobResult result = await job.RunAsync(context);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("css", state.Profile?.Name);
        EntityReview? review = context.TryGet<EntityReview>();
        Assert.NotNull(review);
        Assert.True(review.NeedsManualChanges);

        var ported = new BSP(new FileInfo(outputPath));
        Assert.Contains(ported.Entities, x => x.ClassName == "trigger_momentum_bhop" && EntityTools.IsTagged(x));
        EntityReview again = EntityReview.AnalyzeFile(outputPath);
        Assert.False(again.Items.Single(x => x.Tool == "Bhop Trigger Fix").IsPending);
    }

    [Theory]
    [InlineData("bhop_arcane_v1", BhopTriggerFix.Off, true, ReviewSeverity.Review)]
    [InlineData("arcane_v1", BhopTriggerFix.BhopAndKzMaps, true, ReviewSeverity.Note)]
    [InlineData("kz_bhop_arcane", BhopTriggerFix.BhopAndKzMaps, false, ReviewSeverity.Review)]
    [InlineData("arcane_v1", BhopTriggerFix.On, false, ReviewSeverity.Note)]
    public async Task FixesBhopTriggersOnTheMapsTheOptionSays(string mapName, BhopTriggerFix fix, bool left, ReviewSeverity severity)
    {
        if (FindCssMap("bhop_arcane_v1") is not { } input)
            return;

        using var temp = new TempFolder();
        string outputPath = temp.Combine(mapName + ".bsp");
        var options = new MapPortOptions { Pack = false, Install = false, FixBhopTriggers = fix };
        (PortJob job, MapPortState state) = PortJobFactory.CreateSourceMapJob(input, outputPath, options);
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = temp.Path };
        context.Set(state);

        PortJobResult result = await job.RunAsync(context);

        Assert.True(result.Succeeded, result.Error);
        var ported = new BSP(new FileInfo(outputPath));
        Assert.Equal(!left, BhopTriggers.IsFixed(ported.Entities.ToList()));
        ReviewItem bhop = context.TryGet<EntityReview>()!.Items.Single(x => x.Tool == "Bhop Trigger Fix");
        Assert.Equal(left, bhop.IsPending);
        Assert.Equal(severity, bhop.Severity);
    }

    // ---- Maps in Momentum's maps folder that went through the in-game entity tools ----

    private static string? FindMomentumMap(string name)
    {
        string? maps = new SteamLocator().FindMomentum()?.ContentDirectory is { } content ? Path.Combine(content, "maps") : null;
        string? path = maps is null ? null : Path.Combine(maps, name + ".bsp");
        return path is not null && File.Exists(path) ? path : null;
    }

    [Theory]
    [InlineData("bhop_alvo2", "Bhop Trigger Fix")]
    [InlineData("bhop_abarcy_bhwc", "Model Scale Fix")]
    public void SeesWhatTheEntityToolsDidToInstalledMaps(string map, string tool)
    {
        if (FindMomentumMap(map) is not { } path)
            return;

        EntityReview review = EntityReview.AnalyzeFile(path);
        output.WriteLine(string.Join(Environment.NewLine, review.Lines()));

        Assert.True(review.ThroughEntityTools);
        ReviewItem item = Assert.Single(review.Items, x => x.Tool == tool);
        Assert.True(item.Found > 0, $"{tool} found nothing");
        Assert.Equal(0, item.Pending);
        Assert.False(review.HasMissingRequiredFixes);
        Assert.StartsWith("Verdict:", review.Verdict);
    }
}
