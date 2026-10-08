namespace MomentumPorter.Assets.Common;

/// <summary>
/// A short, valid, silent PCM WAV, for muting a soundscript entry without removing it (the game still asks for it).
/// </summary>
public static class SilentWav
{
    /// <summary>A 50 ms 22050 Hz 16-bit mono silent WAV.</summary>
    public static byte[] Bytes => Create(TimeSpan.FromMilliseconds(50));

    public static byte[] Create(TimeSpan duration, int sampleRate = 22050)
    {
        int frames = Math.Max((int)Math.Round(duration.TotalSeconds * sampleRate), 1);
        return new WavFile(WavFile.FormatPcm, 1, sampleRate, 16, new byte[frames * 2]).ToBytes();
    }
}
