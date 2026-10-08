namespace MomentumPorter.Assets.Weapons;

using MomentumPorter.Assets.Models;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;

/// <summary>
/// What a weapon pack job works on; steps fill it in as they go. Dispose it (or let the last step) to close the
/// source game's files.
/// </summary>
public sealed class WeaponPackState(WeaponPackOptions options) : IDisposable
{
    public WeaponPackOptions Options { get; } = options;

    /// <summary>The catalog to use; <see cref="WeaponCatalog.Default"/> if not set before the job runs.</summary>
    public WeaponCatalog? Catalog { get; set; }

    /// <summary>Steam libraries to find games in; the local Steam install if not set.</summary>
    public SteamLocator? Steam { get; set; }

    public SourceGameInfo? GameInfo { get; set; }

    public CatalogGame? Game { get; set; }

    public CatalogWeapon? Weapon { get; set; }

    public WeaponSlot? Slot { get; set; }

    public MomentumInstall? Momentum { get; set; }

    public GameContent? Content { get; set; }

    public WeaponScript? Script { get; set; }

    /// <summary>Sound role -> the Momentum soundscript entry the slot plays for it (from its server script).</summary>
    public IReadOnlyDictionary<string, string> SlotSounds { get; set; } = new Dictionary<string, string>();

    public string PackName { get; set; } = "";

    /// <summary>The pack folder.</summary>
    public string PackDirectory { get; set; } = "";

    public IPackSink? Sink { get; set; }

    /// <summary>The models the script will use (Momentum game paths).</summary>
    public string? ViewModel { get; set; }

    public string? ViewSideModel { get; set; }

    public string? WorldModel { get; set; }

    public string? ProjectileModel { get; set; }

    /// <summary>Soundscript entries the pack defines, to be appended to its copy of game_sounds_momentum_weapons.txt.</summary>
    public List<KeyValuesNode> AddedSoundEntries { get; } = [];

    /// <summary>The weapon script's SoundData/Overrides.</summary>
    public Dictionary<string, string> SoundOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Momentum soundscript entries the pack redefines outright (keyed by the entry's name), for sounds that
    /// SoundData Overrides can't reach. See <see cref="WeaponSlot.ShotFromPlayer"/>.
    /// </summary>
    public Dictionary<string, KeyValuesNode> RedefinedSoundEntries { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A world model converted from a game Momentum doesn't mount, not yet in the pack: <see cref="FitWorldModelStep"/>
    /// adds it, fitted to Momentum's player or as it is.
    /// </summary>
    public ConvertedModel? ConvertedWorldModel { get; set; }

    /// <summary>The arms model Momentum should bone-merge onto the view model (ModelData "arms"), if any.</summary>
    public string? ArmsModel { get; set; }

    /// <summary>Whether the pack is written straight into Momentum's custom folder, where the game loads it.</summary>
    public bool PackIsInMomentumCustom =>
        Momentum is { } momentum &&
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(PackDirectory)!)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(momentum.GameDirectory, "custom"))),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>ParticleData entries for effects the pack makes (on top of the catalog's).</summary>
    public Dictionary<string, string> Particles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Particle files the pack's effects need that Momentum's manifest doesn't list ("!" precaches).</summary>
    public List<string> ParticleFiles { get; } = [];

    public void Dispose()
    {
        Content?.Dispose();
        Content = null;
    }
}

/// <summary>
/// Builds the job that ports a weapon into a Momentum weapon slot as a custom pack:
/// scripts/weapon_momentum_&lt;slot&gt;.txt, plus converted models, materials and sounds for games Momentum doesn't
/// mount, plus a copy of game_sounds_momentum_weapons.txt with any soundscript entries the pack needs.
/// </summary>
public static class WeaponPackBuilder
{
    /// <summary>
    /// The job and the state to add to its context with <see cref="PortJobContext.Set{T}"/> before running it.
    /// </summary>
    public static (PortJob Job, WeaponPackState State) CreateJob(WeaponPackOptions options)
    {
        var state = new WeaponPackState(options);
        var steps = new List<IPortStep>
        {
            new LoadWeaponStep(),
            new CheckMountedAssetsStep(),
            new ConvertModelsStep(),
            new FitWorldModelStep(),
            new Quake3EffectsStep(),
            new Quake3ViewModelStep(),
            new ResolveWeaponSoundsStep(),
            new WriteWeaponPackStep(),
        };

        return (new PortJob($"{options.Game}_{options.Weapon}", steps), state);
    }

    /// <summary>
    /// Runs the job with a fresh work folder (deleted afterwards).
    /// </summary>
    public static async Task<PortJobResult> RunAsync(WeaponPackOptions options, IPortLog log, Action<WeaponPackState>? configure = null, CancellationToken cancellation = default)
    {
        (PortJob job, WeaponPackState state) = CreateJob(options);
        configure?.Invoke(state);
        string work = PortJob.CreateWorkDirectory(job.Name);
        try
        {
            var context = new PortJobContext { Log = log, WorkDirectory = work, Cancellation = cancellation };
            context.Set(state);
            return await job.RunAsync(context).ConfigureAwait(false);
        }
        finally
        {
            state.Dispose();
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // Leave it for the OS to clean up
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// The default pack name: <c>&lt;game&gt;_&lt;weapon&gt;_&lt;slot&gt;</c>.
    /// </summary>
    /// <summary>
    /// The folder every weapon pack goes into unless named otherwise. Packs in one folder merge the files they'd
    /// otherwise each replace (the weapons soundscript, the particle manifest), which packs in separate folders can't:
    /// Momentum takes one folder's copy and the others' are hidden.
    /// </summary>
    public const string SharedPackName = "momentumporter";

    /// <summary>A weapon's id inside a pack folder: its sidecar files are named after it.</summary>
    public static string WeaponPackId(string game, string weapon, string slot) => $"{game}_{weapon}_{slot}".ToLowerInvariant();
}
