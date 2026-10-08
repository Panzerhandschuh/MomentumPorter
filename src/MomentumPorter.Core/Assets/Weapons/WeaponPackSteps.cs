namespace MomentumPorter.Assets.Weapons;

using System.Text;
using MomentumPorter.Assets.Common;
using MomentumPorter.Assets.Models;
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
        state.Momentum = momentum;
        state.PackName = SanitizePackName(string.IsNullOrWhiteSpace(options.PackName) ? WeaponPackBuilder.DefaultPackName(game.Id, weapon.Id, slot.Id) : options.PackName);
        state.PackDirectory = Path.GetFullPath(Path.Combine(options.OutputDirectory, state.PackName));
        state.Sink ??= new FolderPackSink(state.PackDirectory);

        context.Log.Info($"Porting {weapon.Name} from {game.Name} ({string.Join(", ", state.Content.Sources.Select(x => x.Description))}) to the {slot.Name}, pack {state.PackDirectory}");
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

        Func<string, ViewModelKind?, Task<ConvertedModel>> convert;
        switch (info.Engine)
        {
            case ContentEngine.GoldSrc:
            {
                var converter = new GoldSrcWeaponConverter(state.Content!, compiler, info.Id, context.WorkDirectory);
                convert = (path, viewKind) => converter.ConvertAsync(path, viewKind, context.Log, context.Cancellation);
                break;
            }
            case ContentEngine.Quake3:
            {
                var converter = new Quake3WeaponConverter(state.Content!, compiler, info.Id);
                convert = (path, viewKind) => converter.ConvertAsync(path, viewKind, context.Log, context.Cancellation);
                break;
            }
            default:
                throw new PortStepException($"{info.Name}'s models can't be converted");
        }

        ConvertedModel view = await convert(weapon.View, kind).ConfigureAwait(false);
        Add(context, state, view);
        state.ViewModel = view.GamePath;
        state.ViewSideModel = view.GamePath;

        if (!string.IsNullOrEmpty(weapon.World))
        {
            try
            {
                ConvertedModel world = await convert(weapon.World, null).ConfigureAwait(false);
                Add(context, state, world);
                state.WorldModel = world.GamePath;
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
                ConvertedModel projectile = await convert(weapon.Projectile, null).ConfigureAwait(false);
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
            WeaponPackFiles.CopySound(context, state, source, pack);
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
    /// Bullet weapons' shots are played with the player as the source (FX_FireBullets), and the engine only applies a
    /// weapon's SoundData Overrides to sounds the weapon itself plays. So for those the pack redefines Momentum's
    /// shot entry (e.g. Weapon_Pistol.Single) as the replacement sound.
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
        context.Report.Change($"{shotEntry} redefined in the pack's soundscript");

        MomentumInstall momentum = state.Momentum!;
        var sharing = WeaponSlots.All
            .Where(x => x != slot && momentum.ReadText(x.ServerScriptPath) is { } text
                && WeaponScript.ReadSoundEntries(text, x.ServerScriptPath).Values.Contains(shotEntry, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.Name)
            .ToList();
        if (sharing.Count > 0)
            context.Report.Warn($"{shotEntry} is also the shot of Momentum's {string.Join(", ", sharing)}, which now sound like the {state.Weapon!.Name} too");
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
            context.Report.Warn($"ModelData arms ({state.ArmsModel}) only takes effect on Momentum builds with the arms change in docs/engine-patches; until then Momentum attaches its own arms, which {game.Name}'s view models have no bones for, so no arms show");
        }

        foreach ((string entry, string replacement) in state.SoundOverrides)
            script.SetSoundOverride(entry, replacement);

        if (state.Options.Particles)
        {
            foreach ((string key, string particle) in weapon.Particles)
                script.SetParticle(key, particle);
        }

        string header = $"{weapon.Name} from {game.Name} as Momentum's {slot.Name}, made by MomentumPorter.\n"
            + $"Remove the {state.PackName} pack (or this file) to get Momentum's weapon back. Reload with weapon_reload_scripts.";
        sink.AddFile(slot.ScriptPath, Encoding.UTF8.GetBytes(script.Write(header)));

        if (state.AddedSoundEntries.Count > 0 || state.RedefinedSoundEntries.Count > 0)
        {
            string momentumText = state.Momentum!.ReadRequiredText(SoundscriptPath);

            // Source keeps the first definition of an entry and skips later ones, so a redefined entry has to replace
            // Momentum's rather than follow it
            if (state.RedefinedSoundEntries.Count > 0)
            {
                var file = MomentumPorter.Assets.Sounds.SoundscriptFile.Parse(momentumText, SoundscriptPath);
                foreach (string name in state.RedefinedSoundEntries.Keys)
                    file.Remove(name);

                momentumText = file.ToText();
            }

            var text = new StringBuilder(momentumText.TrimEnd());
            text.Append("\n\n// ----------------------------------------------------------------------------\n");
            text.Append($"// Added by MomentumPorter for {state.PackName}: {weapon.Name} ({game.Name})\n");
            text.Append("// ----------------------------------------------------------------------------\n\n");
            foreach (KeyValuesNode entry in state.AddedSoundEntries)
                text.Append(entry.Write()).Append('\n');

            foreach (KeyValuesNode entry in state.RedefinedSoundEntries.Values)
            {
                text.Append($"// Replaces Momentum's {entry.Key}\n");
                text.Append(entry.Write()).Append('\n');
            }

            int count = state.AddedSoundEntries.Count + state.RedefinedSoundEntries.Count;
            sink.AddFile(SoundscriptPath, Encoding.UTF8.GetBytes(text.ToString()));
            context.Report.Change("Soundscript entries added to the pack", count);
            context.Report.Warn($"The pack replaces {SoundscriptPath} (Momentum's copy plus {count} entries), since the base game_sounds_manifest.txt isn't overridden: only one pack that does this works at a time, and Momentum's updates to that file are hidden until the pack is rebuilt");
        }

        foreach (string file in sink.Files)
            context.Report.Change($"Wrote {file}");

        context.Set(new WeaponPackOutput(state.PackDirectory, sink.Files.ToList()));
        // Packs written straight into Momentum's custom folder are already live; anywhere else they have to be copied there
        bool inCustom = state.Momentum is { } momentum &&
            string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(state.PackDirectory)!)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(momentum.GameDirectory, "custom"))),
                StringComparison.OrdinalIgnoreCase);
        context.Log.Info(inCustom
            ? $"Wrote {sink.Files.Count} files to {state.PackDirectory}. Run weapon_reload_scripts in game (sounds need a restart)."
            : $"Wrote {sink.Files.Count} files to {state.PackDirectory}. Copy the folder into momentum/custom/ and run weapon_reload_scripts (sounds need a restart).");
        state.Dispose();
        return Task.CompletedTask;
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
