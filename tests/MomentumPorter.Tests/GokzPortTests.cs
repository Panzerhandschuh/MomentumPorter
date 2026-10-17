namespace MomentumPorter.Tests;

using System.Text.Json;
using BSPConvert.Lib.Zones;
using LibBSP;
using MomentumPorter.Detection;
using MomentumPorter.Games.Gokz;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Review;
using MomentumPorter.Steam;
using MomentumPorter.Steps;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// There's no GOKZ map to ship as a fixture, so these turn brush models of an installed Source map (KZ Mod's kz_dance,
/// else a Counter-Strike: Source map) into GOKZ mapping API entities and check each conversion rule. Nothing runs
/// when neither game is installed. <see cref="PortsInstalledGokzMaps"/> also ports real GOKZ maps from CS:GO if present.
/// </summary>
public class GokzPortTests(ITestOutputHelper output)
{
    private static readonly Lazy<string?> SourceMap = new(FindSourceMap);

    private static string? FindSourceMap()
    {
        var steam = new SteamLocator();
        if (steam.Find(KnownGame.KreedzClimbing)?.ContentDirectory is { } kz && File.Exists(Path.Combine(kz, "maps", "kz_dance.bsp")))
            return Path.Combine(kz, "maps", "kz_dance.bsp");

        if (steam.Find(KnownGame.CounterStrikeSource)?.ContentDirectory is { } css && Directory.Exists(Path.Combine(css, "maps")))
            return Directory.EnumerateFiles(Path.Combine(css, "maps"), "*.bsp").FirstOrDefault();

        return null;
    }

    /// <summary>
    /// A loaded Source map plus helpers to add GOKZ entities that reuse its brush models.
    /// </summary>
    private sealed class SyntheticMap
    {
        private readonly List<int> _models;
        private int _nextModel;

        public SyntheticMap(string path)
        {
            Bsp = new BSP(new FileInfo(path));
            _models = Bsp.Entities.Select(x => x.ModelNumber).Where(x => x > 0).Distinct().Order().ToList();
            Assert.NotEmpty(_models);

            // Some destination for climb_teleport to point at
            Add("info_teleport_destination", "dest", null, ("origin", "0 0 64"));
        }

        public BSP Bsp { get; }

        public PortContext Context { get; private set; } = null!;

        public PortReport Report => Context.Report;

        public Entity Add(string className, string name, int? model = -1, params (string Key, string Value)[] keys)
        {
            var entity = new Entity(className, Bsp.Entities) { Name = name };
            if (model == -1)
                model = _models[_nextModel++ % _models.Count];
            if (model is not null)
                entity.Model = $"*{model}";

            foreach ((string key, string value) in keys)
                entity[key] = value;

            Bsp.Entities.Add(entity);
            return entity;
        }

        public Entity AddTrigger(string name, params (string Key, string Value)[] keys) => Add("trigger_multiple", name, -1, keys);

        public void Port(string mapName = "gokz_test")
        {
            Context = new PortContext(Bsp, mapName, null);
            new GokzProfile().Apply(Context);
        }

        public string PrintReport()
        {
            using var writer = new StringWriter();
            Report.Print(writer);
            return writer.ToString();
        }
    }

    private static SyntheticMap? CreateMap()
    {
        return SourceMap.Value is { } path ? new SyntheticMap(path) : null;
    }

