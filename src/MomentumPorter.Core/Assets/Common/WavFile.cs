namespace MomentumPorter.Assets.Common;

using System.Buffers.Binary;
using System.Text;

/// <summary>
/// A RIFF WAVE file's format and sample data, enough to check whether Source can play it and to re-encode it if not.
/// </summary>
public sealed class WavFile
{
    public const int FormatPcm = 1;
    public const int FormatIeeeFloat = 3;
    public const int FormatExtensible = 0xFFFE;

    /// <summary>The sample rates Source mixes. Sounds at any other rate play nothing.</summary>
    public static readonly IReadOnlyList<int> SourceSampleRates = [11025, 22050, 44100];

    public WavFile(int format, int channels, int sampleRate, int bitsPerSample, byte[] data)
    {
        Format = format;
        Channels = channels;
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        Data = data;
    }

    /// <summary>The format tag, with WAVE_FORMAT_EXTENSIBLE resolved to its sub format.</summary>
    public int Format { get; }

    public int Channels { get; }

    public int SampleRate { get; }

    public int BitsPerSample { get; }

    /// <summary>The data chunk: interleaved samples, 8-bit unsigned or 16/24/32-bit signed little endian, or floats.</summary>
    public byte[] Data { get; }

    /// <summary>Whether the file had chunks other than fmt and data (cue points, LIST...). Source ignores most.</summary>
    public bool HasExtraChunks { get; private init; }

    public int BlockAlign => Channels * BitsPerSample / 8;

    public int FrameCount => BlockAlign == 0 ? 0 : Data.Length / BlockAlign;

