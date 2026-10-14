namespace MomentumPorter.BspConvert;

using System.Globalization;
using BSPConvert.Lib;
using BSPConvert.Lib.GoldSrc;
using MomentumPorter.Options;

public enum OffModeEntities
{
    CPM,
    VQ3,
}

/// <summary>
/// Options for converting Quake 3 and GoldSrc maps with BSPConvert. Mirrors BSPConvert's command line one to one
/// (same defaults, help text from its README), grouped like the README: BSPConvert (general), Quake 3, Experimental,
/// Animated textures, GoldSrc. The comment on each option is its BSPConvert command line name.
/// </summary>
public sealed class BspConvertOptions
{
    public const string GeneralGroup = "BSPConvert";
    public const string Quake3Group = "Quake 3";
    public const string ExperimentalGroup = "Experimental";
    public const string GoldSrcGroup = "GoldSrc";
    public const string AnimatedTexturesGroup = "Animated textures";

    // --output
    [PortOption("Output game folder", BspConvertHelp.Output, Group = GeneralGroup, Order = 0, Path = PathKind.Folder)]
    public string? OutputDirectory { get; set; }

    // --prefix
    [PortOption("File name prefix", BspConvertHelp.Prefix, Group = GeneralGroup, Order = 20)]
    public string? Prefix { get; set; }

    // --compress
    [PortOption("Compress", BspConvertHelp.Compress, Group = GeneralGroup, Order = 30)]
    public bool Compress { get; set; }

    // --nopak
    [PortOption("Don't pack assets", BspConvertHelp.NoPak, Group = GeneralGroup, Order = 40)]
    public bool NoPak { get; set; }

    // --maps
    [PortOption("Maps", BspConvertHelp.Maps, Group = GeneralGroup, Order = 50, Advanced = true)]
    public string? Maps { get; set; }

    // Not a BSPConvert option: BSPConvert always overwrites, and so does the porter unless this is turned off
    [PortOption("Overwrite existing files", BspConvertHelp.Overwrite, Group = GeneralGroup, Order = 60, Advanced = true)]
    public bool Overwrite { get; set; } = true;

    // --subdiv
    [PortOption("Displacement subdivisions", BspConvertHelp.Subdiv, Group = Quake3Group, Order = 0, Min = 2, Max = 4)]
    public int Subdiv { get; set; } = 3;

    // --lavatriggers
    [PortOption("Lava kills/respawns", BspConvertHelp.LavaTriggers, Group = Quake3Group, Order = 10)]
    public bool LavaTriggers { get; set; }

    // --nozones
    [PortOption("No zones", BspConvertHelp.NoZones, Group = Quake3Group, Order = 20)]
    public bool NoZones { get; set; }

    // --offmodeents
    [PortOption("Entities outside Defrag modes", BspConvertHelp.OffModeEnts, Group = Quake3Group, Order = 30)]
    public OffModeEntities OffModeEntities { get; set; } = OffModeEntities.CPM;

    // --scale
    [PortOption("Scale", BspConvertHelp.Scale, Group = Quake3Group, Order = 40, Min = 0.01, Max = 100)]
    public float Scale { get; set; } = 1f;

    // --q3mod; null detects it
    [PortOption("Game content", BspConvertHelp.Q3Mod, Group = Quake3Group, Order = 50, Choices = true)]
    public string? Quake3Mod { get; set; }

    // Not a BSPConvert option, see Quake3Content
    [PortOption("Set up Quake 3 content", BspConvertHelp.Q3Content, Group = Quake3Group, Order = 60)]
    public bool SetUpQuake3Content { get; set; } = true;

    // --mindmg
    [PortOption("Respawn damage", BspConvertHelp.MinDmg, Group = Quake3Group, Order = 100, Advanced = true, Min = 0)]
    public int MinDamage { get; set; } = 50;

    // --notooldisps
    [PortOption("No tool texture displacements", BspConvertHelp.NoToolDisps, Group = Quake3Group, Order = 110, Advanced = true)]
    public bool NoToolDisplacements { get; set; }

