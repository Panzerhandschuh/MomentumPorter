namespace MomentumPorter.Assets.Weapons;

using MomentumPorter.Assets.Models;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;

/// <summary>
/// Recompiles the weapon's world model onto Momentum's <c>weapon_bone.R</c>, placed like the Momentum model it
/// replaces, so it sits in the player's hand in third person (and in replays and on spawners); see
/// <see cref="WorldModelFitter"/>. Covers the games Momentum mounts and Quake 3; GoldSrc's world models are pickups lying
/// on the ground with nothing that marks where they're held, so they're left as they are.
/// </summary>
public sealed class FitWorldModelStep : IPortStep
{
    public string Name => "Fit world model";

    public async Task RunAsync(PortJobContext context)
    {
        WeaponPackState state = context.Get<WeaponPackState>();
        SourceGameInfo info = state.GameInfo!;
        ConvertedModel? converted = state.ConvertedWorldModel;
        if (state.WorldModel is not { } source)
            return;

        if (!info.MountedByMomentum && converted is null)
        {
            context.Report.Warn($"{info.Name}'s world model is used as it is; it doesn't attach to Momentum's player and sits at their feet in third person");
            return;
        }

        if (!state.Options.FitWorldModel)
        {
            AddConverted(state, converted);
            context.Report.Warn($"The world model {source} is used as it is; it doesn't attach to Momentum's player and sits at their feet in third person");
            return;
        }

        SourceModelMesh? mesh = converted is not null ? ReadConverted(context, converted) : ReadMounted(context, state.Content!, source);
        if (mesh is null)
        {
            AddConverted(state, converted);
            return;
        }

        if (mesh.Header.FindBone(WorldModelFitter.MomentumGripBone) >= 0)
        {
            AddConverted(state, converted);
            context.Log.Info($"{source} already has {WorldModelFitter.MomentumGripBone}, Momentum's player holds it as it is");
            return;
        }

        string studioMdl = string.IsNullOrWhiteSpace(state.Options.StudioMdlPath) ? state.Momentum!.StudioMdlPath : state.Options.StudioMdlPath;
        string modelName = converted is not null
            ? source["models/".Length..]
            : $"porter/{state.Game!.Id}/{Path.GetFileName(source).ToLowerInvariant()}";
        GunFrame? sourceFrame = info.Engine == ContentEngine.Quake3 ? WorldModelFitter.Quake3Source : null;
        try
        {
            var compiler = new StudioMdlCompiler(studioMdl, Path.Combine(context.WorkDirectory, "studiomdl_world"), context.Log);
            (CompiledModel model, WorldModelFit fit) = await WorldModelFitter.CompileAsync(
                mesh, sourceFrame, ReadReplacedModel(state), compiler, modelName, context.Cancellation).ConfigureAwait(false);
            foreach ((string gamePath, string file) in model.Files)
            {
                // It keeps the source model's materials, which another mounted game may ship too
                if (state.Materials is { } materials && gamePath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
                    state.Sink!.AddFile(gamePath, materials.Patch(context, source, File.ReadAllBytes(file)));
                else
                    state.Sink!.AddFile(gamePath, file);
            }

            state.WorldModel = model.GamePath;
            context.Log.Info($"Fitted {source} to Momentum's player's hand as {model.GamePath}: {fit}");
            context.Report.Change("World model fitted to Momentum's player's hand");
            if (fit.Source.From.Contains("a guess", StringComparison.Ordinal))
                context.Report.Warn($"{source} has no landmark (shell ejection, slide, muzzle) that shows which way is up, so it may be held upside down in third person");
        }
        catch (Exception ex) when (ex is PortStepException or InvalidDataException or IOException)
        {
            AddConverted(state, converted);
            context.Report.Warn($"The world model {source} couldn't be fitted to Momentum's player, so it sits at their feet in third person: {ex.Message}");
        }
    }

    /// <summary>A converted world model goes into the pack as it is when it isn't fitted.</summary>
    private static void AddConverted(WeaponPackState state, ConvertedModel? converted)
    {
        if (converted is null)
            return;

        foreach ((string gamePath, string file) in converted.Files)
            state.Sink!.AddFile(gamePath, file);
    }

    private static SourceModelMesh? ReadMounted(PortJobContext context, GameContent content, string source)
    {
        string basePath = Path.ChangeExtension(source, null);
        byte[]? mdl = content.Read(source);
        byte[]? vvd = content.Read(basePath + ".vvd");
        byte[]? vtx = content.Read(basePath + ".dx90.vtx") ?? content.Read(basePath + ".vtx");
        if (mdl is null || vvd is null || vtx is null)
        {
            context.Report.Warn($"The world model {source} wasn't fitted to Momentum's player (its .mdl, .vvd or .vtx is missing), so it sits at their feet in third person");
            return null;
        }

        return Read(context, source, mdl, vvd, vtx);
    }

    private static SourceModelMesh? ReadConverted(PortJobContext context, ConvertedModel converted)
    {
        string? Find(string extension) => converted.Files.FirstOrDefault(x => x.GamePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)).File;
        string? mdl = Find(".mdl");
        string? vvd = Find(".vvd");
        string? vtx = Find(".dx90.vtx") ?? Find(".vtx");
        if (mdl is null || vvd is null || vtx is null)
        {
            context.Report.Warn($"The converted world model {converted.GamePath} is missing its .mdl, .vvd or .vtx, so it isn't fitted to Momentum's player");
            return null;
        }

        return Read(context, converted.GamePath, File.ReadAllBytes(mdl), File.ReadAllBytes(vvd), File.ReadAllBytes(vtx));
    }

    private static SourceModelMesh? Read(PortJobContext context, string source, byte[] mdl, byte[] vvd, byte[] vtx)
    {
        try
        {
            return SourceModelMesh.Read(mdl, vvd, vtx);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            context.Report.Warn($"The world model {source} couldn't be read to fit it to Momentum's player: {ex.Message}");
            return null;
        }
    }

    /// <summary>The Momentum world model the slot has now, if it can be read.</summary>
    private static SourceModelMesh? ReadReplacedModel(WeaponPackState state)
    {
        if (state.Script?.GetModel("world") is not { Length: > 0 } world)
            return null;

        string basePath = Path.Combine(state.Momentum!.GameDirectory, Path.ChangeExtension(world, null).Replace('/', Path.DirectorySeparatorChar));
        string vtx = File.Exists(basePath + ".dx90.vtx") ? basePath + ".dx90.vtx" : basePath + ".vtx";
        try
        {
            return File.Exists(basePath + ".mdl") && File.Exists(basePath + ".vvd") && File.Exists(vtx)
                ? SourceModelMesh.Read(File.ReadAllBytes(basePath + ".mdl"), File.ReadAllBytes(basePath + ".vvd"), File.ReadAllBytes(vtx))
                : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }
}
