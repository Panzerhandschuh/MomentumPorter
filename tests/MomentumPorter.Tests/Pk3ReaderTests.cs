namespace MomentumPorter.Tests;

using System.IO.Compression;
using MomentumPorter.Assets.Common;
using Xunit;

public sealed class Pk3ReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));

    public Pk3ReaderTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Writes a zip with the given files (directories get their own entries, as in Quake 3's pk3s).</summary>
    internal static void WritePk3(string path, IReadOnlyDictionary<string, byte[]> files)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string directory in files.Keys.Select(x => x[..(x.LastIndexOf('/') + 1)]).Where(x => x.Length > 0).Distinct())
            zip.CreateEntry(directory);

        foreach ((string name, byte[] bytes) in files)
        {
            using Stream stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }

    [Fact]
    public void LaterPk3sOverrideEarlierOnes()
    {
        WritePk3(Path.Combine(_dir, "pak0.pk3"), new Dictionary<string, byte[]>
        {
            ["sound/player/ranger/jump1.wav"] = [1],
            ["sound/player/land1.wav"] = [2],
        });
        WritePk3(Path.Combine(_dir, "pak1.pk3"), new Dictionary<string, byte[]>
        {
            ["sound/player/Ranger/Jump1.wav"] = [3],
        });

        using Pk3Reader reader = Pk3Reader.OpenDirectory(_dir);

        Assert.Equal(2, reader.Archives.Count);
        Assert.Equal([3], reader.ReadAllBytes("sound/player/ranger/jump1.wav"));
        Assert.EndsWith("pak1.pk3", reader.ArchiveOf(@"sound\player\ranger\jump1.wav"));
        Assert.Equal([2], reader.ReadAllBytes("sound/player/land1.wav"));
        Assert.Null(reader.TryReadAllBytes("sound/player/none.wav"));
        Assert.Equal(2, reader.List("sound/player").Count());
        Assert.Single(reader.List("sound/player/ranger/"));

        string extracted = Path.Combine(_dir, "out", "land1.wav");
        reader.Extract("sound/player/land1.wav", extracted);
        Assert.Equal([2], File.ReadAllBytes(extracted));
    }
}
