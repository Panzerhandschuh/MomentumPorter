namespace MomentumPorter.Tests;

using BSPConvert.Lib;
using MomentumPorter.BspConvert;
using MomentumPorter.Options;
using Xunit;

public class BspConvertOptionsTests
{
    [Fact]
    public void DefaultsMatchBspConvertsCommandLine()
    {
        // What BSPConv.exe passes when no option is given (BSPConvert.Cmd/Program.cs)
        BSPConverterOptions converter = new BspConvertOptions().ToConverterOptions("in.pk3", "out");

        Assert.Equal("in.pk3", converter.inputFile);
        Assert.Equal("out", converter.outputDir);
        Assert.Null(converter.prefix);
        Assert.Null(converter.mapFilter);
        Assert.False(converter.noPak);
        Assert.False(converter.compress);
        Assert.False(converter.ignoreZones);
        Assert.Equal(50, converter.minDamageToRespawnPlayer);
        Assert.Equal(1f, converter.scale);
        Assert.Null(converter.goldSrcHullSource);
        Assert.Null(converter.goldSrc.wadDirs);
        Assert.Null(converter.goldSrc.studiomdlPath);
        Assert.False(converter.goldSrc.pointSample);
        Assert.Equal(2.2f, converter.goldSrc.brightness.gamma);
        Assert.Equal(0f, converter.goldSrc.brightness.brightness);
        Assert.True(converter.goldSrc.brightness.overbright);
        Assert.Equal(2.5f, converter.goldSrc.brightness.lightGamma);
        Assert.Equal(2f, converter.goldSrc.brightness.texGamma);

        Q3ConverterOptions q3 = converter.q3;
        Assert.False(q3.noToolDisplacements);
        Assert.True(q3.patchesAsPrimitives);
        Assert.Equal(3, q3.DisplacementPower);
        Assert.False(q3.lavaTriggers);
        Assert.False(q3.useObbFog);
        Assert.Equal(256f, q3.fogMinHeight);
        Assert.False(q3.noFogOverlay);
        Assert.Equal("cpm", q3.offModeEntityFallback);
        Assert.False(q3.clampOverbright);
        Assert.Equal(1f, q3.brightness.gamma);
        Assert.Equal(2, q3.brightness.mapOverBrightBits);
        Assert.Equal(0, q3.brightness.minLight);
        Assert.Equal(0, converter.goldSrc.brightness.minLight);
        Assert.Null(q3.mod);

        FlipbookOptions flipbook = q3.flipbook;
        Assert.True(flipbook.enabled);
        Assert.Equal(16L * 1024 * 1024, flipbook.byteBudget);
        Assert.Equal(512, flipbook.maxResolution);
        Assert.Equal(16, flipbook.fps);
        Assert.Equal(512, flipbook.maxFrames);
        Assert.Equal(1f, flipbook.alpha);
        Assert.True(flipbook.autoAlpha);
    }

    [Fact]
    public void AdvancedOptionsReachConverterOptions()
    {
        var options = new BspConvertOptions
        {
            PatchDisplacements = true,
            NoToolDisplacements = true,
            FogObb = true,
            FogMinHeight = 128,
            NoFogOverlay = true,
            ClampOverbright = true,
            MinDamage = 10,
            AnimMaxRes = 256,
            AnimFps = 30,
            AnimMaxFrames = 64,
            AnimAlpha = 0.5f,
            GoldSrcHulls = @"C:\hl\kz_persia.bsp",
        };
        Assert.All(
            new[] { nameof(options.PatchDisplacements), nameof(options.FogObb), nameof(options.ClampOverbright), nameof(options.AnimAlpha), nameof(options.GoldSrcHulls) },
            name => Assert.True(PortOptions.Describe<BspConvertOptions>().Single(x => x.Name == name).Attribute.Advanced, name));

        BSPConverterOptions converter = options.ToConverterOptions("in.bsp", "out");

        Assert.False(converter.q3.patchesAsPrimitives);
        Assert.True(converter.q3.noToolDisplacements);
        Assert.True(converter.q3.useObbFog);
        Assert.Equal(128f, converter.q3.fogMinHeight);
        Assert.True(converter.q3.noFogOverlay);
        Assert.True(converter.q3.clampOverbright);
        Assert.Equal(10, converter.minDamageToRespawnPlayer);
        Assert.Equal(256, converter.q3.flipbook.maxResolution);
        Assert.Equal(30, converter.q3.flipbook.fps);
        Assert.Equal(64, converter.q3.flipbook.maxFrames);
        Assert.Equal(0.5f, converter.q3.flipbook.alpha);
        Assert.False(converter.q3.flipbook.autoAlpha);
        Assert.Equal(@"C:\hl\kz_persia.bsp", converter.goldSrcHullSource);
    }

