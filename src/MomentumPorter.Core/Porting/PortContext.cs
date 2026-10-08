namespace MomentumPorter.Porting;

using LibBSP;
using MomentumPorter.Bsp;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;

/// <summary>
/// A text file to write alongside the ported map.
/// </summary>
/// <param name="RelativePath">Path relative to the output BSP's folder, with forward slashes (e.g. "zones/local/kz_x.json")</param>
public sealed record ExtraOutput(string RelativePath, string Content);

/// <param name="mapName">The ported map's name, which map specific files (e.g. its soundscapes) are named after</param>
/// <param name="gameDirectory">The game's folder that the map came from (holding gameinfo.txt), if known</param>
/// <param name="report">Where to count changes and warnings; a job passes its own so every step shares one</param>
public sealed class PortContext(BSP bsp, string mapName, string? gameDirectory, PortReport? report = null)
{
    // Precache table size (MAX_MODELS), shared by brush models and studio models
    private const int MaxModels = 4096;

    private BrushModelEditor? _brushModels;

    public BSP Bsp { get; } = bsp;

    public LibBSP.Entities EntityLump { get; } = bsp.Entities ?? throw new InvalidDataException("BSP has no entity lump");

    // Snapshot, so conversions can add entities while iterating
    public List<Entity> Entities => EntityLump.ToList();

    public BrushModelEditor BrushModels => _brushModels ??= new BrushModelEditor(Bsp);

    public PortReport Report { get; } = report ?? new PortReport();

    public string MapName { get; } = mapName;

    public string? GameDirectory { get; } = gameDirectory;

    /// <summary>
    /// Files the conversion produced besides the BSP (e.g. a zone file), written next to the output BSP.
    /// </summary>
    public List<ExtraOutput> ExtraOutputs { get; } = [];

    /// <summary>
    /// Reads a text file packed in the map, or returns null if it isn't packed.
    /// </summary>
    public string? ReadPakFile(string path)
    {
        if (Bsp.PakFile is not { Data.Length: > 0 } pak)
            return null;

        using ZipArchive zip = ZipArchive.Open(new MemoryStream(pak.Data));
        ZipArchiveEntry? entry = FindEntry(zip, path);
        if (entry is null)
            return null;

        using var reader = new StreamReader(entry.OpenEntryStream(), Entity.TextEncoding);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Packs a text file into the map, replacing any packed file with the same path.
    /// </summary>
    public void WritePakFile(string path, string text)
    {
        PakFile pak = Bsp.PakFile ?? throw new InvalidDataException("BSP has no pak file lump");
        using ZipArchive zip = pak.Data.Length > 0 ? ZipArchive.Open(new MemoryStream(pak.Data)) : ZipArchive.Create();

        // Keep the files compressed the way they were. The writer re-zips them if the output is compressed.
        bool compress = zip.Entries.Any(x => x.CompressionType == SharpCompress.Common.CompressionType.LZMA);

        if (FindEntry(zip, path) is { } existing)
            zip.RemoveEntry(existing);

        zip.AddEntry(path, new MemoryStream(Entity.TextEncoding.GetBytes(text)), closeStream: true);
        pak.SetZipArchive(zip, compress);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string path)
    {
        return zip.Entries.FirstOrDefault(x => !x.IsDirectory && string.Equals(x.Key?.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<Entity> OfClass(string className)
    {
        return Entities.Where(x => x.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase));
    }

    public void AddEntity(Entity entity)
    {
        EntityLump.Add(entity);
    }

    /// <summary>
    /// Turns a brush entity into one entity per brush, each a copy of the original with its own model.
    /// The original entity keeps the first brush.
    /// </summary>
    public List<Entity> SplitByBrush(Entity entity)
    {
        int model = entity.ModelNumber;
        if (model <= 0)
            return [entity];

        List<int> models = BrushModels.SplitModel(model);
        var entities = new List<Entity> { entity };
        foreach (int newModel in models.Skip(1))
        {
            var copy = new Entity(entity, EntityLump) { Model = $"*{newModel}" };
            AddEntity(copy);
            entities.Add(copy);
        }

        return entities;
    }

    /// <summary>
    /// Replaces every output with what <paramref name="rewrite"/> returns for it.
    /// </summary>
    public void RewriteOutputs(Func<Entity, Entity.EntityConnection, Entity.EntityConnection> rewrite)
    {
        foreach (Entity entity in EntityLump)
        {
            for (int i = 0; i < entity.connections.Count; i++)
                entity.connections[i] = rewrite(entity, entity.connections[i]);
        }
    }

    public void CheckLimits()
    {
        if (_brushModels is null)
            return;

        int studioModels = EntityLump
            .Select(x => x.Model)
            .Where(x => x?.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) == true)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (_brushModels.ModelCount + studioModels > MaxModels)
        {
            Report.Warn(
                $"{_brushModels.ModelCount} brush models and {studioModels} entity models are more than the engine can precache ({MaxModels})"
            );
        }
    }
}
