namespace MomentumPorter.Assets.Models;

using System.Numerics;
using System.Text;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using sourcepp.vtfpp;

/// <summary>
/// Converts Quake 3 weapons (models/weapons2/&lt;weapon&gt;/&lt;weapon&gt;.md3) to Source models. The weapon and its
/// barrel (at <c>tag_barrel</c>) become one mesh; the muzzle flash is left out. A view model hangs the weapon from
/// the hand model's <c>tag_weapon</c> on a bone, which the hand's frames move as Quake 3 does: frame 0 idle,
/// 1-6 firing recoil, 6-14 lowering (played backwards to raise it).
/// </summary>
public sealed class Quake3WeaponConverter
{
    // Quake 3 plays the torso's attack and drop animations at about this rate for the hand
    private const float HandFps = 15f;

    private static readonly VTF.CreationOptions VtfOptions = new()
    {
        Version = 6,
        CompressionLevel = 0,
        OutputFormat = ImageFormat.STRATA_BC7,
        WidthResizeMethod = ImageConversion.ResizeMethod.POWER_OF_TWO_BIGGER,
        HeightResizeMethod = ImageConversion.ResizeMethod.POWER_OF_TWO_BIGGER,
        ComputeMips = 1,
        ComputeThumbnail = 1,
        ComputeReflectivity = 1,
        ComputeTransparencyFlags = 1,
    };

    private readonly GameContent _content;
    private readonly StudioMdlCompiler _compiler;
    private readonly string _gameId;
    private Dictionary<string, Q3Shader>? _shaders;

    public Quake3WeaponConverter(GameContent content, StudioMdlCompiler compiler, string gameId)
    {
        _content = content;
        _compiler = compiler;
        _gameId = gameId;
    }

    private sealed record Q3Shader(string? Image, bool Additive, bool AlphaTest, bool TwoSided);

