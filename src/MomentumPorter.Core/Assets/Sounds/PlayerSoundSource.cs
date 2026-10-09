namespace MomentumPorter.Assets.Sounds;

using MomentumPorter.Assets.Vpk;

/// <summary>
/// A sound file of the source game and where the pack puts it.
/// </summary>
/// <param name="Source">Its path in the game's files, e.g. <c>sound/player/footsteps/concrete1.wav</c></param>
/// <param name="Wave">Where the pack's soundscript plays it from, relative to sound/</param>
public sealed record GameSoundFile(string Source, string Wave);

/// <summary>
/// The source game's sound for one of Momentum's movement sounds: its files (one is picked at random each time) and how
/// the game plays them.
/// </summary>
/// <param name="Label">What it is, for the report and log (e.g. <c>clank</c>, <c>CT_Concrete.StepLeft</c>)</param>
/// <param name="Pitch">The game's pitch for it, or null for the default</param>
/// <param name="SoundLevel">The game's sound level for it, or null to keep the entry's</param>
public sealed record GameSound(string Label, IReadOnlyList<GameSoundFile> Files, string? Pitch = null, string? SoundLevel = null);

/// <summary>
/// Another game's player movement sounds: what it plays for Momentum's jump, landings and footsteps, and the files.
/// </summary>
public abstract class PlayerSoundSource : IDisposable
{
    public abstract SourceGameInfo Game { get; }

    /// <summary>The folder or archive the sounds come from.</summary>
    public abstract string Path { get; }

    /// <summary>
    /// Who the game has separate sounds for: Quake 3's characters, CS:GO's teams. Lower case; empty when the game
    /// has one set.
    /// </summary>
    public virtual IReadOnlyList<string> Characters => [];

    /// <summary>What a character is called in this game (character, team), for messages.</summary>
    public virtual string CharacterKind => "character";

    /// <summary>The character used when none is given, or null if the game has none.</summary>
    public virtual string? DefaultCharacter => null;

    /// <summary>The jump sound, or null if the game has none (it plays the footstep, as Momentum does).</summary>
    public abstract GameSound? GetJump(string? character);

    /// <summary>The landing sounds, or null if the game has none.</summary>
    public abstract (GameSound? Soft, GameSound? Hard) GetLand(string? character);

    /// <summary>
    /// The sounds to try for one of Momentum's footstep entries (e.g. <c>SolidMetal.StepLeft</c>), best first. The
    /// first whose files can be read is used.
    /// </summary>
    public abstract IEnumerable<GameSound> GetFootsteps(string entryName, string? character, PlayerSoundPackOptions options);

    /// <summary>A line about the footsteps to log before they're set, or null.</summary>
    public virtual string? DescribeFootsteps(string? character, PlayerSoundPackOptions options) => null;

    /// <summary>
    /// A sound file's bytes, or null with why when the game doesn't have it in a form Momentum can play (null
    /// <paramref name="problem"/> just means it's missing).
    /// </summary>
    public abstract byte[]? ReadSound(string path, out string? problem);

    public abstract void Dispose();
}
