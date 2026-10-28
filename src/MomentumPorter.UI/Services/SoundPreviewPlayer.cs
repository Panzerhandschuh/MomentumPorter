namespace MomentumPorter.UI.Services;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using MomentumPorter.Assets.Common;

/// <summary>
/// Plays a sound from memory for the Player Sounds preview and the Sounds review, one at a time, a new one cutting off
/// the last. <see cref="Play"/> goes through Windows' winmm, which plays PCM WAVs and the compressed ones its codecs
/// know (e.g. ADPCM); <see cref="Files"/> through MCI from a temporary copy, which also plays MP3s, has a volume and
/// can seek. Elsewhere nothing plays.
/// </summary>
public static class SoundPreviewPlayer
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    private const string Alias = "momentumporterpreview";

    private const string NotWindows = "Previewing sounds only works on Windows";

    private static readonly Lock Gate = new();

    // winmm reads an async sound from this memory while it plays, so it stays pinned until the next one replaces it
    private static GCHandle _playing;

    // The file MCI has open, deleted once it's closed
    private static string? _file;

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>WAVs and MP3s with a volume and a timeline, for the Sounds review.</summary>
    public static ISoundPreview Files { get; } = new FilePreview();

    /// <summary>
    /// Starts playing a WAV's bytes. Returns why it can't be played, or null when it started.
    /// </summary>
    public static string? Play(byte[] bytes)
    {
        if (!IsSupported)
            return NotWindows;

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

    private static int Mci(string command) => MciSendString(command, null, 0, IntPtr.Zero);

    // One of MCI's "status" items for the open file, e.g. "mode" or "position", or null when it can't say
    private static string? StatusLocked(string item)
    {
        var text = new StringBuilder(32);
        return MciSendString($"status {Alias} {item}", text, text.Capacity, IntPtr.Zero) == 0 ? text.ToString() : null;
    }

    private static long? MillisecondsLocked(string item)
    {
        return long.TryParse(StatusLocked(item), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
    private static extern int MciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    // MCI's mpegvideo device is DirectShow, which plays both WAVs and MP3s and, unlike waveaudio, has a volume
    private sealed class FilePreview : ISoundPreview
    {
        private double _volume = 1;

        public double Volume
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

        public string? Play(byte[] bytes, string fileName)
        {
            if (!IsSupported)
                return NotWindows;

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

                if (Mci($"open \"{_file}\" type mpegvideo alias {Alias}") == 0)
                {
                    Mci($"set {Alias} time format milliseconds");
                    SetVolumeLocked();
                    if (Mci($"play {Alias}") == 0)
                        return null;
                }

                StopLocked();
                return "Windows can't play this sound; Momentum plays it fine";
            }
        }

        public PreviewPosition? Position()
        {
            if (!IsSupported)
                return null;

            lock (Gate)
            {
                if (_file is null || MillisecondsLocked("length") is not { } length || MillisecondsLocked("position") is not { } position)
                    return null;

                // The mode goes to "stopped" at the end of the sound
                bool finished = StatusLocked("mode") == "stopped";
                return new PreviewPosition(TimeSpan.FromMilliseconds(position), TimeSpan.FromMilliseconds(length), finished);
            }
        }

        public void Seek(TimeSpan position)
        {
            if (!IsSupported)
                return;

            lock (Gate)
            {
                // MCI refuses a position past the end
                if (_file is null || MillisecondsLocked("length") is not { } length)
                    return;

                long from = Math.Clamp((long)position.TotalMilliseconds, 0, Math.Max(0, length - 1));
                Mci(string.Create(CultureInfo.InvariantCulture, $"play {Alias} from {from}"));
            }
        }

        public void Stop() => SoundPreviewPlayer.Stop();

        // MCI's volume is 0 to 1000
        private void SetVolumeLocked()
        {
            int volume = (int)Math.Round(_volume * 1000);
            Mci(string.Create(CultureInfo.InvariantCulture, $"setaudio {Alias} volume to {volume}"));
        }
    }
}