    [Fact]
    public void GeneralAndGoldSrcOptionsReachConverterOptions()
    {
        var options = new BspConvertOptions
        {
            NoPak = true,
            Compress = true,
            Prefix = "df_",
            Maps = "pgrocket, PGPlasma,",
            NoZones = true,
            Scale = 2,
            Subdiv = 4,
            LavaTriggers = true,
            OffModeEntities = OffModeEntities.VQ3,
            NoAnim = true,
            AnimBudget = 8,
            Wads = @"C:\wads, D:\more wads",
            Studiomdl = @"C:\momentum\bin\win64\studiomdl.exe",
            PointSample = true,
            Gamma = 2.5f,
            Brightness = 1f,
            Overbright = false,
            LightGamma = 2.4f,
            TexGamma = 1.9f,
        };

        BSPConverterOptions converter = options.ToConverterOptions("in.zip", "out");

        Assert.True(converter.noPak);
        Assert.True(converter.compress);
        Assert.Equal("df_", converter.prefix);
        Assert.Equal(["pgrocket", "PGPlasma"], converter.mapFilter);
        Assert.True(converter.ignoreZones);
        Assert.Equal(2f, converter.scale);
        Assert.Equal(4, converter.q3.DisplacementPower);
        Assert.True(converter.q3.lavaTriggers);
        Assert.Equal("vq3", converter.q3.offModeEntityFallback);
        Assert.False(converter.q3.flipbook.enabled);
        Assert.Equal(8L * 1024 * 1024, converter.q3.flipbook.byteBudget);
        Assert.Equal([@"C:\wads", @"D:\more wads"], converter.goldSrc.wadDirs);
        Assert.Equal(@"C:\momentum\bin\win64\studiomdl.exe", converter.goldSrc.studiomdlPath);
        Assert.True(converter.goldSrc.pointSample);
        Assert.Equal(2.5f, converter.goldSrc.brightness.gamma);
        Assert.Equal(1f, converter.goldSrc.brightness.brightness);
        Assert.False(converter.goldSrc.brightness.overbright);
        Assert.Equal(2.4f, converter.goldSrc.brightness.lightGamma);
        Assert.Equal(1.9f, converter.goldSrc.brightness.texGamma);

        IReadOnlyList<string> args = options.ToCommandLine();
        Assert.Contains("--pointsample", args);
        Assert.Contains("--nooverbright", args);
        Assert.Equal("2.5", args[args.ToList().IndexOf("--gamma") + 1]);
        Assert.Equal("1", args[args.ToList().IndexOf("--brightness") + 1]);
    }

