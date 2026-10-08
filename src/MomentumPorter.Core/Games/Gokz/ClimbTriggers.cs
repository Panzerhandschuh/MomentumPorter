namespace MomentumPorter.Games.Gokz;

using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Porting;

public sealed partial class GokzProfile
{
    // Targetnames GOKZ gives meaning to on trigger_multiple
    private const string TeleportName = "climb_teleport";
    private const string AntiCheckpointName = "climb_anti_checkpoint";
    private const string AntiBhopName = "climb_anti_bhop";

    // kz_mapping_api.fgd defaults, used when the map leaves a keyvalue out
    private const float AntiBhopTimeDefault = 0.2f;
    private const bool UseDestAnglesDefault = true;
    private const bool ResetSpeedDefault = true;

    // "For only bhop triggers the minimum delay is 0.08 seconds" (kz_mapping_api.fgd)
    private const float BhopMinDelay = 0.08f;

    // SF_TRIGGER_ALLOW_CLIENTS, the default when a trigger has no spawnflags
    private const int TriggerAllowClients = 1;

    // trigger_momentum_bhop spawnflags. 1 is also SF_TRIGGER_ALLOW_CLIENTS, so it has to stay set.
    private const int BhopResetOnActivate = 1;
    private const int BhopResetOnLandNotFuncBhop = 2;

    // trigger_momentum_limitmovement SF_LIMIT_BHOP
    private const int LimitBhop = 1 << 19;

    // trigger_momentum_teleport VelocityMode values
    private const int VelocityModeRetain = 0;
    private const int VelocityModeReset = 1;

