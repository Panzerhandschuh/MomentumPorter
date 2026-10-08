namespace MomentumPorter.Tests;

using System.IO.Compression;
using System.Text;
using BSPConvert.Lib;
using MomentumPorter.BspConvert;
using MomentumPorter.Detection;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Steam;
using Xunit;

public sealed class BspConvertHelpersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));

    public BspConvertHelpersTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private static byte[] Header(string magic, int version)
    {
        byte[] header = new byte[64];
        Encoding.ASCII.GetBytes(magic).CopyTo(header, 0);
        BitConverter.GetBytes(version).CopyTo(header, 4);
        return header;
    }

    private static byte[] GoldSrcHeader()
    {
        byte[] header = new byte[64];
        BitConverter.GetBytes(30).CopyTo(header, 0);
        return header;
    }

    private string CreateZip(string name, params (string Path, byte[] Data)[] entries)
    {
        string path = Path.Combine(_root, name);
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string entryPath, byte[] data) in entries)
        {
            using Stream stream = zip.CreateEntry(entryPath).Open();
            stream.Write(data);
        }

        return path;
    }

    private string CreateFile(string relativePath, string content = "")
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ListsTheMapsInAnArchive()
    {
        string pk3 = CreateZip(
            "maps.pk3",
            ("maps/pgrocket.bsp", Header("IBSP", 46)),
            ("maps/sub/PGPlasma.BSP", Header("IBSP", 47)),
            ("maps/q2map.bsp", Header("IBSP", 38)),
            ("maps/source.bsp", Header("VBSP", 21)),
            ("cstrike/maps/kz_test.bsp", GoldSrcHeader()),
            ("maps/pgrocket.aas", [1, 2, 3]),
            ("textures/a.tga", [1]));

        IReadOnlyList<ArchiveMap> maps = Pk3Inspector.ListMaps(pk3);

        Assert.Equal(["kz_test", "PGPlasma", "pgrocket", "q2map", "source"], maps.Select(x => x.Name));
        Assert.Equal(InputKind.GoldSrcBsp, maps[0].Kind);
        Assert.Equal(InputKind.Quake3Bsp, maps[1].Kind);
        Assert.Equal("maps/sub/PGPlasma.BSP", maps[1].EntryPath);
        Assert.Equal(InputKind.Quake3Bsp, maps[2].Kind);
        Assert.Equal(InputKind.Unknown, maps[3].Kind);
        Assert.Equal(InputKind.SourceBsp, maps[4].Kind);
        Assert.Equal(64, maps[2].Size);
        Assert.Equal([true, true, true, false, false], maps.Select(x => x.CanConvert));
        Assert.True(Pk3Inspector.HasQuake3Maps(pk3));
    }

    [Fact]
    public void ListsALooseBsp()
    {
        string bsp = Path.Combine(_root, "kz_test.bsp");
        File.WriteAllBytes(bsp, GoldSrcHeader());

        ArchiveMap map = Assert.Single(Pk3Inspector.ListMaps(bsp));
        Assert.Equal("kz_test", map.Name);
        Assert.Equal(InputKind.GoldSrcBsp, map.Kind);
        Assert.False(Pk3Inspector.HasQuake3Maps(bsp));
    }

    [Fact]
    public void SelectsMapsByFilter()
    {
        string pk3 = CreateZip("maps.pk3", ("maps/a.bsp", Header("IBSP", 46)), ("maps/b.bsp", Header("IBSP", 46)));
        var warnings = new List<string>();
        var log = new DelegateLog((level, message) =>
        {
            if (level == LogLevel.Warning)
                warnings.Add(message);
        });

        Assert.Equal(["b"], BspConvertStep.SelectMaps(pk3, new BspConvertOptions { Maps = "B,c" }, log).Select(x => x.Name));
        Assert.Equal(["maps.pk3 has no map named c"], warnings);

        var ex = Assert.Throws<PortStepException>(() => BspConvertStep.SelectMaps(pk3, new BspConvertOptions { Maps = "c" }, NullLog.Instance));
        Assert.Contains("a (Quake 3 map), b (Quake 3 map)", ex.Message);

        string sourceOnly = CreateZip("source.zip", ("maps/s.bsp", Header("VBSP", 20)));
        Assert.Throws<PortStepException>(() => BspConvertStep.SelectMaps(sourceOnly, new BspConvertOptions(), NullLog.Instance));
    }

    [Fact]
    public void FillsDefaultsFromSteam()
    {
        string steamapps = Path.Combine(_root, "steamapps");
        string halfLife = Path.Combine(steamapps, "common", "Half-Life");
        foreach (string mod in new[] { "valve", "cstrike", "cstrike_downloads", "ag" })
            CreateFile(Path.Combine(halfLife, mod, "liblist.gam"));
        string studiomdl = CreateFile(Path.Combine(steamapps, "common", "Momentum Mod Playtest", "bin", "win64", "studiomdl.exe"));
        Directory.CreateDirectory(Path.Combine(steamapps, "common", "Momentum Mod Playtest", "momentum"));
        var steam = new SteamLocator(_root);

        // A map of another mod searches that mod first
        string input = Path.Combine(halfLife, "ag", "maps", "boot_camp.bsp");
        File.WriteAllBytes(CreateFile(input), GoldSrcHeader());

        var options = new BspConvertOptions();
        IReadOnlyList<string> filled = BspConvertDefaults.Fill(options, steam, input);

        Assert.Equal(3, filled.Count);
        Assert.Equal(
            string.Join(',', new[] { "ag", "cstrike", "cstrike_downloads", "valve" }.Select(x => Path.Combine(halfLife, x))),
            options.Wads);
        Assert.Equal(studiomdl, options.Studiomdl);
        Assert.Equal(Path.Combine(steamapps, "common", "Momentum Mod Playtest", "momentum"), options.OutputDirectory);

        // What the user set stays
        var custom = new BspConvertOptions { Wads = @"C:\wads", OutputDirectory = @"C:\out" };
        BspConvertDefaults.Fill(custom, steam, input);
        Assert.Equal(@"C:\wads", custom.Wads);
        Assert.Equal(@"C:\out", custom.OutputDirectory);
        Assert.Equal(studiomdl, custom.Studiomdl);

        // GoldSrc options aren't filled for Quake 3 maps
        string pk3 = CreateZip("q3.pk3", ("maps/a.bsp", Header("IBSP", 46)));
        var q3 = new BspConvertOptions();
        BspConvertDefaults.Fill(q3, steam, pk3, output: false);
        Assert.Null(q3.Wads);
        Assert.Null(q3.Studiomdl);
        Assert.Null(q3.OutputDirectory);
    }

    [Fact]
    public async Task SetsUpQuake3ContentFromThePakFiles()
    {
        string baseq3 = Path.Combine(_root, "steamapps", "common", "Quake 3 Arena", "baseq3");
        Directory.CreateDirectory(baseq3);
        File.Move(CreateZip("pak0.pk3", ("scripts/base.shader", "pak0"u8.ToArray()), ("textures/base/a.tga", [1])), Path.Combine(baseq3, "pak0.pk3"));
        File.Move(CreateZip("pak1.pk3", ("scripts/base.shader", "pak1"u8.ToArray()), ("../escape.txt", [1])), Path.Combine(baseq3, "pak1.pk3"));
        var steam = new SteamLocator(_root);

        IReadOnlyList<string> paks = Quake3Content.FindPakFiles(steam);
        Assert.Equal(["pak0.pk3", "pak1.pk3"], paks.Select(Path.GetFileName));

        string content = Path.Combine(_root, "Q3Content");
        CreateFile(Path.Combine("Q3Content", "instructions.txt"), "Extract pak0.pk3 here");
        Assert.True(Quake3Content.IsEmpty(content));

        await Quake3Content.ExtractAsync(paks, content, NullLog.Instance, CancellationToken.None);

        Assert.False(Quake3Content.IsEmpty(content));
        Assert.Equal("pak1", File.ReadAllText(Path.Combine(content, "scripts", "base.shader")));
        Assert.True(File.Exists(Path.Combine(content, "textures", "base", "a.tga")));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
        Assert.Equal(3, Directory.GetFiles(content, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void ForwardsBspConvertsLog()
    {
        var lines = new List<(LogLevel, string)>();
        var report = new PortReport();
        var logger = new PortLogAdapter(new DelegateLog((level, message) => lines.Add((level, message))), report);

        logger.Log("Converting a.bsp...");
        logger.Log("Warning: WAD not found: flash.wad");
        logger.Log("Error: a.bsp doesn't exist");

        Assert.Equal([(LogLevel.Info, "Converting a.bsp..."), (LogLevel.Warning, "Warning: WAD not found: flash.wad"), (LogLevel.Error, "Error: a.bsp doesn't exist")], lines);
        Assert.Equal(["a.bsp doesn't exist"], logger.Errors);
        using var writer = new StringWriter();
        report.Print(writer);
        Assert.Contains("warning: WAD not found: flash.wad", writer.ToString());
    }

    [Fact]
    public void DescribesBspConvertsProgress()
    {
        Assert.Equal("Converting textures 12/67", BspConvertStep.Describe(new ConversionProgress(0.5f, "Converting textures", "a", 0, 1, 12, 67)));
        Assert.Equal("Extracting a.pk3", BspConvertStep.Describe(new ConversionProgress(0f, "Extracting a.pk3", null, 0, 0, 0, 0)));
        Assert.Equal("b (2/3): Writing BSP", BspConvertStep.Describe(new ConversionProgress(0.6f, "Writing BSP", "b", 1, 3, 0, 0)));
    }
}
