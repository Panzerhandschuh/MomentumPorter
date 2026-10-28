namespace MomentumPorter.UI.Views.Pages;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MomentumPorter.UI.ViewModels.Pages;

/// <summary>
/// A map's Sounds review. Closes with true when the picks are saved.
/// </summary>
public partial class MapSoundsWindow : Window
{
    // Forgives the pointer briefly slipping off the volume button or its slider
    private static readonly TimeSpan VolumeCloseDelay = TimeSpan.FromMilliseconds(300);

    private IDisposable? _closeVolume;
    private bool _draggingVolume;
    private bool _volumeFromKeyboard;

    public MapSoundsWindow()
    {
        InitializeComponent();
        SaveButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);

        // The slider is inside VolumeHost, so the pointer is over the host while it's over either the button or the slider
        VolumeHost.PointerEntered += (_, _) => OpenVolume();
        VolumeHost.PointerExited += (_, _) => CloseVolumeSoon();

        // Dragging the slider can take the pointer off it; that doesn't close it until the button is let go. The thumb
        // captures the pointer while dragging, but its release still tunnels through the panel
        VolumePanel.AddHandler(PointerPressedEvent, (_, _) => _draggingVolume = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        VolumePanel.AddHandler(PointerReleasedEvent, (_, _) => StopDraggingVolume(), RoutingStrategies.Tunnel, handledEventsToo: true);

        // Tabbing to the button (whose click mutes) opens it too, so the next Tab reaches the slider. It stays open until
        // focus leaves it or Escape
        VolumeHost.GotFocus += (_, e) =>
        {
            if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional)
            {
                OpenVolume();
                _volumeFromKeyboard = true;
            }
        };
        VolumeHost.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_volumeFromKeyboard && !VolumeHost.IsKeyboardFocusWithin)
                CloseVolume();
        });
        VolumeHost.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && VolumePanel.IsVisible)
            {
                CloseVolume();
                VolumeButton.Focus();
                e.Handled = true;
            }
        };

        VolumeHost.PointerWheelChanged += OnVolumeWheel;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closeVolume?.Dispose();
        (DataContext as MapSoundsViewModel)?.Stop();
        base.OnClosed(e);
    }

    private void OpenVolume()
    {
        _closeVolume?.Dispose();
        _closeVolume = null;
        VolumePanel.IsVisible = true;
    }

    private void CloseVolumeSoon()
    {
        _closeVolume?.Dispose();
        _closeVolume = DispatcherTimer.RunOnce(() =>
        {
            if (!_draggingVolume && !VolumeHost.IsPointerOver && !(_volumeFromKeyboard && VolumeHost.IsKeyboardFocusWithin))
                CloseVolume();
        }, VolumeCloseDelay);
    }

    private void CloseVolume()
    {
        _closeVolume?.Dispose();
        _closeVolume = null;
        _volumeFromKeyboard = false;
        VolumePanel.IsVisible = false;
    }

    private void StopDraggingVolume()
    {
        if (!_draggingVolume)
            return;

        _draggingVolume = false;
        CloseVolumeSoon();
    }

    // Scrolling over the volume changes it 5 at a time, as media players do
    private void OnVolumeWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MapSoundsViewModel review || e.Delta.Y == 0)
            return;

        review.Volume += Math.Sign(e.Delta.Y) * 5;
        OpenVolume();
        e.Handled = true;
    }
}
