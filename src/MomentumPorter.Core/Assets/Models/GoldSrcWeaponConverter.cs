namespace MomentumPorter.Assets.Models;

using System.Globalization;
using System.Numerics;
using System.Text;
using BSPConvert.Lib.GoldSrc;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;

/// <summary>What kind of weapon a view model is for, which decides the activities its sequences get.</summary>
public enum ViewModelKind
{
    Gun,
    Melee,

    /// <summary>The df gauntlet, which only plays ACT_VM_SWINGHIT.</summary>
    Gauntlet,
    Grenade,
}

/// <param name="GamePath">The compiled model's game path</param>
/// <param name="Files">Model and material files: game path and file on disk</param>
/// <param name="Sounds">The sounds its animation events play: the source game's path and the pack's (both sound/...)</param>
public sealed record ConvertedModel(string GamePath, IReadOnlyList<(string GamePath, string File)> Files, IReadOnlyList<(string Source, string Pack)> Sounds);

/// <summary>A GoldSrc model's attachment point (mstudioattachment_t): its origin is relative to its bone.</summary>
public sealed record GoldSrcAttachment(string Name, int Bone, Vector3 Origin);

/// <summary>
/// Converts GoldSrc weapon models (v_, w_, p_ .mdl) to Source models with BSPConvert's <see cref="GoldSrcModel"/>
/// reader and material converter. Unlike BSPConvert's map model compiler, view models keep their sequences with
/// the activities Momentum's weapons play (ACT_VM_IDLE, ACT_VM_PRIMARYATTACK...), and their sound events, which
/// play the sounds copied into the pack.
/// </summary>
public sealed class GoldSrcWeaponConverter
{
    // Event that plays a sound on the client (CL_EVENT_SOUND), in GoldSrc and Source
    private const int SoundEvent = 5004;

    // Events that draw a muzzle flash at attachment 0-3 (CL_EVENT_MUZZLEFLASH0-3), the same numbers in GoldSrc and Source
    private const int MuzzleFlashEvent = 5001;
    private const int MuzzleFlashEventStep = 10;

    private readonly GameContent _content;
    private readonly StudioMdlCompiler _compiler;
    private readonly string _gameId;
    private readonly string _extractDirectory;
    private readonly GoldSrcMaterialConverter _materials;

    /// <param name="gameId">Folder name the converted files go under (models/porter/&lt;gameId&gt;/...)</param>
    public GoldSrcWeaponConverter(GameContent content, StudioMdlCompiler compiler, string gameId, string workDirectory)
    {
        _content = content;
        _compiler = compiler;
        _gameId = gameId;
        _extractDirectory = Path.Combine(workDirectory, "goldsrc", gameId);
        _materials = new GoldSrcMaterialConverter(Path.Combine(compiler.GameDirectory, "materials"));
        Directory.CreateDirectory(_extractDirectory);
    }

    /// <summary>The game path a converted model gets: <c>models/v_ak47.mdl</c> -> <c>models/porter/cs16/v_ak47.mdl</c>.</summary>
    public string GetGamePath(string modelPath) => "models/" + GetModelName(modelPath);

    /// <summary>Where a GoldSrc sound path (under sound/) goes in the pack.</summary>
    public string GetSoundPath(string soundPath) => $"sound/porter/{_gameId}/{ContentPath.Normalize(soundPath)["sound/".Length..].ToLowerInvariant()}";

    /// <summary>
    /// The soundscript entry a model's animation events play a pack sound through (the pack defines it), e.g.
    /// <c>Porter_cs16.usp_clipin</c>. GoldSrc games share waves between weapons, so it's named after the wave.
    /// </summary>
    public static string SoundEntryName(string gameId, string packSoundPath) => $"Porter_{gameId}.{Path.GetFileNameWithoutExtension(packSoundPath).ToLowerInvariant()}";

