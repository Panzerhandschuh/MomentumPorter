namespace MomentumPorter.UI.ViewModels.Shared;

using System.Globalization;
using System.Reflection;
using MomentumPorter.Options;
using ReactiveUI;

/// <summary>
/// A form generated from the <see cref="PortOptionAttribute"/>s of an options class, editing an instance of it in place.
/// Works for any options class (map options, BSPConvert options...). Options marked advanced only show while
/// <see cref="ShowAdvanced"/> is on, inline under their group rather than in an expander of their own.
/// </summary>
public sealed class OptionsFormViewModel : ViewModel
{
    private readonly List<OptionFieldViewModel> _fields;
    private Func<OptionFieldViewModel, bool> _filter = _ => true;
    private IReadOnlyList<OptionGroupViewModel> _groups;
    private bool _showAdvanced;

    /// <param name="exclude">Options the page edits with its own controls, left out of the form</param>
    public OptionsFormViewModel(object options, Type? optionsType = null, IEnumerable<string>? exclude = null)
    {
        Options = options;
        OptionsType = optionsType ?? options.GetType();
        if (!OptionsType.IsInstanceOfType(options))
            throw new ArgumentException($"{options.GetType().Name} isn't a {OptionsType.Name}", nameof(options));

        var excluded = new HashSet<string>(exclude ?? [], StringComparer.Ordinal);
        IReadOnlyList<PortOptionInfo> infos = PortOptions.Describe(OptionsType).Where(x => !excluded.Contains(x.Name)).ToList();
        _fields = infos.Select(x => OptionFieldViewModel.Create(x, options)).OfType<OptionFieldViewModel>().ToList();
        _groups = BuildGroups();
    }

    public object Options { get; }

    public Type OptionsType { get; }

    /// <summary>The groups on show, see <see cref="SetFilter"/> and <see cref="ShowAdvanced"/>.</summary>
    public IReadOnlyList<OptionGroupViewModel> Groups
    {
        get => _groups;
        private set => this.RaiseAndSetIfChanged(ref _groups, value);
    }

    /// <summary>Whether the options marked advanced are on show too. Hidden fields keep their values.</summary>
    public bool ShowAdvanced
    {
        get => _showAdvanced;
        set
        {
            if (_showAdvanced == value)
                return;

            this.RaiseAndSetIfChanged(ref _showAdvanced, value);
            Groups = BuildGroups();
        }
    }

    /// <summary>Whether any option, shown or not, is marked advanced, so a page knows to offer the toggle.</summary>
    public bool HasAdvanced => _fields.Any(x => x.Info.Attribute.Advanced);

    /// <summary>Every field, shown or not.</summary>
    public IEnumerable<OptionFieldViewModel> Fields => _fields;

    /// <summary>The fields on show, in order.</summary>
    public IEnumerable<OptionFieldViewModel> VisibleFields => Groups.SelectMany(x => x.Fields);

    /// <summary>
    /// Shows only the fields <paramref name="show"/> accepts (all of them when null); groups left empty are hidden.
    /// Hidden fields keep their values.
    /// </summary>
    public void SetFilter(Func<OptionFieldViewModel, bool>? show)
    {
        _filter = show ?? (_ => true);
        Groups = BuildGroups();
    }

    public OptionFieldViewModel? Find(string propertyName) => Fields.FirstOrDefault(x => x.Name == propertyName);

    /// <summary>Re-reads every field from the options object, after it was changed from code.</summary>
    public void Reload()
    {
        foreach (OptionFieldViewModel field in Fields)
            field.Load();
    }

    // Groups in the order the options class declares them (PortOptions.Describe), each with its visible fields
    private List<OptionGroupViewModel> BuildGroups()
    {
        List<OptionGroupViewModel> groups = _fields
            .Where(x => _filter(x) && (ShowAdvanced || !x.Info.Attribute.Advanced))
            .GroupBy(x => x.Info.Attribute.Group)
            .Select(x => new OptionGroupViewModel(x.Key, x.ToList()))
            .ToList();

        // A form of one group needs no heading over it
        foreach (OptionGroupViewModel group in groups)
            group.ShowHeader = groups.Count > 1;

        return groups;
    }
}

public sealed class OptionGroupViewModel(string name, IReadOnlyList<OptionFieldViewModel> fields) : ViewModel
{
    public string Name { get; } = name;

    public IReadOnlyList<OptionFieldViewModel> Fields { get; } = fields;

    /// <summary>Whether the group's name is shown over its fields (not for a form of one group).</summary>
    public bool ShowHeader { get; internal set; } = true;
}

/// <summary>
/// One option's editor. Changes are written straight to the options object.
/// </summary>
public abstract class OptionFieldViewModel(PortOptionInfo info, object target) : ViewModel
{
    private static readonly NullabilityInfoContext Nullability = new();

    public PortOptionInfo Info { get; } = info;

    public object Target { get; } = target;

    public string Name => Info.Name;

    public string Label => Info.Attribute.Label;

    public string Help => Info.Attribute.Help;

    /// <summary>Whether the property accepts null (string?, int?...).</summary>
    public bool IsNullable =>
        Nullable.GetUnderlyingType(Info.Property.PropertyType) is not null
        || (!Info.Property.PropertyType.IsValueType && Nullability.Create(Info.Property).WriteState != NullabilityState.NotNull);

    public abstract void Load();

    protected object? Read() => Info.GetValue(Target);

    protected void Write(object? value) => Info.SetValue(Target, value);

