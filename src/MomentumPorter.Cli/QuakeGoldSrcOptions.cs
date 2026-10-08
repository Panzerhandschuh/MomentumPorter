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

    [Option("nozones", HelpText = BspConvertHelp.NoZones)]
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

    [Option("noenvmap", HelpText = BspConvertHelp.NoEnvMap)]
    public bool NoEnvMap { get; set; }

    [Option("clampoverbright", HelpText = BspConvertHelp.ClampOverbright)]
    public bool ClampOverbright { get; set; }

    [Option("noq3content", HelpText = "Don't set up the Q3Content folder from Steam's Quake 3 install when it's empty. " + BspConvertHelp.Q3Content)]
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
}
