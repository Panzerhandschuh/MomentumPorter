namespace MomentumPorter.Review;

/// <summary>
/// Momentum Mod's map submission documentation, down to the section.
/// </summary>
public static class DocsLinks
{
    public const string Root = "https://docs.momentum-mod.org/guide/map_submission/";

    public const string FixingEntities = Root + "fixing_entities/";
    public const string MapPorting = Root + "map_porting/";
    public const string MapZoning = Root + "map_zoning/";
    public const string MapSubmission = Root + "map_submission/";
    public const string ReviewChecklist = Root + "review_checklist/";

    // Fixing Entities and Other Issues
    public const string EntityToolsSetup = FixingEntities + "#setup";
    public const string EntityToolsList = FixingEntities + "#entity-tools";
    public const string TeleportVelocityMode = FixingEntities + "#teleport-velocity-mode";
    public const string KeepNegativeZ = FixingEntities + "#keep-negative-z";
    public const string ResetVelocity = FixingEntities + "#reset";
    public const string BoostTriggers = FixingEntities + "#boost-triggers";
    public const string GravityTriggers = FixingEntities + "#gravity-triggers";
    public const string ModelScaleFix = FixingEntities + "#model-scale-fix";
    public const string BhopTriggerFix = FixingEntities + "#bhop-trigger-fix";
    public const string BhopBlockFix = FixingEntities + "#bhop-block-fix";
    public const string CsmFix = FixingEntities + "#csm-fix";
    public const string ExportToLumper = FixingEntities + "#export-to-lumper";
    public const string FuncButton = FixingEntities + "#func_button";
    public const string LogicTimer = FixingEntities + "#logic_timer";
    public const string BhopFailTriggers = FixingEntities + "#bhop-triggers-used-as-fail-triggers";
    public const string Collectibles = FixingEntities + "#collectibles";

    // Map Porting
    public const string PortingSetup = MapPorting + "#preparing-the-game-for-porting";
    public const string RemoveValveAssets = MapPorting + "#step-1-remove-valve-assets";
    public const string Textures = MapPorting + "#textures";
    public const string Sounds = MapPorting + "#sounds";
    public const string Cubemaps = MapPorting + "#cubemaps";
    public const string FixEntitiesInLumper = MapPorting + "#step-3-fix-entities";
    public const string CompressTheMap = MapPorting + "#step-4-compress-the-map";
    public const string TakeScreenshots = MapPorting + "#step-5-take-screenshots";
    public const string ZoneTheMap = MapPorting + "#step-6-zone-the-map";

    // Map Zoning
    public const string ZoningSetup = MapZoning + "#setup";
    public const string BasicZoning = MapZoning + "#basic-zoning";
    public const string ZoneProperties = MapZoning + "#setting-zone-properties";
    public const string AllowBhop = MapZoning + "#allow-bhop";

    // Map Submission
    public const string SubmissionFiles = MapSubmission + "#files";
    public const string SubmissionDetails = MapSubmission + "#details";
    public const string MapName = MapSubmission + "#map-name";
    public const string RequiredGames = MapSubmission + "#required-games";
    public const string PortingChangelog = MapSubmission + "#porting-changelog";
    public const string Images = MapSubmission + "#images";
    public const string Credits = MapSubmission + "#credits";

    // Review Checklist
    public const string ChecklistZones = ReviewChecklist + "#step-1-review-zones";
    public const string ChecklistEntityTools = ReviewChecklist + "#step-2-review-in-game-entities-with-entity-tools";
    public const string ChecklistInGame = ReviewChecklist + "#step-3-other-in-game-things-to-review";
    public const string ChecklistLumper = ReviewChecklist + "#step-4-lumper-review";
    public const string ChecklistSubmission = ReviewChecklist + "#step-5-submission-page-review";

    public const string Dashboard = "https://dashboard.momentum-mod.org/";
}
