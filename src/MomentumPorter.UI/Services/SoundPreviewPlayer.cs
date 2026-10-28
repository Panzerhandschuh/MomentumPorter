namespace MomentumPorter.UI.Services;

using System.Globalization;
using System.Runtime.InteropServices;
using MomentumPorter.Assets.Common;

/// <summary>
/// Plays a sound from memory for the Player Sounds preview and the Sounds review, one at a time, a new one cutting off
/// the last. <see cref="Play"/> goes through Windows' winmm, which plays PCM WAVs and the compressed ones its codecs
/// know (e.g. ADPCM); <see cref="PlayFile"/> through MCI from a temporary copy, which also plays MP3s and follows
/// <see cref="Volume"/>, even mid-sound. Elsewhere nothing plays.
/// </summary>
public static class SoundPreviewPlayer
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    private const string Alias = "momentumporterpreview";

    private static readonly Lock Gate = new();

    // winmm reads an async sound from this memory while it plays, so it stays pinned until the next one replaces it
    private static GCHandle _playing;

    // The file MCI has open, deleted once it's closed
    private static string? _file;

    private static double _volume = 1;

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>How loud <see cref="PlayFile"/> plays, from 0 to 1. Changing it changes the sound that's playing.</summary>
    public static double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (!IsSupported)
                return;

            lock (Gate)
            {
                if (_file is not null)
                    SetVolumeLocked();
            }
        }
    }

    /// <summary>
    /// Starts playing a WAV's bytes. Returns why it can't be played, or null when it started.
    /// </summary>
    public static string? Play(byte[] bytes)
    {
        if (!IsSupported)
            return "Previewing sounds only works on Windows";

        bytes = ToPlayableWav(bytes);
        lock (Gate)
        {
            StopLocked();
            _playing = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            if (PlaySound(_playing.AddrOfPinnedObject(), IntPtr.Zero, SndMemory | SndAsync | SndNoDefault))
                return null;

            _playing.Free();
            return "Windows can't play this sound (it may be a WAV codec that isn't installed); Momentum plays it fine";
        }
    }

    /// <summary>
    /// Starts playing a WAV or MP3 at <see cref="Volume"/>. Returns why it can't be played, or null when it started.
    /// </summary>
    /// <param name="fileName">The file's name, whose extension says what it is</param>
    public static string? PlayFile(byte[] bytes, string fileName)
    {
        if (!IsSupported)
            return "Previewing sounds only works on Windows";

        bool mp3 = fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
        if (!mp3)
            bytes = ToPlayableWav(bytes);

        lock (Gate)
        {
            StopLocked();
            try
            {
                string folder = Path.Combine(Path.GetTempPath(), "MomentumPorter");
                Directory.CreateDirectory(folder);
                _file = Path.Combine(folder, $"preview_{Guid.NewGuid():N}{(mp3 ? ".mp3" : ".wav")}");
                File.WriteAllBytes(_file, bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _file = null;
                return $"Couldn't write the sound to play it: {ex.Message}";
            }

            // mpegvideo is DirectShow, which plays both and, unlike waveaudio, has a volume
            if (MciSendString($"open \"{_file}\" type mpegvideo alias {Alias}", null, 0, IntPtr.Zero) == 0)
            {
                SetVolumeLocked();
                if (MciSendString($"play {Alias}", null, 0, IntPtr.Zero) == 0)
                    return null;
            }

            StopLocked();
            return "Windows can't play this sound; Momentum plays it fine";
        }
    }

    public static void Stop()
    {
        if (!IsSupported)
            return;

        lock (Gate)
            StopLocked();
    }

    // Float or odd-rate WAVs are made into 16-bit PCM first, the same conversion the pack makes for Source
    private static byte[] ToPlayableWav(byte[] bytes)
    {
        return WavFile.TryParse(bytes, out WavFile? wav, out _) && !wav.IsSourceCompatible ? wav.ToSourceCompatible().ToBytes() : bytes;
    }

    // MCI's volume is 0 to 1000
    private static void SetVolumeLocked()
    {
        int volume = (int)Math.Round(_volume * 1000);
        MciSendString(string.Create(CultureInfo.InvariantCulture, $"setaudio {Alias} volume to {volume}"), null, 0, IntPtr.Zero);
    }

    private static void StopLocked()
    {
        PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
        if (_playing.IsAllocated)
            _playing.Free();

        if (_file is not null)
        {
            MciSendString($"close {Alias}", null, 0, IntPtr.Zero);
            try
            {
                File.Delete(_file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left in the temp folder
            }

            _file = null;
        }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
    private static extern int MciSendString(string command, System.Text.StringBuilder? returnValue, int returnLength, IntPtr callback);
}
