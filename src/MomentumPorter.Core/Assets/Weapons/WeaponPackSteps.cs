namespace MomentumPorter.Assets.Weapons;

using System.Text;
using MomentumPorter.Assets.Common;
using MomentumPorter.Assets.Models;
using MomentumPorter.Assets.Particles;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;

/// <summary>
/// Where a weapon pack was written and what's in it.
/// </summary>
public sealed record WeaponPackOutput(string PackDirectory, IReadOnlyList<string> Files);

/// <summary>
/// Finds the weapon, the slot, Momentum and the source game, and loads the slot's base script.
/// </summary>
public sealed class LoadWeaponStep : IPortStep
{
    public string Name => "Load weapon";

    public Task RunAsync(PortJobContext context)
    {
        WeaponPackState state = context.Get<WeaponPackState>();
        WeaponPackOptions options = state.Options;
        WeaponCatalog catalog = state.Catalog ??= WeaponCatalog.Default;

        CatalogGame game = catalog.FindGame(options.Game)
            ?? throw new PortStepException($"Unknown game \"{options.Game}\". Known games: {string.Join(", ", catalog.Games.Select(x => x.Id))}");
        CatalogWeapon weapon = game.FindWeapon(options.Weapon)
            ?? throw new PortStepException($"{game.Name} has no weapon \"{options.Weapon}\" in the catalog. Known weapons: {string.Join(", ", game.Weapons.Select(x => x.Id))}");
        string slotId = string.IsNullOrWhiteSpace(options.Slot) ? weapon.Slots[0] : options.Slot;
        WeaponSlot slot = WeaponSlots.Find(slotId)
            ?? throw new PortStepException($"Unknown Momentum weapon \"{slotId}\". Known: {string.Join(", ", WeaponSlots.All.Select(x => x.Id))}");
        if (!weapon.Slots.Contains(slot.Id, StringComparer.OrdinalIgnoreCase))
            context.Report.Warn($"{weapon.Name} isn't one of the catalog's fits for the {slot.Name} ({string.Join(", ", weapon.Slots)}); its sounds and projectile may not match");

        IReadOnlyList<GameMode> modes;
        try
        {
            modes = GameModes.Parse(options.GameModes);
        }
        catch (FormatException ex)
        {
            throw new PortStepException(ex.Message, ex);
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new PortStepException("No output folder given");

        SourceGameInfo info = SourceGames.Find(game.Id) ?? throw new PortStepException($"The porter can't open {game.Name}'s files");
        SteamLocator steam = state.Steam ??= new SteamLocator();
        MomentumInstall momentum;
        try
        {
            momentum = options.MomentumDirectory is { Length: > 0 } dir
                ? new MomentumInstall(dir)
                : MomentumInstall.Find(steam) ?? throw new PortStepException("Momentum Mod isn't installed through Steam; give its install folder");
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new PortStepException(ex.Message, ex);
        }

        try
        {
            state.Content = SourceGames.Open(info, steam, momentum.InstallDirectory, string.IsNullOrWhiteSpace(options.GameDirectory) ? null : options.GameDirectory);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new PortStepException(ex.Message, ex);
        }

        try
        {
            state.Script = WeaponScript.Parse(momentum.ReadRequiredText(slot.ScriptPath), slot.ScriptPath);
            state.SlotSounds = momentum.ReadText(slot.ServerScriptPath) is { } server
                ? WeaponScript.ReadSoundEntries(server, slot.ServerScriptPath)
                : new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is FileNotFoundException or FormatException)
        {
            throw new PortStepException(ex.Message, ex);
        }

        if (state.SlotSounds.Count == 0)
            context.Report.Warn($"Momentum's {slot.ServerScriptPath} is missing or has no sounds, so the {slot.Name}'s sounds can't be replaced");

        state.GameInfo = info;
        state.Game = game;
        state.Weapon = weapon;
        state.Slot = slot;
        state.GameModes = modes;
        state.Momentum = momentum;
        state.PackName = SanitizePackName(string.IsNullOrWhiteSpace(options.PackName) ? WeaponPackBuilder.SharedPackName : options.PackName);
        state.PackDirectory = Path.GetFullPath(Path.Combine(options.OutputDirectory, state.PackName));
        state.Sink ??= new FolderPackSink(state.PackDirectory);

        context.Log.Info($"Porting {weapon.Name} from {game.Name} ({string.Join(", ", state.Content.Sources.Select(x => x.Description))}) to the {slot.Name} in {GameModes.Describe(modes)}, pack {state.PackDirectory}");
        foreach (string limitation in game.Limitations.Concat(weapon.Notes))
            context.Report.Warn(limitation);

        return Task.CompletedTask;
    }

    private static string SanitizePackName(string name)
    {
        string sanitized = new string(name.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_').ToArray()).Trim('.');
        return sanitized.Length > 0 ? sanitized : "weapon_pack";
    }
}

/// <summary>
/// For games Momentum mounts: checks the models exist in the VPKs Momentum mounts, and uses them as they are.
/// </summary>
public sealed class CheckMountedAssetsStep : IPortStep
{
    public string Name => "Check models";

    public Task RunAsync(PortJobContext context)
    {
        WeaponPackState state = context.Get<WeaponPackState>();
        if (state.GameInfo is not { MountedByMomentum: true })
            return Task.CompletedTask;

        GameContent content = state.Content!;
        CatalogWeapon weapon = state.Weapon!;

        if (!content.Exists(weapon.View))
            throw new PortStepException($"{weapon.View} isn't in {content.Name}'s VPKs ({string.Join(", ", content.Sources.Select(x => x.Description))})");

        if (!content.Exists(Path.ChangeExtension(weapon.View, ".vvd")))
            context.Report.Warn($"{weapon.View} has no .vvd next to it in {content.Name}'s VPKs; it may not load");

        state.ViewModel = weapon.View;
        state.ViewSideModel = Check(weapon.ViewSide, "side view model") ?? weapon.View;
        state.WorldModel = Check(weapon.World, "world model");
        state.ProjectileModel = state.Options.Projectile ? Check(weapon.Projectile, "projectile model") : null;
        if (state.Options.Arms == WeaponArms.Auto && state.Game!.ArmsModel is { Length: > 0 } arms)
            state.ArmsModel = Check(arms, "arms model");

        context.Log.Info($"Found {string.Join(", ", weapon.Models.Concat(state.ArmsModel is null ? [] : [state.ArmsModel]))} in {content.Name}");

        // Momentum mounts CS:S, CS:GO, TF2 and HL2 together, and a path two of them ship (CS:S's and CS:GO's
        // models/weapons/v_pist_deagle.mdl) is one game's file for every weapon script that names it. A model another
        // mounted game ships too is copied into the pack under its own path; the pack's folder is mounted first.
        using var copier = new MountedModelCopier(state);
        bool sideIsView = state.ViewSideModel == state.ViewModel;
        state.ViewModel = copier.Resolve(context, state, state.ViewModel);
        state.ViewSideModel = sideIsView ? state.ViewModel : copier.Resolve(context, state, state.ViewSideModel);
        state.ProjectileModel = copier.Resolve(context, state, state.ProjectileModel);
        state.ArmsModel = copier.Resolve(context, state, state.ArmsModel);
        if (!state.Options.FitWorldModel)
        {
            // Fitted world models are recompiled under the pack's own path anyway (FitWorldModelStep)
            state.WorldModel = copier.Resolve(context, state, state.WorldModel);
        }

        // The game's own particle files (CS:GO's muzzle flashes, TF2's explosions): the same goes for them, since
        // Momentum's manifest names them by path too (its HL2 content ships a particles/explosion.pcf that hides TF2's)
        if (state.Options.Particles)
        {
            foreach (string file in state.Game!.ParticleFiles)
            {
                if (!content.Exists(file))
                    context.Report.Warn($"{file} isn't in {content.Name}'s files, so the effects in it aren't drawn");
                else if (copier.ResolveParticleFile(context, state, file) is { } manifestPath)
                    state.ParticleFiles.Add(manifestPath);
            }
        }

        return Task.CompletedTask;

        string? Check(string? model, string what)
        {
            if (string.IsNullOrEmpty(model))
                return null;

            if (content.Exists(model))
                return model;

            context.Report.Warn($"The {what} {model} isn't in {content.Name}'s VPKs; Momentum's is kept");
            return null;
        }
    }
}

/// <summary>
/// Copies a mounted game's model into the pack when another game Momentum mounts ships the same path. See
/// <see cref="CheckMountedAssetsStep"/>.
/// </summary>
file sealed class MountedModelCopier : IDisposable
{
    // A compiled model's files, next to its .mdl
    private static readonly string[] Companions = [".mdl", ".vvd", ".dx90.vtx", ".dx80.vtx", ".sw.vtx", ".phy", ".ani"];

    private readonly List<GameContent> _others = [];

    public MountedModelCopier(WeaponPackState state)
    {
        foreach (SourceGameInfo other in SourceGames.All.Where(x => x.MountedByMomentum && !x.Id.Equals(state.GameInfo!.Id, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                _others.Add(SourceGames.Open(other, state.Steam!, state.Momentum!.InstallDirectory));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Not installed, or unreadable: it can't be mounted either
            }
        }
    }

    /// <summary>The model's path in the pack when another mounted game ships it too, else the model as it is.</summary>
    public string? Resolve(PortJobContext context, WeaponPackState state, string? model)
    {
        if (model is null)
            return null;

        GameContent? other = _others.FirstOrDefault(x => x.Exists(model));
        if (other is null)
            return model;

        GameContent content = state.Content!;
        string normalized = ContentPath.Normalize(model).ToLowerInvariant();
        string bare = Path.ChangeExtension(normalized, null);
        string target = $"models/porter/{state.Game!.Id}/{(bare.StartsWith("models/", StringComparison.Ordinal) ? bare["models/".Length..] : bare)}";
        byte[]? mdl = null;
        foreach (string extension in Companions)
        {
            if (content.Read(bare + extension) is not { } data)
                continue;

            state.Sink!.AddFile(target + extension, data);
            if (extension == ".mdl")
                mdl = data;
        }

        if (mdl is null)
        {
            context.Report.Warn($"{other.Name} ships {model} too and Momentum mounts both, but {content.Name}'s copy couldn't be read, so the game's own choice between them is used");
            return model;
        }

        // The materials stay at their own paths; they're a problem only when the other game ships those too
        var sharedTextures = new List<string>();
        try
        {
            StudioModelHeader header = StudioModelHeader.Read(mdl);
            foreach (string directory in header.TextureDirectories)
            {
                foreach (string texture in header.Textures)
                {
                    string material = ContentPath.Normalize("materials/" + directory.Trim('/', '\\') + "/" + texture.Trim('/', '\\') + ".vmt");
                    if (other.Exists(material))
                        sharedTextures.Add(material);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            context.Log.Info($"Couldn't read {model}'s materials to check them against {other.Name}: {ex.Message}");
        }

        context.Report.Change($"{Path.GetFileName(model)} copied into the pack: {other.Name} ships it too");
        context.Log.Info($"{other.Name} ships {model} too, and Momentum mounts both, so {content.Name}'s copy is in the pack as {target}.mdl");
        if (sharedTextures.Count > 0)
            context.Report.Warn($"{other.Name} also ships {model}'s materials ({string.Join(", ", sharedTextures.Take(3))}{(sharedTextures.Count > 3 ? ", ..." : "")}), so the model may show {other.Name}'s textures");

        return target + ".mdl";
    }

    /// <summary>
    /// The path the manifest should list for a game's particle file: the file itself, or the pack's copy under its
    /// own name when another mounted game ships that path (the manifest loads the first copy found, and Momentum's
    /// own manifest may list the path already, for the other game's file).
    /// </summary>
    public string? ResolveParticleFile(PortJobContext context, WeaponPackState state, string file)
    {
        GameContent content = state.Content!;
        GameContent? other = _others.FirstOrDefault(x => x.Exists(file));
        if (other is null)
            return file;

        if (content.Read(file) is not { } data)
        {
            context.Report.Warn($"{other.Name} ships {file} too, and {content.Name}'s copy couldn't be read, so the effects in it may be missing");
            return file;
        }

        string target = $"particles/porter_{state.Game!.Id}_{Path.GetFileName(ContentPath.Normalize(file)).ToLowerInvariant()}";
        state.Sink!.AddFile(target, data);
        context.Report.Change($"{Path.GetFileName(file)} copied into the pack as {target}: {other.Name} ships it too");
        context.Log.Info($"{other.Name} ships {file} too, which the manifest would load instead of {content.Name}'s, so {content.Name}'s copy is in the pack as {target}");
        return target;
    }

    public void Dispose()
    {
        foreach (GameContent other in _others)
            other.Dispose();
    }
}

/// <summary>
/// For games Momentum doesn't mount (GoldSrc, Quake 3): converts the models and their textures and compiles them
/// with studiomdl into the work folder, then adds them to the pack.
/// </summary>
public sealed class ConvertModelsStep : IPortStep
{
    public string Name => "Convert models";

    public async Task RunAsync(PortJobContext context)
    {
        WeaponPackState state = context.Get<WeaponPackState>();
        SourceGameInfo info = state.GameInfo!;
        if (info.MountedByMomentum)
            return;

        CatalogWeapon weapon = state.Weapon!;
        string studioMdl = string.IsNullOrWhiteSpace(state.Options.StudioMdlPath) ? state.Momentum!.StudioMdlPath : state.Options.StudioMdlPath;
        var compiler = new StudioMdlCompiler(studioMdl, Path.Combine(context.WorkDirectory, "studiomdl"), context.Log);
        ViewModelKind kind = state.Slot!.Id switch
        {
            "knife" => ViewModelKind.Melee,
            "df_knife" => ViewModelKind.Gauntlet,
            "grenade" or "concgrenade" => ViewModelKind.Grenade,
            _ => ViewModelKind.Gun,
        };

        // Model path, view model kind (null for world and projectile models), whether it's a projectile
        Func<string, ViewModelKind?, bool, Task<ConvertedModel>> convert;
        switch (info.Engine)
        {
            case ContentEngine.GoldSrc:
            {
                var converter = new GoldSrcWeaponConverter(state.Content!, compiler, info.Id, context.WorkDirectory);
                convert = (path, viewKind, _) => converter.ConvertAsync(path, viewKind, context.Log, context.Cancellation, viewKind == ViewModelKind.Gun);
                break;
            }
            case ContentEngine.Quake3:
            {
                var converter = new Quake3WeaponConverter(state.Content!, compiler, info.Id);
                // Momentum draws no trail for some projectiles (the Defrag grenade), so their model draws Quake 3's
                string? modelTrail = state.Options.Particles ? Quake3Effects.ModelTrailFor(weapon.Id) : null;
                convert = (path, viewKind, projectile) => converter.ConvertAsync(path, viewKind, context.Log, context.Cancellation, projectile, projectile ? modelTrail : null);
                break;
            }
            default:
                throw new PortStepException($"{info.Name}'s models can't be converted");
        }

        ConvertedModel view = await convert(weapon.View, kind, false).ConfigureAwait(false);
        Add(context, state, view);
        state.ViewModel = view.GamePath;
        state.ViewSideModel = view.GamePath;

        if (!string.IsNullOrEmpty(weapon.World))
        {
            try
            {
                ConvertedModel world = await convert(weapon.World, null, false).ConfigureAwait(false);
                state.WorldModel = world.GamePath;
                if (info.Engine == ContentEngine.Quake3)
                {
                    // FitWorldModelStep adds it, recompiled to sit in Momentum's player's hand
                    state.ConvertedWorldModel = world;
                    context.Report.Change("Converted models");
                }
                else
                {
                    Add(context, state, world);
                }
            }
            catch (PortStepException ex)
            {
                context.Report.Warn($"The world model wasn't converted, Momentum's is kept: {ex.Message}");
            }
        }

        if (state.Options.Projectile && !string.IsNullOrEmpty(weapon.Projectile))
        {
            try
            {
                ConvertedModel projectile = await convert(weapon.Projectile, null, true).ConfigureAwait(false);
                Add(context, state, projectile);
                state.ProjectileModel = projectile.GamePath;
            }
            catch (PortStepException ex)
            {
                context.Report.Warn($"The projectile model wasn't converted, Momentum's is kept: {ex.Message}");
            }
        }
    }

    private static void Add(PortJobContext context, WeaponPackState state, ConvertedModel model)
    {
        foreach ((string gamePath, string file) in model.Files)
            state.Sink!.AddFile(gamePath, file);

        context.Report.Change("Converted models");
        if (!state.Options.Sounds)
            return;

        foreach ((string source, string pack) in model.Sounds)
        {
            if (WeaponPackFiles.CopySound(context, state, source, pack) is null)
                continue;

            // The model's animation events name this entry (GoldSrcWeaponConverter.SoundEntryName); playing the wave
            // itself works too, but the engine warns about it on every play
            string name = GoldSrcWeaponConverter.SoundEntryName(state.Game!.Id, pack);
            if (state.AddedSoundEntries.Any(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;

            var entry = new KeyValuesNode(name);
            entry.Set("channel", "CHAN_ITEM");
            entry.Set("volume", "1.0");
            entry.Set("soundlevel", "SNDLVL_NORM");
            entry.Set("pitch", "PITCH_NORM");
            entry.Set("wave", ")" + ContentPath.Normalize(pack)["sound/".Length..]);
            state.AddedSoundEntries.Add(entry);
        }
    }
}

/// <summary>
/// Works out the sound overrides: soundscript entries of the source game where Momentum loads them, entries copied
/// into the pack where it doesn't (or where a Momentum entry hides them), and entries made from converted waves.
/// </summary>
public sealed class ResolveWeaponSoundsStep : IPortStep
{
    public string Name => "Sounds";

    public Task RunAsync(PortJobContext context)
    {
        WeaponPackState state = context.Get<WeaponPackState>();
        if (!state.Options.Sounds)
        {
            context.Log.Info("Keeping Momentum's sounds");
            return Task.CompletedTask;
        }

        CatalogGame game = state.Game!;
        CatalogWeapon weapon = state.Weapon!;
        MomentumInstall momentum = state.Momentum!;
        GameContent content = state.Content!;

        SoundscriptLibrary loaded = SoundscriptLibrary.LoadMomentum(momentum);
        var sourceGame = new SoundscriptLibrary();
        foreach (string script in game.Soundscripts)
        {
            string? text = content.Read(script) is { } bytes ? Encoding.UTF8.GetString(bytes) : momentum.ReadText(script);
            if (text is null)
                context.Report.Warn($"{game.Name}'s {script} wasn't found");
            else
                sourceGame.AddFile(script, text);
        }

        var resolver = new WeaponSoundResolver(loaded, sourceGame, game, path => content.Exists(path) || momentum.ReadText(path) is not null || File.Exists(Path.Combine(momentum.GameDirectory, path)));

        foreach ((string role, string entry) in weapon.Sounds)
        {
            if (!TryGetSlotEntry(context, state, role, out string? slotEntry))
                continue;

            ResolvedSound resolved = resolver.Resolve(entry);
            if (resolved.Entry is null)
            {
                context.Report.Warn($"{weapon.Name}'s {role} sound wasn't ported: {resolved.Problem}");
                continue;
            }

            state.SoundOverrides[slotEntry] = resolved.Entry;
            context.Log.Info($"{slotEntry} -> {(resolved.Entry.Length == 0 ? "(silent)" : resolved.Entry)}{(resolved.Added ? " (added to the pack)" : "")}");
        }

        foreach (string prefix in weapon.SoundPrefixes)
        {
            foreach (string hidden in resolver.AddByPrefix(prefix))
                context.Report.Warn($"The view model's animations play {hidden}, which a Momentum sound of the same name replaces");
        }

        foreach ((string role, List<string> waves) in weapon.Waves)
        {
            if (!TryGetSlotEntry(context, state, role, out string? slotEntry))
                continue;

            var packed = waves
                .Select(x => WeaponPackFiles.CopySound(context, state, x, WeaponPackFiles.PackSoundPath(game.Id, x)))
                .Where(x => x is not null)
                .Select(x => x!)
                .ToList();
            if (packed.Count == 0)
                continue;

            state.SoundOverrides[slotEntry] = resolver.AddWaves($"Porter_{game.Id}.{weapon.Id}.{role}", role, packed);
        }

        RedefinePlayerShot(context, state, resolver, loaded, sourceGame);

        state.AddedSoundEntries.AddRange(resolver.AddedEntries);
        foreach (string unused in state.SlotSounds.Keys.Where(x => !weapon.Sounds.ContainsKey(x) && !weapon.Waves.ContainsKey(x)))
            context.Log.Info($"The {state.Slot!.Name}'s {unused} sound stays Momentum's");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Bullet weapons' shots are played with the player as the source (FX_FireBullets), where a weapon's SoundData
    /// Overrides don't apply, so the pack also redefines Momentum's shot entry (e.g. Weapon_Pistol.Single) as the
    /// replacement sound: in its copy of Momentum's weapon soundscript for a weapon used in every mode, which works on
    /// any build, or in each mode's scripts/&lt;mode&gt;/game_sounds_momentum_weapons.txt for a weapon for some modes
    /// (the gamemode-weapon-scripts engine change, docs/engine-changes.md). See <see cref="MergedSoundscript"/>.
    /// </summary>
    private static void RedefinePlayerShot(PortJobContext context, WeaponPackState state, WeaponSoundResolver resolver, SoundscriptLibrary momentumSounds, SoundscriptLibrary sourceGame)
    {
        WeaponSlot slot = state.Slot!;
        if (!slot.ShotFromPlayer
            || !state.SlotSounds.TryGetValue("single_shot", out string? shotEntry)
            || !state.SoundOverrides.TryGetValue(shotEntry, out string? replacement)
            || replacement.Length == 0)
        {
            return;
        }

        KeyValuesNode? body = resolver.AddedEntries.FirstOrDefault(x => x.Key.Equals(replacement, StringComparison.OrdinalIgnoreCase))
            ?? momentumSounds.Find(replacement)?.Node
            ?? sourceGame.Find(replacement)?.Node;
        if (body is null)
        {
            context.Report.Warn($"Couldn't find {replacement} to redefine {shotEntry} with; the {slot.Name} keeps Momentum's shot");
            return;
        }

        state.RedefinedSoundEntries[shotEntry] = body.Clone(shotEntry);
        context.Log.Info($"{shotEntry} redefined as {replacement}: Momentum plays the {slot.Name}'s shot from the player, where weapon sound overrides don't apply");
        context.Report.Change(state.GameModes.Count == 0
            ? $"{shotEntry} redefined in the pack's soundscript"
            : $"{shotEntry} redefined for {GameModes.Describe(state.GameModes)} only");

        MomentumInstall momentum = state.Momentum!;
        var sharing = WeaponSlots.All
            .Where(x => x != slot && momentum.ReadText(x.ServerScriptPath) is { } text
                && WeaponScript.ReadSoundEntries(text, x.ServerScriptPath).Values.Contains(shotEntry, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.Name)
            .ToList();
        if (sharing.Count > 0)
            context.Report.Warn($"{shotEntry} is also the shot of Momentum's {string.Join(", ", sharing)}, which now sound like the {state.Weapon!.Name} too{(state.GameModes.Count == 0 ? "" : $" in {GameModes.Describe(state.GameModes)}")}");
    }

    private static bool TryGetSlotEntry(PortJobContext context, WeaponPackState state, string role, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? slotEntry)
    {
        if (state.SlotSounds.TryGetValue(role, out slotEntry))
            return true;

        context.Log.Info($"The {state.Slot!.Name} has no {role} sound; {state.Weapon!.Name}'s isn't used");
        return false;
    }
}

/// <summary>
/// Writes the weapon script and, when the pack defines soundscript entries, its copy of
/// scripts/game_sounds_momentum_weapons.txt.
/// </summary>
public sealed class WriteWeaponPackStep : IPortStep
{
    /// <summary>
    /// The soundscript the pack's entries are appended to. Momentum only loads the soundscripts its
    /// game_sounds_manifest.txt lists, and the pack doesn't replace the manifest, so it replaces this file instead.
    /// </summary>
    public const string SoundscriptPath = "scripts/game_sounds_momentum_weapons.txt";

    /// <summary>
    /// Each Momentum weapon's soundscript entries from the weapon ported over it (<c>&lt;slot&gt;.txt</c>, or
    /// <c>&lt;slot&gt;.&lt;mode&gt;.txt</c> for a weapon used in one game mode), kept in the pack so that
    /// <see cref="SoundscriptPath"/> can be rebuilt from Momentum's copy plus every weapon's entries whenever a weapon
    /// is added to the folder. A slot holds one weapon per mode, so porting another over it replaces its file. The
    /// game doesn't load these (they're not in its manifest).
    /// </summary>
    public const string SoundEntriesFolder = "scripts/porter_weapons";

    /// <summary>
    /// A mode's redefinitions of Momentum's weapon sounds (a per-mode weapon's shot), which the engine loads over
    /// <see cref="SoundscriptPath"/> in that mode (the gamemode-weapon-scripts engine change, docs/engine-changes.md).
    /// </summary>
    public static string ModeSoundscriptPath(GameMode mode) => $"scripts/{mode.Id}/game_sounds_momentum_weapons.txt";

    /// <summary>The pack's soundscript entry file for a slot, in one mode or (null) every mode.</summary>
    public static string SoundEntriesPath(WeaponSlot slot, GameMode? mode) => $"{SoundEntriesFolder}/{slot.Id}{(mode is null ? "" : "." + mode.Id)}.txt";

    /// <summary>The slot and mode an entry file in <see cref="SoundEntriesFolder"/> is for, or null if it isn't one.</summary>
    public static (WeaponSlot Slot, GameMode? Mode)? ParseSoundEntriesPath(string path)
    {
        string[] parts = Path.GetFileNameWithoutExtension(path).Split('.');
        if (parts.Length is not (1 or 2) || WeaponSlots.Find(parts[0]) is not { } slot)
            return null;

        if (parts.Length == 1)
            return (slot, null);

        return GameModes.Find(parts[1]) is { } mode ? (slot, mode) : null;
    }

    /// <summary>
    /// The reload config earlier packs had, which used reload commands some Momentum builds crash or lose effects with.
    /// Removed when the pack is rebuilt.
    /// </summary>
    private const string OldReloadConfigPath = "cfg/porter_reload.cfg";

    /// <summary>Momentum loads packs, and the models and sounds in them, at startup.</summary>
    private const string RestartHint = "Restart Momentum to use the pack, and after rebuilding it.";

    public string Name => "Write pack";

    public Task RunAsync(PortJobContext context)
    {
        WeaponPackState state = context.Get<WeaponPackState>();
        WeaponScript script = state.Script!;
        CatalogGame game = state.Game!;
        CatalogWeapon weapon = state.Weapon!;
        WeaponSlot slot = state.Slot!;
        IPackSink sink = state.Sink!;

        script.SetModel("view", state.ViewModel!);
        if (script.GetModel("view_side") is not null)
            script.SetModel("view_side", state.ViewSideModel ?? state.ViewModel!);

        if (state.WorldModel is not null)
        {
            script.SetModel("world", state.WorldModel);
            script.SetModel("spawner", state.WorldModel);
        }

        if (state.ProjectileModel is not null)
        {
            if (slot.ProjectileKey is not null)
                script.SetModel(slot.ProjectileKey, state.ProjectileModel);
            else
                context.Log.Info($"The {slot.Name} has no projectile model; {state.ProjectileModel} isn't used");
        }

        script.AttachToHands = state.Options.Arms switch
        {
            WeaponArms.Momentum => 1,
            WeaponArms.None => 0,
            _ => game.AttachToHands,
        };
        script.BuiltRightHanded = game.BuiltRightHanded;

        if (script.AttachToHands == 1 && state.ArmsModel is not null)
        {
            script.SetModel("arms", state.ArmsModel);
            context.Report.Warn($"ModelData arms ({state.ArmsModel}) only takes effect on Momentum builds with the weapon-arms engine change in docs/engine-changes.md; until then Momentum attaches its own arms, which {game.Name}'s view models have no bones for, so no arms show");
        }

        foreach ((string entry, string replacement) in state.SoundOverrides)
            script.SetSoundOverride(entry, replacement);

        if (state.Options.Particles)
        {
            foreach ((string key, string particle) in weapon.Particles.Concat(state.Particles))
                script.SetParticle(key, particle);

            // Guns from other games fire muzzle flash events (CS:S's and HL2's models, converted GoldSrc ones), which
            // the engine draws the script's MuzzleFlashEffect for; a gun the catalog gives none gets one for its slot
            if (script.GetParticle(MuzzleFlashes.Key) is null or "" && MuzzleFlashes.SlotFamily(slot) is { } family)
            {
                (string effect, string? file) = MuzzleFlashes.Pick(state, family);
                script.SetParticle(MuzzleFlashes.Key, effect);
                if (file is not null)
                    state.ParticleFiles.Add(file);

                context.Log.Info($"Muzzle flash: {effect}{(file is null ? " (Momentum's own)" : $" from {file}")}");
            }

            // The game's own particle files were added by CheckMountedAssetsStep
        }

        if (state.ParticleFiles.Count > 0)
        {
            // Onto the folder's manifest when an earlier pack wrote one, so the folder's packs add up
            string manifest = sink.ReadFile(ParticleManifest.Path) is { } existing
                ? Encoding.UTF8.GetString(existing)
                : state.Momentum!.ReadRequiredText(ParticleManifest.Path);
            sink.AddFile(ParticleManifest.Path, Encoding.UTF8.GetBytes(ParticleManifest.WithFiles(manifest, state.ParticleFiles)));
            context.Report.Warn($"The pack replaces {ParticleManifest.Path} (Momentum's plus {string.Join(", ", state.ParticleFiles.Select(x => x.TrimStart('!')))}), since particle files only load through it: packs in other folders that replace it hide each other's, and Momentum's updates to it are hidden until a pack in this folder is rebuilt");
        }

        IReadOnlyList<GameMode> modes = state.GameModes;
        string header = $"{weapon.Name} from {game.Name} as Momentum's {slot.Name} in {GameModes.Describe(modes)}, made by MomentumPorter.\n"
            + $"Remove the {state.PackName} pack (or this file) to get Momentum's weapon back, then restart Momentum.";
        byte[] scriptText = Encoding.UTF8.GetBytes(script.Write(header));
        if (modes.Count == 0)
        {
            sink.AddFile(slot.ScriptPath, scriptText);

            // A mode's own script wins over this one there
            var perMode = GameModes.All.Where(x => sink.ReadFile(slot.ScriptPathFor(x)) is not null).ToList();
            if (perMode.Count > 0)
                context.Report.Warn($"The pack also has a {slot.Name} for {GameModes.Describe(perMode)}, which stays theirs; delete {string.Join(", ", perMode.Select(x => slot.ScriptPathFor(x)))} to use the {weapon.Name} there too");
        }
        else
        {
            foreach (GameMode mode in modes)
                sink.AddFile(slot.ScriptPathFor(mode), scriptText);

            context.Report.Warn($"The {weapon.Name} is only the {slot.Name} in {GameModes.Describe(modes)}, which needs a Momentum build with the gamemode-weapon-scripts engine change in docs/engine-changes.md (other builds ignore scripts/<mode>/). "
                + (sink.ReadFile(slot.ScriptPath) is not null ? $"Other modes keep the pack's {slot.ScriptPath}." : $"Other modes keep Momentum's {slot.Name}."));
        }

        if (sink.ReadFile(OldReloadConfigPath) is not null)
        {
            sink.DeleteFile(OldReloadConfigPath);
            context.Log.Info($"Removed {OldReloadConfigPath} from an older Momentum Porter: restart Momentum to pick up a rebuilt pack");
        }

        IEnumerable<GameMode?> entryModes = modes.Count == 0 ? [null] : modes;
        foreach (GameMode? mode in entryModes)
        {
            string ownEntries = SoundEntriesPath(slot, mode);
            if (state.AddedSoundEntries.Count > 0 || state.RedefinedSoundEntries.Count > 0)
            {
                var own = new StringBuilder();
                own.Append($"// {weapon.Name} ({game.Name}) as Momentum's {slot.Name} in {(mode is null ? "every mode" : mode.Name)}: the soundscript entries MomentumPorter\n");
                own.Append($"// merges into {SoundscriptPath}, together with those of the other weapons in this folder. Entries\n");
                own.Append("// Momentum's copy has too replace them there. The game doesn't load this file.\n\n");
                foreach (KeyValuesNode entry in state.AddedSoundEntries)
                    own.Append(entry.Write()).Append('\n');

                foreach (KeyValuesNode entry in state.RedefinedSoundEntries.Values)
                {
                    own.Append($"// Replaces Momentum's {entry.Key}\n");
                    own.Append(entry.Write()).Append('\n');
                }

                sink.AddFile(ownEntries, Encoding.UTF8.GetBytes(own.ToString()));
            }
            else if (sink.ReadFile(ownEntries) is not null)
            {
                // The weapon ported over this slot before had entries; this one doesn't
                sink.DeleteFile(ownEntries);
                context.Log.Info($"Removed {ownEntries}: the {slot.Name} is now the {weapon.Name}, which adds no soundscript entries");
            }
        }

        if (state.AddedSoundEntries.Count > 0 || state.RedefinedSoundEntries.Count > 0)
            context.Report.Change("Soundscript entries added to the pack", state.AddedSoundEntries.Count + state.RedefinedSoundEntries.Count);

        MergedSoundscript.Write(context, state);

        foreach (string file in sink.Files)
            context.Report.Change($"Wrote {file}");

        context.Set(new WeaponPackOutput(state.PackDirectory, sink.Files.ToList()));
        // Packs written straight into Momentum's custom folder are already live; anywhere else they have to be copied there
        bool inCustom = state.PackIsInMomentumCustom;
        context.Log.Info(inCustom
            ? $"Wrote {sink.Files.Count} files to {state.PackDirectory}. {RestartHint}"
            : $"Wrote {sink.Files.Count} files to {state.PackDirectory}. Copy the folder into momentum/custom/. {RestartHint}");
        state.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// The particle muzzle flash a gun's script names when its catalog entry has none. CS:GO's flashes (its
/// particles/weapons/cs_weapon_fx.pcf) have one per weapon family and are used when CS:GO is mounted; Momentum's own
/// particles/weapons.pcf, always loaded, carries only the assault rifle's, which every family falls back to.
/// </summary>
file static class MuzzleFlashes
{
    public const string Key = "MuzzleFlashEffect";
    public const string CsgoParticleFile = "particles/weapons/cs_weapon_fx.pcf";
    public const string MomentumRifleFlash = "weapon_muzzle_flash_assaultrifle";

    /// <summary>The CS:GO flash family for a slot's guns, null for weapons without a muzzle.</summary>
    public static string? SlotFamily(WeaponSlot slot) => slot.Id switch
    {
        "pistol" => "pistol",
        "machinegun" or "df_machinegun" => "assaultrifle",
        "shotgun" or "df_shotgun" => "autoshotgun",
        "sniper" or "df_railgun" => "awp",
        _ => null,
    };

    public static (string Effect, string? File) Pick(WeaponPackState state, string family)
    {
        if (family != "assaultrifle" && CsgoHasParticles(state))
            return ($"weapon_muzzle_flash_{family}", CsgoParticleFile);

        return (MomentumRifleFlash, null);
    }

    private static bool CsgoHasParticles(WeaponPackState state)
    {
        if (state.GameInfo!.Id.Equals(SourceGames.CounterStrikeGlobalOffensive.Id, StringComparison.OrdinalIgnoreCase))
            return state.Content!.Exists(CsgoParticleFile);

        try
        {
            using GameContent csgo = SourceGames.Open(SourceGames.CounterStrikeGlobalOffensive, state.Steam!, state.Momentum!.InstallDirectory);
            return csgo.Exists(CsgoParticleFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }
}

/// <summary>Merges the weapon soundscript and the pack's own entry files; see <see cref="WriteWeaponPackStep"/>.</summary>
file static class MergedSoundscript
{
    /// <summary>
    /// Writes Momentum's weapons soundscript with every weapon's entries from the pack's
    /// <see cref="WriteWeaponPackStep.SoundEntriesFolder"/>: an entry Momentum defines too replaces it (Source keeps
    /// the first definition of a name), and the last weapon's wins when two define the same entry. A weapon for some
    /// modes redefines Momentum's entries (its shot) in each mode's <see cref="WriteWeaponPackStep.ModeSoundscriptPath"/>
    /// instead, which the engine loads over Momentum's in that mode only; its new entries go in the shared file.
    /// </summary>
    public static void Write(PortJobContext context, WeaponPackState state)
    {
        IPackSink sink = state.Sink!;
        var file = MomentumPorter.Assets.Sounds.SoundscriptFile.Parse(state.Momentum!.ReadRequiredText(WriteWeaponPackStep.SoundscriptPath), WriteWeaponPackStep.SoundscriptPath);
        var momentumEntries = file.Entries.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = new List<KeyValuesNode>();
        var perMode = new Dictionary<GameMode, List<KeyValuesNode>>();
        var weapons = new List<string>();
        foreach (string path in sink.ListFiles(WriteWeaponPackStep.SoundEntriesFolder))
        {
            // Files from before the entries were kept per slot (<game>_<weapon>_<slot>.txt) would pile up
            if (WriteWeaponPackStep.ParseSoundEntriesPath(path) is not { } owner)
            {
                sink.DeleteFile(path);
                context.Report.Warn($"Removed {path}, from an older Momentum Porter: build that weapon again to get its sounds back");
                continue;
            }

            if (sink.ReadFile(path) is not { } data)
                continue;

            KeyValuesNode document;
            try
            {
                document = KeyValuesNode.ParseDocument(Encoding.UTF8.GetString(data), path);
            }
            catch (FormatException ex)
            {
                context.Report.Warn($"{path} in the pack couldn't be read, its sounds are left out: {ex.Message}");
                continue;
            }

            foreach (KeyValuesNode entry in document.Children)
            {
                List<KeyValuesNode> entries;
                if (owner.Mode is { } mode && momentumEntries.Contains(entry.Key))
                {
                    entries = perMode.TryGetValue(mode, out List<KeyValuesNode>? modeEntries) ? modeEntries : perMode[mode] = [];
                }
                else
                {
                    file.Remove(entry.Key);
                    entries = merged;
                }

                entries.RemoveAll(x => x.Key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
                entries.Add(entry);
            }

            weapons.Add(Path.GetFileNameWithoutExtension(path));
        }

        WriteModeOverrides(context, sink, perMode);

        if (merged.Count == 0)
        {
            // Nothing in the folder adds sounds for every mode any more, so Momentum's own file can serve
            if (sink.ReadFile(WriteWeaponPackStep.SoundscriptPath) is not null)
            {
                sink.DeleteFile(WriteWeaponPackStep.SoundscriptPath);
                context.Log.Info($"Removed the pack's {WriteWeaponPackStep.SoundscriptPath}: no weapon in the folder adds soundscript entries");
            }

            return;
        }

        var text = new StringBuilder(file.ToText().TrimEnd());
        text.Append("\n\n// ----------------------------------------------------------------------------\n");
        text.Append($"// Added by MomentumPorter from {WriteWeaponPackStep.SoundEntriesFolder}/: {string.Join(", ", weapons)}\n");
        text.Append("// ----------------------------------------------------------------------------\n\n");
        foreach (KeyValuesNode entry in merged)
            text.Append(entry.Write()).Append('\n');

        sink.AddFile(WriteWeaponPackStep.SoundscriptPath, Encoding.UTF8.GetBytes(text.ToString()));
        context.Report.Warn($"The pack replaces {WriteWeaponPackStep.SoundscriptPath} (Momentum's copy plus the entries of the folder's {weapons.Count} weapon pack(s)), since the base game_sounds_manifest.txt isn't overridden: a pack in another folder that replaces it hides this one's, so keep weapon packs in one folder, and Momentum's updates to that file are hidden until a pack here is rebuilt");
    }

    // Each mode's redefinitions of Momentum's entries; a mode no weapon redefines anything in any more loses its file
    private static void WriteModeOverrides(PortJobContext context, IPackSink sink, Dictionary<GameMode, List<KeyValuesNode>> perMode)
    {
        foreach (GameMode mode in GameModes.All)
        {
            string path = WriteWeaponPackStep.ModeSoundscriptPath(mode);
            if (!perMode.TryGetValue(mode, out List<KeyValuesNode>? entries))
            {
                if (sink.ReadFile(path) is not null)
                {
                    sink.DeleteFile(path);
                    context.Log.Info($"Removed the pack's {path}: no weapon in the folder redefines a sound for {mode.Name}");
                }

                continue;
            }

            var text = new StringBuilder();
            text.Append($"// Momentum's weapon sounds redefined for {mode.Name} only, merged by MomentumPorter from {WriteWeaponPackStep.SoundEntriesFolder}/.\n");
            text.Append($"// Loaded over {WriteWeaponPackStep.SoundscriptPath} in this mode by Momentum builds with the gamemode-weapon-scripts\n");
            text.Append("// engine change (other builds ignore it). Each entry replaces Momentum's whole entry.\n\n");
            foreach (KeyValuesNode entry in entries)
                text.Append(entry.Write()).Append('\n');

            sink.AddFile(path, Encoding.UTF8.GetBytes(text.ToString()));
        }
    }
}

internal static class WeaponPackFiles
{
    /// <summary>Where a converted game's sound goes in the pack, away from the mounted games' paths.</summary>
    public static string PackSoundPath(string gameId, string soundPath) =>
        $"sound/porter/{gameId}/{ContentPath.Normalize(soundPath)["sound/".Length..].ToLowerInvariant()}";

    /// <summary>
    /// Copies a sound from the source game into the pack, re-encoding it if Source can't play it. Returns the pack
    /// path, or null if it's missing.
    /// </summary>
    public static string? CopySound(PortJobContext context, WeaponPackState state, string source, string packPath)
    {
        if (state.Sink!.Files.Contains(packPath, StringComparer.OrdinalIgnoreCase))
            return packPath;

        byte[]? data = state.Content!.Read(source);
        if (data is null)
        {
            context.Report.Warn($"{source} isn't in {state.Content.Name}'s files");
            return null;
        }

        if (WavFile.TryParse(data, out WavFile? wav, out string? error))
        {
            if (!wav.IsSourceCompatible)
            {
                data = wav.ToSourceCompatible().ToBytes();
                context.Report.Change("Re-encoded sounds Source can't play");
            }
        }
        else
        {
            context.Report.Warn($"{source} couldn't be read as a WAV ({error}); copied as it is");
        }

        state.Sink.AddFile(packPath, data);
        return packPath;
    }
}
