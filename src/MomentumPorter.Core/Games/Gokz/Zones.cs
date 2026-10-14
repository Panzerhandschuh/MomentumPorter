namespace MomentumPorter.Games.Gokz;

using System.Numerics;
using System.Text.RegularExpressions;
using BSPConvert.Lib.Zones;
using LibBSP;
using MomentumPorter.Porting;

public sealed partial class GokzProfile
{
    // GOKZ_MAX_COURSES: bonus numbers go from 1 to 99
    private const int MaxGokzCourse = 99;

    // MAX_BONUS_TRACKS in Momentum's mom_timer_defs.h
    private const int MaxMomentumBonuses = 25;

    // Button zones reach this far past the button sideways and up, so standing next to it and pressing touches them
    private const float ButtonZonePadding = 32;

    // and this far down, to reach the player's feet below a button at chest height
    private const float ButtonZoneDepth = 64;

    // Momentum loads zones for a map not from its servers from maps/zones/local/<map>.json
    private const string ZoneFolder = "zones/local";

    // Entity classes a zone's teleport destination can be (mom_zone_runtime_data.cpp GetTeleDestEntity)
    private static readonly HashSet<string> TeleportDestinationClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "info_teleport_destination",
        "info_player_start",
        "info_player_teamspawn",
    };

    // Where CS:GO spawns players, for a main course without climb_start
    private static readonly string[] SpawnClasses = ["info_player_counterterrorist", "info_player_terrorist", "info_player_start"];

    // climb_startzone, climb_endbutton, climb_bonus2_startbutton... GOKZ compares the main course's names ignoring case.
    private static readonly Regex TimerName = new(
        @"^climb_(?:bonus(?<course>\d+)_)?(?<end>start|end)(?<kind>zone|button)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    // Outputs a player sets off by touching a trigger or pressing a button; a timer button pressed from one of these is
    // pressed by the player, in effect
    private static readonly Dictionary<string, string[]> PressOutputs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trigger_multiple"] = ["OnStartTouch", "OnStartTouchAll", "OnTrigger"],
        ["func_button"] = ["OnPressed"],
    };

    private static readonly Regex StartName = new(@"^climb_(?:bonus(?<course>\d+)_)?start$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed class Course(int number)
    {
        public int Number { get; } = number;

        public List<Region> Start { get; } = [];

        public List<Region> End { get; } = [];

        public string Name => Number == 0 ? "the main course" : $"bonus {Number}";

        public void AddRegions(bool start, List<Region> regions) => (start ? Start : End).AddRange(regions);
    }

    private static Course GetCourse(SortedDictionary<int, Course> courses, int number)
    {
        if (!courses.TryGetValue(number, out Course? course))
            courses[number] = course = new Course(number);

        return course;
    }

    /// <summary>
    /// GOKZ times a course whenever its timer button is pressed, so maps also start and end the timer with a trigger
    /// (or another button) whose output presses the timer button, which is often hidden out of reach. Those triggers
    /// become zones of their own shape and those buttons zones around them, next to the timer button's own zone.
    /// </summary>
    private static void AddButtonPressers(PortContext context, SortedDictionary<int, Course> courses)
    {
        foreach (Entity entity in context.Entities)
        {
            // Timer entities are zoned already
            if (TimerName.IsMatch(entity.Name))
                continue;

            var pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entity.EntityConnection connection in entity.connections)
            {
                if (!connection.action.Equals("Press", StringComparison.OrdinalIgnoreCase))
                    continue;

                Match match = TimerName.Match(connection.target);
                if (!match.Success || !match.Groups["kind"].Value.Equals("button", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!PressOutputs.TryGetValue(entity.ClassName, out string[]? outputs) || !outputs.Contains(connection.name, StringComparer.OrdinalIgnoreCase))
                {
                    context.Report.Warn($"{Describe(entity)} presses {connection.target} on {connection.name}, which isn't zoned; zone it in game if it times the run");
                    continue;
                }

                if (!pressed.Add(connection.target))
                    continue;

                int number = CourseNumber(match);
                if (match.Groups["course"].Success && number is < 1 or > MaxGokzCourse)
                    continue;

                bool button = entity.ClassName.Equals("func_button", StringComparison.OrdinalIgnoreCase);
                List<Region> regions = button ? ButtonRegions(context, entity) : ZoneShapes.FromBrushEntity(context.Bsp, entity);
                if (regions.Count == 0)
                {
                    context.Report.Warn($"{Describe(entity)} presses {connection.target} but has no brush model, so it has no zone");
                    continue;
                }

                bool start = match.Groups["end"].Value.Equals("start", StringComparison.OrdinalIgnoreCase);
                GetCourse(courses, number).AddRegions(start, regions);
                context.Report.Change($"{entity.ClassName} pressing climb_*{(start ? "start" : "end")}button -> {(start ? "start" : "end")} zone {(button ? "around the button" : "of the trigger's shape")}");
            }
        }
    }

    private static string Describe(Entity entity) => entity.Name.Length > 0 ? $"{entity.ClassName} \"{entity.Name}\"" : $"a {entity.ClassName}";

    /// <summary>
    /// Momentum's timer comes from a zone file instead of entities. GOKZ's timer zones (trigger_multiple) become zones
    /// of the same shape and its timer buttons (func_button) zones around the button.
    /// </summary>
    private static void WriteZones(PortContext context)
    {
        var courses = new SortedDictionary<int, Course>();

        foreach (Entity entity in context.Entities)
        {
            Match match = TimerName.Match(entity.Name);
            if (!match.Success)
                continue;

            int number = match.Groups["course"].Success ? int.Parse(match.Groups["course"].Value) : 0;
            bool start = match.Groups["end"].Value.Equals("start", StringComparison.OrdinalIgnoreCase);
            bool button = match.Groups["kind"].Value.Equals("button", StringComparison.OrdinalIgnoreCase);

            string expectedClass = button ? "func_button" : "trigger_multiple";
            if (!entity.ClassName.Equals(expectedClass, StringComparison.OrdinalIgnoreCase))
            {
                context.Report.Warn($"{entity.Name} is a {entity.ClassName}, not a {expectedClass}, so it isn't a timer {(button ? "button" : "zone")}");
                continue;
            }

            if (match.Groups["course"].Success && number is < 1 or > MaxGokzCourse)
            {
                context.Report.Warn($"{entity.Name}: GOKZ bonus numbers go from 1 to {MaxGokzCourse}");
                continue;
            }

            List<Region> regions = button ? ButtonRegions(context, entity) : ZoneShapes.FromBrushEntity(context.Bsp, entity);
            if (regions.Count == 0)
            {
                context.Report.Warn($"{entity.Name} has no brush model, so it has no zone");
                continue;
            }

            GetCourse(courses, number).AddRegions(start, regions);

            if (button)
            {
                context.Report.Change($"climb_*{(start ? "start" : "end")}button -> {(start ? "start" : "end")} zone around the button");
                context.Report.Warn("timer buttons are approximated by a zone around the button: the timer starts on leaving it and stops on touching it, without pressing");
            }
            else
            {
                context.Report.Change($"climb_*{(start ? "start" : "end")}zone -> {(start ? "start" : "end")} zone");
            }
        }

        AddButtonPressers(context, courses);

        if (!courses.TryGetValue(0, out Course? main) || main.Start.Count == 0 || main.End.Count == 0)
        {
            if (courses.Count > 0 || context.Entities.Any(x => x.Name.StartsWith("climb_", StringComparison.OrdinalIgnoreCase)))
                context.Report.Warn("the main course has no timer start and end, so no zone file was written; zone the map in game");

            return;
        }

        var zoneDefs = new ZoneDefsBase
        {
            FormatVersion = 1,
            DataTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        zoneDefs.Tracks.Main = new MainTrack { Zones = CreateTrackZones(context, main), StagesEndAtStageStarts = true };

        foreach (Course bonus in courses.Values.Where(x => x.Number > 0))
        {
            if (bonus.Start.Count == 0 || bonus.End.Count == 0)
            {
                context.Report.Warn($"{bonus.Name} has no timer {(bonus.Start.Count == 0 ? "start" : "end")}, so it was left out");
                continue;
            }

            if (zoneDefs.Tracks.Bonuses.Count == MaxMomentumBonuses)
            {
                context.Report.Warn($"{bonus.Name} left out, Momentum maps can have at most {MaxMomentumBonuses} bonuses");
                continue;
            }

            zoneDefs.Tracks.Bonuses.Add(new BonusTrack { Zones = CreateTrackZones(context, bonus) });
            if (zoneDefs.Tracks.Bonuses.Count != bonus.Number)
                context.Report.Warn($"GOKZ {bonus.Name} is Momentum bonus {zoneDefs.Tracks.Bonuses.Count}, since Momentum numbers bonuses without gaps");
        }

        string path = $"{ZoneFolder}/{context.MapName}.json";
        context.ExtraOutputs.Add(new ExtraOutput(path, ZoneWriter.WriteToString(zoneDefs)));
        context.Report.Change($"zone file {path} with the main course and {zoneDefs.Tracks.Bonuses.Count} bonuses");
    }

    private static List<Region> ButtonRegions(PortContext context, Entity entity)
    {
        if (ZoneShapes.GetBounds(context.Bsp, entity) is not var (mins, maxs))
            return [];

        var below = new Vector3(ButtonZonePadding, ButtonZonePadding, ButtonZoneDepth);
        var above = new Vector3(ButtonZonePadding);
        return [ZoneShapes.Box(mins - below, maxs + above)];
    }

    /// <summary>
    /// One segment from the start zone to the end zone. Every start region gets the course's start position, which
    /// Momentum needs to restart the player there.
    /// </summary>
    private static TrackZones CreateTrackZones(PortContext context, Course course)
    {
        Action<Region> setDestination = GetTeleportDestination(context, course);
        foreach (Region region in course.Start)
            setDestination(region);

        var segment = new Segment
        {
            LimitStartGroundSpeed = false,
            CheckpointsRequired = false,
            CheckpointsOrdered = false,
        };
        segment.Checkpoints.Add(new Zone { Regions = course.Start });

        var zones = new TrackZones { End = new Zone { Regions = course.End } };
        zones.Segments.Add(segment);
        return zones;
    }

    /// <summary>
    /// How to set a start region's teleport destination: the course's climb_start (or climb_bonusX_start), else the
    /// map's spawn for the main course, else the middle of the course's first start region.
    /// </summary>
    private static Action<Region> GetTeleportDestination(PortContext context, Course course)
    {
        List<Entity> starts = context.Entities.Where(x => StartName.Match(x.Name) is { Success: true } m && CourseNumber(m) == course.Number).ToList();

        // Momentum looks the name up itself, as long as it's unambiguous and a class it can teleport to
        if (starts.Count == 1 && TeleportDestinationClasses.Contains(starts[0].ClassName))
        {
            string name = starts[0].Name;
            return region => region.TeleDestTargetname = name;
        }

        Entity? position = starts.FirstOrDefault();
        if (starts.Count > 1)
        {
            context.Report.Warn($"{course.Name} has {starts.Count} start positions named {starts[0].Name}; using the first one's position");
        }
        else if (position is null && course.Number == 0)
        {
            position = SpawnClasses.SelectMany(context.OfClass).FirstOrDefault();
            context.Report.Warn("the main course has no climb_start, so restarting goes to the map's first spawn point");
        }

        float[] pos;
        float yaw;
        if (position is not null)
        {
            Vector3 origin = position.Origin;
            pos = [origin.X, origin.Y, origin.Z];
            yaw = position.Angles.Y;
        }
        else
        {
            context.Report.Warn($"{course.Name} has no start position, so restarting goes to the bottom middle of its start zone; set one with the zone editor");
            Region first = course.Start[0];
            pos = [first.Points.Average(p => p[0]), first.Points.Average(p => p[1]), first.Bottom ?? 0];
            yaw = 0;
        }

        return region =>
        {
            region.TeleDestPos = pos;
            region.TeleDestYaw = yaw;
        };
    }

    private static int CourseNumber(Match match)
    {
        return match.Groups["course"].Success && int.TryParse(match.Groups["course"].Value, out int number) ? number : 0;
    }
}
