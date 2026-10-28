namespace MomentumPorter.Tests;

using MomentumPorter.UI.Services;

/// <summary>
/// A Sounds review preview that plays nothing: every sound is 3 minutes long, and the test moves it along by setting
/// <see cref="Now"/>.
/// </summary>
internal sealed class FakeSoundPreview : ISoundPreview
{
    public double Volume { get; set; }

    public PreviewPosition? Now { get; set; }

    public List<TimeSpan> Seeks { get; } = [];

    public string? Play(byte[] bytes, string fileName)
    {
        Now = new PreviewPosition(TimeSpan.Zero, TimeSpan.FromMinutes(3), false);
        return null;
    }

    public PreviewPosition? Position() => Now;

    public void Seek(TimeSpan position)
    {
        Seeks.Add(position);
        Now = Now!.Value with { Position = position };
    }

    public void Stop() => Now = null;
}
