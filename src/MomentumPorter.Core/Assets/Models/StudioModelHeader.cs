namespace MomentumPorter.Assets.Models;

using System.Numerics;
using System.Text;

/// <summary>
/// A bone of a Source studio model in its rest pose, relative to its parent.
/// </summary>
/// <param name="Rotation">The rest rotation as Euler angles in radians (x, y, z), as SMD skeletons write it</param>
/// <param name="PoseToBone">Model space to bone space in the rest pose (3x4, row major)</param>
public sealed record StudioBone(
    int Index,
    string Name,
    int Parent,
    Vector3 Position,
    Quaternion Quaternion,
    Vector3 Rotation,
    float[] PoseToBone,
    int Flags,
    int ProceduralType,
    string SurfaceProp);

public sealed record StudioHitbox(string Name, int Bone, int Group, Vector3 Min, Vector3 Max);

public sealed record StudioHitboxSet(string Name, IReadOnlyList<StudioHitbox> Hitboxes);

/// <param name="Local">The attachment's transform relative to its bone (3x4, row major)</param>
public sealed record StudioAttachment(string Name, int Flags, int Bone, float[] Local);

/// <param name="VertexOffset">First vertex of the mesh, relative to its model's first vertex</param>
public sealed record StudioMesh(int Material, int VertexCount, int VertexOffset);

/// <param name="VertexIndex">Byte offset of the model's first vertex in the .vvd vertex data</param>
public sealed record StudioSubModel(string Name, int VertexCount, int VertexIndex, IReadOnlyList<StudioMesh> Meshes);

public sealed record StudioBodyPart(string Name, int Base, IReadOnlyList<StudioSubModel> Models);

public sealed record StudioSequence(string Label, string Activity, int Flags, int BlendCount);

public sealed record StudioPoseParameter(string Name, float Start, float End, float Loop);

/// <summary>
/// The parts of a Source studio model (.mdl, versions 44-49) needed to inspect it or rebuild its reference mesh:
/// skeleton, hitboxes, attachments, body groups, materials, sequences and included models. Animation data isn't read.
/// </summary>
public sealed class StudioModelHeader
{
    public const int StudioHeaderId = 0x54534449; // "IDST"

    private const int BoneSize = 216;
    private const int HitboxSetSize = 12;
    private const int HitboxSize = 68;
    private const int AttachmentSize = 92;
    private const int BodyPartSize = 16;
    private const int ModelSize = 148;
    private const int MeshSize = 116;
    private const int TextureSize = 64;
    private const int SequenceSize = 212;
    private const int ModelGroupSize = 8;
    private const int PoseParameterSize = 20;

    public int Version { get; private init; }
    public int Checksum { get; private init; }
    public string Name { get; private init; } = "";
    public Vector3 EyePosition { get; private init; }
    public Vector3 HullMin { get; private init; }
    public Vector3 HullMax { get; private init; }
    public Vector3 ViewMin { get; private init; }
    public Vector3 ViewMax { get; private init; }
    public int Flags { get; private init; }
    public string SurfaceProp { get; private init; } = "";
    public string KeyValues { get; private init; } = "";
    public float Mass { get; private init; }

    public IReadOnlyList<StudioBone> Bones { get; private init; } = [];
    public IReadOnlyList<StudioHitboxSet> HitboxSets { get; private init; } = [];
    public IReadOnlyList<StudioAttachment> Attachments { get; private init; } = [];
    public IReadOnlyList<StudioBodyPart> BodyParts { get; private init; } = [];
    /// <summary>Material names, without the $cdmaterials folder</summary>
    public IReadOnlyList<string> Textures { get; private init; } = [];
    /// <summary>The $cdmaterials folders</summary>
    public IReadOnlyList<string> TextureDirectories { get; private init; } = [];
    /// <summary>Skin families: per skin, the texture index of each skin reference</summary>
    public IReadOnlyList<short[]> SkinFamilies { get; private init; } = [];
    public IReadOnlyList<StudioSequence> Sequences { get; private init; } = [];
    public int LocalAnimationCount { get; private init; }
    public IReadOnlyList<StudioPoseParameter> PoseParameters { get; private init; } = [];
    /// <summary>$includemodel paths, relative to models/</summary>
    public IReadOnlyList<string> IncludeModels { get; private init; } = [];

    public static StudioModelHeader Read(string path)
    {
        return Read(File.ReadAllBytes(path));
    }

