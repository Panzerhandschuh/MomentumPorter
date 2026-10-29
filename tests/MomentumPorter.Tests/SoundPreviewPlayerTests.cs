namespace MomentumPorter.Tests;

using System.Text;
using MomentumPorter.UI.Services;
using Xunit;

/// <summary>
/// The sound preview is one player for the whole app, so tests that play or stop it (the Player Sounds page stops it
/// when the game changes) can't run in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SoundPreviewCollection
{
    public const string Name = "Sound preview";
}

/// <summary>
/// The Sounds review's preview through Windows' MCI, on a silent WAV at no volume. Does nothing elsewhere.
/// </summary>
[Collection(SoundPreviewCollection.Name)]
public class SoundPreviewPlayerTests
{
    [Fact]
    public void PlaysSeeksAndFinishes()
    {
        if (!SoundPreviewPlayer.IsSupported)
            return;

        // MCI's DirectShow device won't open from xUnit's MTA threads; the app plays from its UI thread, which is STA
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                PlaySeekAndFinish();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
    }

    private static void PlaySeekAndFinish()
    {
        ISoundPreview preview = SoundPreviewPlayer.Files;
        double volume = preview.Volume;
        preview.Volume = 0;
        try
        {
            Assert.Null(preview.Play(SilentWav(TimeSpan.FromSeconds(2)), "silence.wav"));
            PreviewPosition start = Assert.NotNull(preview.Position());
            Assert.InRange(start.Length.TotalMilliseconds, 1950, 2050);
            Assert.False(start.Finished);

            preview.Seek(TimeSpan.FromSeconds(1));
            Assert.InRange(Assert.NotNull(preview.Position()).Position.TotalMilliseconds, 950, 1500);

            // Past the end plays its last moment, then it's finished
            preview.Seek(TimeSpan.FromMinutes(1));
            Thread.Sleep(300);
            Assert.True(Assert.NotNull(preview.Position()).Finished);

            preview.Stop();
            Assert.Null(preview.Position());
        }
        finally
        {
            preview.Stop();
            preview.Volume = volume;
        }
    }

    // 16-bit mono PCM of nothing
    private static byte[] SilentWav(TimeSpan length)
    {
        const int Rate = 22050;
        int data = (int)(Rate * length.TotalSeconds) * 2;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + data);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(Rate);
            writer.Write(Rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(data);
            writer.Write(new byte[data]);
        }

        return stream.ToArray();
    }
}