    /// <param name="modelPath">Game path of the GoldSrc model, e.g. <c>models/v_ak47.mdl</c></param>
    /// <param name="kind">For view models, what activities its sequences get; null for world models</param>
    /// <param name="muzzleFlashes">
    /// Keep the model's muzzle flash events, which draw the weapon script's MuzzleFlashEffect at the attachment GoldSrc
    /// drew its own flash at; false drops them.
    /// </param>
    public async Task<ConvertedModel> ConvertAsync(string modelPath, ViewModelKind? kind, IPortLog log, CancellationToken cancellation, bool muzzleFlashes = false)
    {
        string file = Extract(modelPath) ?? throw new PortStepException($"{modelPath} isn't in {_content.Name}'s files");
        GoldSrcModel model = GoldSrcModel.Read(file, FindCompanion, out string? error)
            ?? throw new PortStepException($"Couldn't read {modelPath}: {error}");
        byte[] data = File.ReadAllBytes(file);

        string modelName = GetModelName(modelPath);
        string materialFolder = "models/" + Path.ChangeExtension(modelName, null);
        string sourceFolder = _compiler.CreateSourceFolder(modelName);

        string[] materialNames = MakeUnique(model.Textures.Select(x => Sanitize(Path.GetFileNameWithoutExtension(x.Name).Replace('.', '_'), "texture")));
        var files = new List<(string GamePath, string File)>();
        for (int i = 0; i < model.Textures.Count; i++)
        {
            string material = materialFolder + "/" + materialNames[i];
            if (!_materials.ConvertModelTexture(material, model.Textures[i]))
                throw new PortStepException($"Couldn't convert texture {model.Textures[i].Name} of {modelPath}");

            foreach (string extension in new[] { ".vtf", ".vmt" })
                files.Add(($"materials/{material}{extension}", Path.Combine(_compiler.GameDirectory, "materials", (material + extension).Replace('/', Path.DirectorySeparatorChar))));
        }

        var bones = MakeUnique(model.Bones.Select(x => Sanitize(x.Name, "bone"))).Select((name, i) => new SmdBone(name, model.Bones[i].Parent)).ToList();
        var referencePose = model.Bones.Select(x => new SmdBonePose(x.Position, x.Rotation)).ToList();

        // GoldSrc's studiomdl turned models 90 degrees about z, which Source's would do again
        var qc = new QcWriter(modelName, materialFolder).Origin(0, 0, 0, -90).SurfaceProp("metal");
        bool hasTriangles = false;
        for (int part = 0; part < model.BodyParts.Count; part++)
        {
            var choices = new List<string?>();
            for (int sub = 0; sub < model.BodyParts[part].Models.Count; sub++)
            {
                GoldSrcModel.SubModel subModel = model.BodyParts[part].Models[sub];
                if (subModel.Triangles.Count == 0)
                {
                    choices.Add(null);
                    continue;
                }

                string smd = $"body{part}_{sub}.smd";
                IEnumerable<SmdTriangle> triangles = subModel.Triangles.Select(x =>
                {
                    bool chrome = IsChrome(model, x.SkinRef);
                    return new SmdTriangle(MaterialFor(model, x.SkinRef, materialNames), ToSmd(x.A, chrome), ToSmd(x.B, chrome), ToSmd(x.C, chrome));
                });
                await File.WriteAllTextAsync(Path.Combine(sourceFolder, smd), SmdWriter.WriteReference(bones, referencePose, triangles), cancellation).ConfigureAwait(false);
                choices.Add(smd);
                hasTriangles = true;
            }

            qc.BodyGroup(Sanitize(model.BodyParts[part].Name, "body"), choices);
        }

        if (!hasTriangles)
            throw new PortStepException($"{modelPath} has no triangles");

        WriteTextureGroup(qc, model, materialNames);

        // View models' attachments, in GoldSrc's order: Source numbers them from 1 the same way, which is how a
        // muzzle flash event finds its attachment
        IReadOnlyList<GoldSrcAttachment> attachments = kind is null ? [] : ReadAttachments(data);
        for (int i = 0; i < attachments.Count; i++)
        {
            GoldSrcAttachment attachment = attachments[i];
            string bone = bones[Math.Clamp(attachment.Bone, 0, bones.Count - 1)].Name;
            qc.Attachment(i == 0 ? "muzzle" : $"attachment{i}", bone, attachment.Origin.X, attachment.Origin.Y, attachment.Origin.Z);
        }

        IReadOnlyList<List<(int Frame, int Event, string Options)>> events = ReadEvents(data, model.Sequences.Count);
        string[] sequenceNames = MakeUnique(model.Sequences.Select(x => Sanitize(x.Name, "sequence")));
        IReadOnlyList<string?> activities = kind is { } viewKind ? AssignActivities(sequenceNames, viewKind) : new string?[sequenceNames.Length];
        var sounds = new List<(string Source, string Pack)>();
        for (int i = 0; i < model.Sequences.Count; i++)
        {
            GoldSrcModel.Sequence sequence = model.Sequences[i];
            string smd = $"sequence{i}.smd";
            var frames = Enumerable.Range(0, sequence.FrameCount)
                .Select(f => (IReadOnlyList<SmdBonePose>)sequence.Positions[f].Select((p, b) => new SmdBonePose(p, sequence.Rotations[f][b])).ToList())
                .ToList();
            await File.WriteAllTextAsync(Path.Combine(sourceFolder, smd), SmdWriter.WriteAnimation(bones, frames), cancellation).ConfigureAwait(false);

            var qcEvents = new List<QcEvent>();
            foreach ((int frame, int eventId, string options) in events[i])
            {
                int clampedFrame = Math.Min(frame, Math.Max(sequence.FrameCount - 1, 0));
                if (IsMuzzleFlashEvent(eventId, out int attachment))
                {
                    // No options: the engine finds the attachment from the event, the same one in Source
                    if (muzzleFlashes && attachment < attachments.Count)
                        qcEvents.Add(new QcEvent(eventId, clampedFrame, ""));

                    continue;
                }

                if (eventId != SoundEvent || options.Length == 0)
                    continue;

                string source = "sound/" + ContentPath.Normalize(options);
                string pack = GetSoundPath(source);
                sounds.Add((source, pack));
                qcEvents.Add(new QcEvent(SoundEvent, clampedFrame, SoundEntryName(_gameId, pack)));
            }

            qc.Sequence(new QcSequence(sequenceNames[i], smd, sequence.Fps > 0 ? sequence.Fps : 30, sequence.Looping, activities[i], qcEvents));
        }

        string qcPath = Path.Combine(sourceFolder, "model.qc");
        await File.WriteAllTextAsync(qcPath, qc.ToString(), cancellation).ConfigureAwait(false);
        CompiledModel compiled = await _compiler.CompileAsync(qcPath, modelName, cancellation).ConfigureAwait(false);
        files.AddRange(compiled.Files);
        log.Info($"Converted {modelPath} to {compiled.GamePath}");

        return new ConvertedModel(compiled.GamePath, files, sounds.Distinct().ToList());
    }

