namespace MomentumPorter.UI.ViewModels.Shared;

using System.Globalization;
using System.Reflection;
using MomentumPorter.Options;
using MomentumPorter.Porting;
using ReactiveUI;

/// <summary>
/// A form generated from the <see cref="PortOptionAttribute"/>s of an options class, editing an instance of it in place.
/// Works for any options class (map options, BSPConvert options...).
/// </summary>
public sealed class OptionsFormViewModel : ViewModel
{
    public const string DefaultGroup = "General";

    /// <param name="exclude">Options the page edits with its own controls, left out of the form</param>
    public OptionsFormViewModel(object options, Type? optionsType = null, IEnumerable<string>? exclude = null)
    {
        Options = options;
        OptionsType = optionsType ?? options.GetType();
        if (!OptionsType.IsInstanceOfType(options))
            throw new ArgumentException($"{options.GetType().Name} isn't a {OptionsType.Name}", nameof(options));

        var excluded = new HashSet<string>(exclude ?? [], StringComparer.Ordinal);
        IReadOnlyList<PortOptionInfo> infos = PortOptions.Describe(OptionsType).Where(x => !excluded.Contains(x.Name)).ToList();
        var fields = infos.Select(x => OptionFieldViewModel.Create(x, options)).OfType<OptionFieldViewModel>().ToList();

        // General first, then the rest in the order Describe sorts them
        Groups = fields
            .GroupBy(x => x.Info.Attribute.Group)
            .OrderBy(x => x.Key == DefaultGroup ? 0 : 1)
            .Select(x => new OptionGroupViewModel(
                x.Key,
                x.Where(f => !f.Info.Attribute.Advanced).ToList(),
                x.Where(f => f.Info.Attribute.Advanced).ToList()))
            .ToList();
    }

    public object Options { get; }

    public Type OptionsType { get; }

    public IReadOnlyList<OptionGroupViewModel> Groups { get; }

    public IEnumerable<OptionFieldViewModel> Fields => Groups.SelectMany(x => x.Fields.Concat(x.AdvancedFields));

    public OptionFieldViewModel? Find(string propertyName) => Fields.FirstOrDefault(x => x.Name == propertyName);

    /// <summary>Re-reads every field from the options object, after it was changed from code.</summary>
    public void Reload()
    {
        foreach (OptionFieldViewModel field in Fields)
            field.Load();
    }
}

public sealed class OptionGroupViewModel(string name, IReadOnlyList<OptionFieldViewModel> fields, IReadOnlyList<OptionFieldViewModel> advancedFields) : ViewModel
{
    public string Name { get; } = name;

    public IReadOnlyList<OptionFieldViewModel> Fields { get; } = fields;

    public IReadOnlyList<OptionFieldViewModel> AdvancedFields { get; } = advancedFields;

    public bool HasAdvanced => AdvancedFields.Count > 0;
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
        {
            return info.Attribute.Choices == OptionChoices.GameProfile
                ? new ChoiceOptionViewModel(info, target, GameChoices())
                : new TextOptionViewModel(info, target);
        }

        return null;
    }

    /// <summary>"(detect)" (null) plus each game profile.</summary>
    public static IReadOnlyList<OptionChoice> GameChoices()
    {
        return [new OptionChoice(null, "(detect)"), .. GameProfiles.All.Select(x => new OptionChoice(x.Name, $"{x.Name} ({x.Description})"))];
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

/// <param name="Value">What's written to the option, e.g. a game profile name; null for "none"/"detect"</param>
public sealed record OptionChoice(string? Value, string Display)
{
    public override string ToString() => Display;
}

/// <summary>
/// A string option picked from a fixed list, such as the source game.
/// </summary>
public sealed class ChoiceOptionViewModel : OptionFieldViewModel
{
    private OptionChoice? _selected;

    public ChoiceOptionViewModel(PortOptionInfo info, object target, IReadOnlyList<OptionChoice> choices) : base(info, target)
    {
        Choices = choices;
        Load();
    }

    public IReadOnlyList<OptionChoice> Choices { get; private set; }

    public OptionChoice? Selected
    {
        get => _selected;
        set
        {
            if (value is null)
                return;

            this.RaiseAndSetIfChanged(ref _selected, value);
            Write(value.Value);
        }
    }

    public override void Load()
    {
        string? current = Read() as string;
        _selected = Choices.FirstOrDefault(x => string.Equals(x.Value, current, StringComparison.OrdinalIgnoreCase));
        if (_selected is null && current is not null)
        {
            // Keep a value from a preset even if this build doesn't know that game
            _selected = new OptionChoice(current, current);
            Choices = [.. Choices, _selected];
            this.RaisePropertyChanged(nameof(Choices));
        }

        this.RaisePropertyChanged(nameof(Selected));
    }
}
