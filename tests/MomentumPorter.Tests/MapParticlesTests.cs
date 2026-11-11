namespace MomentumPorter.Tests;

using System.Text;
using LibBSP;
using MomentumPorter.Assets.Particles;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Fixes;
using MomentumPorter.Porting;
using MomentumPorter.Steam;
using Xunit;

/// <summary>
/// The map particle manifest: reading particle system names out of .pcf files, and listing the files a map needs that
/// Momentum doesn't load. The map runs are hosted in an installed CS:S map and do nothing when it isn't there.
/// </summary>
public class MapParticlesTests
{
    private static string? FindCssMap(string name)
    {
        GameLocation? css = new SteamLocator().Find(KnownGame.CounterStrikeSource);
        string? path = css?.ContentDirectory is { } content ? Path.Combine(content, "maps", name + ".bsp") : null;
        return path is not null && File.Exists(path) ? path : null;
    }

    private static string Pcf(params string[] systems) => ParticleSystemFile.Write(systems.Select(x => new ParticleSystem(x)));

    // A binary DMX with element headers only, as the engine's dmxloader lays them out for each encoding version
    private static byte[] BinaryPcf(int version, params (string Type, string Name)[] elements)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes($"<!-- dmx encoding binary {version} format pcf 1 -->\n\0"));
        var strings = elements.Select(x => x.Type).Concat(version >= 4 ? elements.Select(x => x.Name) : []).Distinct().ToList();
        if (version >= 2)
        {
            if (version >= 4)
                writer.Write(strings.Count);
            else
                writer.Write((short)strings.Count);

            foreach (string s in strings)
                writer.Write(Encoding.ASCII.GetBytes(s + "\0"));
        }

        void WriteString(string s)
        {
            if (version >= 5)
                writer.Write(strings.IndexOf(s));
            else
                writer.Write((short)strings.IndexOf(s));
        }

        writer.Write(elements.Length);
        foreach ((string type, string name) in elements)
        {
            if (version >= 2)
                WriteString(type);
            else
                writer.Write(Encoding.ASCII.GetBytes(type + "\0"));

            if (version >= 4)
                WriteString(name);
            else
                writer.Write(Encoding.ASCII.GetBytes(name + "\0"));

            writer.Write(new byte[16]);
        }

        // Attributes follow; the reader stops at the headers
        writer.Write(new byte[64]);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    public void BinaryPcfsListTheirParticleSystems(int version)
    {
        byte[] pcf = BinaryPcf(version, ("DmElement", "untitled"), ("DmeParticleSystemDefinition", "buildingdamage_fire3"), ("DmeParticleOperator", "Lifespan Decay"), ("DmeParticleSystemDefinition", "buildingdamage_fire1"));

        Assert.Equal(["buildingdamage_fire3", "buildingdamage_fire1"], ParticleSystemNames.Read(pcf));
    }

    [Fact]
    public void TextPcfsListTheirParticleSystems()
    {
        var parent = new ParticleSystem("torch");
        parent.Children.Add(new ParticleSystem("torch_embers"));

        Assert.Equal(["torch", "torch_embers"], ParticleSystemNames.Read(Encoding.UTF8.GetBytes(ParticleSystemFile.Write([parent]))));
        Assert.Throws<InvalidDataException>(() => ParticleSystemNames.Read("not a dmx file"u8.ToArray()));
    }

    [Fact]
    public void ManifestsListTheirFilesWithoutCommentsOrPrecacheMarks()
    {
        const string manifest = "particles_manifest\n{\n    \"file\"  \"!particles/weapons.pcf\"\n    // \"file\" \"particles/old.pcf\"\n\t\"file\"\t\"particles\\fire.pcf\" // torches\n}\n";

        Assert.Equal(["particles/weapons.pcf", "particles/fire.pcf"], MapParticles.ManifestFiles(manifest));
        Assert.Equal(["particles/a.pcf", "particles/b.pcf"], MapParticles.ManifestFiles(MapParticles.WriteManifest(["particles/a.pcf", "particles/b.pcf", "PARTICLES/A.pcf"])));
    }

    [Fact]
    public void TheMapListsTheParticleFilesMomentumDoesntLoad()
    {
        if (FindCssMap("bhop_arcane_v1") is not { } path)
            return;

        using var folders = new TempFolder();
        void Write(string root, string file, string text)
        {
            string full = folders.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        // Momentum loads base.pcf; TF2 has the torch twice (its manifest's file wins over the dx80 fallback), a base.pcf
        // that Momentum's shadows, and a file outside particles/ that the game wouldn't read
        Write("momentum", "particles/particles_manifest.txt", "particles_manifest\n{\n\t\"file\"\t\"!particles/base.pcf\"\n}\n");
        Write("momentum", "particles/base.pcf", Pcf("base_fx"));
        Write("tf", "particles/particles_manifest.txt", "particles_manifest\n{\n\t\"file\"\t\"particles/fire.pcf\"\n}\n");
        Write("tf", "particles/fire.pcf", Pcf("torch_fire", "torch_smoke"));
        Write("tf", "particles/fire_dx80.pcf", Pcf("torch_fire"));
        Write("tf", "particles/base.pcf", Pcf("tf_base_fx"));
        Write("tf", "particles/sub/hidden.pcf", Pcf("hidden_fx"));

        List<MapParticles.SearchPath> searchPaths =
        [
            new("Momentum", new GameContent("Momentum", [new FolderSource(folders.Combine("momentum"))])),
            new("Team Fortress 2", new GameContent("Team Fortress 2", [new FolderSource(folders.Combine("tf"))])),
        ];
        try
        {
            var context = new PortContext(new BSP(new FileInfo(path)), "sj_test", null);
            foreach (string effect in new[] { "base_fx", "torch_fire", "torch_fire", "map_fx", "tf_base_fx", "hidden_fx", "nowhere_fx" })
            {
                var entity = new Entity("info_particle_system");
                entity["effect_name"] = effect;
                context.AddEntity(entity);
            }

            var trigger = new Entity("trigger_particle");
            trigger["particle_name"] = "torch_smoke";
            context.AddEntity(trigger);

            // A system the map packs a file for, which its old manifest already lists
            context.EditPakFiles(zip => zip.AddEntry("particles/sj_test_fx.pcf", new MemoryStream(Encoding.UTF8.GetBytes(Pcf("map_fx"))), closeStream: true));
            context.WritePakFile("maps/sj_test_old_particles.txt", MapParticles.WriteManifest(["particles/sj_test_fx.pcf"]));

            MapParticles.Result result = MapParticles.Apply(context, searchPaths, previousMapName: "sj_test_old");

            Assert.Equal(["particles/fire.pcf"], result.AddedFiles);
            Assert.Equal(["torch_fire", "torch_smoke"], result.FixedSystems);
            Assert.Equal(["tf_base_fx", "hidden_fx", "nowhere_fx"], result.Unresolved);
            Assert.Equal(["particles/sj_test_fx.pcf", "particles/fire.pcf"], MapParticles.ManifestFiles(context.ReadPakFile("maps/sj_test_particles.txt")!));

            using var writer = new StringWriter();
            context.Report.Print(writer);
            string report = writer.ToString();
            Assert.Contains("particle files from Team Fortress 2 listed in maps/sj_test_particles.txt", report);
            Assert.Contains("tf_base_fx is in Team Fortress 2's particles/base.pcf, but Momentum loads particles/base.pcf from Momentum", report);
            Assert.Contains("hidden_fx isn't defined by Momentum", report);
            Assert.Contains("nowhere_fx isn't defined by Momentum", report);

            // Ported again, everything it can fix is already listed
            MapParticles.Result again = MapParticles.Apply(context, searchPaths);
            Assert.Empty(again.AddedFiles);
            Assert.Equal(["tf_base_fx", "hidden_fx", "nowhere_fx"], again.Unresolved);
        }
        finally
        {
            foreach (MapParticles.SearchPath searchPath in searchPaths)
                searchPath.Dispose();
        }
    }
}
