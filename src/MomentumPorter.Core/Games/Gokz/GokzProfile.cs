namespace MomentumPorter.Games.Gokz;

using LibBSP;
using MomentumPorter.Porting;

/// <summary>
/// CS:GO maps made for GOKZ's mapping API (github.com/KZGlobalTeam/gokz/wiki/Mapping-API). The API is driven by
/// targetnames on stock entities (trigger_multiple, func_button, info_teleport_destination) plus keyvalues from
/// kz_mapping_api.fgd.
/// </summary>
public sealed partial class GokzProfile : IGameProfile
{
    // Tagging converted entities tells the in-game tools (and the porter's entity review) they're done
    private const string PortedByToolsKey = EntityTools.PortedByToolsKey;
    private const int EntityToolsVersion = EntityTools.Version;

    // kz_mapping_api.fgd's worldspawn key ("KZ | Mapping API Version")
    private const string MappingApiVersionKey = "climb_mapping_api_version";
    private const int KnownMappingApiVersion = 1;

    public string Name => "gokz";

    public string Description => "Counter-Strike: Global Offensive (GOKZ)";

    public string? MountedGame => "Counter-Strike: Global Offensive";

    // CS:GO already reverts gravity on leaving the trigger, like Momentum
    public bool GravityPersists => false;

    public void Apply(PortContext context)
    {
        CheckMappingApiVersion(context);
        ConvertClimbTriggers(context);
        RemoveUnsupportedTriggers(context);
        ConvertAntiCrouchTeleports(context);
        FixContextFilters(context);
        AddCascadeLight(context);
        WriteZones(context);
    }

    private static void CheckMappingApiVersion(PortContext context)
    {
        Entity? world = context.OfClass("worldspawn").FirstOrDefault();
        if (world is null || !world.ContainsKey(MappingApiVersionKey))
            return;

        int version = world.GetInt(MappingApiVersionKey, 0);
        if (version > KnownMappingApiVersion)
            context.Report.Warn($"the map uses GOKZ mapping API version {version}; only version {KnownMappingApiVersion} is known");

        world.Remove(MappingApiVersionKey);
        context.Report.Change("removed worldspawn climb_mapping_api_version");
    }
}
