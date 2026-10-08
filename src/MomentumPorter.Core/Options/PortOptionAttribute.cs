namespace MomentumPorter.Options;

/// <summary>
/// Describes an option property for UIs that build their forms from the option classes, and for CLI help.
/// Put it on public settable properties of bool, int, float, string, enum or string? type.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PortOptionAttribute(string label, string help) : Attribute
{
    public string Label { get; } = label;

    public string Help { get; } = help;

    /// <summary>Section the option is shown under, e.g. "General" or "Quake 3".</summary>
    public string Group { get; init; } = "General";

    /// <summary>Hidden behind an "Advanced" expander by default.</summary>
    public bool Advanced { get; init; }

    /// <summary>Order within the group; lower first.</summary>
    public int Order { get; init; } = 100;

    /// <summary>For numbers: inclusive bounds the UI should enforce.</summary>
    public double Min { get; init; } = double.NaN;

    public double Max { get; init; } = double.NaN;

    /// <summary>For strings: the kind of path, so the UI can show a browse button.</summary>
    public PathKind Path { get; init; } = PathKind.None;
}

public enum PathKind
{
    None,
    File,
    Folder,
    OutputFile,
}

/// <summary>
/// Reads <see cref="PortOptionAttribute"/> metadata off an options object.
/// </summary>
public static class PortOptions
{
    public static IReadOnlyList<PortOptionInfo> Describe(Type optionsType)
    {
        return optionsType
            .GetProperties()
            .Select(p => (Property: p, Attribute: p.GetCustomAttributes(typeof(PortOptionAttribute), inherit: true).OfType<PortOptionAttribute>().FirstOrDefault()))
            .Where(x => x.Attribute is not null && x.Property.CanWrite)
            .Select(x => new PortOptionInfo(x.Property, x.Attribute!))
            .OrderBy(x => x.Attribute.Group)
            .ThenBy(x => x.Attribute.Order)
            .ThenBy(x => x.Attribute.Label)
            .ToList();
    }

    public static IReadOnlyList<PortOptionInfo> Describe<T>() => Describe(typeof(T));
}

public sealed record PortOptionInfo(System.Reflection.PropertyInfo Property, PortOptionAttribute Attribute)
{
    public string Name => Property.Name;

    public Type Type => Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;

    public object? GetValue(object options) => Property.GetValue(options);

    public void SetValue(object options, object? value) => Property.SetValue(options, value);
}