    // --clampoverbright
    [PortOption("Clamp overbright", BspConvertHelp.ClampOverbright, Group = Quake3Group, Order = 130, Advanced = true)]
    public bool ClampOverbright { get; set; }

    // --nofogoverlay
    [PortOption("No fog overlay", BspConvertHelp.NoFogOverlay, Group = Quake3Group, Order = 160, Advanced = true)]
    public bool NoFogOverlay { get; set; }

    // --patchdisps
    [PortOption("Patches as displacements", BspConvertHelp.PatchDisps, Group = ExperimentalGroup, Order = 0, Advanced = true)]
    public bool PatchDisplacements { get; set; }

    // --fogobb
    [PortOption("Volumetric (obb) fog", BspConvertHelp.FogObb, Group = ExperimentalGroup, Order = 10, Advanced = true)]
    public bool FogObb { get; set; }

    // --fogminheight
    [PortOption("Fog minimum height", BspConvertHelp.FogMinHeight, Group = ExperimentalGroup, Order = 20, Advanced = true, Min = 0)]
    public float FogMinHeight { get; set; } = 256f;

    // --noanim
    [PortOption("Don't bake animated textures", BspConvertHelp.NoAnim, Group = AnimatedTexturesGroup, Order = 0)]
    public bool NoAnim { get; set; }

    // --animbudget
    [PortOption("Size budget (MB)", BspConvertHelp.AnimBudget, Group = AnimatedTexturesGroup, Order = 10, Min = 0.1)]
    public float AnimBudget { get; set; } = 16f;

    // --animmaxres
    [PortOption("Max resolution", BspConvertHelp.AnimMaxRes, Group = AnimatedTexturesGroup, Order = 20, Advanced = true, Min = 16, Max = 4096)]
    public int AnimMaxRes { get; set; } = 512;

    // --animfps
    [PortOption("Frames per second", BspConvertHelp.AnimFps, Group = AnimatedTexturesGroup, Order = 30, Advanced = true, Min = 1, Max = 120)]
    public int AnimFps { get; set; } = 16;

    // --animmaxframes
    [PortOption("Max frames", BspConvertHelp.AnimMaxFrames, Group = AnimatedTexturesGroup, Order = 40, Advanced = true, Min = 1)]
    public int AnimMaxFrames { get; set; } = 512;

    // --animalpha
    [PortOption("Liquid translucency", BspConvertHelp.AnimAlpha, Group = AnimatedTexturesGroup, Order = 50, Advanced = true, Min = 0, Max = 1)]
    public float AnimAlpha { get; set; } = 1f;

    // --wads
    [PortOption("WAD folders", BspConvertHelp.Wads, Group = GoldSrcGroup, Order = 0)]
    public string? Wads { get; set; }

    // --studiomdl; the app fills it from the Settings page or Momentum's install instead of showing it
    [PortOption("studiomdl.exe", BspConvertHelp.Studiomdl, Group = GoldSrcGroup, Order = 10, Path = PathKind.File)]
    public string? Studiomdl { get; set; }

    // --goldsrchulls
    [PortOption("Copy clip hulls from", BspConvertHelp.GoldSrcHulls, Group = GoldSrcGroup, Order = 20, Advanced = true, Path = PathKind.File)]
    public string? GoldSrcHulls { get; set; }

    // --pointsample
    [PortOption("Point sampled textures", BspConvertHelp.PointSample, Group = GoldSrcGroup, Order = 25)]
    public bool PointSample { get; set; }

    // --gamma
    [PortOption("Gamma", BspConvertHelp.Gamma, Group = GoldSrcGroup, Order = 26, Min = 1, Max = 5)]
    public float Gamma { get; set; } = 2.2f;

    // --brightness
    [PortOption("Brightness", BspConvertHelp.Brightness, Group = GoldSrcGroup, Order = 27, Min = 0, Max = 10)]
    public float Brightness { get; set; }

    // --nooverbright
    [PortOption("Overbright lighting", BspConvertHelp.Overbright, Group = GoldSrcGroup, Order = 42, Advanced = true)]
    public bool Overbright { get; set; } = true;