    /// <summary>
    /// The editor for an option, or null for a property type the form can't edit.
    /// </summary>
    public static OptionFieldViewModel? Create(PortOptionInfo info, object target)
    {
        Type type = info.Type;
        if (type == typeof(bool))
            return new BoolOptionViewModel(info, target);

        if (type.IsEnum)
            return new EnumOptionViewModel(info, target);

        if (NumberOptionViewModel.IsNumber(type))
            return new NumberOptionViewModel(info, target);

        if (type == typeof(string))
            return info.Attribute.Choices ? new ChoiceOptionViewModel(info, target) : new TextOptionViewModel(info, target);

        return null;
    }
}

public sealed class BoolOptionViewModel : OptionFieldViewModel
{
    private bool _value;

    public BoolOptionViewModel(PortOptionInfo info, object target) : base(info, target) => Load();

    public bool Value
    {
        get => _value;
        set
        {
            this.RaiseAndSetIfChanged(ref _value, value);
            Write(value);
        }
    }

    public override void Load()
    {
        _value = Read() is true;
        this.RaisePropertyChanged(nameof(Value));
    }
}

public sealed class NumberOptionViewModel : OptionFieldViewModel
{
    private decimal? _value;

    public NumberOptionViewModel(PortOptionInfo info, object target) : base(info, target)
    {
        IsInteger = info.Type == typeof(int) || info.Type == typeof(long) || info.Type == typeof(short) || info.Type == typeof(byte);
        decimal typeMin = IsInteger ? Convert.ToDecimal(info.Type.GetField("MinValue")!.GetValue(null), CultureInfo.InvariantCulture) : -1_000_000_000m;
        decimal typeMax = IsInteger ? Convert.ToDecimal(info.Type.GetField("MaxValue")!.GetValue(null), CultureInfo.InvariantCulture) : 1_000_000_000m;
        Minimum = double.IsNaN(info.Attribute.Min) ? typeMin : (decimal)info.Attribute.Min;
        Maximum = double.IsNaN(info.Attribute.Max) ? typeMax : (decimal)info.Attribute.Max;
        Increment = IsInteger ? 1 : 0.1m;
        FormatString = IsInteger ? "0" : "0.###";
        Load();
    }

    public bool IsInteger { get; }

    public decimal Minimum { get; }

    public decimal Maximum { get; }

    public decimal Increment { get; }

    public string FormatString { get; }

    public decimal? Value
    {
        get => _value;
        set
        {
            if (value is { } number)
                value = Math.Clamp(IsInteger ? Math.Round(number) : number, Minimum, Maximum);
            else if (!IsNullable)
                return;

            this.RaiseAndSetIfChanged(ref _value, value);
            Write(value is null ? null : Convert.ChangeType(value.Value, Info.Type, CultureInfo.InvariantCulture));
        }
    }

    public override void Load()
    {
        _value = Read() is { } value ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : null;
        this.RaisePropertyChanged(nameof(Value));
    }

    public static bool IsNumber(Type type)
    {
        return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }
}

public sealed class TextOptionViewModel : OptionFieldViewModel
{
    private string? _value;

    public TextOptionViewModel(PortOptionInfo info, object target) : base(info, target) => Load();

    public PathKind PathKind => Info.Attribute.Path;

    public bool CanBrowse => PathKind != PathKind.None;

    public string? Value
    {
        get => _value;
        set
        {
            this.RaiseAndSetIfChanged(ref _value, value);
            Write(string.IsNullOrWhiteSpace(value) && IsNullable ? null : value ?? "");
        }
    }

    public override void Load()
    {
        _value = Read() as string;
        this.RaisePropertyChanged(nameof(Value));
    }
}

public sealed class EnumOptionViewModel : OptionFieldViewModel
{
    private object? _value;

    public EnumOptionViewModel(PortOptionInfo info, object target) : base(info, target)
    {
        Choices = Enum.GetValues(info.Type).Cast<object>().ToList();
        Load();
    }

    public IReadOnlyList<object> Choices { get; }

    public object? Value
    {
        get => _value;
        set
        {
            if (value is null && !IsNullable)
                return;

            this.RaiseAndSetIfChanged(ref _value, value);
            Write(value);
        }
    }

    public override void Load()
    {
        _value = Read();
        this.RaisePropertyChanged(nameof(Value));
    }
}

/// <summary>A value a <see cref="ChoiceOptionViewModel"/> offers, shown as its label.</summary>
public sealed record OptionChoice(string? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// A string option picked from a list (<see cref="PortOptionAttribute.Choices"/>), which the page fills in with
/// <see cref="SetChoices"/>. A value the list doesn't have, e.g. from a preset made on another machine, is added to it
/// as it is, so it's kept.
/// </summary>
public sealed class ChoiceOptionViewModel : OptionFieldViewModel
{
    private IReadOnlyList<OptionChoice> _choices = [];
    private OptionChoice? _selected;

    public ChoiceOptionViewModel(PortOptionInfo info, object target) : base(info, target) => Load();

    public IReadOnlyList<OptionChoice> Choices
    {
        get => _choices;
        private set => this.RaiseAndSetIfChanged(ref _choices, value);
    }

    public OptionChoice? Selected
    {
        get => _selected;
        set
        {
            // The ComboBox clears its selection while its items are replaced
            if (value is null)
                return;

            this.RaiseAndSetIfChanged(ref _selected, value);
            Write(value.Value);
        }
    }

    public void SetChoices(IEnumerable<OptionChoice> choices)
    {
        Choices = choices.ToList();
        Load();
    }

    public override void Load()
    {
        string? value = Read() as string;
        OptionChoice? match = Choices.FirstOrDefault(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            match = new OptionChoice(value, value ?? "");
            Choices = [.. Choices, match];
        }

        _selected = match;
        this.RaisePropertyChanged(nameof(Selected));
    }
}
