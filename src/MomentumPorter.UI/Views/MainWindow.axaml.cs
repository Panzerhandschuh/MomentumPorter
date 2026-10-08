namespace MomentumPorter.UI.Views;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MomentumPorter.UI.ViewModels;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        // No pointer events arrive during a drag, so this is a drag that was cancelled inside the window
        if (ViewModel is { IsDragOver: true } vm)
            vm.IsDragOver = false;

        base.OnPointerMoved(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Don't leave bspPack running in the background
        ViewModel?.Maps.Runner.Cancel();
        base.OnClosing(e);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
            return;

        e.DragEffects = DragDropEffects.Copy;
        if (ViewModel is { } vm)
            vm.IsDragOver = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // Leaving a child control bubbles up here too; only hide the overlay when the drag leaves the window
        Avalonia.Point position = e.GetPosition(this);
        bool insideWindow = position.X > 0 && position.Y > 0 && position.X < Bounds.Width && position.Y < Bounds.Height;
        if (!insideWindow && ViewModel is { } vm)
            vm.IsDragOver = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        vm.IsDragOver = false;
        List<string> paths = e.Data.GetFiles()?.Select(x => x.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        if (paths.Count > 0)
            vm.OpenFiles(paths);
    }

    private void DismissStatus_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            vm.StatusMessage = null;
    }
}