    // GoldSrc ignored a chrome texture's stored coordinates and mapped it as a sphere map from each vertex's normal
    // against the view every frame (R_StudioChrome: (n . right + 1) / 2, (n . up + 1) / 2). Source has no such mode,
    // so the map is fixed against the model's own right (-y) and up (+z): the look of the chrome, without the shimmer.
    private static SmdVertex ToSmd(GoldSrcModel.Vertex vertex, bool chrome)
    {
        Vector2 texCoord = chrome
            ? new Vector2((1f - vertex.Normal.Y) * 0.5f, (1f - vertex.Normal.Z) * 0.5f)
            : vertex.TexCoord;
        return new SmdVertex(vertex.Bone, vertex.Position, vertex.Normal, texCoord);
    }

    private const int StudioChromeFlag = 0x0002;

    private static bool IsChrome(GoldSrcModel model, int skinRef)
    {
        int texture = model.SkinFamilies.Length > 0 && skinRef >= 0 && skinRef < model.SkinFamilies[0].Length ? model.SkinFamilies[0][skinRef] : skinRef;
        return texture >= 0 && texture < model.Textures.Count && (model.Textures[texture].Flags & StudioChromeFlag) != 0;
    }

    private string GetModelName(string modelPath)
    {
        string name = ContentPath.Normalize(modelPath).ToLowerInvariant();
        if (name.StartsWith("models/", StringComparison.Ordinal))
            name = name["models/".Length..];

        string[] parts = Path.ChangeExtension(name, null).Split('/', StringSplitOptions.RemoveEmptyEntries).Select(x => Sanitize(x, "model")).ToArray();
        return $"porter/{_gameId}/{string.Join('/', parts)}.mdl";
    }

