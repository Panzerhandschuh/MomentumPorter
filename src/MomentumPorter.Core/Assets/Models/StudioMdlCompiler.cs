namespace MomentumPorter.Assets.Models;

using MomentumPorter.Pipeline;
using MomentumPorter.Tools;

/// <param name="GamePath">The model's game path, e.g. <c>models/porter/cs16/v_ak47.mdl</c></param>
/// <param name="Files">Each compiled file's game path and where it is on disk (.mdl, .vvd, .vtx...)</param>
public sealed record CompiledModel(string GamePath, IReadOnlyList<(string GamePath, string File)> Files);

/// <summary>
/// Runs studiomdl on QCs against a stub game folder in the work directory, so nothing is written into a game.
/// </summary>
public sealed class StudioMdlCompiler
{
    private readonly string _studioMdlPath;
    private readonly IPortLog _log;

    public StudioMdlCompiler(string studioMdlPath, string workDirectory, IPortLog log)
    {
        if (!File.Exists(studioMdlPath))
            throw new PortStepException($"studiomdl wasn't found at {studioMdlPath}");

        _studioMdlPath = studioMdlPath;
        _log = log;
        GameDirectory = Path.Combine(workDirectory, "game");
        SourceDirectory = Path.Combine(workDirectory, "modelsrc");
        Directory.CreateDirectory(GameDirectory);
        Directory.CreateDirectory(SourceDirectory);
        File.WriteAllText(Path.Combine(GameDirectory, "gameinfo.txt"),
            "\"GameInfo\"\n{\n\tgame \"MomentumPorter\"\n\tFileSystem\n\t{\n\t\tSearchPaths\n\t\t{\n\t\t\tgame |gameinfo_path|.\n\t\t}\n\t}\n}\n");
    }

    /// <summary>The stub game folder models are compiled into; converted materials go under its materials/ too.</summary>
    public string GameDirectory { get; }

    /// <summary>Where each model's SMDs and QC are written.</summary>
    public string SourceDirectory { get; }

    /// <summary>
    /// A fresh folder for one model's QC and SMDs.
    /// </summary>
    public string CreateSourceFolder(string modelName)
    {
        string folder = Path.Combine(SourceDirectory, Path.ChangeExtension(modelName, null).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <param name="modelName">The QC's $modelname, a path under models/</param>
    public async Task<CompiledModel> CompileAsync(string qcPath, string modelName, CancellationToken cancellation)
    {
        ExternalToolResult result = await ExternalTool.RunAsync(
            _studioMdlPath,
            ["-nop4", "-game", GameDirectory, qcPath],
            _log,
            Path.GetDirectoryName(qcPath),
            cancellation).ConfigureAwait(false);

        string gamePath = "models/" + modelName;
        string modelFile = Path.Combine(GameDirectory, gamePath.Replace('/', Path.DirectorySeparatorChar));
        if (!result.Succeeded || !File.Exists(modelFile))
        {
            string errors = string.Join(" ", (result.StandardOutput + result.StandardError)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => x.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                .Take(5));
            throw new PortStepException($"studiomdl couldn't compile {gamePath} (exit code {result.ExitCode}){(errors.Length > 0 ? ": " + errors : "")}");
        }

        string directory = Path.GetDirectoryName(modelFile)!;
        string baseName = Path.GetFileNameWithoutExtension(modelFile);
        string gameDirectory = Path.GetDirectoryName(gamePath)!.Replace('\\', '/');
        // x.mdl, x.vvd, x.dx90.vtx, x.phy...
        var files = Directory.EnumerateFiles(directory, baseName + ".*")
            .Where(x => x.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".vvd", StringComparison.OrdinalIgnoreCase)
                || x.EndsWith(".vtx", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".phy", StringComparison.OrdinalIgnoreCase))
            .Select(x => ($"{gameDirectory}/{Path.GetFileName(x)}", x))
            .ToList();

        return new CompiledModel(gamePath, files);
    }
}
