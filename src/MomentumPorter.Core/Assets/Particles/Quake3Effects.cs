namespace MomentumPorter.Assets.Particles;

using System.Numerics;
using System.Text;
using MomentumPorter.Assets.Models;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
using sourcepp.vtfpp;

/// <summary>
/// Quake 3's projectile effects rebuilt as Source particle systems from Quake 3's own textures, timed as cgame draws
/// them (cg_weapons.c CG_MissileHitWall, cg_localents.c CG_AddSpriteExplosion, CG_RocketTrail):
/// <list type="bullet">
/// <item>Rocket, grenade and BFG explosions are a camera-facing sprite 16 units off the wall that grows from 30 to 72
/// units across the explosion, showing the shader's animmap frames, each fading out as the next fades in.</item>
/// <item>The plasma impact is a glow that spins and fades over 0.6 seconds.</item>
/// <item>Rocket and grenade trails leave a smoke puff every 50 ms that grows from 8 units and fades out.</item>
/// <item>The plasma ball is a spinning additive sprite on the (invisible) projectile.</item>
/// </list>
/// Every Quake 3 pack writes all of them to the same files, so packs for different Quake 3 weapons can sit side by
/// side.
/// </summary>
public static class Quake3Effects
{
    public const string ParticleFile = "particles/porter_q3.pcf";
    public const string MaterialFolder = "porter/q3/effects";

    /// <summary>A model with nothing to draw and a <c>trail</c> attachment, for projectiles that are only a sprite.</summary>
    public const string InvisibleProjectile = "models/porter/q3/effects/invisible_projectile.mdl";

    private const string None = "porter_q3_none";

    private static readonly VTF.CreationOptions VtfOptions = new()
    {
        Version = 6,
        CompressionLevel = 0,
        OutputFormat = ImageFormat.STRATA_BC7,
        WidthResizeMethod = ImageConversion.ResizeMethod.POWER_OF_TWO_BIGGER,
        HeightResizeMethod = ImageConversion.ResizeMethod.POWER_OF_TWO_BIGGER,
        ComputeMips = 1,
        ComputeThumbnail = 1,
        ComputeReflectivity = 0,
        ComputeTransparencyFlags = 1,
    };

    // Material name, Quake 3 image, additive
    private static readonly (string Name, string Image, bool Additive)[] Textures =
    [
        .. Enumerable.Range(1, 8).Select(i => ($"rlboom_{i}", $"models/weaphits/rlboom/rlboom_{i}.jpg", true)),
        .. Enumerable.Range(1, 3).Select(i => ($"glboom_{i}", $"models/weaphits/glboom/glboom_{i}.jpg", true)),
        .. Enumerable.Range(1, 3).Select(i => ($"bfgboom_{i}", $"models/weaphits/bfgboom/bfgboom_{i}.jpg", true)),
        ("plasmaboom", "models/weaphits/plasmaboom.jpg", true),
        ("plasmaa", "sprites/plasmaa.jpg", true),
        ("smokepuff3", "gfx/misc/smokepuff3.tga", false),
    ];

    /// <summary>The weapon script's ParticleData for a Quake 3 weapon (by catalog id), or null when it has no effects here.</summary>
    public static IReadOnlyDictionary<string, string>? ParticlesFor(string weaponId)
    {
        static Dictionary<string, string> Explosion(string name, string trailKey, string trail) => new()
        {
            ["ExplosionEffect"] = $"porter_q3_{name}_explosion",
            ["ExplosionMidAirEffect"] = $"porter_q3_{name}_explosion_midair",
            ["ExplosionWaterEffect"] = $"porter_q3_{name}_explosion",
            [trailKey] = trail,
        };

        return weaponId.ToLowerInvariant() switch
        {
            "rocketlauncher" => Explosion("rocket", "RocketTrail", "porter_q3_rocket_trail"),
            "grenadelauncher" => Explosion("grenade", "GrenadeTrail", "porter_q3_grenade_trail"),
            "plasmagun" => Explosion("plasma", "PlasmaTrail", "porter_q3_plasma_ball"),
            "bfg" => Explosion("bfg", "BFGTrail", None),
            _ => null,
        };
    }