    // The model and its texture (<name>T.mdl) and sequence group (<name>01.mdl) files, extracted side by side
    private string? Extract(string modelPath)
    {
        string destination = Path.Combine(_extractDirectory, Path.GetFileName(modelPath));
        if (!_content.Extract(modelPath, destination))
            return null;

        string directory = Path.GetDirectoryName(ContentPath.Normalize(modelPath))!.Replace('\\', '/');
        string baseName = Path.GetFileNameWithoutExtension(modelPath);
        foreach (string companion in new[] { baseName + "T.mdl", baseName + "t.mdl" }.Concat(Enumerable.Range(1, 9).Select(i => $"{baseName}{i:00}.mdl")))
            _content.Extract($"{directory}/{companion}", Path.Combine(_extractDirectory, companion));

        return destination;
    }

    private string? FindCompanion(string fileName)
    {
        string path = Path.Combine(_extractDirectory, fileName);
        if (File.Exists(path))
            return path;

        return Directory.EnumerateFiles(_extractDirectory).FirstOrDefault(x => string.Equals(Path.GetFileName(x), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string MaterialFor(GoldSrcModel model, int skinRef, string[] materialNames)
    {
        int texture = model.SkinFamilies.Length > 0 && skinRef >= 0 && skinRef < model.SkinFamilies[0].Length ? model.SkinFamilies[0][skinRef] : skinRef;
        return texture >= 0 && texture < materialNames.Length ? materialNames[texture] : "missing";
    }

    private static void WriteTextureGroup(QcWriter qc, GoldSrcModel model, string[] materialNames)
    {
        short[][] families = model.SkinFamilies;
        if (families.Length < 2)
            return;

        var skinRefs = Enumerable.Range(0, families[0].Length).Where(skinRef => families.Any(x => x[skinRef] != families[0][skinRef])).ToList();
        if (skinRefs.Count == 0)
            return;

        qc.TextureGroup("skinfamilies", families.Select(family => skinRefs.Select(skinRef =>
            family[skinRef] >= 0 && family[skinRef] < materialNames.Length ? materialNames[family[skinRef]] : materialNames[families[0][skinRef]])));
    }

    private static bool IsMuzzleFlashEvent(int eventId, out int attachment)
    {
        attachment = (eventId - MuzzleFlashEvent) / MuzzleFlashEventStep;
        return eventId >= MuzzleFlashEvent && eventId <= MuzzleFlashEvent + 3 * MuzzleFlashEventStep && (eventId - MuzzleFlashEvent) % MuzzleFlashEventStep == 0;
    }

    /// <summary>
    /// The model's attachments (studiohdr_t: numattachments at 212, attachmentindex at 216; mstudioattachment_t:
    /// name[32], type, bone, org, vectors[3], 88 bytes).
    /// </summary>
    public static IReadOnlyList<GoldSrcAttachment> ReadAttachments(byte[] data)
    {
        var result = new List<GoldSrcAttachment>();
        if (data.Length < 220)
            return result;

        int count = BitConverter.ToInt32(data, 212);
        int index = BitConverter.ToInt32(data, 216);
        for (int i = 0; i < count && index >= 0 && index + (i + 1) * 88 <= data.Length; i++)
        {
            int offset = index + i * 88;
            int end = Array.IndexOf(data, (byte)0, offset, 32);
            string name = Encoding.ASCII.GetString(data, offset, (end >= 0 ? end : offset + 32) - offset).Trim();
            int bone = BitConverter.ToInt32(data, offset + 36);
            var origin = new Vector3(BitConverter.ToSingle(data, offset + 40), BitConverter.ToSingle(data, offset + 44), BitConverter.ToSingle(data, offset + 48));
            result.Add(new GoldSrcAttachment(name, bone, origin));
        }

        return result;
    }

    /// <summary>
    /// Each sequence's events (mstudioseqdesc_t: numevents at 48, eventindex at 52; mstudioevent_t: frame, event,
    /// type, options[64]).
    /// </summary>
    public static IReadOnlyList<List<(int Frame, int Event, string Options)>> ReadEvents(byte[] data, int sequenceCount)
    {
        int sequenceIndex = BitConverter.ToInt32(data, 168);
        var result = new List<List<(int, int, string)>>();
        for (int i = 0; i < sequenceCount; i++)
        {
            var events = new List<(int, int, string)>();
            int offset = sequenceIndex + i * 176;
            int count = BitConverter.ToInt32(data, offset + 48);
            int eventIndex = BitConverter.ToInt32(data, offset + 52);
            for (int e = 0; e < count && eventIndex + (e + 1) * 76 <= data.Length; e++)
            {
                int eventOffset = eventIndex + e * 76;
                int end = Array.IndexOf(data, (byte)0, eventOffset + 12, 64);
                string options = Encoding.ASCII.GetString(data, eventOffset + 12, (end >= 0 ? end : eventOffset + 76) - (eventOffset + 12));
                events.Add((BitConverter.ToInt32(data, eventOffset), BitConverter.ToInt32(data, eventOffset + 4), options.Trim()));
            }

            result.Add(events);
        }

        return result;
    }

    /// <summary>
    /// The activity each sequence gets, from its name: GoldSrc weapons pick sequences by number in code, Source ones
    /// by activity. Silenced/unsilenced pairs (CS 1.6 M4A1 and USP) keep the unsilenced ones, which is how they're drawn.
    /// </summary>
    public static IReadOnlyList<string?> AssignActivities(IReadOnlyList<string> names, ViewModelKind kind)
    {
        var lower = names.Select(x => x.ToLowerInvariant()).ToList();
        var result = new string?[names.Count];
        int meleeAttacks = 0;
        for (int i = 0; i < lower.Count; i++)
        {
            string name = lower[i];
            bool hasUnsilenced = !name.Contains("unsil") && (lower.Contains(name + "_unsil") || lower.Contains("unsil_" + name));
            if (hasUnsilenced || name.Contains("silencer"))
                continue;

            string bare = name.Replace("unsil_", "").Replace("_unsil", "");
            result[i] = kind switch
            {
                _ when bare.StartsWith("idle") && !bare.Contains("empty") => "ACT_VM_IDLE",
                _ when bare.StartsWith("draw") || bare.StartsWith("deploy") => "ACT_VM_DRAW",
                _ when bare.StartsWith("holster") => "ACT_VM_HOLSTER",
                ViewModelKind.Grenade when bare.StartsWith("pull") => "ACT_VM_PULLPIN",
                ViewModelKind.Grenade when bare.StartsWith("throw") => "ACT_VM_THROW",
                ViewModelKind.Gauntlet when IsAttack(bare) => "ACT_VM_SWINGHIT",
                ViewModelKind.Melee when bare.StartsWith("stab") => bare.Contains("miss") ? "ACT_VM_SWINGMISS" : "ACT_VM_SWINGHIT",
                ViewModelKind.Melee when bare.Contains("miss") => "ACT_VM_MISSCENTER",
                ViewModelKind.Melee when IsAttack(bare) => meleeAttacks++ % 2 == 0 ? "ACT_VM_HITCENTER" : "ACT_VM_MISSCENTER",
                ViewModelKind.Gun when IsAttack(bare) && !bare.Contains("last") && !bare.Contains("empty") => "ACT_VM_PRIMARYATTACK",
                ViewModelKind.Gun when bare.StartsWith("reload") || bare == "insert" => "ACT_VM_RELOAD",
                _ => null,
            };
        }

        // Shotguns reload a shell at a time (start_reload, insert, after_reload); only use insert without a reload
        if (kind == ViewModelKind.Gun && lower.Any(x => x.StartsWith("reload")))
        {
            for (int i = 0; i < lower.Count; i++)
            {
                if (lower[i] == "insert")
                    result[i] = null;
            }
        }

        if (!result.Contains("ACT_VM_IDLE") && result.Length > 0)
            result[0] = "ACT_VM_IDLE";

        return result;
    }

    private static bool IsAttack(string name) =>
        name.StartsWith("shoot") || name.StartsWith("fire") || name.StartsWith("attack") || name.Contains("slash") || name.StartsWith("shot") || name.StartsWith("hit");

    private static string Sanitize(string name, string fallback)
    {
        string sanitized = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray()).Trim('_');
        return sanitized.Length > 0 ? sanitized : fallback;
    }

    private static string[] MakeUnique(IEnumerable<string> names)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return names.Select(name =>
        {
            string unique = name;
            for (int i = 1; !used.Add(unique); i++)
                unique = $"{name}_{i}";

            return unique;
        }).ToArray();
    }
}
