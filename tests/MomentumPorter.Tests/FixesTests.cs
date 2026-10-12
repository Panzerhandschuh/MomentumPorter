namespace MomentumPorter.Tests;

using LibBSP;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Fixes;
using MomentumPorter.Games.CounterStrikeSource;
using MomentumPorter.Games.Gokz;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Review;
using MomentumPorter.Steam;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// The fixes Lumper's entity review and the entity tools would otherwise ask for by hand. The entity rules run on
/// hand-made entities; the rest hosts them in an installed CS:S map, or uses Momentum's installed maps, and does
/// nothing when those aren't there.
/// </summary>
public class FixesTests(ITestOutputHelper output)
{
    private static Entity Make(string className, params (string Key, string Value)[] keys)
    {
        var entity = new Entity(className);
        foreach ((string key, string value) in keys)
            entity[key] = value;
        return entity;
    }

    private static string? FindCssMap(string name)
    {
        GameLocation? css = new SteamLocator().Find(KnownGame.CounterStrikeSource);
        string? path = css?.ContentDirectory is { } content ? Path.Combine(content, "maps", name + ".bsp") : null;
        return path is not null && File.Exists(path) ? path : null;
    }

    private static string? FindMomentumMap(string name)
    {
        string? path = new SteamLocator().FindMomentum()?.ContentDirectory is { } content ? Path.Combine(content, "maps", name + ".bsp") : null;
        return path is not null && File.Exists(path) ? path : null;
    }

    [Fact]
    public void DeadEntitiesAreTheOnesLumperDeniesForDoingNothing()
    {
        List<Entity> entities =
        [
            Make("trigger_changelevel", ("map", "de_dust2")),
            Make("point_servercommand", ("targetname", "cmd")),
            Make("func_buyzone", ("model", "*1")),
            Make("info_map_parameters"),
            Make("trigger_push", ("model", "*2")),
            Make("env_surface_teleport", ("model", "*3")),
            Make("weapon_knife"),
        ];

        List<Entity> dead = DeadEntities.Find(entities);

        // Entities that need replacing (env_surface_teleport) or a decision (weapons) aren't dead
        Assert.Equal(["trigger_changelevel", "point_servercommand", "func_buyzone", "info_map_parameters"], dead.Select(x => x.ClassName));
        Assert.All(dead, x => Assert.False(string.IsNullOrEmpty(DeadEntities.Rules[x.ClassName].Reason)));
        (List<Entity> denied, List<Entity> unknown) = DeadEntities.Sort(entities);
        Assert.Equal(["trigger_changelevel", "point_servercommand"], denied.Select(x => x.ClassName));
        Assert.Equal(["func_buyzone", "info_map_parameters"], unknown.Select(x => x.ClassName));
    }

    [Fact]
    public void GlobalStatesKeepTheOnesALogicAutoReads()
    {
        List<Entity> entities =
        [
            Make("env_global", ("targetname", "a_on"), ("globalstate", "a")),
            Make("env_global", ("targetname", "b_on"), ("globalstate", "b")),
            Make("logic_auto", ("globalstate", "B")),
            Make("logic_branch", ("targetname", "__goldsrc_global_a")),
        ];

        (List<Entity> removable, List<Entity> kept) = GlobalStates.Sort(entities);

        Assert.Equal(["a_on"], removable.Select(x => x.Name));
        Assert.Equal(["b_on"], kept.Select(x => x.Name));
    }

    [Fact]
    public void FixesRunOnAHostedMap()
    {
        if (FindCssMap("de_dust") is not { } path)
            return;

        var bsp = new BSP(new FileInfo(path));
        var context = new PortContext(bsp, "de_dust", null);
        int before = context.Entities.Count;
        Entity dead = Make("trigger_changelevel", ("map", "x"));
        Entity global = Make("env_global", ("targetname", "g"), ("globalstate", "s"));
        Entity keptGlobal = Make("env_global", ("targetname", "k"), ("globalstate", "read"));
        Entity auto = Make("logic_auto", ("globalstate", "read"));
        Entity gravity = Make("trigger_gravity", ("model", "*1"), ("gravity", "0.5"));
        Entity decided = Make("trigger_gravity", ("model", "*2"), ("gravity", "0.5"), ("persist", "0"));
        Entity command = Make("point_servercommand", ("targetname", "server"));
        Entity spawn = Make("logic_auto");
        spawn.connections.Add(new Entity.EntityConnection { name = "OnMapSpawn", target = "server", action = "Command", param = "sv_maxvelocity 3500", delay = 0, fireOnce = -1 });
        foreach (Entity entity in new[] { dead, global, keptGlobal, auto, gravity, decided, command, spawn })
            bsp.Entities.Add(entity);

        // de_dust has the stock CS entities a port drops
        int counterStrike = DeadEntities.Find(context.Entities).Count(x => x.ClassName is "func_buyzone" or "func_bomb_target" or "info_map_parameters");
        Assert.True(counterStrike > 0, "de_dust should have buy zones and bomb targets");

        int removed = DeadEntities.Remove(context) + GlobalStates.Remove(context);
        int persisted = GravityPersistence.Apply(context);

        Assert.Equal(counterStrike + 3, removed);
        Assert.Equal(1, persisted);
        Assert.DoesNotContain(dead, context.Entities);
        Assert.DoesNotContain(command, context.Entities);
        Assert.Contains(spawn, context.Entities);
        Assert.DoesNotContain(global, context.Entities);
        Assert.Contains(keptGlobal, context.Entities);
        Assert.Equal("1", gravity["persist"]);
        Assert.True(EntityTools.IsTagged(gravity));
        Assert.Equal("0", decided["persist"]);
        Assert.False(EntityTools.IsTagged(decided));
        Assert.Equal(before + 8 - removed, context.Entities.Count);

        using var writer = new StringWriter();
        context.Report.Print(writer);
        string report = writer.ToString();
        output.WriteLine(report);
        Assert.Contains("func_buyzone removed", report);
        Assert.Contains("point_servercommand removed", report);
        Assert.Contains("the map ran \"sv_maxvelocity 3500\" through a point_servercommand", report);
        Assert.Contains("env_global removed", report);
        Assert.Contains("env_global \"read\" kept", report);
        Assert.Contains("trigger_gravity set to persist", report);

        // The review now sees the gravity triggers as done and nothing invalid left
        EntityReview review = EntityReview.Analyze(context);
        Assert.Equal(0, review.Items.Single(x => x.Tool == "Invalid entities").Found);
        Assert.Equal(0, review.Items.Single(x => x.Tool == "Unknown entities").Found);
        Assert.Equal(1, review.Items.Single(x => x.Tool == "env_global").Pending);
        Assert.Equal(0, review.Items.Single(x => x.Tool == "Gravity Triggers").Pending);
    }

