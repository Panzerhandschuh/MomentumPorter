namespace MomentumPorter.Assets.Particles;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// One operator, initializer, emitter or renderer of a particle system: its function name (e.g.
/// <c>"Radius Scale"</c>) and the attributes that differ from the engine's defaults.
/// </summary>
public sealed class ParticleOperator(string function)
{
    private readonly List<(string Name, string Type, string Value)> _attributes = [];

    public string Function { get; } = function;

    public IReadOnlyList<(string Name, string Type, string Value)> Attributes => _attributes;

    public ParticleOperator Set(string name, float value) => Add(name, "float", value.ToString("0.######", CultureInfo.InvariantCulture));

    public ParticleOperator Set(string name, int value) => Add(name, "int", value.ToString(CultureInfo.InvariantCulture));

    public ParticleOperator Set(string name, bool value) => Add(name, "bool", value ? "1" : "0");

    public ParticleOperator SetVector(string name, float x, float y, float z) =>
        Add(name, "vector3", string.Create(CultureInfo.InvariantCulture, $"{x:0.######} {y:0.######} {z:0.######}"));

    public ParticleOperator SetColor(string name, int r, int g, int b, int a) => Add(name, "color", $"{r} {g} {b} {a}");

    private ParticleOperator Add(string name, string type, string value)
    {
        _attributes.RemoveAll(x => x.Name == name);
        _attributes.Add((name, type, value));
        return this;
    }
}

/// <summary>A particle system definition, as the particle editor saves it in a .pcf.</summary>
public sealed class ParticleSystem(string name)
{
    public string Name { get; } = name;

    /// <summary>The material, relative to materials/ (e.g. <c>porter/q3/effects/rlboom_1.vmt</c>); null draws nothing.</summary>
    public string? Material { get; set; }

    public int MaxParticles { get; set; } = 16;

    public List<ParticleOperator> Renderers { get; } = [];

    public List<ParticleOperator> Operators { get; } = [];

    public List<ParticleOperator> Initializers { get; } = [];

    public List<ParticleOperator> Emitters { get; } = [];

    /// <summary>Systems started with this one, at the same control points.</summary>
    public List<ParticleSystem> Children { get; } = [];
}

/// <summary>
/// Writes particle systems as a keyvalues2 DMX (.pcf) text file. The game only reads binary DMX, so the text is
/// converted with Momentum's dmxconvert (<see cref="DmxConvert"/>).
/// </summary>
public static class ParticleSystemFile
{
    public static string Write(IEnumerable<ParticleSystem> systems)
    {
        // Every system, children included, is a top-level definition; children refer to theirs by id
        var all = new List<ParticleSystem>();
        void Collect(ParticleSystem system)
        {
            if (all.Contains(system))
                return;

            all.Add(system);
            foreach (ParticleSystem child in system.Children)
                Collect(child);
        }

        foreach (ParticleSystem system in systems)
            Collect(system);

        var text = new StringBuilder();
        text.Append("<!-- dmx encoding keyvalues2 1 format pcf 2 -->\n");
        text.Append("\"DmElement\"\n{\n");
        text.Append($"\t\"id\" \"elementid\" \"{Id("root")}\"\n");
        text.Append("\t\"name\" \"string\" \"untitled\"\n");
        text.Append("\t\"editorType\" \"string\" \"particleSystemDefinitionList\"\n");
        text.Append("\t\"particleSystemDefinitions\" \"element_array\"\n\t[\n");
        for (int i = 0; i < all.Count; i++)
        {
            WriteSystem(text, all[i]);
            text.Append(i + 1 < all.Count ? ",\n" : "\n");
        }

        text.Append("\t]\n}\n");
        return text.ToString();
    }

    private static void WriteSystem(StringBuilder text, ParticleSystem system)
    {
        const string indent = "\t\t";
        text.Append($"{indent}\"DmeParticleSystemDefinition\"\n{indent}{{\n");
        text.Append($"{indent}\t\"id\" \"elementid\" \"{Id(system.Name)}\"\n");
        text.Append($"{indent}\t\"name\" \"string\" \"{system.Name}\"\n");
        WriteOperators(text, system, "renderers", system.Renderers);
        WriteOperators(text, system, "operators", system.Operators);
        WriteOperators(text, system, "initializers", system.Initializers);
        WriteOperators(text, system, "emitters", system.Emitters);

        text.Append($"{indent}\t\"children\" \"element_array\"\n{indent}\t[\n");
        for (int i = 0; i < system.Children.Count; i++)
        {
            ParticleSystem child = system.Children[i];
            text.Append($"{indent}\t\t\"DmeParticleChild\"\n{indent}\t\t{{\n");
            text.Append($"{indent}\t\t\t\"id\" \"elementid\" \"{Id($"{system.Name}/child/{i}")}\"\n");
            text.Append($"{indent}\t\t\t\"name\" \"string\" \"{child.Name}\"\n");
            text.Append($"{indent}\t\t\t\"child\" \"element\" \"{Id(child.Name)}\"\n");
            text.Append($"{indent}\t\t}}{(i + 1 < system.Children.Count ? "," : "")}\n");
        }

        text.Append($"{indent}\t]\n");
        text.Append($"{indent}\t\"forces\" \"element_array\"\n{indent}\t[\n{indent}\t]\n");
        text.Append($"{indent}\t\"constraints\" \"element_array\"\n{indent}\t[\n{indent}\t]\n");
        text.Append($"{indent}\t\"preventNameBasedLookup\" \"bool\" \"0\"\n");
        text.Append($"{indent}\t\"max_particles\" \"int\" \"{system.MaxParticles}\"\n");
        if (system.Material is not null)
        {
            // Backslashed like the particle editor saves it, escaped for keyvalues2
            string material = system.Material.Replace('/', '\\').Replace("\\", "\\\\");
            text.Append($"{indent}\t\"material\" \"string\" \"{material}\"\n");
        }

        text.Append($"{indent}}}");
    }

    private static void WriteOperators(StringBuilder text, ParticleSystem system, string list, List<ParticleOperator> operators)
    {
        const string indent = "\t\t\t";
        text.Append($"{indent}\"{list}\" \"element_array\"\n{indent}[\n");
        for (int i = 0; i < operators.Count; i++)
        {
            ParticleOperator op = operators[i];
            text.Append($"{indent}\t\"DmeParticleOperator\"\n{indent}\t{{\n");
            text.Append($"{indent}\t\t\"id\" \"elementid\" \"{Id($"{system.Name}/{list}/{i}")}\"\n");
            text.Append($"{indent}\t\t\"name\" \"string\" \"{op.Function}\"\n");
            text.Append($"{indent}\t\t\"functionName\" \"string\" \"{op.Function}\"\n");
            foreach ((string name, string type, string value) in op.Attributes)
                text.Append($"{indent}\t\t\"{name}\" \"{type}\" \"{value}\"\n");

            text.Append($"{indent}\t}}{(i + 1 < operators.Count ? "," : "")}\n");
        }

        text.Append($"{indent}]\n");
    }

    // Stable ids, so rebuilding a pack writes the same file
    private static string Id(string key)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("MomentumPorter/" + key));
        return new Guid(hash).ToString();
    }
}
