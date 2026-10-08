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

        Q3ConverterOptions q3 = converter.q3;
        Assert.False(q3.noToolDisplacements);
        Assert.True(q3.patchesAsPrimitives);
        Assert.Equal(3, q3.DisplacementPower);
        Assert.False(q3.lavaTriggers);
        Assert.False(q3.useObbFog);
        Assert.Equal(256f, q3.fogMinHeight);
        Assert.False(q3.noFogOverlay);
        Assert.False(q3.noEnvMap);
        Assert.Equal("cpm", q3.offModeEntityFallback);
        Assert.False(q3.clampOverbright);

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
            NoEnvMap = true,
            OffModeEntities = OffModeEntities.Vq3,
            NoAnim = true,
            AnimBudget = 8,
            Wads = @"C:\wads, D:\more wads",
            Studiomdl = @"C:\momentum\bin\win64\studiomdl.exe",
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
        Assert.True(converter.q3.noEnvMap);
        Assert.Equal("vq3", converter.q3.offModeEntityFallback);
        Assert.False(converter.q3.flipbook.enabled);
        Assert.Equal(8L * 1024 * 1024, converter.q3.flipbook.byteBudget);
        Assert.Equal([@"C:\wads", @"D:\more wads"], converter.goldSrc.wadDirs);
        Assert.Equal(@"C:\momentum\bin\win64\studiomdl.exe", converter.goldSrc.studiomdlPath);
    }

    [Fact]
    public void DescribesEveryOptionForTheForm()
    {
        IReadOnlyList<PortOptionInfo> options = PortOptions.Describe<BspConvertOptions>();

        Assert.Equal(
            [BspConvertOptions.AnimatedTexturesGroup, BspConvertOptions.GeneralGroup, BspConvertOptions.GoldSrcGroup, BspConvertOptions.Quake3Group],
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

        var options = new BspConvertOptions { Subdiv = 2, NoPak = true, OffModeEntities = OffModeEntities.Vq3, Scale = 1.5f, Maps = "a,b" };
        Assert.Equal(["--nopak", "--maps", "a,b", "--subdiv", "2", "--offmodeents", "vq3", "--scale", "1.5"], options.ToCommandLine());
    }
}
