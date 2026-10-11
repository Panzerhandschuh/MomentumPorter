namespace MomentumPorter.UI;

using System.Diagnostics;
using System.Reactive;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels;
using MomentumPorter.UI.ViewModels.Pages;
using MomentumPorter.UI.Views;
using MomentumPorter.UI.Views.Pages;
using MomentumPorter.Updates;
using ReactiveUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        ViewLocator.Register<MapsPageViewModel, MapsPageView>();
        ViewLocator.Register<SettingsPageViewModel, SettingsPageView>();
        ViewLocator.Register<PlayerSoundsPageViewModel, PlayerSoundsPageView>();
        ViewLocator.Register<WeaponsPageViewModel, WeaponsPageView>();
        ViewLocator.Register<PlaceholderPageViewModel, PlaceholderPageView>();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            Action<Action> post = action => Dispatcher.UIThread.Post(action);
            var dialogs = new AvaloniaDialogService(() => window);
            var runner = new JobRunnerService(post);
            var settings = SettingsService.Load();
            var viewModel = new MainWindowViewModel(settings, dialogs, runner, post);
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // A failing command (e.g. a folder that can't be opened) shows in the status bar instead of crashing the app
            RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex => post(() => viewModel.StatusMessage = ex.Message));

            var updates = new UpdateService(settings, dialogs, message => viewModel.StatusMessage = message, () => viewModel.IsBusy, executable =>
            {
                viewModel.CancelJobs();
                var start = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(executable),
                    Environment = { [UpdateService.UpdatedFromVariable] = AppUpdater.CurrentVersion.ToString() },
                };
                Process.Start(start);
                desktop.Shutdown();
            });
            desktop.Exit += (_, _) => updates.ApplyPending();
            if (updates.TakeUpdatedMessage() is { } updated)
                viewModel.StatusMessage = updated;

            _ = viewModel.Settings.ScanAsync();
            _ = viewModel.PlayerSounds.ScanAsync();
            _ = updates.CheckAsync();
            if (desktop.Args is { Length: > 0 } args)
                viewModel.OpenFiles(args);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
