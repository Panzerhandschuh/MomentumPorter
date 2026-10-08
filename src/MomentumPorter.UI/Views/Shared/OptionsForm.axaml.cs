namespace MomentumPorter.UI.Views.Shared;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MomentumPorter.Options;
using MomentumPorter.UI.ViewModels.Shared;

/// <summary>
/// Edits any options object whose properties carry <see cref="PortOptionAttribute"/>s. Either bind
/// <see cref="Form"/> to an <see cref="OptionsFormViewModel"/>, or set <see cref="Options"/> (and optionally
/// <see cref="OptionsType"/>) and the control builds one.
/// </summary>
public partial class OptionsForm : UserControl
{
    public static readonly StyledProperty<OptionsFormViewModel?> FormProperty =
        AvaloniaProperty.Register<OptionsForm, OptionsFormViewModel?>(nameof(Form));

    public static readonly StyledProperty<object?> OptionsProperty =
        AvaloniaProperty.Register<OptionsForm, object?>(nameof(Options));

    public static readonly StyledProperty<Type?> OptionsTypeProperty =
        AvaloniaProperty.Register<OptionsForm, Type?>(nameof(OptionsType));

    public OptionsForm()
    {
        InitializeComponent();
    }

    public OptionsFormViewModel? Form
    {
        get => GetValue(FormProperty);
        set => SetValue(FormProperty, value);
    }

    public object? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>The options class to describe, when <see cref="Options"/> is a subclass of it. Defaults to its type.</summary>
    public Type? OptionsType
    {
        get => GetValue(OptionsTypeProperty);
        set => SetValue(OptionsTypeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FormProperty || change.Property == OptionsProperty || change.Property == OptionsTypeProperty)
        {
            // The form is the inner root's data context, so bindings on this control keep the page's context
            Root.DataContext = Form ?? (Options is { } options ? new OptionsFormViewModel(options, OptionsType) : null);
        }
    }

    private async void Browse_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TextOptionViewModel field || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        string? path = null;
        switch (field.PathKind)
        {
            case PathKind.File:
            {
                IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = field.Label });
                path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
                break;
            }
            case PathKind.Folder:
            {
                IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = field.Label });
                path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
                break;
            }
            case PathKind.OutputFile:
            {
                IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = field.Label, ShowOverwritePrompt = true });
                path = file?.TryGetLocalPath();
                break;
            }
        }

        if (path is not null)
            field.Value = path;
    }
}