    private static void AssertNoClimbKeys(Entity entity)
    {
        Assert.DoesNotContain(entity.Keys, x => x.StartsWith("climb_", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("2", entity["_ported_by_tools"]);
    }

    [Fact]
    public void ConvertsTeleport()
    {
        if (CreateMap() is not { } map)
            return;

        Entity entity = map.AddTrigger(
            "climb_teleport",
            ("spawnflags", "4097"),
            ("wait", "1"),
            ("StartDisabled", "1"),
            ("filtername", "filter"),
            ("climb_teleport_type", "0"),
            ("climb_teleport_destination", "dest"),
            ("climb_teleport_delay", "0.5"),
            ("climb_teleport_use_dest_angles", "0"),
            ("climb_teleport_reset_speed", "0")
        );
        entity.connections.Add(new Entity.EntityConnection { name = "OnTrigger", target = "relay", action = "Trigger", param = "", delay = 0, fireOnce = -1 });
        string model = entity.Model;

        map.Port();

        Assert.Equal("trigger_momentum_teleport", entity.ClassName);
        Assert.Equal("4097", entity["spawnflags"]);
        Assert.Equal("dest", entity["target"]);
        Assert.Equal("0.5", entity["TeleportDelay"]);
        Assert.Equal("0", entity["VelocityMode"]);
        Assert.Equal("0", entity["UseLandmarkAngles"]);
        Assert.Equal(model, entity.Model);
        Assert.Equal("climb_teleport", entity.Name);
        Assert.Equal("1", entity["StartDisabled"]);
        Assert.Equal("filter", entity["filtername"]);
        Assert.False(entity.ContainsKey("wait"));
        Assert.Single(entity.connections);
        AssertNoClimbKeys(entity);
        Assert.Contains("climb_teleport -> trigger_momentum_teleport", map.PrintReport());
    }

    [Fact]
    public void ConvertsTeleportWithFgdDefaults()
    {
        if (CreateMap() is not { } map)
            return;

        Entity entity = map.AddTrigger("climb_teleport", ("climb_teleport_destination", "dest"));

        map.Port();

        Assert.Equal("trigger_momentum_teleport", entity.ClassName);
        Assert.Equal("1", entity["spawnflags"]);
        Assert.False(entity.ContainsKey("TeleportDelay"));
        Assert.Equal("1", entity["VelocityMode"]);
        Assert.Equal("1", entity["UseLandmarkAngles"]);
        Assert.DoesNotContain("warning: climb_teleport", map.PrintReport());
    }

    [Theory]
    [InlineData("1", "0", "1", "0", "0.08")]
    // A single bhop forgets a block when the player lands on another one, a sequential one only off them
    [InlineData("2", "0.1", "7", "1", "0.1")]
    [InlineData("3", "0.05", "3", "1", "0.08")]
    public void ConvertsBhopTriggers(string type, string delay, string spawnflags, string landingLimit, string timeLimit)
    {
        if (CreateMap() is not { } map)
            return;

        Entity entity = map.AddTrigger(
            "climb_teleport",
            ("spawnflags", "4097"),
            ("climb_teleport_type", type),
            ("climb_teleport_destination", "dest"),
            ("climb_teleport_delay", delay),
            ("climb_teleport_reset_speed", "0")
        );

        map.Port();

        Assert.Equal("trigger_momentum_bhop", entity.ClassName);
        Assert.Equal(spawnflags, entity["spawnflags"]);
        Assert.Equal(landingLimit, entity["LandingLimit"]);
        Assert.Equal(timeLimit, entity["TimeLimit"]);
        Assert.Equal("dest", entity["target"]);
        Assert.Equal("0", entity["ResetVelocityOnTeleport"]);
        Assert.Equal("1", entity["UseDestinationAngles"]);
        AssertNoClimbKeys(entity);

        string report = map.PrintReport();
        Assert.Contains("-> trigger_momentum_bhop", report);
        Assert.DoesNotContain("sequential bhop", report);
    }

    [Fact]
    public void ConvertsAntiCheckpoint()
    {
        if (CreateMap() is not { } map)
            return;

        Entity entity = map.AddTrigger("climb_anti_checkpoint", ("spawnflags", "4097"));

        map.Port();

        Assert.Equal("trigger_momentum_disallowcheckpoint", entity.ClassName);
        Assert.Equal("4097", entity["spawnflags"]);
        AssertNoClimbKeys(entity);
    }

    [Theory]
    [InlineData(null, "0.2")]
    [InlineData("0.5", "0.5")]
    public void ConvertsAntiBhop(string? time, string cooldown)
    {
        if (CreateMap() is not { } map)
            return;

        Entity entity = map.AddTrigger("climb_anti_bhop", ("spawnflags", "1"));
        if (time is not null)
            entity["climb_anti_bhop_time"] = time;

        map.Port();

        Assert.Equal("trigger_momentum_limitmovement", entity.ClassName);
        Assert.Equal((1 | (1 << 19)).ToString(), entity["spawnflags"]);
        Assert.Equal(cooldown, entity["BhopCooldown"]);
        AssertNoClimbKeys(entity);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0")]
    public void ConvertsAntiBhopWithoutTimeToNoJumping(string time)
    {
        if (CreateMap() is not { } map)
            return;

        // GOKZ blocks jumping for good with a time of 0; a BhopCooldown of 0 would block nothing
        Entity entity = map.AddTrigger("climb_anti_bhop", ("spawnflags", "1"), ("climb_anti_bhop_time", time));

        map.Port();

        Assert.Equal("trigger_momentum_limitmovement", entity.ClassName);
        Assert.Equal((1 | (1 << 17)).ToString(), entity["spawnflags"]);
        Assert.False(entity.ContainsKey("BhopCooldown"));
        AssertNoClimbKeys(entity);
        Assert.Contains("jumping disabled", map.PrintReport());
    }

    [Fact]
    public void WarnsAboutUnreproducibleTeleports()
    {
        if (CreateMap() is not { } map)
            return;

        map.AddTrigger("climb_teleport", ("climb_teleport_destination", "dest"), ("climb_teleport_reorient_player", "1"));
        map.AddTrigger("climb_teleport", ("climb_teleport_destination", "dest"), ("climb_teleport_relative", "1"));
        map.AddTrigger("climb_teleport", ("climb_teleport_destination", "missing"));

        map.Port();

        string report = map.PrintReport();
        Assert.Contains("Reorient Player", report);
        Assert.Contains("Relative To Trigger Origin", report);
        Assert.Contains("destination \"missing\" doesn't exist", report);
    }

    [Fact]
    public void LeavesUnknownAndMiscasedTriggersAlone()
    {
        if (CreateMap() is not { } map)
            return;

        Entity unknownType = map.AddTrigger("climb_teleport", ("climb_teleport_type", "7"));
        Entity miscased = map.AddTrigger("Climb_Teleport");
        // GOKZ matches these in any case
        Entity antiCheckpoint = map.AddTrigger("Climb_Anti_Checkpoint");
        Entity noModel = map.Add("trigger_multiple", "climb_teleport", model: null);

        map.Port();

        Assert.Equal("trigger_multiple", unknownType.ClassName);
        Assert.Equal("7", unknownType["climb_teleport_type"]);
        Assert.Equal("trigger_multiple", miscased.ClassName);
        Assert.Equal("trigger_momentum_disallowcheckpoint", antiCheckpoint.ClassName);
        Assert.Equal("trigger_multiple", noModel.ClassName);

        string report = map.PrintReport();
        Assert.Contains("unknown climb_teleport_type 7", report);
        Assert.Contains("matched case sensitively", report);
    }

    [Fact]
    public void RemovesTriggersMomentumHasNoEquivalentFor()
    {
        if (CreateMap() is not { } map)
            return;

        map.AddTrigger("climb_anti_pause");
        map.AddTrigger("climb_anti_pause");
        map.AddTrigger("climb_anti_jumpstat");
        map.AddTrigger("climb_bhop_reset");
        // GOKZ matches the anti triggers in any case and climb_bhop_reset exactly
        map.AddTrigger("CLIMB_ANTI_PAUSE");
        Entity miscasedReset = map.AddTrigger("Climb_Bhop_Reset");

        map.Port();

        Assert.DoesNotContain(map.Bsp.Entities, x => x.Name is "climb_anti_pause" or "CLIMB_ANTI_PAUSE" or "climb_anti_jumpstat" or "climb_bhop_reset");
        Assert.Contains(miscasedReset, map.Bsp.Entities);
        string report = map.PrintReport();
        Assert.Contains("3 x warning: climb_anti_pause removed", report);
        Assert.Contains("1 x warning: climb_anti_jumpstat removed", report);
        Assert.Contains("1 x warning: climb_bhop_reset removed", report);
    }

    [Fact]
    public void RemovesMappingApiVersion()
    {
        if (CreateMap() is not { } map)
            return;

        Entity world = map.Bsp.Entities.First(x => x.ClassName == "worldspawn");
        world["climb_mapping_api_version"] = "1";

        map.Port();

        Assert.False(world.ContainsKey("climb_mapping_api_version"));
    }

    [Fact]
    public void WritesZonesForCourses()
    {
        if (CreateMap() is not { } map)
            return;

        map.AddTrigger("climb_startzone");
        map.Add("func_button", "climb_endbutton");
        map.Add("info_teleport_destination", "climb_start", null, ("origin", "1 2 3"), ("angles", "0 90 0"));
        map.Add("func_button", "climb_bonus1_startbutton");
        map.AddTrigger("climb_bonus1_endzone");
        map.Add("info_teleport_destination", "climb_bonus1_start", null, ("origin", "4 5 6"));
        // Bonus 3 without bonus 2 becomes Momentum's bonus 2, with no start position
        map.AddTrigger("climb_bonus3_startzone");
        map.AddTrigger("climb_bonus3_endzone");
        // Incomplete courses are left out
        map.AddTrigger("climb_bonus4_startzone");

        map.Port("gokz_zones");

        ExtraOutput zoneFile = Assert.Single(map.Context.ExtraOutputs);
        Assert.Equal("zones/local/gokz_zones.json", zoneFile.RelativePath);

        ZoneDefsBase zones = ParseAndValidate(zoneFile.Content);
        Assert.Equal("climb_start", zones.Tracks.Main!.Zones.Segments[0].Checkpoints[0].Regions[0].TeleDestTargetname);
        Assert.Equal(2, zones.Tracks.Bonuses.Count);
        Assert.Equal("climb_bonus1_start", zones.Tracks.Bonuses[0].Zones!.Segments[0].Checkpoints[0].Regions[0].TeleDestTargetname);
        Assert.NotNull(zones.Tracks.Bonuses[1].Zones!.Segments[0].Checkpoints[0].Regions[0].TeleDestPos);

        // The end button's zone reaches past the button
        Entity button = map.Bsp.Entities.First(x => x.Name == "climb_endbutton");
        Model model = map.Bsp.Models[button.ModelNumber];
        Region end = Assert.Single(zones.Tracks.Main.Zones.End.Regions);
        Assert.True(end.Bottom < model.Minimums.Z);
        Assert.True(end.Bottom + end.Height > model.Maximums.Z);

        string report = map.PrintReport();
        Assert.Contains("timer buttons are approximated", report);
        Assert.Contains("GOKZ bonus 3 is Momentum bonus 2", report);
        Assert.Contains("bonus 4 has no timer end", report);
    }

    [Fact]
    public void ZonesTriggersThatPressTimerButtons()
    {
        if (CreateMap() is not { } map)
            return;

        // Timer buttons tucked away, pressed by a trigger at the end and a button at the start
        Entity startButton = map.Add("func_button", "climb_startbutton");
        Entity endButton = map.Add("func_button", "climb_endbutton");
        Entity starter = map.Add("func_button", "start_press");
        starter.connections.Add(Output("OnPressed", "climb_startbutton", "Press"));
        Entity ender = map.AddTrigger("");
        ender.connections.Add(Output("OnStartTouch", "climb_endbutton", "Press"));
        ender.connections.Add(Output("OnTrigger", "climb_endbutton", "Press"));
        // A bonus ended from a trigger, and an output that isn't the player's doing
        map.AddTrigger("climb_bonus1_startzone");
        Entity bonusEnder = map.AddTrigger("");
        bonusEnder.connections.Add(Output("OnStartTouch", "climb_bonus1_endbutton", "Press"));
        Entity relay = map.Add("logic_relay", "relay", null);
        relay.connections.Add(Output("OnTrigger", "climb_endbutton", "Press"));

        map.Port("gokz_pressers");

        ZoneDefsBase zones = ParseAndValidate(Assert.Single(map.Context.ExtraOutputs).Content);
        TrackZones main = zones.Tracks.Main!.Zones;

        // The timer buttons keep their zones, the button and trigger that press them get their own
        Assert.Equal(2, main.Segments[0].Checkpoints[0].Regions.Count);
        Assert.Contains(main.Segments[0].Checkpoints[0].Regions, x => Contains(x, map.Bsp, starter));
        Assert.Contains(main.Segments[0].Checkpoints[0].Regions, x => Contains(x, map.Bsp, startButton));
        Assert.Equal(1 + ZoneShapes.FromBrushEntity(map.Bsp, ender).Count, main.End.Regions.Count);
        Assert.Contains(main.End.Regions, x => Contains(x, map.Bsp, endButton));
        Assert.Single(zones.Tracks.Bonuses);

        string report = map.PrintReport();
        Assert.Contains("trigger_multiple pressing climb_*endbutton -> end zone of the trigger's shape", report);
        Assert.Contains("func_button pressing climb_*startbutton -> start zone around the button", report);
        Assert.Contains("logic_relay \"relay\" presses climb_endbutton on OnTrigger, which isn't zoned", report);
    }

    private static Entity.EntityConnection Output(string name, string target, string action, string param = "", float delay = 0)
    {
        return new Entity.EntityConnection { name = name, target = target, action = action, param = param, delay = delay, fireOnce = -1 };
    }

    [Fact]
    public void GivesContextFiltersTheValueTheMapSets()
    {
        if (CreateMap() is not { } map)
            return;

        // kz_armored_core's checkpoints: set to 1 on reaching one, cleared with 0
        Entity checkpoint = map.AddTrigger("");
        checkpoint.connections.Add(Output("OnStartTouch", "!activator", "AddContext", "cp1:0,cp2:1"));
        Entity start = map.AddTrigger("");
        start.connections.Add(Output("OnStartTouch", "!activator", "AddContext", "cp1:0", 0.02f));
        start.connections.Add(Output("OnStartTouch", "!activator", "RemoveContext", "cp2", 0.02f));
        Entity cp1 = map.Add("filter_activator_context", "context_cp1", null, ("ResponseContext", "cp1"), ("Negated", "Allow entities that match criteria"));
        Entity cp2 = map.Add("filter_activator_context", "context_cp2", null, ("ResponseContext", " cp2 "), ("Negated", "1"));
        // Already Momentum's way, and a context the map sets to two values
        Entity pair = map.Add("filter_activator_context", "context_pair", null, ("ResponseContext", "cp2:1"));
        Entity counter = map.AddTrigger("");
        counter.connections.Add(Output("OnStartTouch", "!activator", "AddContext", "laps:1"));
        counter.connections.Add(Output("OnEndTouch", "!activator", "AddContext", "laps:2"));
        Entity laps = map.Add("filter_activator_context", "context_laps", null, ("ResponseContext", "laps"));

        map.Port();

        // Nothing sets cp1 above 0, so it never passes, as in CS:GO
        Assert.Equal("cp1:1", cp1["ResponseContext"]);
        Assert.Equal("cp2:1", cp2["ResponseContext"]);
        Assert.Equal("1", cp2["Negated"]);
        Assert.Equal("cp2:1", pair["ResponseContext"]);
        Assert.Equal("laps", laps["ResponseContext"]);
        string report = map.PrintReport();
        Assert.Contains("2 x filter_activator_context context name -> name:value", report);
        Assert.Contains("filter_activator_context \"context_laps\" wasn't converted: the map sets \"laps\" to 1, 2", report);
    }

    [Fact]
    public void ConvertsAntiCrouchTeleports()
    {
        if (CreateMap() is not { } map)
            return;

        // kz_armored_core's anti-crouch: crouching on the floor leaves the trigger above the teleport, which sets
        // "crouched" for a moment
        Entity teleport = map.Add("trigger_teleport", "", -1, ("spawnflags", "4097"), ("target", "dest"), ("filtername", "crouch_context"), ("UseLandmarkAngles", "1"));
        Entity trigger = map.AddTrigger("", ("wait", "0.002"));
        trigger.connections.Add(Output("OnEndTouch", "!activator", "AddContext", "crouched:1"));
        trigger.connections.Add(Output("OnEndTouch", "!activator", "AddContext", "crouched:0", 0.03f));
        PlaceAbove(map.Bsp, trigger, teleport);
        map.Add("filter_activator_context", "crouch_context", null, ("ResponseContext", "crouched"));
        Entity reset = map.AddTrigger("");
        reset.connections.Add(Output("OnStartTouch", "!activator", "AddContext", "crouched:0"));

        // kz_kareful's also stop the player they teleport, which goes with the teleport
        Entity scripted = map.Add("trigger_teleport", "", -1, ("target", "dest"), ("filtername", "crouch_context"));
        scripted.connections.Add(Output("OnStartTouch", "!activator", "RunScriptCode", "activator.SetVelocity(activator.GetVelocity() * 0);"));
        Entity scriptedTrigger = map.AddTrigger("");
        scriptedTrigger.connections.Add(Output("OnEndTouch", "!activator", "AddContext", "crouched:1"));
        PlaceAbove(map.Bsp, scriptedTrigger, scripted);

        // The same, with the trigger off to the side, isn't one
        Entity otherTeleport = map.Add("trigger_teleport", "", -1, ("target", "dest"), ("filtername", "other_context"));
        Entity otherTrigger = map.AddTrigger("");
        otherTrigger.connections.Add(Output("OnEndTouch", "!activator", "AddContext", "other:1"));
        PlaceAbove(map.Bsp, otherTrigger, otherTeleport);
        otherTrigger["origin"] = $"{otherTrigger.Origin.X + 100000} {otherTrigger.Origin.Y} {otherTrigger.Origin.Z}";
        map.Add("filter_activator_context", "other_context", null, ("ResponseContext", "other"));

        map.Port();

        Assert.Equal("trigger_momentum_limitmovement", teleport.ClassName);
        Assert.Equal((4097 | (1 << 18)).ToString(), teleport["spawnflags"]);
        Assert.False(teleport.ContainsKey("target"));
        Assert.False(teleport.ContainsKey("filtername"));
        Assert.False(teleport.ContainsKey("UseLandmarkAngles"));
        Assert.Equal("2", teleport["_ported_by_tools"]);
        Assert.DoesNotContain(trigger, map.Bsp.Entities);
        Assert.Contains(reset, map.Bsp.Entities);
        Assert.Equal("trigger_momentum_limitmovement", scripted.ClassName);
        Assert.Empty(scripted.connections);
        Assert.DoesNotContain(scriptedTrigger, map.Bsp.Entities);

        Assert.Equal("trigger_teleport", otherTeleport.ClassName);
        Assert.Contains(otherTrigger, map.Bsp.Entities);
        string report = map.PrintReport();
        Assert.Contains("2 x anti-crouch trigger_teleport -> trigger_momentum_limitmovement with crouching disabled", report);
        Assert.Contains("2 x removed the end touch trigger of an anti-crouch section", report);
    }

    [Theory]
    [InlineData("self.SetVelocity(Vector(0,0,0))", "OnStartTouch", 0f, true)]
    [InlineData(" self.SetVelocity( Vector( 0, 0, 0 ) ); ", "OnEndTouch", 0f, true)]
    [InlineData("activator.SetVelocity(activator.GetVelocity() * 0);", "OnStartTouch", 0.05f, true)]
    [InlineData("self.SetVelocity(Vector(0.0,0.0,0.0))", "OnTrigger", 0.1f, true)]
    // A syntax error in CS:GO too, someone else's velocity, too late, or not zero
    [InlineData("self.SetVelocity(Vector(0.0.0))", "OnStartTouch", 0f, false)]
    [InlineData("self.SetVelocity(Vector(0,0,0)", "OnStartTouch", 0f, false)]
    [InlineData("activator.SetVelocity(self.GetVelocity() * 0)", "OnStartTouch", 0f, false)]
    [InlineData("self.SetVelocity(Vector(0,0,0))", "OnStartTouch", 0.5f, false)]
    [InlineData("self.SetVelocity(Vector(0,0,-50))", "OnStartTouch", 0f, false)]
    [InlineData("self.SetVelocity(Vector(0,0,0))", "OnUser1", 0f, false)]
    public void ConvertsScriptVelocityResetsOnTeleports(string code, string output, float delay, bool converted)
    {
        if (CreateMap() is not { } map)
            return;

        Entity teleport = map.Add("trigger_teleport", "", -1, ("target", "dest"));
        teleport.connections.Add(Output(output, "!activator", "RunScriptCode", code, delay));
        teleport.connections.Add(Output("OnStartTouch", "sound", "PlaySound"));

        map.Port();

        Assert.Equal(converted ? "1" : "", teleport["VelocityMode"]);
        Assert.Equal(converted ? 1 : 2, teleport.connections.Count);
    }

    [Fact]
    public void LeavesTeleportsWithAVelocityModeAlone()
    {
        if (CreateMap() is not { } map)
            return;

        Entity teleport = map.Add("trigger_teleport", "", -1, ("target", "dest"), ("VelocityMode", "2"));
        teleport.connections.Add(Output("OnStartTouch", "!activator", "RunScriptCode", "self.SetVelocity(Vector(0,0,0))"));

        map.Port();

        Assert.Equal("2", teleport["VelocityMode"]);
        Assert.Single(teleport.connections);
    }

    [Fact]
    public void ConvertsScriptJumpPadsToPlayerSpeed()
    {
        if (CreateMap() is not { } map)
            return;

        // kz_bounce's jump pads keep the horizontal speed and set the vertical one
        const string jump = "self.SetVelocity(Vector(self.GetVelocity().x, self.GetVelocity().y, 500))";
        Entity pad = map.AddTrigger("", ("spawnflags", "4097"));
        pad.connections.Add(Output("OnStartTouch", "!activator", "RunScriptCode", jump));
        pad.connections.Add(Output("OnStartTouch", "sound", "PlaySound"));
        Entity late = map.AddTrigger("", ("spawnflags", "1"));
        Entity.EntityConnection once = Output("OnEndTouch", "!activator", "RunScriptCode", jump, 0.1f) with { fireOnce = 1 };
        late.connections.Add(once);

        map.Port();

        Entity speed = Assert.Single(map.Bsp.Entities, x => x.ClassName == "player_speed");
        Assert.Equal("porter_speed_z_500", speed["targetname"]);
        Assert.Equal(((1 << 8) | (1 << 6)).ToString(), speed["spawnflags"]);
        Assert.Equal("500", speed["speed"]);

        Assert.Equal(2, pad.connections.Count);
        Assert.Equal(Output("OnStartTouch", "porter_speed_z_500", "Fire"), pad.connections[0]);
        Entity.EntityConnection fire = Assert.Single(late.connections);
        Assert.Equal(once with { target = "porter_speed_z_500", action = "Fire", param = "" }, fire);
        Assert.Contains("2 x RunScriptCode setting the player's velocity -> player_speed", map.PrintReport());
    }

    [Theory]
    [InlineData("self.SetVelocity(Vector(0,0,640))", "trigger_teleport", "4097", "x 0|y 0|z 640")]
    [InlineData(" activator.SetVelocity( Vector( activator.GetVelocity().x, -50.5, 0 ) ); ", "trigger_once", "1", "y -50.5|z 0")]
    [InlineData("self.SetVelocity(Vector(0,0,0))", "trigger_multiple", "4097", "x 0|y 0|z 0")]
    // Syntax errors in CS:GO too, nothing to set, speed moved between axes, someone else's velocity, a multiplied one
    [InlineData("self.SetVelocity(Vector(0.0.0))", "trigger_multiple", "4097", "")]
    [InlineData("self.SetVelocity(Vector(0,0,0)", "trigger_multiple", "4097", "")]
    [InlineData("self.setvelocity(vector(0,0,0))", "trigger_multiple", "4097", "")]
    [InlineData("self.SetVelocity(Vector(self.GetVelocity().x,self.GetVelocity().y,self.GetVelocity().z))", "trigger_multiple", "4097", "")]
    [InlineData("self.SetVelocity(Vector(self.GetVelocity().y,self.GetVelocity().x,500))", "trigger_multiple", "4097", "")]
    [InlineData("activator.SetVelocity(Vector(self.GetVelocity().x,self.GetVelocity().y,500))", "trigger_multiple", "4097", "")]
    [InlineData("self.SetVelocity(Vector(self.GetVelocity().x,self.GetVelocity().y,0)*2)", "trigger_multiple", "4097", "")]
    // Triggers that let other things through (player_speed launches the player whatever touched it), or nothing
    [InlineData("self.SetVelocity(Vector(0,0,640))", "trigger_multiple", "9", "")]
    [InlineData("self.SetVelocity(Vector(0,0,640))", "trigger_multiple", "0", "")]
    // Not a trigger's touch output
    [InlineData("self.SetVelocity(Vector(0,0,640))", "func_illusionary", "4097", "")]
    public void ConvertsScriptVelocitiesPerAxis(string code, string className, string spawnflags, string expected)
    {
        if (CreateMap() is not { } map)
            return;

        Entity trigger = map.Add(className, "", -1, ("spawnflags", spawnflags), ("target", "dest"));
        trigger.connections.Add(Output("OnStartTouch", "!activator", "RunScriptCode", code));

        map.Port();

        Dictionary<string, Entity> speeds = map.Bsp.Entities.Where(x => x.ClassName == "player_speed").ToDictionary(x => x["targetname"]);
        if (expected.Length == 0)
        {
            Assert.Empty(speeds);
            Assert.Equal("RunScriptCode", Assert.Single(trigger.connections).action);
            return;
        }

        string[] axes = expected.Split('|');
        Assert.Equal(axes.Length, speeds.Count);
        Assert.Equal(axes.Length, trigger.connections.Count);
        foreach (string axis in axes)
        {
            string[] parts = axis.Split(' ');
            string name = $"porter_speed_{parts[0]}_{parts[1]}";
            Assert.Contains(Output("OnStartTouch", name, "Fire"), trigger.connections);
            int flag = parts[0] switch { "x" => 1 << 2, "y" => 1 << 4, _ => 1 << 6 };
            Assert.Equal(((1 << 8) | flag).ToString(), speeds[name]["spawnflags"]);
            Assert.Equal(parts[1], speeds[name]["speed"]);
        }
    }

    [Fact]
    public void ReviewCountsEntitiesUsingVScript()
    {
        if (CreateMap() is not { } map)
            return;

        map.Add("logic_script", "script", null, ("vscripts", "danzay/misc.nut"));
        map.AddTrigger("", ("vscripts", "lele/prosave/trigger_save.nut"));
        // kz_kareful's booster doubles the player's speed, which player_speed can't do
        Entity booster = map.AddTrigger("", ("spawnflags", "4097"));
        booster.connections.Add(Output("OnEndTouch", "!activator", "RunScriptCode", "activator.SetVelocity(activator.GetVelocity() * 2);"));
        Entity file = map.Add("func_button", "");
        file.connections.Add(Output("OnPressed", "!activator", "RunScriptFile", "danzay/nonlinear.nut"));
        // Converted, so no longer counted
        Entity teleport = map.Add("trigger_teleport", "", -1, ("target", "dest"));
        teleport.connections.Add(Output("OnStartTouch", "!activator", "RunScriptCode", "self.SetVelocity(Vector(0,0,0))"));
        Entity pad = map.AddTrigger("", ("spawnflags", "4097"));
        pad.connections.Add(Output("OnStartTouch", "!activator", "RunScriptCode", "self.SetVelocity(Vector(self.GetVelocity().x, self.GetVelocity().y, 500))"));

        map.Port();

        ReviewItem item = Assert.Single(EntityReview.Analyze(map.Context).Items, x => x.Tool == "VScript");
        Assert.Equal(4, item.Found);
        Assert.Equal(ReviewSeverity.Review, item.Severity);
    }

    // Moves the trigger so its bottom is the teleport's top, over its middle
    private static void PlaceAbove(BSP bsp, Entity trigger, Entity teleport)
    {
        Model below = bsp.Models[teleport.ModelNumber];
        Model above = bsp.Models[trigger.ModelNumber];
        System.Numerics.Vector3 offset = (below.Minimums + below.Maximums) / 2 - (above.Minimums + above.Maximums) / 2 + teleport.Origin;
        offset.Z = below.Maximums.Z + teleport.Origin.Z - above.Minimums.Z;
        trigger["origin"] = $"{offset.X} {offset.Y} {offset.Z}";
    }

    // Whether the region's box holds the middle of the entity's model
    private static bool Contains(Region region, BSP bsp, Entity entity)
    {
        Model model = bsp.Models[entity.ModelNumber];
        System.Numerics.Vector3 middle = (model.Minimums + model.Maximums) / 2 + entity.Origin;
        return region.Points.Min(p => p[0]) <= middle.X && middle.X <= region.Points.Max(p => p[0])
            && region.Points.Min(p => p[1]) <= middle.Y && middle.Y <= region.Points.Max(p => p[1])
            && region.Bottom <= middle.Z && middle.Z <= region.Bottom + region.Height;
    }

    [Fact]
    public void AddsCascadeLight()
    {
        if (CreateMap() is not { } map)
            return;

        Assert.Empty(map.Bsp.Entities.Where(x => x.ClassName == "env_cascade_light"));
        if (!map.Bsp.Entities.Any(x => x.ClassName == "light_environment"))
            map.Add("light_environment", "", null, ("angles", "0 300 0"), ("pitch", "-60"));

        map.Port();

        Entity light = Assert.Single(map.Bsp.Entities.Where(x => x.ClassName == "env_cascade_light"));
        Assert.Equal("1", light["uselightenvangles"]);
        Assert.Equal("0", light["startdisabled"]);
        Assert.Equal("400", light["maxshadowdistance"]);
        Assert.Equal("255 255 255 1", light["color"]);
        Assert.Equal("2", light["_ported_by_tools"]);
        Assert.Contains("env_cascade_light added", map.PrintReport());
    }

    [Fact]
    public void KeepsTheMapsCascadeLight()
    {
        if (CreateMap() is not { } map)
            return;

        Entity light = map.Add("env_cascade_light", "cascadelight", null, ("maxshadowdistance", "1000"));

        map.Port();

        Assert.Same(light, Assert.Single(map.Bsp.Entities.Where(x => x.ClassName == "env_cascade_light")));
        Assert.Equal("1000", light["maxshadowdistance"]);
        Assert.DoesNotContain("env_cascade_light", map.PrintReport());
    }

    [Fact]
    public void SkipsCascadeLightWithoutLightEnvironment()
    {
        if (CreateMap() is not { } map)
            return;

        map.Bsp.Entities.RemoveAll(x => x.ClassName == "light_environment");

        map.Port();

        Assert.Empty(map.Bsp.Entities.Where(x => x.ClassName == "env_cascade_light"));
        Assert.Contains("warning: no env_cascade_light added", map.PrintReport());
    }

    [Fact]
    public void WritesNoZonesWithoutMainCourse()
    {
        if (CreateMap() is not { } map)
            return;

        map.AddTrigger("climb_bonus1_startzone");
        map.AddTrigger("climb_bonus1_endzone");

        map.Port();

        Assert.Empty(map.Context.ExtraOutputs);
        Assert.Contains("no zone file was written", map.PrintReport());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JobWritesZoneFileNextToMap(bool convertZones)
    {
        if (CreateMap() is not { } map)
            return;

        map.AddTrigger("climb_startzone");
        map.AddTrigger("climb_endzone");
        map.AddTrigger("climb_teleport", ("climb_teleport_destination", "dest"));

        string dir = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));
        string input = Path.Combine(dir, "in", "kz_synthetic.bsp");
        string outputPath = Path.Combine(dir, "out", "maps", "kz_synthetic_mom.bsp");
        string work = PortJob.CreateWorkDirectory("test");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(input)!);
            new BSPWriter(map.Bsp).WriteBSP(input, false);

            (PortJob job, MapPortState state) = PortJobFactory.CreateSourceMapJob(input, outputPath, new MapPortOptions { Pack = false, Install = false, ConvertZones = convertZones });
            var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = work };
            context.Set(state);

            PortJobResult result = await job.RunAsync(context);

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("gokz", state.Profile?.Name);
            string zonePath = Path.Combine(dir, "out", "maps", "zones", "local", "kz_synthetic_mom.json");
            Assert.Equal(convertZones, File.Exists(zonePath));
            if (convertZones)
                ParseAndValidate(File.ReadAllText(zonePath));

            var ported = new BSP(new FileInfo(outputPath));
            Assert.Contains(ported.Entities, x => x.ClassName == "trigger_momentum_teleport" && x["_ported_by_tools"] == "2");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void RefusesExtraOutputsOutsideTheMapFolder()
    {
        string root = Path.GetTempPath();
        Assert.Throws<InvalidOperationException>(() => WriteBspStep.GetExtraOutputPath(root, "../x.json"));
        Assert.Throws<InvalidOperationException>(() => WriteBspStep.GetExtraOutputPath(root, Path.Combine(root, "x.json")));
        Assert.Equal(Path.Combine(root, "zones", "local", "x.json"), WriteBspStep.GetExtraOutputPath(root, "zones/local/x.json"));
    }

    /// <summary>
    /// Ports every GOKZ map in CS:GO's (legacy) maps folder and its workshop folders, if CS:GO is installed.
    /// </summary>
    [Fact]
    public void PortsInstalledGokzMaps()
    {
        string? csgo = new SteamLocator().Find(KnownGame.CounterStrikeGlobalOffensive)?.ContentDirectory;
        string maps = csgo is null ? "" : Path.Combine(csgo, "maps");
        if (!Directory.Exists(maps))
            return;

        var failures = new List<string>();
        foreach (string path in Directory.EnumerateFiles(maps, "*.bsp", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            try
            {
                var bsp = new BSP(new FileInfo(path));
                if (SourceGameSniffer.DetectProfile(bsp) != "gokz")
                    continue;

                var context = new PortContext(bsp, name, csgo);
                new GokzProfile().Apply(context);

                using var writer = new StringWriter();
                context.Report.Print(writer);
                output.WriteLine($"{name} ({bsp.MapType}):\n{writer}");

                foreach (ExtraOutput extra in context.ExtraOutputs)
                {
                    output.WriteLine(extra.Content);
                    ParseAndValidate(extra.Content);
                }

                // The map still has to write
                string temp = Path.Combine(Path.GetTempPath(), $"gokz_{Guid.NewGuid():N}.bsp");
                try
                {
                    new BSPWriter(bsp).WriteBSP(temp, false);
                    var reloaded = new BSP(new FileInfo(temp));
                    Assert.Equal(bsp.Entities.Count, reloaded.Entities.Count);
                }
                finally
                {
                    File.Delete(temp);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex}");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Reads a zone file into BSPConvert's ZoneDefs and checks it against the rules Momentum enforces when loading it
    /// (mom_zone_runtime_data.cpp).
    /// </summary>
    private static ZoneDefsBase ParseAndValidate(string json)
    {
        ZoneDefsBase zones = JsonSerializer.Deserialize<ZoneDefsBase>(json) ?? throw new InvalidDataException("empty zone file");
        Assert.Equal(1, zones.FormatVersion);
        Assert.NotNull(zones.Tracks.Main);
        Assert.True(zones.Tracks.Bonuses.Count <= 25);

        var tracks = new List<TrackZones> { zones.Tracks.Main.Zones };
        tracks.AddRange(zones.Tracks.Bonuses.Select(x => x.Zones ?? throw new InvalidDataException("bonus without zones")));
        foreach (TrackZones track in tracks)
        {
            Segment segment = Assert.Single(track.Segments);
            Zone start = Assert.Single(segment.Checkpoints);
            Assert.NotEmpty(start.Regions);
            Assert.NotEmpty(track.End.Regions);

            foreach (Region region in start.Regions)
            {
                // Start zones need a destination for each region: a name, or a position and yaw
                Assert.True(region.TeleDestTargetname is { Length: > 0 } ^ region.TeleDestPos is not null);
                Assert.Equal(region.TeleDestPos is not null, region.TeleDestYaw is not null);
            }

            foreach (Region region in start.Regions.Concat(track.End.Regions))
            {
                Assert.NotNull(region.Bottom);
                Assert.True(region.Height > 1);
                Assert.True(ZoneShapes.IsValidPolygon(region), string.Join(" ", region.Points.Select(p => $"({p[0]} {p[1]})")));
            }
        }

        return zones;
    }
}
