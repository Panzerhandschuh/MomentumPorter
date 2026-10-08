namespace PlayerModelSpike;

using System.Globalization;
using System.Numerics;
using System.Text;
using MomentumPorter.Assets.Models;

/// <summary>
/// Writes the QC for a body retargeted onto a target rig (fit mode): the target's animations come from its
/// $includemodel, and its attachments, $bonemerge bones and (optionally) its full bone list are copied from the compiled
/// target model, so the result can stand in for it.
/// </summary>
public static class QcWriter
{
    public sealed class Options
    {
        public string ModelName { get; set; } = "player/mom_player.mdl";
        public List<string> CdMaterials { get; set; } = [];
        public string SurfaceProp { get; set; } = "flesh";
        /// <summary>Copy every target bone with $definebone, so unweighted bones (fingers, weapon_bone) survive</summary>
        public bool DefineBones { get; set; } = true;
        /// <summary>
        /// False for a static model: no $includemodel, so the game finds none of the sequences it asks for and the
        /// body stays in the pose baked into its mesh (e.g. Momentum's idle, applied with the pose command)
        /// </summary>
        public bool IncludeAnimations { get; set; } = true;
    }

    public static string Write(StudioModelHeader target, string meshSmd, Options options)
    {
        var qc = new StringBuilder();
        void Line(string text) => qc.Append(text).Append('\n');

        Line($"$modelname \"{options.ModelName}\"");
        foreach (string dir in options.CdMaterials)
            Line($"$cdmaterials \"{dir}\"");
        // The SMDs are written in compiled model space; turn off studiomdl's default 90 degree turn
        Line("$origin 0 0 0 -90");
        Line($"$surfaceprop \"{options.SurfaceProp}\"");
        foreach (string include in options.IncludeAnimations ? target.IncludeModels : [])
        {
            string path = include.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? include[7..] : include;
            Line($"$includemodel \"{path}\"");
        }

        Line("");
        Line($"$model \"body\" \"{meshSmd}\"");
        Line("");

        if (options.DefineBones)
        {
            foreach (var bone in target.Bones)
            {
                string parent = bone.Parent >= 0 ? target.Bones[bone.Parent].Name : "";
                var deg = bone.Rotation * (180f / MathF.PI);
                // $definebone takes QAngle-ordered degrees (pitch = about y, yaw = about z, roll = about x)
                Line(Invariant($"$definebone \"{bone.Name}\" \"{parent}\" {bone.Position.X:0.######} {bone.Position.Y:0.######} {bone.Position.Z:0.######} {deg.Y:0.######} {deg.Z:0.######} {deg.X:0.######} 0 0 0 0 0 0"));
            }

            Line("");
        }

        const int BoneUsedByBoneMerge = 0x40000;
        foreach (var bone in target.Bones.Where(b => (b.Flags & BoneUsedByBoneMerge) != 0 && b.Parent >= 0 && target.Bones.All(o => o.Parent != b.Index)))
            Line($"$bonemerge \"{bone.Name}\"");

        foreach (var attachment in target.Attachments)
        {
            var m = attachment.Local;
            // Rows of a 3x4: columns 0-2 are the attachment's forward, left and up axes in bone space
            float pitch = MathF.Atan2(-m[8], MathF.Sqrt(m[0] * m[0] + m[4] * m[4])) * 180f / MathF.PI;
            float yaw = MathF.Atan2(m[4], m[0]) * 180f / MathF.PI;
            float roll = MathF.Atan2(m[9], m[10]) * 180f / MathF.PI;
            Line(Invariant($"$attachment \"{attachment.Name}\" \"{target.Bones[attachment.Bone].Name}\" {m[3]:0.####} {m[7]:0.####} {m[11]:0.####} rigid rotate {pitch:0.##} {yaw:0.##} {roll:0.##}"));
        }

        Line("");
        Line($"$sequence \"ref\" \"{meshSmd}\" fps 30");
        return qc.ToString();
    }

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
