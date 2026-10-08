namespace MomentumPorter.BspConvert;

using BSPConvert.Lib;
using MomentumPorter.Detection;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;

/// <summary>
/// Converts a Quake 3 or GoldSrc map (.bsp, or every selected map of a .pk3/.zip) with BSPConvert, in process, into
/// a game-folder-shaped staging folder in the job's work directory. <see cref="BspConvertState"/> lists what it wrote.
/// </summary>
/// <remarks>
/// BSPConvert has no cancellation token. Cancelling stops it between two maps of an archive (see
/// <see cref="PortLogAdapter"/>); a map that has started converting runs to the end, and the job then stops.
/// </remarks>
public sealed class BspConvertStep(SteamLocator? steam = null) : IPortStep
{
    // BSPConvert extracts its input to %TEMP%\<input name>, deleting whatever is there first, so two conversions of
    // inputs with the same name would break each other. Its converters aren't written to run side by side either.
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    public string Name => "Convert map";

    public async Task RunAsync(PortJobContext context)
    {
        BspConvertState state = context.Get<BspConvertState>();
        BspConvertOptions options = state.Options;
        string input = Path.GetFullPath(state.InputPath);

        if (!File.Exists(input))
            throw new PortStepException($"{input} doesn't exist");

        IReadOnlyList<string> errors = options.Validate();
        if (errors.Count > 0)
            throw new PortStepException(string.Join(" ", errors));

        if (!string.IsNullOrWhiteSpace(options.GoldSrcHulls))
        {
            if (!File.Exists(options.GoldSrcHulls))
                throw new PortStepException($"{options.GoldSrcHulls} doesn't exist");
        }
        else
        {
            IReadOnlyList<ArchiveMap> maps = SelectMaps(input, options, context.Log);
            if (options.SetUpQuake3Content && maps.Any(x => x.Kind == InputKind.Quake3Bsp))
                await Quake3Content.EnsureAsync(steam ?? new SteamLocator(), context.Log, context.Cancellation).ConfigureAwait(false);
        }

        string staging = Path.Combine(context.WorkDirectory, "bspconvert");
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        BSPConverterOptions converterOptions = options.ToConverterOptions(input, staging);
        context.Log.Info($"BSPConvert {string.Join(' ', options.ToCommandLine().Append(input).Select(Quote))}");

        var logger = new PortLogAdapter(context.Log, context.Report, context.Cancellation);
        await OneAtATime.WaitAsync(context.Cancellation).ConfigureAwait(false);
        try
        {
            await Task.Factory.StartNew(
                () => new BSPConverter(converterOptions, logger).Convert(),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // BSPConvert only cleans up its extraction folder when it finishes
            DeleteExtractionFolder(input);
            if (ex is OperationCanceledException)
                throw;

            // BSPConvert reports maps it can't convert (e.g. "exceeded max extents") with plain exceptions
            throw new PortStepException($"BSPConvert couldn't convert {Path.GetFileName(input)}: {ex.Message}", ex);
        }
        finally
        {
            OneAtATime.Release();
        }

        if (logger.Errors.Count > 0)
            throw new PortStepException($"BSPConvert couldn't convert {Path.GetFileName(input)}: {string.Join("; ", logger.Errors)}");

        state.StagingDirectory = staging;
        state.ProducedFiles = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (state.MapPaths.Count == 0)
            throw new PortStepException($"BSPConvert didn't write a map for {Path.GetFileName(input)}, see the log");

        context.Report.Change("maps converted with BSPConvert", state.MapPaths.Count);
        if (state.ZonePaths.Count > 0)
            context.Report.Change("zone files written", state.ZonePaths.Count);

        context.Cancellation.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// The maps of the input that will be converted, after <see cref="BspConvertOptions.Maps"/>. Throws when that
    /// leaves nothing BSPConvert can convert.
    /// </summary>
    public static IReadOnlyList<ArchiveMap> SelectMaps(string input, BspConvertOptions options, IPortLog log)
    {
        IReadOnlyList<ArchiveMap> maps;
        try
        {
            maps = Pk3Inspector.ListMaps(input);
        }
        catch (InvalidDataException ex)
        {
            throw new PortStepException($"{Path.GetFileName(input)} isn't a valid archive: {ex.Message}", ex);
        }

        string[]? filter = options.MapFilter;
        if (filter is not null)
        {
            foreach (string name in filter.Where(x => !maps.Any(m => m.Name.Equals(x, StringComparison.OrdinalIgnoreCase))))
                log.Warn($"{Path.GetFileName(input)} has no map named {name}");

            maps = maps.Where(m => filter.Contains(m.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        if (!maps.Any(x => x.CanConvert))
        {
            IReadOnlyList<ArchiveMap> all = Pk3Inspector.ListMaps(input);
            string available = all.Count == 0 ? "no maps" : string.Join(", ", all.Select(x => $"{x.Name} ({PortJobFactory.Describe(x.Kind)})"));
            throw new PortStepException($"Nothing in {Path.GetFileName(input)} to convert. It has {available}.");
        }

        return maps;
    }

    // ContentManager's folder: %TEMP%\<input file name without extension>
    private static void DeleteExtractionFolder(string input)
    {
        string folder = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(input));
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // BSPConvert deletes it next time it converts this input
        }
    }

    private static string Quote(string argument)
    {
        return argument.Contains(' ') ? $"\"{argument}\"" : argument;
    }
}
