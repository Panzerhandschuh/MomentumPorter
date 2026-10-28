// [AvaloniaFact] tests run on this app's dispatcher
[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(MomentumPorter.Tests.HeadlessApp))]

namespace MomentumPorter.Tests;

using Avalonia;
using Avalonia.Headless;
using Avalonia.ReactiveUI;
using MomentumPorter.UI;

/// <summary>
/// The app with its theme and styles on Avalonia's headless platform, rendering with Skia so windows can be captured.
/// </summary>
public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseReactiveUI();
}