    // The only keyvalues the engine's converter copies (besides outputs); everything else, climb_* included, is dropped
    private static readonly HashSet<string> KeptKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "classname",
        "model",
        "targetname",
        "StartDisabled",
        "solid",
        "origin",
        "filtername",
        "hammerid",
    };

    // GOKZ triggers Momentum has nothing like
    private static readonly Dictionary<string, string> UnsupportedTriggers = new(StringComparer.Ordinal)
    {
        ["climb_anti_pause"] = "climb_anti_pause removed: Momentum has no run pausing to block",
        ["climb_anti_jumpstat"] = "climb_anti_jumpstat removed: Momentum has no jumpstats",
        ["climb_bhop_reset"] = "climb_bhop_reset removed: Momentum's bhop triggers can't be reset by another trigger",
    };

    /// <summary>
    /// climb_teleport_type values in kz_mapping_api.fgd's order, then the triggers that have no type keyvalue.
    /// </summary>
    private enum ClimbTriggerType
    {
        Teleport,
        MultiBhop,
        SingleBhop,
        SequentialBhop,
        AntiCheckpoint,
        AntiBhop,
    }

    private sealed record ClimbTrigger(
        ClimbTriggerType Type,
        string Destination = "",
        float Delay = 0,
        bool UseDestAngles = UseDestAnglesDefault,
        bool ResetSpeed = ResetSpeedDefault,
        bool ReorientPlayer = false,
        bool Relative = false,
        float AntiBhopTime = AntiBhopTimeDefault
    );

    private static void ConvertClimbTriggers(PortContext context)
    {
        var names = new HashSet<string>(context.Entities.Select(x => x.Name).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);

        foreach (Entity entity in context.OfClass("trigger_multiple").ToList())
        {
            // The keyvalues only work on brush triggers with these exact names
            if (entity.ModelNumber <= 0)
                continue;

            ClimbTrigger? trigger = ReadClimbTrigger(context, entity);
            if (trigger is null)
                continue;

            if (trigger.Type <= ClimbTriggerType.SequentialBhop)
                CheckDestination(context, trigger, names);

            ConvertClimbTrigger(context, entity, trigger);
        }
    }

    private static ClimbTrigger? ReadClimbTrigger(PortContext context, Entity entity)
    {
        string name = entity.Name;
        switch (name)
        {
            case TeleportName:
            {
                int typeValue = entity.GetInt("climb_teleport_type", 0);
                ClimbTriggerType? type = typeValue switch
                {
                    0 => ClimbTriggerType.Teleport,
                    1 => ClimbTriggerType.MultiBhop,
                    2 => ClimbTriggerType.SingleBhop,
                    3 => ClimbTriggerType.SequentialBhop,
                    _ => null,
                };

                if (type is null)
                {
                    context.Report.Warn($"climb_teleport with unknown climb_teleport_type {typeValue} wasn't converted");
                    return null;
                }

                return new ClimbTrigger(
                    type.Value,
                    Destination: entity["climb_teleport_destination"],
                    Delay: entity.GetFloat("climb_teleport_delay", 0),
                    UseDestAngles: GetBool(entity, "climb_teleport_use_dest_angles", UseDestAnglesDefault),
                    ResetSpeed: GetBool(entity, "climb_teleport_reset_speed", ResetSpeedDefault),
                    ReorientPlayer: GetBool(entity, "climb_teleport_reorient_player", false),
                    Relative: GetBool(entity, "climb_teleport_relative", false)
                );
            }
            case AntiCheckpointName:
                return new ClimbTrigger(ClimbTriggerType.AntiCheckpoint);
            case AntiBhopName:
                return new ClimbTrigger(ClimbTriggerType.AntiBhop, AntiBhopTime: entity.GetFloat("climb_anti_bhop_time", AntiBhopTimeDefault));
            default:
                // Momentum's converter matches names exactly, so a differently cased name is left alone
                if (name.Equals(TeleportName, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(AntiCheckpointName, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(AntiBhopName, StringComparison.OrdinalIgnoreCase))
                {
                    context.Report.Warn($"trigger_multiple named \"{name}\" wasn't converted, GOKZ names are matched case sensitively");
                }

                return null;
        }
    }

    private static void CheckDestination(PortContext context, ClimbTrigger trigger, HashSet<string> names)
    {
        if (trigger.Destination.Length == 0)
            context.Report.Warn("climb_teleport without a climb_teleport_destination doesn't teleport");
        else if (!names.Contains(trigger.Destination))
            context.Report.Warn($"climb_teleport destination \"{trigger.Destination}\" doesn't exist");

        if (trigger.ReorientPlayer)
            context.Report.Warn("climb_teleport \"Reorient Player\" isn't reproducible; it teleports to the destination as is");

        if (trigger.Relative)
            context.Report.Warn("climb_teleport \"Relative To Trigger Origin\" isn't reproducible; it teleports to the destination itself");

        if (trigger.Type == ClimbTriggerType.SequentialBhop)
            context.Report.Warn("sequential bhop triggers only remember their own landings, not which triggers of the set were used");
    }

    private static void ConvertClimbTrigger(PortContext context, Entity entity, ClimbTrigger trigger)
    {
        int spawnflags = entity.GetInt("spawnflags", TriggerAllowClients);

        if (entity.ContainsKey("parentname"))
            context.Report.Warn($"{entity.Name} lost its parentname, Momentum's converter doesn't keep it");

        foreach (string key in entity.Keys.Where(x => !KeptKeys.Contains(x)).ToList())
            entity.Remove(key);

        switch (trigger.Type)
        {
            case ClimbTriggerType.Teleport:
                entity.ClassName = "trigger_momentum_teleport";
                entity.Set("spawnflags", spawnflags);
                if (trigger.Destination.Length > 0)
                    entity["target"] = trigger.Destination;

                // Both are time spent inside the trigger
                if (trigger.Delay > 0)
                    entity.Set("TeleportDelay", trigger.Delay);

                // GOKZ resets speed by default, Momentum retains it, so this is always written
                entity.Set("VelocityMode", trigger.ResetSpeed ? VelocityModeReset : VelocityModeRetain);
                entity.Set("UseLandmarkAngles", trigger.UseDestAngles ? 1 : 0);
                context.Report.Change("climb_teleport -> trigger_momentum_teleport");
                break;

            case ClimbTriggerType.MultiBhop:
            case ClimbTriggerType.SingleBhop:
            case ClimbTriggerType.SequentialBhop:
            {
                bool multi = trigger.Type == ClimbTriggerType.MultiBhop;
                entity.ClassName = "trigger_momentum_bhop";

                // Landing outside any bhop trigger ends a single/sequential bhop chain
                entity.Set("spawnflags", multi ? BhopResetOnActivate : BhopResetOnActivate | BhopResetOnLandNotFuncBhop);

                // Multi bhops can be landed on indefinitely, the others teleport on the second landing
                entity.Set("LandingLimit", multi ? 0 : 1);
                if (trigger.Destination.Length > 0)
                    entity["target"] = trigger.Destination;

                // Both are time on the ground inside the trigger. Always written, or sv_bhop_time_limit applies.
                entity.Set("TimeLimit", Math.Max(trigger.Delay, BhopMinDelay));
                entity.Set("ResetVelocityOnTeleport", trigger.ResetSpeed ? 1 : 0);
                entity.Set("UseDestinationAngles", trigger.UseDestAngles ? 1 : 0);
                context.Report.Change($"climb_teleport ({DescribeBhop(trigger.Type)}) -> trigger_momentum_bhop");
                break;
            }

            case ClimbTriggerType.AntiCheckpoint:
                entity.ClassName = "trigger_momentum_disallowcheckpoint";
                entity.Set("spawnflags", spawnflags);
                context.Report.Change("climb_anti_checkpoint -> trigger_momentum_disallowcheckpoint");
                break;

            case ClimbTriggerType.AntiBhop:
                entity.ClassName = "trigger_momentum_limitmovement";
                // OR'd in, dropping SF_TRIGGER_ALLOW_CLIENTS would stop the trigger registering touches
                entity.Set("spawnflags", spawnflags | LimitBhop);
                entity.Set("BhopCooldown", trigger.AntiBhopTime);
                context.Report.Change("climb_anti_bhop -> trigger_momentum_limitmovement");
                break;
        }

        entity.Set(PortedByToolsKey, EntityToolsVersion);
    }

    private static void RemoveUnsupportedTriggers(PortContext context)
    {
        foreach (Entity entity in context.OfClass("trigger_multiple").ToList())
        {
            if (!UnsupportedTriggers.TryGetValue(entity.Name, out string? warning))
                continue;

            context.EntityLump.Remove(entity);
            context.Report.Warn(warning);
        }
    }

    private static string DescribeBhop(ClimbTriggerType type)
    {
        return type switch
        {
            ClimbTriggerType.MultiBhop => "Multi Bhop",
            ClimbTriggerType.SingleBhop => "Single Bhop",
            _ => "Sequential Bhop",
        };
    }

    // Hammer writes booleans as 0/1. A missing or unreadable value gets the fgd's default.
    private static bool GetBool(Entity entity, string key, bool defaultValue)
    {
        return entity.GetInt(key, defaultValue ? 1 : 0) != 0;
    }
}