    [Fact]
    public void DescribesEveryOptionForTheForm()
    {
        IReadOnlyList<PortOptionInfo> options = PortOptions.Describe<BspConvertOptions>();

        Assert.Equal(
            [BspConvertOptions.AnimatedTexturesGroup, BspConvertOptions.GeneralGroup, BspConvertOptions.ExperimentalGroup, BspConvertOptions.GoldSrcGroup, BspConvertOptions.Quake3Group],
            options.Select(x => x.Attribute.Group).Distinct().Order().ToArray());

        // Every settable property is an option, so the form shows all of them
        Assert.Equal(typeof(BspConvertOptions).GetProperties().Count(x => x.CanWrite), options.Count);

        foreach (PortOptionInfo option in options)
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Attribute.Label), option.Name);
            Assert.False(string.IsNullOrWhiteSpace(option.Attribute.Help), option.Name);
            Assert.True(option.Type == typeof(bool) || option.Type == typeof(int) || option.Type == typeof(float) || option.Type == typeof(string) || option.Type.IsEnum, option.Name);
        }

        Assert.Contains(options, x => x.Attribute.Advanced);
        Assert.Contains(options, x => !x.Attribute.Advanced);
        Assert.Equal(PathKind.Folder, options.Single(x => x.Name == nameof(BspConvertOptions.OutputDirectory)).Attribute.Path);

        // The bhop block fix is on unless it's turned off in the form's GoldSrc options
        PortOptionInfo blockFix = options.Single(x => x.Name == nameof(BspConvertOptions.FixBhopBlocks));
        Assert.Equal(BspConvertOptions.GoldSrcGroup, blockFix.Attribute.Group);
        Assert.False(blockFix.Attribute.Advanced);
        Assert.True(new BspConvertOptions().FixBhopBlocks);
    }

    [Theory]
    [InlineData(nameof(BspConvertOptions.Subdiv), 5)]
    [InlineData(nameof(BspConvertOptions.Subdiv), 1)]
    [InlineData(nameof(BspConvertOptions.Scale), 0f)]
    [InlineData(nameof(BspConvertOptions.AnimAlpha), 1.5f)]
    [InlineData(nameof(BspConvertOptions.AnimFps), 0)]
    [InlineData(nameof(BspConvertOptions.Quake3Gamma), 0.4f)]
    [InlineData(nameof(BspConvertOptions.Quake3Gamma), 3.5f)]
    [InlineData(nameof(BspConvertOptions.MapOverBrightBits), -1)]
    [InlineData(nameof(BspConvertOptions.MapOverBrightBits), 5)]
    public void RejectsOutOfRangeValues(string property, object value)
    {
        var options = new BspConvertOptions();
        Assert.Empty(options.Validate());

        typeof(BspConvertOptions).GetProperty(property)!.SetValue(options, value);
        Assert.NotEmpty(options.Validate());
    }

    [Fact]
    public void WritesBspConvertsCommandLine()
    {
        Assert.Empty(new BspConvertOptions().ToCommandLine());

        var options = new BspConvertOptions { Subdiv = 2, NoPak = true, OffModeEntities = OffModeEntities.VQ3, Scale = 1.5f, Maps = "a,b" };
        Assert.Equal(["--nopak", "--maps", "a,b", "--subdiv", "2", "--offmodeents", "vq3", "--scale", "1.5"], options.ToCommandLine());

        var brightness = new BspConvertOptions { Quake3Gamma = 1.3f, MapOverBrightBits = 3 };
        Assert.Equal(["--q3gamma", "1.3", "--mapoverbrightbits", "3"], brightness.ToCommandLine());
        Q3Brightness q3Brightness = brightness.ToConverterOptions("in.pk3", "out").q3.brightness;
        Assert.Equal(1.3f, q3Brightness.gamma);
        Assert.Equal(3, q3Brightness.mapOverBrightBits);

        // Clamp dark shadows is one checkbox for both games, a fixed minimum light
        var clampDarkShadows = new BspConvertOptions { ClampDarkShadows = true };
        Assert.Equal(["--minlight", "4"], clampDarkShadows.ToCommandLine());
        BSPConverterOptions clampConverter = clampDarkShadows.ToConverterOptions("in.pk3", "out");
        Assert.Equal(4, clampConverter.q3.brightness.minLight);
        Assert.Equal(4, clampConverter.goldSrc.brightness.minLight);

        var mod = new BspConvertOptions { Quake3Mod = "missionpack" };
        Assert.Equal(["--q3mod", "missionpack"], mod.ToCommandLine());
        Assert.Equal("missionpack", mod.ToConverterOptions("in.pk3", "out").q3.mod);
        Assert.Null(new BspConvertOptions { Quake3Mod = " " }.ToConverterOptions("in.pk3", "out").q3.mod);
    }
}