    /// <summary>Whether the weapon's projectile is only a sprite, drawn by its trail on an invisible model.</summary>
    public static bool HasSpriteProjectile(string weaponId) => weaponId.Equals("plasmagun", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ParticleSystem> Systems()
    {
        return
        [
            .. AnimatedExplosion("rocket", "rlboom", 8, 8, 1.0f),
            .. AnimatedExplosion("grenade", "glboom", 3, 5, 0.6f),
            .. AnimatedExplosion("bfg", "bfgboom", 3, 5, 0.6f),
            PlasmaExplosion("porter_q3_plasma_explosion", 8),
            PlasmaExplosion("porter_q3_plasma_explosion_midair", 0),
            SmokeTrail("porter_q3_rocket_trail", 64, 2.0f),
            SmokeTrail("porter_q3_grenade_trail", 32, 0.7f),
            PlasmaBall(),
            new ParticleSystem(None) { MaxParticles = 1 },
        ];
    }

    /// <summary>
    /// Writes the effects' materials and particle file into the pack. <see cref="ParticleFile"/> still has to go into
    /// the pack's particle manifest (<see cref="ParticleManifest"/>).
    /// </summary>
    public static async Task WriteAsync(GameContent content, MomentumInstall momentum, IPackSink sink, string workDirectory, IPortLog log, CancellationToken cancellation)
    {
        string folder = Path.Combine(workDirectory, "q3_effects");
        Directory.CreateDirectory(folder);
        foreach ((string name, string image, bool additive) in Textures)
        {
            byte[] data = content.Read(image) ?? throw new PortStepException($"{image} isn't in {content.Name}'s files");
            string imageFile = Path.Combine(folder, name + Path.GetExtension(image));
            await File.WriteAllBytesAsync(imageFile, data, cancellation).ConfigureAwait(false);
            string vtfFile = Path.Combine(folder, name + ".vtf");
            if (!VTF.Create(imageFile, vtfFile, VtfOptions))
                throw new PortStepException($"Couldn't convert {image} to a VTF");

            sink.AddFile($"materials/{MaterialFolder}/{name}.vtf", vtfFile);
            sink.AddFile($"materials/{MaterialFolder}/{name}.vmt", Encoding.ASCII.GetBytes(SpriteMaterial(name, additive)));
        }

        string text = Path.Combine(folder, "porter_q3_text.pcf");
        string binary = Path.Combine(folder, "porter_q3.pcf");
        await File.WriteAllTextAsync(text, ParticleSystemFile.Write(Systems()), cancellation).ConfigureAwait(false);
        await new DmxConvert(momentum.DmxConvertPath, momentum.GameDirectory, log).ToBinaryAsync(text, binary, cancellation).ConfigureAwait(false);
        sink.AddFile(ParticleFile, binary);
        log.Info($"Wrote Quake 3's projectile effects to {ParticleFile}");
    }

    /// <summary>Compiles <see cref="InvisibleProjectile"/> and adds it to the pack.</summary>
    public static async Task<string> WriteInvisibleProjectileAsync(StudioMdlCompiler compiler, IPackSink sink, CancellationToken cancellation)
    {
        const string material = "invisible";
        string modelName = InvisibleProjectile["models/".Length..];
        string sourceFolder = compiler.CreateSourceFolder(modelName);

        var bones = new List<SmdBone> { new("root", -1) };
        var pose = new List<SmdBonePose> { new(Vector3.Zero, Vector3.Zero) };
        var a = new SmdVertex(0, new Vector3(0, 0, 0), Vector3.UnitZ, Vector2.Zero);
        var b = new SmdVertex(0, new Vector3(0.1f, 0, 0), Vector3.UnitZ, Vector2.UnitX);
        var c = new SmdVertex(0, new Vector3(0, 0.1f, 0), Vector3.UnitZ, Vector2.UnitY);
        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "reference.smd"), SmdWriter.WriteReference(bones, pose, [new SmdTriangle(material, a, b, c)]), cancellation).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "idle.smd"), SmdWriter.WriteAnimation(bones, [pose]), cancellation).ConfigureAwait(false);

        var qc = new QcWriter(modelName, $"models/{MaterialFolder}")
            .Origin(0, 0, 0, -90)
            .Body("projectile", "reference.smd")
            .Attachment("trail", "root", 0, 0, 0)
            .Sequence(new QcSequence("idle", "idle.smd", 30, true));
        string qcPath = Path.Combine(sourceFolder, "model.qc");
        await File.WriteAllTextAsync(qcPath, qc.ToString(), cancellation).ConfigureAwait(false);
        CompiledModel compiled = await compiler.CompileAsync(qcPath, modelName, cancellation).ConfigureAwait(false);
        foreach ((string gamePath, string file) in compiled.Files)
            sink.AddFile(gamePath, file);

        sink.AddFile($"materials/models/{MaterialFolder}/{material}.vmt", Encoding.ASCII.GetBytes("\"UnlitGeneric\"\n{\n\t\"$basetexture\" \"tools/toolsnodraw\"\n\t\"$no_draw\" \"1\"\n}\n"));
        return compiled.GamePath;
    }

    private static string SpriteMaterial(string name, bool additive)
    {
        var vmt = new StringBuilder();
        vmt.Append("\"SpriteCard\"\n{\n");
        vmt.Append($"\t\"$basetexture\" \"{MaterialFolder}/{name}\"\n");
        if (additive)
            vmt.Append("\t\"$additive\" \"1\"\n");
        vmt.Append("\t\"$vertexcolor\" \"1\"\n\t\"$vertexalpha\" \"1\"\n");
        vmt.Append("}\n");
        return vmt.ToString();
    }

    private static string Material(string texture) => $"{MaterialFolder}/{texture}.vmt";

    private static ParticleOperator AtControlPoint() => new("Position Within Sphere Random");

    // Along the wall's normal: an explosion's control point faces away from the wall it hit
    private static ParticleOperator OffFromWall(float distance) =>
        new ParticleOperator("Position Modify Offset Random")
            .SetVector("offset min", distance, 0, 0)
            .SetVector("offset max", distance, 0, 0)
            .Set("offset in local space 0/1", true);

    private static ParticleOperator Lifetime(float seconds) =>
        new ParticleOperator("Lifetime Random").Set("lifetime_min", seconds).Set("lifetime_max", seconds);

    private static ParticleOperator Radius(float radius) =>
        new ParticleOperator("Radius Random").Set("radius_min", radius).Set("radius_max", radius);

    private static ParticleOperator Spin(float degreesPerSecond) =>
        new ParticleOperator("Rotation Speed Random")
            .Set("rotation_speed_constant", degreesPerSecond)
            .Set("rotation_speed_random_max", 0f)
            .Set("randomly_flip_direction", false);

    /// <summary>
    /// A sprite explosion with an animmap of <paramref name="frames"/> frames at <paramref name="fps"/>, crossfaded
    /// as Quake 3's two-stage explosion shaders do: stage one shows frame k fading out while stage two shows frame k+1
    /// fading in, so frame k fades in over the period before its own and out over its own. Returns the explosion
    /// against a wall and the one in midair (no wall to step away from), each a parent of one system per frame.
    /// </summary>
    private static IEnumerable<ParticleSystem> AnimatedExplosion(string name, string texture, int frames, float fps, float duration)
    {
        float period = 1f / fps;
        float SpriteRadius(float t) => 30 + 42 * Math.Clamp(t / duration, 0, 1);

        ParticleSystem Build(string systemName, float offset)
        {
            var parent = new ParticleSystem(systemName) { MaxParticles = 1 };
            for (int k = 1; k <= frames; k++)
            {
                float start = Math.Max(0, (k - 2) * period);
                float end = Math.Min(duration, k * period);
                var frame = new ParticleSystem($"{systemName}_{k}") { Material = Material($"{texture}_{k}"), MaxParticles = 1 };
                frame.Renderers.Add(new ParticleOperator("render_animated_sprites"));
                frame.Emitters.Add(new ParticleOperator("emit_instantaneously").Set("num_to_emit", 1).Set("emission_start_time", start));
                frame.Initializers.Add(AtControlPoint());
                if (offset != 0)
                    frame.Initializers.Add(OffFromWall(offset));

                frame.Initializers.Add(Lifetime(end - start));
                frame.Initializers.Add(Radius(SpriteRadius(end)));
                frame.Operators.Add(new ParticleOperator("Radius Scale").Set("radius_start_scale", SpriteRadius(start) / SpriteRadius(end)));
                if (k > 1)
                    frame.Operators.Add(new ParticleOperator("Alpha Fade In Simple").Set("proportional fade in time", 0.5f));

                frame.Operators.Add(new ParticleOperator("Alpha Fade Out Simple").Set("proportional fade out time", k > 1 ? 0.5f : 1f));
                frame.Operators.Add(new ParticleOperator("Lifespan Decay"));
                parent.Children.Add(frame);
            }

            return parent;
        }

        yield return Build($"porter_q3_{name}_explosion", 16);
        yield return Build($"porter_q3_{name}_explosion_midair", 0);
    }

    // plasmaExplosion on ring02.md3: a 40 unit glow (the ring's 69 units at the shader's 0.6 stretch), turning at
    // 999 degrees a second and fading out over the 600 ms the explosion lasts
    private static ParticleSystem PlasmaExplosion(string name, float offset)
    {
        var system = new ParticleSystem(name) { Material = Material("plasmaboom"), MaxParticles = 1 };
        system.Renderers.Add(new ParticleOperator("render_animated_sprites"));
        system.Emitters.Add(new ParticleOperator("emit_instantaneously").Set("num_to_emit", 1));
        system.Initializers.Add(AtControlPoint());
        if (offset != 0)
            system.Initializers.Add(OffFromWall(offset));

        system.Initializers.Add(Lifetime(0.6f));
        system.Initializers.Add(Radius(40));
        system.Initializers.Add(new ParticleOperator("Rotation Random"));
        system.Initializers.Add(Spin(999));
        system.Operators.Add(new ParticleOperator("Rotation Basic"));
        system.Operators.Add(new ParticleOperator("Alpha Fade Out Simple").Set("proportional fade out time", 1f));
        system.Operators.Add(new ParticleOperator("Lifespan Decay"));
        return system;
    }

    // CG_RocketTrail: a smokePuff every 50 ms where the projectile is, 8 units growing to the weapon's trail radius
    // while its alpha fades from 0.33 over the trail time
    private static ParticleSystem SmokeTrail(string name, float radius, float lifetime)
    {
        var system = new ParticleSystem(name) { Material = Material("smokepuff3"), MaxParticles = (int)MathF.Ceiling(lifetime * 20) + 4 };
        system.Renderers.Add(new ParticleOperator("render_animated_sprites"));
        system.Emitters.Add(new ParticleOperator("emit_continuously").Set("emission_rate", 20f));
        system.Initializers.Add(AtControlPoint());
        system.Initializers.Add(Lifetime(lifetime));
        system.Initializers.Add(Radius(radius));
        system.Initializers.Add(new ParticleOperator("Alpha Random").Set("alpha_min", 84).Set("alpha_max", 84));
        system.Operators.Add(new ParticleOperator("Radius Scale").Set("radius_start_scale", 8f / radius));
        system.Operators.Add(new ParticleOperator("Alpha Fade Out Simple").Set("proportional fade out time", 1f));
        system.Operators.Add(new ParticleOperator("Lifespan Decay"));
        return system;
    }

    // The plasma ball: sprites/plasma1 at radius 16 turning 931 degrees a second. Momentum stops a trail's emission
    // when the projectile goes but lets its particles live, so the ball is a stream of short-lived sprites that follow
    // the projectile (two at a time, each at half brightness) rather than one that would stay behind
    private static ParticleSystem PlasmaBall()
    {
        var system = new ParticleSystem("porter_q3_plasma_ball") { Material = Material("plasmaa"), MaxParticles = 8 };
        system.Renderers.Add(new ParticleOperator("render_animated_sprites"));
        system.Emitters.Add(new ParticleOperator("emit_continuously").Set("emission_rate", 40f));
        system.Initializers.Add(AtControlPoint());
        system.Initializers.Add(Lifetime(0.05f));
        system.Initializers.Add(Radius(16));
        system.Initializers.Add(new ParticleOperator("Alpha Random").Set("alpha_min", 128).Set("alpha_max", 128));
        system.Initializers.Add(new ParticleOperator("Rotation Random"));
        system.Initializers.Add(Spin(931));
        system.Operators.Add(new ParticleOperator("Movement Lock to Control Point"));
        system.Operators.Add(new ParticleOperator("Rotation Basic"));
        system.Operators.Add(new ParticleOperator("Lifespan Decay"));
        return system;
    }
}
