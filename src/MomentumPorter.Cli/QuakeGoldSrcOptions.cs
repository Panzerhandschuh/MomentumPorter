namespace MomentumPorter.Cli;

using CommandLine;
using MomentumPorter.BspConvert;

/// <summary>
/// BSPConvert's options, with its command line names. Unset values keep <see cref="BspConvertOptions"/>' defaults,
/// which are BSPConvert's.
/// </summary>
internal sealed partial class CliOptions
{
    [Option("nopak", HelpText = BspConvertHelp.NoPak)]
    public bool NoPak { get; set; }

    [Option("prefix", HelpText = BspConvertHelp.Prefix)]
    public string? Prefix { get; set; }

    [Option("maps", HelpText = BspConvertHelp.Maps)]
    public string? Maps { get; set; }

    [Option("subdiv", HelpText = "(Default: 3) " + BspConvertHelp.Subdiv)]
    public int? Subdiv { get; set; }

    [Option("notooldisps", HelpText = BspConvertHelp.NoToolDisps)]
    public bool NoToolDisps { get; set; }

    [Option("patchdisps", HelpText = BspConvertHelp.PatchDisps)]
    public bool PatchDisps { get; set; }

    [Option("mindmg", HelpText = "(Default: 50) " + BspConvertHelp.MinDmg)]
    public int? MinDmg { get; set; }

    [Option("lavatriggers", HelpText = BspConvertHelp.LavaTriggers)]
    public bool LavaTriggers { get; set; }

    // Also for GOKZ maps, see Program.Run
    [Option("nozones", HelpText = BspConvertHelp.NoZones + " Also for GOKZ maps.")]
    public bool NoZones { get; set; }

    [Option("offmodeents", HelpText = "(Default: cpm) " + BspConvertHelp.OffModeEnts)]
    public string? OffModeEnts { get; set; }

    [Option("scale", HelpText = "(Default: 1) " + BspConvertHelp.Scale)]
    public float? Scale { get; set; }

    [Option("fogobb", HelpText = BspConvertHelp.FogObb)]
    public bool FogObb { get; set; }

    [Option("fogminheight", HelpText = "(Default: 256) " + BspConvertHelp.FogMinHeight)]
    public float? FogMinHeight { get; set; }

    [Option("nofogoverlay", HelpText = BspConvertHelp.NoFogOverlay)]
    public bool NoFogOverlay { get; set; }

    [Option("clampoverbright", HelpText = BspConvertHelp.ClampOverbright)]
    public bool ClampOverbright { get; set; }

    [Option("q3gamma", HelpText = "(Default: 1) " + BspConvertHelp.Q3Gamma)]
    public float? Q3Gamma { get; set; }

    [Option("mapoverbrightbits", HelpText = "(Default: 2) " + BspConvertHelp.MapOverBrightBits)]
    public int? MapOverBrightBits { get; set; }

    [Option("q3mod", HelpText = BspConvertHelp.Q3Mod + " Takes the mod's folder name (missionpack...) or baseq3.")]
    public string? Q3Mod { get; set; }

    [Option("noq3content", HelpText = "Don't set up the Q3Content and Q3Mods folders from Quake 3's install. " + BspConvertHelp.Q3Content)]
    public bool NoQ3Content { get; set; }

    [Option("noanim", HelpText = BspConvertHelp.NoAnim)]
    public bool NoAnim { get; set; }

    [Option("animbudget", HelpText = "(Default: 16) " + BspConvertHelp.AnimBudget)]
    public float? AnimBudget { get; set; }

    [Option("animmaxres", HelpText = "(Default: 512) " + BspConvertHelp.AnimMaxRes)]
    public int? AnimMaxRes { get; set; }

    [Option("animfps", HelpText = "(Default: 16) " + BspConvertHelp.AnimFps)]
    public int? AnimFps { get; set; }

    [Option("animmaxframes", HelpText = "(Default: 512) " + BspConvertHelp.AnimMaxFrames)]
    public int? AnimMaxFrames { get; set; }

    [Option("animalpha", HelpText = "(Default: 1) " + BspConvertHelp.AnimAlpha)]
    public float? AnimAlpha { get; set; }

    [Option("wads", HelpText = BspConvertHelp.Wads + " Defaults to the Half-Life install's cstrike and valve folders.")]
    public string? Wads { get; set; }

    [Option("studiomdl", HelpText = BspConvertHelp.Studiomdl + " Defaults to the Momentum Mod install's.")]
    public string? Studiomdl { get; set; }

    [Option("goldsrchulls", HelpText = BspConvertHelp.GoldSrcHulls)]
    public string? GoldSrcHulls { get; set; }

    [Option("pointsample", HelpText = BspConvertHelp.PointSample)]
    public bool PointSample { get; set; }

    [Option("gamma", HelpText = BspConvertHelp.Gamma)]
    public float? Gamma { get; set; }

    [Option("brightness", HelpText = BspConvertHelp.Brightness)]
    public float? Brightness { get; set; }

    [Option("nooverbright", HelpText = "Match the map's lighting to GoldSrc's fixed function renderer (gl_use_shaders 0) with gl_overbright 0. Half-Life's shaders always draw overbright lighting, whatever gl_overbright says.")]
    public bool NoOverbright { get; set; }

    [Option("lightgamma", HelpText = BspConvertHelp.LightGamma)]
    public float? LightGamma { get; set; }

    [Option("texgamma", HelpText = BspConvertHelp.TexGamma)]
    public float? TexGamma { get; set; }

    [Option("noblockfix", HelpText = "Leave GoldSrc bhop blocks as they are. Otherwise: " + BspConvertHelp.FixBhopBlocks)]
    public bool NoBlockFix { get; set; }
}
