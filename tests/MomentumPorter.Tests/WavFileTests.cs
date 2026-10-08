namespace MomentumPorter.Tests;

using System.Buffers.Binary;
using System.Text;
using MomentumPorter.Assets.Common;
using Xunit;

public class WavFileTests
{
    /// <summary>Writes a WAV the way other tools do, optionally with a LIST chunk before the data.</summary>
    internal static byte[] MakeWav(int format, int channels, int rate, int bits, byte[] data, bool listChunk = false, bool extensible = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        int fmtSize = extensible ? 40 : 16;
        byte[] list = listChunk ? "INFOISFT\u0005\0\0\0test\0\0"u8.ToArray() : [];
        writer.Write("RIFF"u8);
        writer.Write(4 + 8 + fmtSize + (listChunk ? 8 + list.Length : 0) + 8 + data.Length + (data.Length & 1));
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(fmtSize);
        writer.Write((short)(extensible ? WavFile.FormatExtensible : format));
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8));
        writer.Write((short)bits);
        if (extensible)
        {
            writer.Write((short)22);
            writer.Write((short)bits);
            writer.Write(0);
            writer.Write((short)format);
            writer.Write(new byte[14]);
        }

        if (listChunk)
        {
            writer.Write("LIST"u8);
            writer.Write(list.Length);
            writer.Write(list);
        }

        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
        if ((data.Length & 1) != 0)
            writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    internal static byte[] Sine16(int frames, int channels, int rate)
    {
        var data = new byte[frames * channels * 2];
        for (int i = 0; i < frames; i++)
        {
            short value = (short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 16000);
            for (int c = 0; c < channels; c++)
                BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan((i * channels + c) * 2), value);
        }

        return data;
    }

    [Fact]
    public void ReadsQuake3StyleWav()
    {
        byte[] bytes = MakeWav(WavFile.FormatPcm, 1, 22050, 16, Sine16(2205, 1, 22050));
        WavFile wav = WavFile.Parse(bytes);

        Assert.Equal(1, wav.Channels);
        Assert.Equal(22050, wav.SampleRate);
        Assert.Equal(16, wav.BitsPerSample);
        Assert.Equal(2205, wav.FrameCount);
        Assert.Equal(0.1, wav.Duration.TotalSeconds, 3);
        Assert.True(wav.IsSourceCompatible);
        Assert.Same(wav, wav.ToSourceCompatible());
        // A canonical file writes back byte for byte
        Assert.Equal(bytes, wav.ToBytes());
    }

    [Fact]
    public void Keeps8BitAt11k()
    {
        WavFile wav = WavFile.Parse(MakeWav(WavFile.FormatPcm, 1, 11025, 8, Enumerable.Repeat((byte)128, 100).ToArray(), listChunk: true));
        Assert.True(wav.HasExtraChunks);
        Assert.True(wav.IsSourceCompatible);
    }

    [Fact]
    public void ResamplesOddRatesUpToTheNextSourceRate()
    {
        WavFile wav = WavFile.Parse(MakeWav(WavFile.FormatPcm, 2, 16000, 16, Sine16(1600, 2, 16000)));
        Assert.False(wav.IsSourceCompatible);

        WavFile converted = WavFile.Parse(wav.ToSourceCompatible().ToBytes());
        Assert.True(converted.IsSourceCompatible);
        Assert.Equal(22050, converted.SampleRate);
        Assert.Equal(2, converted.Channels);
        Assert.Equal(16, converted.BitsPerSample);
        Assert.Equal(wav.Duration.TotalSeconds, converted.Duration.TotalSeconds, 2);
    }

    [Fact]
    public void ResamplesHighRatesDownTo44k()
    {
        WavFile converted = WavFile.Parse(MakeWav(WavFile.FormatPcm, 1, 48000, 16, Sine16(4800, 1, 48000))).ToSourceCompatible();
        Assert.Equal(44100, converted.SampleRate);
        Assert.Equal(4410, converted.FrameCount);
    }

    [Fact]
    public void ConvertsOtherSampleFormatsTo16Bit()
    {
        // 24-bit, full scale positive then negative
        byte[] data24 = [0xFF, 0xFF, 0x7F, 0x00, 0x00, 0x80];
        WavFile from24 = WavFile.Parse(MakeWav(WavFile.FormatPcm, 1, 44100, 24, data24)).ToSourceCompatible();
        Assert.Equal(16, from24.BitsPerSample);
        Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(from24.Data));
        Assert.Equal(short.MinValue + 1, BinaryPrimitives.ReadInt16LittleEndian(from24.Data.AsSpan(2)));

        var floats = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(floats, 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(floats.AsSpan(4), -0.5f);
        WavFile fromFloat = WavFile.Parse(MakeWav(WavFile.FormatIeeeFloat, 1, 22050, 32, floats, extensible: true)).ToSourceCompatible();
        Assert.Equal(WavFile.FormatPcm, fromFloat.Format);
        Assert.InRange(BinaryPrimitives.ReadInt16LittleEndian(fromFloat.Data), (short)16383, (short)16384);
        Assert.Equal(22050, fromFloat.SampleRate);
    }

    [Fact]
    public void MixesSurroundToMono()
    {
        WavFile converted = WavFile.Parse(MakeWav(WavFile.FormatPcm, 6, 44100, 16, Sine16(100, 6, 44100))).ToSourceCompatible();
        Assert.Equal(1, converted.Channels);
        Assert.Equal(100, converted.FrameCount);
    }

    [Fact]
    public void RejectsWhatItCantRead()
    {
        Assert.False(WavFile.TryParse("OggS"u8.ToArray(), out _, out _));
        Assert.False(WavFile.TryParse(MakeWav(2, 1, 22050, 4, new byte[10]), out _, out string? error));
        Assert.Contains("unsupported", error);
    }

    [Fact]
    public void SilentWavIsValidAndSilent()
    {
        WavFile silent = WavFile.Parse(SilentWav.Bytes);
        Assert.True(silent.IsSourceCompatible);
        Assert.True(silent.FrameCount > 0);
        Assert.All(silent.Data, x => Assert.Equal(0, x));
    }
}
