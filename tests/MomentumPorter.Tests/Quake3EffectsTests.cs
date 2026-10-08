namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Particles;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
using MomentumPorter.Tools;
using Xunit;

public class Quake3EffectsTests
{
    [Fact]
    public void ManifestAddsTheParticleFileOnce()
    {
        const string momentum = "particles_manifest\n{\n    \"file\" \"!particles/mom_rocket.pcf\"\n}\n";

        string manifest = ParticleManifest.WithFiles(momentum, ["!" + Quake3Effects.ParticleFile, "particles/weapons/cs_weapon_fx.pcf"]);

        Assert.Contains("\"!particles/porter_q3.pcf\"", manifest);
        Assert.Contains("\"particles/weapons/cs_weapon_fx.pcf\"", manifest);
        Assert.Contains("\"!particles/mom_rocket.pcf\"", manifest);
        Assert.EndsWith("}\n", manifest);
        Assert.Equal(manifest, ParticleManifest.WithFiles(manifest, ["!" + Quake3Effects.ParticleFile]));
    }

    [Fact]
    public void EveryParticleTheScriptsNameIsDefined()
    {
        var names = new HashSet<string>();
        void Collect(ParticleSystem system)
        {
            names.Add(system.Name);
            foreach (ParticleSystem child in system.Children)
                Collect(child);
        }

        foreach (ParticleSystem system in Quake3Effects.Systems())
            Collect(system);

        foreach (string weapon in new[] { "rocketlauncher", "grenadelauncher", "plasmagun", "bfg" })
            Assert.All(Quake3Effects.ParticlesFor(weapon)!.Values, x => Assert.Contains(x, names));
    }

    [Fact]
    public async Task DmxConvertReadsTheParticleFile()
    {
        if (WeaponTestGames.Momentum is not { } momentum || !File.Exists(momentum.DmxConvertPath))
            return;

        string folder = WeaponTestGames.TempDirectory("q3pcf");
        try
        {
            string text = Path.Combine(folder, "text.pcf");
            string binary = Path.Combine(folder, "binary.pcf");
            string back = Path.Combine(folder, "back.pcf");
            await File.WriteAllTextAsync(text, ParticleSystemFile.Write(Quake3Effects.Systems()));

            var convert = new DmxConvert(momentum.DmxConvertPath, momentum.GameDirectory, NullLog.Instance);
            await convert.ToBinaryAsync(text, binary, CancellationToken.None);

            byte[] header = File.ReadAllBytes(binary)[..40];
            Assert.Contains("encoding binary", System.Text.Encoding.ASCII.GetString(header));

            // Back to text: every system and the attributes the porter sets survive
            ExternalToolResult result = await ExternalTool.RunAsync(
                momentum.DmxConvertPath, ["-i", binary, "-o", back, "-oe", "keyvalues2", "-game", momentum.GameDirectory], NullLog.Instance);
            Assert.True(result.Succeeded, result.StandardError);
            string roundTrip = await File.ReadAllTextAsync(back);
            Assert.Contains("\"porter_q3_rocket_explosion_8\"", roundTrip);
            Assert.Contains("\"porter_q3_plasma_ball\"", roundTrip);
            Assert.Contains("\"emission_start_time\" \"float\" \"0.75\"", roundTrip);
            Assert.Contains("porter\\\\q3\\\\effects\\\\rlboom_8.vmt", roundTrip);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
