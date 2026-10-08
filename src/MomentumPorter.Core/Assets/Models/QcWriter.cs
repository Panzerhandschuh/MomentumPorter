namespace MomentumPorter.Assets.Models;

using System.Globalization;
using System.Text;

/// <summary>An animation event, e.g. a sound (5004) at a frame.</summary>
public sealed record QcEvent(int Event, int Frame, string Options);

/// <param name="Activity">e.g. ACT_VM_PRIMARYATTACK, or null</param>
public sealed record QcSequence(string Name, string Smd, float Fps, bool Loop, string? Activity = null, IReadOnlyList<QcEvent>? Events = null);

/// <summary>
/// Builds a QC for studiomdl.
/// </summary>
public sealed class QcWriter
{
    private readonly StringBuilder _qc = new();

    /// <param name="modelName">Path under models/, e.g. <c>porter/cs16/v_ak47.mdl</c></param>
    /// <param name="materialFolder">Folder under materials/ the model's materials are in</param>
    public QcWriter(string modelName, string materialFolder)
    {
        Line($"$modelname \"{modelName}\"");
        Line($"$cdmaterials \"{materialFolder.TrimEnd('/')}/\"");
    }

    /// <summary>
    /// Moves and turns the model: <c>$origin</c> x y z, then rotation about z in degrees.
    /// </summary>
    public QcWriter Origin(float x, float y, float z, float rotation)
    {
        return Line(string.Create(CultureInfo.InvariantCulture, $"$origin {x:0.###} {y:0.###} {z:0.###} {rotation:0.###}"));
    }

    public QcWriter SurfaceProp(string surfaceProp) => Line($"$surfaceprop \"{surfaceProp}\"");

    public QcWriter Body(string name, string smd) => Line($"$body \"{name}\" \"{smd}\"");

    /// <param name="smds">One entry per choice; null for a blank choice</param>
    public QcWriter BodyGroup(string name, IEnumerable<string?> smds)
    {
        Line($"$bodygroup \"{name}\"");
        Line("{");
        foreach (string? smd in smds)
            Line(smd is null ? "\tblank" : $"\tstudio \"{smd}\"");
        return Line("}");
    }

    /// <param name="families">Each skin family's materials, in the same order</param>
    public QcWriter TextureGroup(string name, IEnumerable<IEnumerable<string>> families)
    {
        Line($"$texturegroup \"{name}\"");
        Line("{");
        foreach (IEnumerable<string> family in families)
            Line("\t{ " + string.Join(' ', family.Select(x => $"\"{x}\"")) + " }");
        return Line("}");
    }

    public QcWriter Attachment(string name, string bone, float x, float y, float z)
    {
        return Line(string.Create(CultureInfo.InvariantCulture, $"$attachment \"{name}\" \"{bone}\" {x:0.###} {y:0.###} {z:0.###} rigid"));
    }

    public QcWriter Sequence(QcSequence sequence)
    {
        var line = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"$sequence \"{sequence.Name}\" \"{sequence.Smd}\" fps {sequence.Fps:0.###}"));
        if (sequence.Loop)
            line.Append(" loop");

        if (sequence.Activity is not null)
            line.Append(" activity ").Append(sequence.Activity).Append(" 1");

        if (sequence.Events is not { Count: > 0 })
            return Line(line.ToString());

        Line(line.Append(" {").ToString());
        foreach (QcEvent qcEvent in sequence.Events)
            Line(string.Create(CultureInfo.InvariantCulture, $"\t{{ event {qcEvent.Event} {qcEvent.Frame} \"{qcEvent.Options}\" }}"));
        return Line("}");
    }

    public QcWriter Line(string line)
    {
        _qc.Append(line).Append('\n');
        return this;
    }

    public override string ToString() => _qc.ToString();
}
