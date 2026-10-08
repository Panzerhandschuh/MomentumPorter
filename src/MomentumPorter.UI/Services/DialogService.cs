namespace MomentumPorter.UI.Services;

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MomentumPorter.Pipeline;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

/// <param name="Patterns">Glob patterns such as <c>*.bsp</c></param>
public sealed record FileFilter(string Name, params string[] Patterns);

/// <summary>
/// Dialogs and pickers, behind an interface so view models can be tested without a window.
/// Every method may be called from any thread.
/// </summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);

    Task ShowMessageAsync(string title, string message);

    Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple, params FileFilter[] filters);

    Task<string?> PickFolderAsync(string title);

    Task<string?> PickSaveFileAsync(string title, string suggestedName, params FileFilter[] filters);
}

/// <summary>
/// Lets job steps ask the user through the app's dialogs (e.g. the install step's overwrite question).
/// </summary>
public sealed class DialogUserPrompts(IDialogService dialogs) : IUserPrompts
{
    public Task<bool> ConfirmAsync(string question) => dialogs.ConfirmAsync("MomentumPorter", question);
}

public sealed class AvaloniaDialogService(Func<Window?> owner) : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message)
    {
        return OnUiThread(async () =>
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNo, Icon.Question);
            ButtonResult result = owner() is { } window ? await box.ShowWindowDialogAsync(window) : await box.ShowAsync();
            return result == ButtonResult.Yes;
        });
    }

    public Task ShowMessageAsync(string title, string message)
    {
        return OnUiThread(async () =>
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Info);
            return owner() is { } window ? await box.ShowWindowDialogAsync(window) : await box.ShowAsync();
        });
    }

    public Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple, params FileFilter[] filters)
    {
        return OnUiThread<IReadOnlyList<string>>(async () =>
        {
            if (Storage() is not { } storage)
                return [];

            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = allowMultiple,
                FileTypeFilter = ToFileTypes(filters),
            });
            return files.Select(x => x.TryGetLocalPath()).OfType<string>().ToList();
        });
    }

    public Task<string?> PickFolderAsync(string title)
    {
        return OnUiThread(async () =>
        {
            if (Storage() is not { } storage)
                return null;

            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        });
    }

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, params FileFilter[] filters)
    {
        return OnUiThread(async () =>
        {
            if (Storage() is not { } storage)
                return null;

            IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                FileTypeChoices = ToFileTypes(filters),
                DefaultExtension = filters.FirstOrDefault()?.Patterns.FirstOrDefault()?.TrimStart('*'),
                ShowOverwritePrompt = true,
            });
            return file?.TryGetLocalPath();
        });
    }

    private IStorageProvider? Storage() => owner()?.StorageProvider;

    private static List<FilePickerFileType>? ToFileTypes(FileFilter[] filters)
    {
        if (filters.Length == 0)
            return null;

        return filters.Select(x => new FilePickerFileType(x.Name) { Patterns = x.Patterns }).ToList();
    }

    private static Task<T> OnUiThread<T>(Func<Task<T>> action)
    {
        return Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.InvokeAsync(action);
    }
}