    /// <param name="modelPath">e.g. <c>models/weapons2/railgun/railgun.md3</c></param>
    /// <param name="kind">Non-null for the view model (hung from the hand model), null for the world model</param>
    /// <param name="projectile">
    /// A projectile: gets a <c>trail</c> attachment at its origin, where Momentum starts its trail particles and Quake 3
    /// leaves its smoke
    /// </param>
    public async Task<ConvertedModel> ConvertAsync(string modelPath, ViewModelKind? kind, IPortLog log, CancellationToken cancellation, bool projectile = false)
    {
        Md3Model weapon = ReadModel(modelPath) ?? throw new PortStepException($"{modelPath} isn't in {_content.Name}'s files");
        string directory = Path.GetDirectoryName(ContentPath.Normalize(modelPath))!.Replace('\\', '/');
        string baseName = Path.GetFileNameWithoutExtension(modelPath).ToLowerInvariant();

        string relativeDirectory = directory.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? directory["models/".Length..] : directory;
        string modelFolder = $"porter/{_gameId}/{relativeDirectory.ToLowerInvariant()}";
        string modelName = $"{modelFolder}/{(kind is null ? "w_" : "v_")}{baseName}.mdl";
        string materialFolder = "models/" + modelFolder;
        string sourceFolder = _compiler.CreateSourceFolder(modelName);

        // The weapon's meshes in its own space, with the barrel moved to its tag
        var meshes = new List<(Md3Model.Surface Surface, Func<Vector3, Vector3> Position, Func<Vector3, Vector3> Normal)>();
        foreach (Md3Model.Surface surface in weapon.Surfaces)
            meshes.Add((surface, x => x, x => x));

        if (ReadModel($"{directory}/{baseName}_barrel.md3") is { } barrel && weapon.FindTag("tag_barrel") is { } barrelTag)
        {
            foreach (Md3Model.Surface surface in barrel.Surfaces)
                meshes.Add((surface, barrelTag.Transform, barrelTag.TransformNormal));
        }

        var bones = new List<SmdBone> { new("root", -1) };
        var pose = new List<SmdBonePose> { new(Vector3.Zero, Vector3.Zero) };
        Md3Model.Tag[]? handFrames = null;
        if (kind is not null)
        {
            Md3Model hand = ReadModel($"{directory}/{baseName}_hand.md3")
                ?? throw new PortStepException($"{directory}/{baseName}_hand.md3, which places the view model, isn't in {_content.Name}'s files");
            handFrames = Enumerable.Range(0, hand.FrameCount).Select(f => hand.FindTag("tag_weapon", f)).OfType<Md3Model.Tag>().ToArray();
            if (handFrames.Length == 0)
                throw new PortStepException($"{baseName}_hand.md3 has no tag_weapon");

            bones.Add(new SmdBone("tag_weapon", 0));
            pose.Add(ToPose(handFrames[0]));
        }

        // Vertices are written in model space at the reference pose, on the weapon bone if there's one
        int weaponBone = bones.Count - 1;
        Md3Model.Tag? reference = handFrames?[0];
        var materials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string GamePath, string File)>();
        var triangles = new List<SmdTriangle>();
        foreach ((Md3Model.Surface surface, Func<Vector3, Vector3> toWeapon, Func<Vector3, Vector3> toWeaponNormal) in meshes)
        {
            string shader = surface.Shaders.FirstOrDefault() ?? surface.Name;
            if (!materials.TryGetValue(shader, out string? material))
            {
                material = ConvertMaterial(shader, materialFolder, files, log);
                materials[shader] = material;
            }

            if (material.Length == 0)
                continue;

            Vector3[] positions = surface.Positions[0];
            Vector3[] normals = surface.Normals[0];
            SmdVertex Vertex(int index)
            {
                Vector3 position = toWeapon(positions[index]);
                Vector3 normal = toWeaponNormal(normals[index]);
                if (reference is not null)
                {
                    position = reference.Transform(position);
                    normal = reference.TransformNormal(normal);
                }

                Vector2 st = surface.TexCoords[index];
                return new SmdVertex(weaponBone, position, normal, new Vector2(st.X, 1f - st.Y));
            }

            for (int i = 0; i + 2 < surface.Triangles.Length; i += 3)
            {
                SmdVertex a = Vertex(surface.Triangles[i]);
                SmdVertex b = Vertex(surface.Triangles[i + 1]);
                SmdVertex c = Vertex(surface.Triangles[i + 2]);

                // Keep the winding that faces the way the vertex normals do
                Vector3 face = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
                triangles.Add(Vector3.Dot(face, a.Normal + b.Normal + c.Normal) >= 0
                    ? new SmdTriangle(material, a, b, c)
                    : new SmdTriangle(material, a, c, b));
            }
        }

        if (triangles.Count == 0)
            throw new PortStepException($"{modelPath} has nothing to draw");

        await File.WriteAllTextAsync(Path.Combine(sourceFolder, "reference.smd"), SmdWriter.WriteReference(bones, pose, triangles), cancellation).ConfigureAwait(false);

        // Source's studiomdl turns models 90 degrees unless told otherwise; these are already in game space
        var qc = new QcWriter(modelName, materialFolder).Origin(0, 0, 0, -90).SurfaceProp("metal").Body("weapon", "reference.smd");
        if (projectile)
            qc.Attachment("trail", "root", 0, 0, 0);

        if (handFrames is null)
        {
            await WriteAnimation(sourceFolder, "idle.smd", bones, [pose], cancellation).ConfigureAwait(false);
            qc.Sequence(new QcSequence("idle", "idle.smd", 30, true));
        }
        else
        {
            List<SmdBonePose> Frame(int index) => [pose[0], ToPose(handFrames[Math.Clamp(index, 0, handFrames.Length - 1)])];

            await WriteAnimation(sourceFolder, "idle.smd", bones, [Frame(0)], cancellation).ConfigureAwait(false);
            await WriteAnimation(sourceFolder, "fire.smd", bones, [.. Enumerable.Range(1, 6).Select(Frame), Frame(0)], cancellation).ConfigureAwait(false);
            await WriteAnimation(sourceFolder, "draw.smd", bones, [.. Enumerable.Range(6, 9).Reverse().Select(Frame), Frame(0)], cancellation).ConfigureAwait(false);
            string attack = kind switch
            {
                ViewModelKind.Gauntlet => "ACT_VM_SWINGHIT",
                ViewModelKind.Melee => "ACT_VM_HITCENTER",
                ViewModelKind.Grenade => "ACT_VM_THROW",
                _ => "ACT_VM_PRIMARYATTACK",
            };
            qc.Sequence(new QcSequence("idle", "idle.smd", 30, true, "ACT_VM_IDLE"));
            qc.Sequence(new QcSequence("fire", "fire.smd", HandFps, false, attack));
            qc.Sequence(new QcSequence("draw", "draw.smd", HandFps, false, "ACT_VM_DRAW"));
        }

