namespace MomentumPorter.UI.Views.Shared;

using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MomentumPorter.UI.ViewModels.Shared;

/// <summary>
/// A job's log, following new lines while scrolled to the bottom.
/// </summary>
public partial class LogView : UserControl
{
    private LogViewModel? _log;
    private bool _scrollQueued;

    public LogView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_log is not null)
            _log.Lines.CollectionChanged -= OnLinesChanged;

        _log = DataContext as LogViewModel;
        if (_log is not null)
        {
            _log.Lines.CollectionChanged += OnLinesChanged;
            QueueScrollToEnd();
        }
    }

    private ScrollViewer? ScrollViewer => Lines.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        // Measured before the new lines are laid out, so this is where the user was
        if (ScrollViewer is not { } scroll || scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 24)
            QueueScrollToEnd();
    }

    private void QueueScrollToEnd()
    {
        if (_scrollQueued)
            return;

        _scrollQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollQueued = false;
            ScrollViewer?.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private async void CopySelected_OnClick(object? sender, RoutedEventArgs e)
    {
        string text = string.Join(Environment.NewLine, Lines.SelectedItems?.OfType<LogLine>().Select(x => x.Text) ?? []);
        await CopyAsync(text);
    }

    private async void CopyAll_OnClick(object? sender, RoutedEventArgs e)
    {
        await CopyAsync(_log?.ToString() ?? "");
    }

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