    public static StudioModelHeader Read(byte[] data)
    {
        var r = new Reader(data);
        if (r.Int(0) != StudioHeaderId)
            throw new InvalidDataException("Not a Source studio model (no IDST header)");

        int version = r.Int(4);
        if (version < 44 || version > 49)
            throw new InvalidDataException($"Unsupported studio model version {version} (44-49 are supported)");

        var bones = new List<StudioBone>();
        int boneCount = r.Int(156), boneIndex = r.Int(160);
        for (int i = 0; i < boneCount; i++)
        {
            int b = boneIndex + i * BoneSize;
            var poseToBone = new float[12];
            for (int k = 0; k < 12; k++)
                poseToBone[k] = r.Float(b + 96 + k * 4);

            bones.Add(new StudioBone(
                i,
                r.String(b + r.Int(b)),
                r.Int(b + 4),
                r.Vector(b + 32),
                new Quaternion(r.Float(b + 44), r.Float(b + 48), r.Float(b + 52), r.Float(b + 56)),
                r.Vector(b + 60),
                poseToBone,
                r.Int(b + 160),
                r.Int(b + 164),
                r.RelativeString(b, r.Int(b + 176))));
        }

        var hitboxSets = new List<StudioHitboxSet>();
        int setCount = r.Int(172), setIndex = r.Int(176);
        for (int i = 0; i < setCount; i++)
        {
            int s = setIndex + i * HitboxSetSize;
            var hitboxes = new List<StudioHitbox>();
            int count = r.Int(s + 4), first = s + r.Int(s + 8);
            for (int k = 0; k < count; k++)
            {
                int h = first + k * HitboxSize;
                hitboxes.Add(new StudioHitbox(r.RelativeString(h, r.Int(h + 32)), r.Int(h), r.Int(h + 4), r.Vector(h + 8), r.Vector(h + 20)));
            }

            hitboxSets.Add(new StudioHitboxSet(r.RelativeString(s, r.Int(s)), hitboxes));
        }

        var attachments = new List<StudioAttachment>();
        int attachmentCount = r.Int(240), attachmentIndex = r.Int(244);
        for (int i = 0; i < attachmentCount; i++)
        {
            int a = attachmentIndex + i * AttachmentSize;
            var local = new float[12];
            for (int k = 0; k < 12; k++)
                local[k] = r.Float(a + 12 + k * 4);

            attachments.Add(new StudioAttachment(r.RelativeString(a, r.Int(a)), r.Int(a + 4), r.Int(a + 8), local));
        }

        var bodyParts = new List<StudioBodyPart>();
        int bodyPartCount = r.Int(232), bodyPartIndex = r.Int(236);
        for (int i = 0; i < bodyPartCount; i++)
        {
            int p = bodyPartIndex + i * BodyPartSize;
            var models = new List<StudioSubModel>();
            int modelCount = r.Int(p + 4), firstModel = p + r.Int(p + 12);
            for (int k = 0; k < modelCount; k++)
            {
                int m = firstModel + k * ModelSize;
                var meshes = new List<StudioMesh>();
                int meshCount = r.Int(m + 72), firstMesh = m + r.Int(m + 76);
                for (int n = 0; n < meshCount; n++)
                {
                    int e = firstMesh + n * MeshSize;
                    meshes.Add(new StudioMesh(r.Int(e), r.Int(e + 8), r.Int(e + 12)));
                }

                models.Add(new StudioSubModel(r.FixedString(m, 64), r.Int(m + 80), r.Int(m + 84), meshes));
            }

            bodyParts.Add(new StudioBodyPart(r.RelativeString(p, r.Int(p)), r.Int(p + 8), models));
        }

        var textures = new List<string>();
        int textureCount = r.Int(204), textureIndex = r.Int(208);
        for (int i = 0; i < textureCount; i++)
        {
            int t = textureIndex + i * TextureSize;
            textures.Add(r.RelativeString(t, r.Int(t)));
        }

        var textureDirectories = new List<string>();
        int cdCount = r.Int(212), cdIndex = r.Int(216);
        for (int i = 0; i < cdCount; i++)
            textureDirectories.Add(r.String(r.Int(cdIndex + i * 4)));

        var skins = new List<short[]>();
        int skinRefCount = r.Int(220), skinFamilyCount = r.Int(224), skinIndex = r.Int(228);
        for (int i = 0; i < skinFamilyCount; i++)
        {
            var family = new short[skinRefCount];
            for (int k = 0; k < skinRefCount; k++)
                family[k] = r.Short(skinIndex + (i * skinRefCount + k) * 2);
            skins.Add(family);
        }

        var sequences = new List<StudioSequence>();
        int sequenceCount = r.Int(188), sequenceIndex = r.Int(192);
        for (int i = 0; i < sequenceCount; i++)
        {
            int q = sequenceIndex + i * SequenceSize;
            sequences.Add(new StudioSequence(r.RelativeString(q, r.Int(q + 4)), r.RelativeString(q, r.Int(q + 8)), r.Int(q + 12), r.Int(q + 56)));
        }

        var poseParameters = new List<StudioPoseParameter>();
        int poseCount = r.Int(300), poseIndex = r.Int(304);
        for (int i = 0; i < poseCount; i++)
        {
            int q = poseIndex + i * PoseParameterSize;
            poseParameters.Add(new StudioPoseParameter(r.RelativeString(q, r.Int(q)), r.Float(q + 8), r.Float(q + 12), r.Float(q + 16)));
        }

        var includes = new List<string>();
        int includeCount = r.Int(336), includeIndex = r.Int(340);
        for (int i = 0; i < includeCount; i++)
        {
            int g = includeIndex + i * ModelGroupSize;
            includes.Add(r.RelativeString(g, r.Int(g + 4)));
        }

        int keyValueIndex = r.Int(312), keyValueSize = r.Int(316);

        return new StudioModelHeader
        {
            Version = version,
            Checksum = r.Int(8),
            Name = r.FixedString(12, 64),
            EyePosition = r.Vector(80),
            HullMin = r.Vector(104),
            HullMax = r.Vector(116),
            ViewMin = r.Vector(128),
            ViewMax = r.Vector(140),
            Flags = r.Int(152),
            SurfaceProp = r.RelativeString(0, r.Int(308)),
            KeyValues = keyValueSize > 0 ? r.FixedString(keyValueIndex, keyValueSize) : "",
            Mass = r.Float(328),
            Bones = bones,
            HitboxSets = hitboxSets,
            Attachments = attachments,
            BodyParts = bodyParts,
            Textures = textures,
            TextureDirectories = textureDirectories,
            SkinFamilies = skins,
            Sequences = sequences,
            LocalAnimationCount = r.Int(180),
            PoseParameters = poseParameters,
            IncludeModels = includes,
        };
    }

