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

    // The mpbhop pattern of CS:S bhop maps (bhop_arcane_v1 and friends)
    private static List<Entity> MpBhopEntities(string filterName = "filter_teleport")
    {
        Entity trigger = Make("trigger_multiple", ("model", "*354"), ("wait", "0"), ("spawnflags", "1"), ("angles", "0 0 0"));
        trigger.connections.Add(Output("OnTrigger", "AddOutput", "targetname default", 0.1f));
        trigger.connections.Add(Output("OnTrigger", "AddOutput", "targetname teleport", 0.09f));
        return
        [
            Make("filter_activator_name", ("targetname", filterName), ("filtername", "teleport"), ("Negated", "0")),
            Make("trigger_teleport", ("model", "*160"), ("target", "stage2"), ("filtername", filterName), ("angles", "0 0 0"), ("spawnflags", "1")),
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
    }

    [Fact]
    public void FindsBoostTriggers()
    {
        Entity baseVelocity = Make("trigger_multiple", ("model", "*1"));
        baseVelocity.connections.Add(Output("OnStartTouch", "AddOutput", "basevelocity 0 0 500", 0));

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
        string filterName = before[0].FilterName;
        int filteredTeleports = context.OfClass("trigger_teleport").Count(x => x["filtername"] == filterName);
        Assert.True(filteredTeleports > 0);

        new CounterStrikeSourceProfile().Apply(context);

        Assert.Empty(BhopTriggers.Find(context));
        Assert.True(BhopTriggers.IsFixed(context.Entities));
        Entity multi = Assert.Single(context.Entities, x => x.Name == EntityTools.BhopFilterMultiName);
        Assert.Equal("filter_multi", multi.ClassName);
        Assert.Equal(filterName, multi["filter01"]);
        Assert.Equal(EntityTools.BhopFilterContextName, multi["filter02"]);
        Entity contextFilter = Assert.Single(context.Entities, x => x.Name == EntityTools.BhopFilterContextName);
        Assert.Equal("_mom_leftground:*", contextFilter["ResponseContext"]);
        Assert.Equal("1", contextFilter["negated"]);

        Assert.Equal(filteredTeleports, context.OfClass("trigger_teleport").Count(x => x["filtername"] == EntityTools.BhopFilterMultiName && EntityTools.IsTagged(x)));
        Assert.DoesNotContain(context.OfClass("trigger_teleport"), x => x["filtername"] == filterName);

        Entity trigger = before[0].Entity;
        Assert.Equal("2", trigger[EntityTools.PortedByToolsKey]);
        Assert.False(trigger.ContainsKey("angles"));
        List<Entity.EntityConnection> onLand = trigger.connections.Where(x => x.name == "OnLand").ToList();
        Assert.Equal(["targetname teleport", "targetname default", "_mom_leftground"], onLand.Select(x => x.param));
        Assert.Equal(["AddOutput", "AddOutput", "RemoveContext"], onLand.Select(x => x.action));
        Assert.DoesNotContain(trigger.connections, x => x.name == "OnTrigger" && x.param.StartsWith("targetname"));
        Assert.Contains(trigger.connections, x => x.name == "OnJump" && x.action == "AddContext" && x.param == "_mom_leftground:1");
        Assert.Contains(trigger.connections, x => x.name == "OnEndTouch" && x.action == "AddContext");

        // The review sees the fix as done and the pushes as still to be looked at
        EntityReview review = EntityReview.Analyze(context);
        output.WriteLine(string.Join(Environment.NewLine, review.Lines()));
        ReviewItem bhop = Assert.Single(review.Items, x => x.Tool == "Bhop Trigger Fix");
        Assert.Equal(before.Count, bhop.Found);
        Assert.False(bhop.IsPending);
        ReviewItem boosts = Assert.Single(review.Items, x => x.Tool == "Boost Triggers");
        Assert.True(boosts.Pending > 0);
        Assert.True(review.NeedsManualChanges);
        Assert.False(review.HasMissingRequiredFixes);
        Assert.Contains("boost triggers", review.Summary);
        Assert.True(review.ThroughEntityTools);

        using var writer = new StringWriter();
        context.Report.Print(writer);
        Assert.Contains("bhop trigger fix: trigger_multiple renames moved to OnLand", writer.ToString());
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
        Assert.Contains(ported.Entities, x => x.Name == EntityTools.BhopFilterMultiName);
        EntityReview again = EntityReview.AnalyzeFile(outputPath);
        Assert.False(again.Items.Single(x => x.Tool == "Bhop Trigger Fix").IsPending);
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
