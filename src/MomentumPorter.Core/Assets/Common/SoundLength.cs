namespace MomentumPorter.Assets.Common;

using System.Buffers.Binary;

/// <summary>
/// How long a WAV or MP3 plays, read from the start of the file (its headers) without decoding it.
/// </summary>
public static class SoundLength
{
    // How much of an MP3 (after its ID3 tag) is searched for the first frame
    private const int Mp3SearchBytes = 64 * 1024;

    private static readonly int[] Mpeg1Layer3Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Layer3Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];

    /// <summary>
    /// The length of a sound file, or null when it isn't a WAV or MP3 or its headers can't be read.
    /// </summary>
    /// <param name="stream">The file from its start; read forward only, so a zip entry's stream will do</param>
    /// <param name="size">The file's size in bytes, for constant bitrate MP3s</param>
    public static TimeSpan? Read(Stream stream, long size)
    {
        byte[] start = ReadUpTo(stream, 12);
        if (start.Length >= 12 && start.AsSpan(0, 4).SequenceEqual("RIFF"u8) && start.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            return ReadWav(stream);

        return ReadMp3(stream, start, size);
    }

    // The data chunk's size over the fmt chunk's bytes per second, which holds for PCM and ADPCM alike
    private static TimeSpan? ReadWav(Stream stream)
    {
        int bytesPerSecond = 0;
        while (true)
        {
            byte[] header = ReadUpTo(stream, 8);
            if (header.Length < 8)
                return null;

            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            if (header.AsSpan(0, 4).SequenceEqual("data"u8))
                return bytesPerSecond > 0 ? TimeSpan.FromSeconds((double)length / bytesPerSecond) : null;

            if (header.AsSpan(0, 4).SequenceEqual("fmt "u8))
            {
                byte[] format = ReadUpTo(stream, (int)Math.Min(length, 64));
                if (format.Length < 12)
                    return null;

                bytesPerSecond = BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8));
                Skip(stream, length - format.Length + (length & 1));
            }
            else if (!Skip(stream, length + (length & 1)))
            {
                return null;
            }
        }
    }

    private static TimeSpan? ReadMp3(Stream stream, byte[] start, long size)
    {
        long offset = 0;
        byte[] head = start;
        // An ID3v2 tag (often with cover art) comes before the first frame; its size is "syncsafe", 7 bits a byte
        if (start.Length >= 10 && start.AsSpan(0, 3).SequenceEqual("ID3"u8))
        {
            long tag = 10 + (start[6] << 21 | start[7] << 14 | start[8] << 7 | start[9]);
            if (!Skip(stream, tag - start.Length))
                return null;

            offset = tag;
            head = [];
        }

        head = [.. head, .. ReadUpTo(stream, Mp3SearchBytes)];
        for (int i = 0; i + 4 <= head.Length; i++)
        {
            if (TryReadFrame(head, i, size - offset - i) is { } length)
                return length;
        }

        return null;
    }

    // An MPEG layer III frame header at i: the length from its Xing/Info or VBRI header if it has one (variable
    // bitrate), else from its bitrate over the rest of the file
    private static TimeSpan? TryReadFrame(byte[] head, int i, long remaining)
    {
        if (head[i] != 0xFF || (head[i + 1] & 0xE0) != 0xE0)
            return null;

        int version = (head[i + 1] >> 3) & 3; // 3 MPEG-1, 2 MPEG-2, 0 MPEG-2.5
        int layer = (head[i + 1] >> 1) & 3; // 1 layer III
        int bitrateIndex = head[i + 2] >> 4;
        int rateIndex = (head[i + 2] >> 2) & 3;
        if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || rateIndex == 3)
            return null;

        int[] rates = version == 3 ? [44100, 48000, 32000] : version == 2 ? [22050, 24000, 16000] : [11025, 12000, 8000];
        int sampleRate = rates[rateIndex];
        int samplesPerFrame = version == 3 ? 1152 : 576;
        int bitrate = (version == 3 ? Mpeg1Layer3Bitrates : Mpeg2Layer3Bitrates)[bitrateIndex] * 1000;

        bool mono = (head[i + 3] >> 6) == 3;
        int sideInfo = version == 3 ? (mono ? 17 : 32) : (mono ? 9 : 17);
        int xing = i + 4 + sideInfo;
        if (xing + 12 <= head.Length && (head.AsSpan(xing, 4).SequenceEqual("Xing"u8) || head.AsSpan(xing, 4).SequenceEqual("Info"u8)))
        {
            // Flag 1: the frame count follows the flags
            if ((head[xing + 7] & 1) != 0)
                return TimeSpan.FromSeconds((double)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(xing + 8)) * samplesPerFrame / sampleRate);
        }

        int vbri = i + 4 + 32;
        if (vbri + 18 <= head.Length && head.AsSpan(vbri, 4).SequenceEqual("VBRI"u8))
            return TimeSpan.FromSeconds((double)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(vbri + 14)) * samplesPerFrame / sampleRate);

        return remaining > 0 ? TimeSpan.FromSeconds(remaining * 8.0 / bitrate) : null;
    }

    private static byte[] ReadUpTo(Stream stream, int count)
    {
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n == 0)
                break;
            read += n;
        }

        return read == count ? buffer : buffer[..read];
    }

    private static bool Skip(Stream stream, long count)
    {
        if (count <= 0)
            return true;

        if (stream.CanSeek)
        {
            if (stream.Position + count > stream.Length)
                return false;

            stream.Seek(count, SeekOrigin.Current);
            return true;
        }

        var buffer = new byte[81920];
        while (count > 0)
        {
            int n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (n == 0)
                return false;
            count -= n;
        }

        return true;
    }
}