    // --lightgamma
    [PortOption("Light gamma", BspConvertHelp.LightGamma, Group = GoldSrcGroup, Order = 40, Advanced = true, Min = 1, Max = 5)]
    public float LightGamma { get; set; } = 2.5f;

    // --texgamma
    [PortOption("Texture gamma", BspConvertHelp.TexGamma, Group = GoldSrcGroup, Order = 41, Advanced = true, Min = 1, Max = 5)]
    public float TexGamma { get; set; } = 2f;

    // Not a BSPConvert option, see BhopBlockFixStep
    [PortOption("Fix bhop blocks", BspConvertHelp.FixBhopBlocks, Group = GoldSrcGroup, Order = 30)]
    public bool FixBhopBlocks { get; set; } = true;

    /// <summary>The <see cref="Maps"/> names, or null to convert every map in the input.</summary>
    public string[]? MapFilter => SplitList(Maps);

    public string[]? WadDirectories => SplitList(Wads);

    /// <summary>
    /// What's wrong with the options, using BSPConvert's own checks. Empty when they're fine.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Subdiv is < 2 or > 4)
            errors.Add("Displacement subdivisions must be between 2 and 4.");
        if (!(Scale > 0f))
            errors.Add("Scale must be greater than 0.");
        if (AnimAlpha is < 0f or > 1f)
            errors.Add("Liquid translucency must be between 0 and 1.");
        if (!(AnimBudget > 0f))
            errors.Add("The animated texture size budget must be greater than 0.");
        if (AnimFps <= 0 || AnimMaxFrames <= 0 || AnimMaxRes <= 0)
            errors.Add("Animated texture fps, max frames and max resolution must be greater than 0.");
        if (!(Gamma > 0f) || !(LightGamma > 0f) || !(TexGamma > 0f))
            errors.Add("Gamma, light gamma and texture gamma must be greater than 0.");
        if (!(Brightness >= 0f))
            errors.Add("Brightness can't be negative.");
        if (!Enum.IsDefined(OffModeEntities))
            errors.Add("Entities outside Defrag modes must be cpm or vq3.");