    [Fact]
    public void OnlyGamesThatKeptGravityGetPersist()
    {
        // Default interface members are reached through the interface
        Assert.True(((IGameProfile)new CounterStrikeSourceProfile()).GravityPersists);
        Assert.True(GameProfiles.Find("kzmod")!.GravityPersists);
        Assert.False(((IGameProfile)new GokzProfile()).GravityPersists);
    }

    [Fact]
    public void RemovesTheEnvGlobalsBspConvertLeavesBehind()
    {
        if (FindMomentumMap("kz_cg_venice") is not { } path)
            return;

        EntityReview before = EntityReview.AnalyzeFile(path);
        output.WriteLine(string.Join(Environment.NewLine, before.Lines()));
        Assert.Equal(4, before.Items.Single(x => x.Tool == "env_global").Found);
        Assert.True(before.HasMissingRequiredFixes);

        var bsp = new BSP(new FileInfo(path));
        var context = new PortContext(bsp, "kz_cg_venice", null);
        int branches = context.OfClass("logic_branch").Count();

        Assert.Equal(4, GlobalStates.Remove(context));

        Assert.Empty(context.OfClass("env_global"));
        Assert.Equal(branches, context.OfClass("logic_branch").Count());
        Assert.False(EntityReview.Analyze(context).HasMissingRequiredFixes);
    }

    [Fact]
    public void RemovesPackedFilesMomentumMountsUnlessTheyDiffer()
    {
        if (FindCssMap("bhop_arcane_v1") is not { } path)
            return;

        var steam = new SteamLocator();
        GameContent css;
        try
        {
            css = SourceGames.Open(SourceGames.CounterStrikeSource, steam);
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        using (css)
        {
            // A stock CS:S material packed as is, one the map changed, and one of its own
            string stock = css.List("materials/concrete/").First(x => x.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase));
            byte[] stockBytes = css.Read(stock)!;
            byte[] changed = (byte[])stockBytes.Clone();
            changed[^1] ^= 0xFF;
            string modified = css.List("materials/concrete/").Skip(1).First(x => x.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase));

            var bsp = new BSP(new FileInfo(path));
            var context = new PortContext(bsp, "bhop_arcane_v1", null);
            context.EditPakFiles(zip =>
            {
                zip.AddEntry(stock, new MemoryStream(stockBytes), closeStream: true);
                zip.AddEntry(modified, new MemoryStream(changed), closeStream: true);
                zip.AddEntry("materials/bhop_arcane_v1/own.vmt", new MemoryStream("\"LightmappedGeneric\" {}"u8.ToArray()), closeStream: true);
            });
            int packed = context.ListPakFiles().Count;

            var games = new List<MountedAssets.MountedGame> { new(SourceGames.CounterStrikeSource.Name, css) };
            int removed = MountedAssets.Remove(context, games);

            Assert.Equal(1, removed);
            Assert.False(context.HasPakFile(stock));
            Assert.True(context.HasPakFile(modified));
            Assert.True(context.HasPakFile("materials/bhop_arcane_v1/own.vmt"));
            Assert.Equal(packed - 1, context.ListPakFiles().Count);

            using var writer = new StringWriter();
            context.Report.Print(writer);
            Assert.Contains("packed files removed that Momentum mounts from Counter-Strike: Source", writer.ToString());
            Assert.Contains($"{modified} differs from Counter-Strike: Source's copy and was kept", writer.ToString());
        }
    }

    [Fact]
    public async Task ThePortJobRunsTheFixes()
    {
        if (FindCssMap("bhop_monster_jam") is not { } input)
            return;

        using var temp = new TempFolder();
        string outputPath = temp.Combine("bhop_monster_jam.bsp");
        (PortJob job, Steps.MapPortState state) = PortJobFactory.CreateSourceMapJob(input, outputPath, new Options.MapPortOptions { Pack = false, Install = false });
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = temp.Path };
        context.Set(state);

        PortJobResult result = await job.RunAsync(context);

        Assert.True(result.Succeeded, result.Error);
        var ported = new BSP(new FileInfo(outputPath));
        List<Entity> gravity = ported.Entities.Where(x => x.ClassName == "trigger_gravity").ToList();
        Assert.Equal(3, gravity.Count);
        Assert.All(gravity, x => Assert.Equal("1", x["persist"]));
        Assert.DoesNotContain(ported.Entities, x => x.ClassName is "func_buyzone" or "info_map_parameters" or "trigger_changelevel");
        Assert.Equal(0, context.Get<EntityReview>().Items.Single(x => x.Tool == "Gravity Triggers").Pending);
    }
}