    public TimeSpan Duration => SampleRate == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)FrameCount / SampleRate);

    /// <summary>
    /// Whether Source plays the file as is: 8 or 16-bit PCM, mono or stereo, at 11025, 22050 or 44100 Hz.
    /// </summary>
    public bool IsSourceCompatible =>
        Format == FormatPcm
        && BitsPerSample is 8 or 16
        && Channels is 1 or 2
        && SourceSampleRates.Contains(SampleRate);

    public string Describe()
    {
        string format = Format switch
        {
            FormatPcm => "PCM",
            FormatIeeeFloat => "float",
            _ => $"format {Format}",
        };
        return $"{format} {BitsPerSample}-bit {(Channels == 1 ? "mono" : Channels == 2 ? "stereo" : $"{Channels} channels")} {SampleRate} Hz";
    }

    public static WavFile Parse(byte[] bytes)
    {
        return TryParse(bytes, out WavFile? wav, out string? error) ? wav : throw new FormatException(error);
    }

    public static bool TryParse(byte[] bytes, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WavFile? wav, out string? error)
    {
        wav = null;
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
        {
            error = "not a RIFF WAVE file";
            return false;
        }

        int format = 0, channels = 0, rate = 0, bits = 0;
        byte[]? data = null;
        bool extra = false;
        for (int offset = 12; offset + 8 <= bytes.Length;)
        {
            string id = Encoding.ASCII.GetString(bytes, offset, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4));
            int start = offset + 8;
            // Some writers leave the size of the last chunk wrong; take what's there
            if (size < 0 || start + size > bytes.Length)
                size = bytes.Length - start;

            ReadOnlySpan<byte> chunk = bytes.AsSpan(start, size);
            switch (id)
            {
                case "fmt ":
                    if (size < 16)
                    {
                        error = "fmt chunk is too short";
                        return false;
                    }

                    format = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
                    rate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
                    bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
                    // WAVE_FORMAT_EXTENSIBLE: the real format is the first two bytes of the sub format GUID
                    if (format == FormatExtensible && size >= 26)
                        format = BinaryPrimitives.ReadUInt16LittleEndian(chunk[24..]);
                    break;
                case "data":
                    data = chunk.ToArray();
                    break;
                default:
                    extra = true;
                    break;
            }

            offset = start + size + (size & 1);
        }

        if (format == 0)
        {
            error = "no fmt chunk";
            return false;
        }

        if (data is null)
        {
            error = "no data chunk";
            return false;
        }

        if (channels < 1 || rate <= 0 || bits is not (8 or 16 or 24 or 32) || (format == FormatIeeeFloat && bits != 32) || format is not (FormatPcm or FormatIeeeFloat))
        {
            error = $"unsupported format (format {format}, {channels} channels, {rate} Hz, {bits}-bit)";
            return false;
        }

        wav = new WavFile(format, channels, rate, bits, data) { HasExtraChunks = extra };
        error = null;
        return true;
    }

    /// <summary>
    /// Returns a copy Source can play: 16-bit PCM (8-bit PCM is kept), at most two channels, at the next sample rate
    /// Source mixes (or 44100 Hz above that). Returns this file if it's already playable.
    /// </summary>
    public WavFile ToSourceCompatible()
    {
        if (IsSourceCompatible)
            return this;

        int frames = FrameCount;
        // More than stereo gets averaged into mono
        int channels = Channels <= 2 ? Channels : 1;
        int rate = SourceSampleRates.FirstOrDefault(x => x >= SampleRate);
        if (rate == 0)
            rate = SourceSampleRates[^1];

        // Samples as floats in -1..1
        var samples = new float[frames * channels];
        for (int frame = 0; frame < frames; frame++)
        {
            if (channels == Channels)
            {
                for (int channel = 0; channel < channels; channel++)
                    samples[frame * channels + channel] = ReadSample(frame * Channels + channel);
            }
            else
            {
                float sum = 0;
                for (int channel = 0; channel < Channels; channel++)
                    sum += ReadSample(frame * Channels + channel);
                samples[frame] = sum / Channels;
            }
        }

        float[] resampled = rate == SampleRate ? samples : Resample(samples, channels, SampleRate, rate);

        // 8-bit PCM is fine at the new rate; everything else becomes 16-bit
        int bits = Format == FormatPcm && BitsPerSample == 8 ? 8 : 16;
        var data = new byte[resampled.Length * bits / 8];
        for (int i = 0; i < resampled.Length; i++)
        {
            float value = Math.Clamp(resampled[i], -1f, 1f);
            if (bits == 8)
                data[i] = (byte)Math.Clamp((int)MathF.Round(value * 127f) + 128, 0, 255);
            else
                BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), (short)Math.Clamp((int)MathF.Round(value * 32767f), short.MinValue, short.MaxValue));
        }

        return new WavFile(FormatPcm, channels, rate, bits, data);
    }

    /// <summary>
    /// Writes a plain RIFF WAVE file with just the fmt and data chunks.
    /// </summary>
    public byte[] ToBytes()
    {
        using var stream = new MemoryStream(44 + Data.Length + 1);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            int padding = Data.Length & 1;
            writer.Write("RIFF"u8);
            writer.Write(4 + 8 + 16 + 8 + Data.Length + padding);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)Format);
            writer.Write((short)Channels);
            writer.Write(SampleRate);
            writer.Write(SampleRate * BlockAlign);
            writer.Write((short)BlockAlign);
            writer.Write((short)BitsPerSample);
            writer.Write("data"u8);
            writer.Write(Data.Length);
            writer.Write(Data);
            if (padding != 0)
                writer.Write((byte)0);
        }

        return stream.ToArray();
    }

    // Sample at an interleaved index as -1..1
    private float ReadSample(int index)
    {
        ReadOnlySpan<byte> data = Data;
        return (Format, BitsPerSample) switch
        {
            (FormatIeeeFloat, _) => BinaryPrimitives.ReadSingleLittleEndian(data[(index * 4)..]),
            (_, 8) => (data[index] - 128) / 128f,
            (_, 16) => BinaryPrimitives.ReadInt16LittleEndian(data[(index * 2)..]) / 32768f,
            (_, 24) => ((data[index * 3] | (data[index * 3 + 1] << 8) | ((sbyte)data[index * 3 + 2] << 16))) / 8388608f,
            (_, 32) => BinaryPrimitives.ReadInt32LittleEndian(data[(index * 4)..]) / 2147483648f,
            _ => 0,
        };
    }

    // Linear interpolation; fine for short effects, which is what gets ported
    private static float[] Resample(float[] samples, int channels, int fromRate, int toRate)
    {
        int frames = samples.Length / channels;
        if (frames == 0)
            return [];

        double step = (double)fromRate / toRate;
        int newFrames = Math.Max((int)Math.Round(frames / step), 1);
        var result = new float[newFrames * channels];
        for (int i = 0; i < newFrames; i++)
        {
            double position = i * step;
            int first = Math.Min((int)position, frames - 1);
            int second = Math.Min(first + 1, frames - 1);
            float fraction = (float)(position - first);
            for (int channel = 0; channel < channels; channel++)
            {
                float a = samples[first * channels + channel];
                float b = samples[second * channels + channel];
                result[i * channels + channel] = a + (b - a) * fraction;
            }
        }

        return result;
    }
}