        string qcPath = Path.Combine(sourceFolder, "model.qc");
        await File.WriteAllTextAsync(qcPath, qc.ToString(), cancellation).ConfigureAwait(false);
        CompiledModel compiled = await _compiler.CompileAsync(qcPath, modelName, cancellation).ConfigureAwait(false);
        files.AddRange(compiled.Files);
        log.Info($"Converted {modelPath} to {compiled.GamePath}");
        return new ConvertedModel(compiled.GamePath, files, []);
    }

    private static Task WriteAnimation(string folder, string name, IReadOnlyList<SmdBone> bones, IReadOnlyList<IReadOnlyList<SmdBonePose>> frames, CancellationToken cancellation)
    {
        return File.WriteAllTextAsync(Path.Combine(folder, name), SmdWriter.WriteAnimation(bones, frames), cancellation);
    }

    private Md3Model? ReadModel(string path)
    {
        byte[]? data = _content.Read(path);
        if (data is null)
            return null;

        try
        {
            return Md3Model.Read(data);
        }
        catch (InvalidDataException ex)
        {
            throw new PortStepException($"Couldn't read {path}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// A tag's placement as an SMD bone pose: rotations about x, then y, then z (R = Rz Ry Rx).
    /// </summary>
    public static SmdBonePose ToPose(Md3Model.Tag tag)
    {
        // Matrix columns are the tag's axes: R[row][column] = Axis[column][row]
        float r00 = tag.Axis[0].X, r10 = tag.Axis[0].Y, r20 = tag.Axis[0].Z;
        float r21 = tag.Axis[1].Z, r22 = tag.Axis[2].Z;
        float y = MathF.Asin(Math.Clamp(-r20, -1f, 1f));
        float x = MathF.Atan2(r21, r22);
        float z = MathF.Atan2(r10, r00);
        if (MathF.Abs(MathF.Cos(y)) < 1e-5f)
        {
            // Gimbal lock: put the whole turn in x
            x = MathF.Atan2(-tag.Axis[2].Y, tag.Axis[1].Y);
            z = 0;
        }

        return new SmdBonePose(tag.Origin, new Vector3(x, y, z));
    }

    // Converts a surface's shader to a material in the model's material folder. Returns its name, or "" to leave
    // the surface out (shaders with nothing to draw).
    private string ConvertMaterial(string shaderName, string materialFolder, List<(string GamePath, string File)> files, IPortLog log)
    {
        string name = ContentPath.Normalize(Path.ChangeExtension(shaderName, null) ?? shaderName);
        Q3Shader shader = FindShader(name) ?? new Q3Shader(name, false, false, false);
        if (shader.Image is null)
            return "";

        string? imagePath = FindImage(shader.Image);
        string material = Sanitize(Path.GetFileName(name));
        if (imagePath is null)
        {
            log.Warn($"No image for {shaderName}; the surface is drawn with a missing texture");
            return material;
        }

        string vtfGamePath = $"materials/{materialFolder}/{material}.vtf";
        string vtfFile = Path.Combine(_compiler.GameDirectory, vtfGamePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(vtfFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(vtfFile)!);
            string imageFile = Path.Combine(_compiler.SourceDirectory, "images", material + Path.GetExtension(imagePath));
            Directory.CreateDirectory(Path.GetDirectoryName(imageFile)!);
            File.WriteAllBytes(imageFile, _content.Read(imagePath)!);
            if (!VTF.Create(imageFile, vtfFile, VtfOptions))
                throw new PortStepException($"Couldn't convert {imagePath} to a VTF");

            var vmt = new StringBuilder();
            vmt.AppendLine(shader.Additive ? "\"UnlitGeneric\"" : "\"VertexLitGeneric\"");
            vmt.AppendLine("{");
            vmt.AppendLine($"\t\"$basetexture\" \"{materialFolder}/{material}\"");
            if (shader.Additive)
                vmt.AppendLine("\t\"$additive\" \"1\"");
            if (shader.AlphaTest)
                vmt.AppendLine("\t\"$alphatest\" \"1\"");
            if (shader.TwoSided)
                vmt.AppendLine("\t\"$nocull\" \"1\"");
            vmt.AppendLine("}");
            string vmtFile = Path.ChangeExtension(vtfFile, ".vmt");
            File.WriteAllText(vmtFile, vmt.ToString());
            files.Add((vtfGamePath, vtfFile));
            files.Add((Path.ChangeExtension(vtfGamePath, ".vmt"), vmtFile));
        }

        return material;
    }

    // Quake 3 loads a .jpg when the .tga a shader names is missing, and the other way round
    private string? FindImage(string image)
    {
        string normalized = ContentPath.Normalize(image);
        foreach (string candidate in new[] { normalized, Path.ChangeExtension(normalized, ".tga"), Path.ChangeExtension(normalized, ".jpg") })
        {
            if (Path.HasExtension(candidate) && _content.Exists(candidate))
                return candidate;
        }

        // Names with dots in them ("railgun2.glow")
        foreach (string extension in new[] { ".tga", ".jpg" })
        {
            if (_content.Exists(normalized + extension))
                return normalized + extension;
        }

        return null;
    }

    private Q3Shader? FindShader(string name)
    {
        _shaders ??= LoadShaders();
        return _shaders.GetValueOrDefault(name);
    }

    // The first texture stage of each shader in scripts/*.shader, and how it's blended
    private Dictionary<string, Q3Shader> LoadShaders()
    {
        var shaders = new Dictionary<string, Q3Shader>(StringComparer.OrdinalIgnoreCase);
        foreach (string script in _content.List("scripts/").Where(x => x.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)))
        {
            string text = Encoding.ASCII.GetString(_content.Read(script)!);
            var lines = text.Split('\n').Select(x => (x.Contains("//") ? x[..x.IndexOf("//", StringComparison.Ordinal)] : x).Trim()).Where(x => x.Length > 0).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] is "{" or "}" || i + 1 >= lines.Count || lines[i + 1] != "{")
                    continue;

                string shaderName = lines[i];
                int depth = 0;
                string? image = null;
                bool additive = false, alphaTest = false, twoSided = false, stageTaken = false;
                int j = i + 1;
                for (; j < lines.Count; j++)
                {
                    string line = lines[j];
                    if (line == "{")
                    {
                        depth++;
                        continue;
                    }

                    if (line == "}")
                    {
                        depth--;
                        if (depth == 1 && image is not null)
                            stageTaken = true;
                        if (depth == 0)
                            break;
                        continue;
                    }

                    string[] parts = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    string keyword = parts[0].ToLowerInvariant();
                    if (depth == 1 && keyword == "cull" && parts.Length > 1 && parts[1].ToLowerInvariant() is "disable" or "none" or "twosided")
                        twoSided = true;

                    if (depth != 2 || stageTaken)
                        continue;

                    if (keyword is "map" or "clampmap" && parts.Length > 1 && !parts[1].StartsWith('$'))
                        image = parts[1];
                    else if (keyword == "animmap" && parts.Length > 2)
                        image = parts[2];
                    else if (keyword == "blendfunc" && parts.Length > 1)
                        additive = parts[1].ToLowerInvariant() is "add" or "gl_one" && (parts.Length < 3 || parts[2].ToLowerInvariant() == "gl_one");
                    else if (keyword == "alphafunc")
                        alphaTest = true;
                }

                shaders.TryAdd(shaderName, new Q3Shader(image, additive, alphaTest, twoSided));
                i = j;
            }
        }

        return shaders;
    }

    private static string Sanitize(string name)
    {
        string sanitized = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray()).Trim('_');
        return sanitized.Length > 0 ? sanitized : "texture";
    }
}
