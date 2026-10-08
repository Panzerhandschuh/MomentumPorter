namespace MomentumPorter.UI;

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
using ReactiveUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        ViewLocator.Register<MapsPageViewModel, MapsPageView>();
        ViewLocator.Register<SettingsPageViewModel, SettingsPageView>();
        ViewLocator.Register<PlayerSoundsPageViewModel, PlayerSoundsPageView>();
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
            var viewModel = new MainWindowViewModel(SettingsService.Load(), dialogs, runner, post);
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // A failing command (e.g. a folder that can't be opened) shows in the status bar instead of crashing the app
            RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex => post(() => viewModel.StatusMessage = ex.Message));

            _ = viewModel.Settings.ScanAsync();
            _ = viewModel.PlayerSounds.ScanAsync();
            if (desktop.Args is { Length: > 0 } args)
                viewModel.OpenFiles(args);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
