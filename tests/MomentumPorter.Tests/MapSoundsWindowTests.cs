namespace MomentumPorter.Tests;

using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MomentumPorter.Fixes;
using MomentumPorter.UI.ViewModels.Pages;
using MomentumPorter.UI.Views.Pages;
using Xunit;

/// <summary>
/// The Sounds review window's layout, laid out and rendered headless. Set MOMENTUMPORTER_SCREENSHOTS to a folder to
/// save what it looks like.
/// </summary>
public class MapSoundsWindowTests
{
    [AvaloniaFact]
    public async Task PlayingKeepsTheLayoutAndTheVolumeSliderIsCentred()
    {
        var sound = new PackedSound(
            "sound/map/a_very_long_name_for_the_music_track_that_gets_cut_short.mp3",
            1000,
            TimeSpan.FromMinutes(3),
            ["ambient_generic, plays everywhere"],
            MusicGuess.Music,
            "long");
        var review = new MapSoundsViewModel("map", new MapSounds([sound], 0), null, false, _ => [1, 2, 3], new FakeSoundPreview(), 0.5, new HistoricalScheduler());

        // At its narrowest, where the timeline has the least room
        var window = new MapSoundsWindow { DataContext = review };
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        window.Show();
        Settle();

        var bar = window.FindControl<DockPanel>("PreviewBar")!;
        Rect idle = InWindow(bar, window);
        Save(window, "idle");

        await review.Rows[0].PlayCommand.Execute();
        Settle();
        Assert.True(review.IsPreviewing);
        Save(window, "playing");

        // The timeline fits the bar: nothing above it moves
        Assert.Equal(idle, InWindow(bar, window));

        // The sound's name stops short of the timeline, and the timeline's thumb is level with the bar's middle
        var name = window.FindControl<TextBlock>("NowPlayingText")!;
        var timeline = window.FindControl<Slider>("TimelineSlider")!;
        Assert.True(InWindow(name, window).Right <= InWindow(timeline, window).Left, $"{InWindow(name, window)} overlaps {InWindow(timeline, window)}");
        Assert.Equal(idle.Center.Y, InWindow(Thumb(timeline), window).Center.Y, 1);

        // The volume slider opens centred over its button
        var panel = window.FindControl<Border>("VolumePanel")!;
        panel.IsVisible = true;
        Settle();
        Save(window, "volume");
        double button = InWindow(window.FindControl<Button>("VolumeButton")!, window).Center.X;
        Assert.Equal(button, InWindow(panel, window).Center.X, 1);
        Assert.Equal(button, InWindow(Thumb(window.FindControl<Slider>("VolumeSlider")!), window).Center.X, 1);

        window.Close();
    }

    [AvaloniaFact]
    public async Task TheVolumeOpensOnHoverAndItsIconMutes()
    {
        var sound = new PackedSound("sound/map/track.mp3", 1000, TimeSpan.FromMinutes(3), [], MusicGuess.Music, "long");
        var review = new MapSoundsViewModel("map", new MapSounds([sound], 0), null, false, _ => [1], new FakeSoundPreview(), 0.4, new HistoricalScheduler());
        var window = new MapSoundsWindow { DataContext = review };
        window.Show();
        Settle();

        var panel = window.FindControl<Border>("VolumePanel")!;
        Point icon = InWindow(window.FindControl<Button>("VolumeButton")!, window).Center;
        Assert.False(panel.IsVisible);

        // Hovering opens it, and it stays open while the pointer moves up onto the slider
        window.MouseMove(icon);
        Settle();
        Assert.True(panel.IsVisible);
        window.MouseMove(InWindow(panel, window).Center);
        await Wait(VolumeCloseWait);
        Assert.True(panel.IsVisible);

        // Clicking the icon mutes, and again puts the volume back
        window.MouseMove(icon);
        window.MouseDown(icon, MouseButton.Left);
        window.MouseUp(icon, MouseButton.Left);
        Settle();
        Assert.True(review.IsMuted);
        window.MouseDown(icon, MouseButton.Left);
        window.MouseUp(icon, MouseButton.Left);
        Settle();
        Assert.Equal(40, review.Volume);

        // Leaving both closes it, after a moment
        window.MouseMove(new Point(window.Width / 2, 10));
        Settle();
        Assert.True(panel.IsVisible);
        await Wait(VolumeCloseWait);
        Assert.False(panel.IsVisible);

        window.Close();
    }

    // Longer than the window waits before closing the volume slider
    private static readonly TimeSpan VolumeCloseWait = TimeSpan.FromMilliseconds(500);

    // Timers only fire while the dispatcher has the thread, so this awaits rather than sleeps
    private static async Task Wait(TimeSpan time)
    {
        await Task.Delay(time);
        Settle();
    }

    private static Thumb Thumb(Slider slider) => slider.GetVisualDescendants().OfType<Thumb>().Single();

    private static Rect InWindow(Visual visual, Visual window) => new(visual.TranslatePoint(default, window)!.Value, visual.Bounds.Size);

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("MOMENTUMPORTER_SCREENSHOTS") is not { Length: > 0 } folder)
            return;

        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, $"sounds_{name}.png"));
    }
}