    /// <summary>
    /// The bone's rest transform in model space.
    /// </summary>
    public Matrix4x4 GetBoneToModel(int bone)
    {
        var local = Matrix4x4.CreateFromQuaternion(Bones[bone].Quaternion);
        local.Translation = Bones[bone].Position;
        int parent = Bones[bone].Parent;
        return parent < 0 ? local : local * GetBoneToModel(parent);
    }

    public int FindBone(string name)
    {
        for (int i = 0; i < Bones.Count; i++)
        {
            if (string.Equals(Bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private readonly struct Reader(byte[] data)
    {
        public int Int(int offset) => InRange(offset, 4) ? BitConverter.ToInt32(data, offset) : 0;

        public short Short(int offset) => InRange(offset, 2) ? BitConverter.ToInt16(data, offset) : (short)0;

        public float Float(int offset) => InRange(offset, 4) ? BitConverter.ToSingle(data, offset) : 0f;

        public Vector3 Vector(int offset) => new(Float(offset), Float(offset + 4), Float(offset + 8));

        public string String(int offset)
        {
            if (offset <= 0 || offset >= data.Length)
                return "";

            int end = Array.IndexOf(data, (byte)0, offset);
            return Encoding.UTF8.GetString(data, offset, (end < 0 ? data.Length : end) - offset);
        }

        /// <summary>A string at an offset relative to a struct; 0 means no string</summary>
        public string RelativeString(int structOffset, int relativeOffset) => relativeOffset == 0 ? "" : String(structOffset + relativeOffset);

        public string FixedString(int offset, int length)
        {
            if (!InRange(offset, length))
                return "";

            int end = Array.IndexOf(data, (byte)0, offset, length);
            return Encoding.UTF8.GetString(data, offset, (end < 0 ? offset + length : end) - offset);
        }

        private bool InRange(int offset, int size) => offset >= 0 && offset + size <= data.Length;
    }
}