        return errors;
    }

    /// <summary>
    /// The options as BSPConvert takes them, mapped the same way as BSPConvert's command line does.
    /// </summary>
    /// <param name="outputDir">Where BSPConvert writes (maps/, maps/zones/local/, materials/...), not necessarily <see cref="OutputDirectory"/></param>
    public BSPConverterOptions ToConverterOptions(string inputFile, string outputDir)
    {
        return new BSPConverterOptions
        {
            inputFile = inputFile,
            outputDir = outputDir,
            prefix = string.IsNullOrEmpty(Prefix) ? null! : Prefix,
            mapFilter = MapFilter!,
            noPak = NoPak,
            compress = Compress,
            ignoreZones = NoZones,
            minDamageToRespawnPlayer = MinDamage,
            scale = Scale,
            goldSrcHullSource = string.IsNullOrWhiteSpace(GoldSrcHulls) ? null : GoldSrcHulls,
            goldSrc = new GoldSrcConverterOptions
            {
                wadDirs = WadDirectories!,
                studiomdlPath = string.IsNullOrWhiteSpace(Studiomdl) ? null! : Studiomdl,
                pointSample = PointSample,
                brightness = new GoldSrcBrightness
                {
                    gamma = Gamma,
                    brightness = Brightness,
                    overbright = Overbright,
                    lightGamma = LightGamma,
                    texGamma = TexGamma,
                },
            },
            q3 = new Q3ConverterOptions
            {
                noToolDisplacements = NoToolDisplacements,
                patchesAsPrimitives = !PatchDisplacements,
                DisplacementPower = Subdiv,
                lavaTriggers = LavaTriggers,
                useObbFog = FogObb,
                fogMinHeight = FogMinHeight,
                noFogOverlay = NoFogOverlay,
                offModeEntityFallback = OffModeEntities.ToString().ToLowerInvariant(),
                clampOverbright = ClampOverbright,
                mod = string.IsNullOrWhiteSpace(Quake3Mod) ? null : Quake3Mod.Trim(),
                flipbook = new FlipbookOptions
                {
                    enabled = !NoAnim,
                    byteBudget = (long)(AnimBudget * 1024 * 1024),
                    maxResolution = AnimMaxRes,
                    fps = AnimFps,
                    maxFrames = AnimMaxFrames,
                    alpha = AnimAlpha,
                    autoAlpha = AnimAlpha >= 1f,
                },
            },
        };
    }

    /// <summary>
    /// The options as BSPConvert's command line arguments (without the input and --output), for logs and for
    /// reproducing a conversion with BSPConv.exe. Options at their defaults are left out.
    /// </summary>
    public IReadOnlyList<string> ToCommandLine()
    {
        var defaults = new BspConvertOptions();
        var args = new List<string>();

        void Flag(bool value, string name)
        {
            if (value)
                args.Add(name);
        }

        void Value<T>(T value, T defaultValue, string name)
        {
            if (value is null || EqualityComparer<T>.Default.Equals(value, defaultValue))
                return;

            args.Add(name);
            args.Add(Convert.ToString(value, CultureInfo.InvariantCulture)!.ToLowerInvariant());
        }

        void Text(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            args.Add(name);
            args.Add(value);
        }

        Flag(NoPak, "--nopak");
        Flag(Compress, "--compress");
        Text(Prefix, "--prefix");
        Text(Maps, "--maps");
        Value(Subdiv, defaults.Subdiv, "--subdiv");
        Flag(NoToolDisplacements, "--notooldisps");
        Flag(PatchDisplacements, "--patchdisps");
        Value(MinDamage, defaults.MinDamage, "--mindmg");
        Flag(LavaTriggers, "--lavatriggers");
        Flag(NoZones, "--nozones");
        Value(OffModeEntities, defaults.OffModeEntities, "--offmodeents");
        Value(Scale, defaults.Scale, "--scale");
        Flag(FogObb, "--fogobb");
        Value(FogMinHeight, defaults.FogMinHeight, "--fogminheight");
        Flag(NoFogOverlay, "--nofogoverlay");
        Flag(ClampOverbright, "--clampoverbright");
        Text(Quake3Mod, "--q3mod");
        Flag(NoAnim, "--noanim");
        Value(AnimBudget, defaults.AnimBudget, "--animbudget");
        Value(AnimMaxRes, defaults.AnimMaxRes, "--animmaxres");
        Value(AnimFps, defaults.AnimFps, "--animfps");
        Value(AnimMaxFrames, defaults.AnimMaxFrames, "--animmaxframes");
        Value(AnimAlpha, defaults.AnimAlpha, "--animalpha");
        Text(Wads, "--wads");
        Text(Studiomdl, "--studiomdl");
        Text(GoldSrcHulls, "--goldsrchulls");
        Flag(PointSample, "--pointsample");
        Value(Gamma, defaults.Gamma, "--gamma");
        Value(Brightness, defaults.Brightness, "--brightness");
        Flag(!Overbright, "--nooverbright");
        Value(LightGamma, defaults.LightGamma, "--lightgamma");
        Value(TexGamma, defaults.TexGamma, "--texgamma");
        return args;
    }

    public BspConvertOptions Clone() => (BspConvertOptions)MemberwiseClone();

    /// <summary>
    /// What <see cref="Quake3Mod"/> can be, for a dropdown: detecting it from the map, Quake 3's content alone, then
    /// each of the mods (their folders in the Quake 3 install, see <see cref="Quake3Content.FindMods"/>).
    /// </summary>
    public static IReadOnlyList<(string? Value, string Label)> Quake3ModChoices(IEnumerable<string> mods)
    {
        var choices = new List<(string?, string)> { (null, "Detect from the map"), (Q3Mods.BaseGame, Q3Mods.GetDisplayName(Q3Mods.BaseGame)) };
        choices.AddRange(mods.Select(mod => ((string?)mod, Q3Mods.GetDisplayName(mod))));
        return choices;
    }

    private static string[]? SplitList(string? value)
    {
        string[]? items = value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items is { Length: > 0 } ? items : null;
    }
}
