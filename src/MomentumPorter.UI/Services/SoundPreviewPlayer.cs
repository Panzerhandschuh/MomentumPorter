namespace MomentumPorter.UI.Services;

using System.Runtime.InteropServices;
using MomentumPorter.Assets.Common;

/// <summary>
/// Plays a WAV from memory for the Player Sounds preview. Windows' winmm plays PCM and the compressed WAVs its codecs
/// know (e.g. ADPCM); one sound at a time, a new one cutting off the last. Elsewhere nothing plays.
/// </summary>
public static class SoundPreviewPlayer
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    private static readonly Lock Gate = new();

    // winmm reads an async sound from this memory while it plays, so it stays pinned until the next one replaces it
    private static GCHandle _playing;

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Starts playing a sound file's bytes. Returns why it can't be played, or null when it started.
    /// </summary>
    public static string? Play(byte[] bytes)
    {
        if (!IsSupported)
            return "Previewing sounds only works on Windows";

        // Float or odd-rate WAVs are made into 16-bit PCM first, the same conversion the pack makes for Source
        if (WavFile.TryParse(bytes, out WavFile? wav, out _) && !wav.IsSourceCompatible)
            bytes = wav.ToSourceCompatible().ToBytes();

        lock (Gate)
        {
            StopLocked();
            _playing = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            if (PlaySound(_playing.AddrOfPinnedObject(), IntPtr.Zero, SndMemory | SndAsync | SndNoDefault))
                return null;

            _playing.Free();
            return "Windows can't play this sound (it may be an MP3 or a WAV codec that isn't installed); Momentum plays it fine";
        }
    }

    public static void Stop()
    {
        if (!IsSupported)
            return;

        lock (Gate)
            StopLocked();
    }

    private static void StopLocked()
    {
        PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
        if (_playing.IsAllocated)
            _playing.Free();
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
}
