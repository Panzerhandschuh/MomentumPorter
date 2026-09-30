namespace MomentumPorter.Porting;

using LibBSP;
using MomentumPorter.Bsp;

internal sealed class PortContext(BSP bsp)
{
    // Precache table size (MAX_MODELS), shared by brush models and studio models
    private const int MaxModels = 4096;

    private BrushModelEditor? _brushModels;

    public BSP Bsp { get; } = bsp;

    public LibBSP.Entities EntityLump { get; } = bsp.Entities ?? throw new InvalidDataException("BSP has no entity lump");

    // Snapshot, so conversions can add entities while iterating
    public List<Entity> Entities => EntityLump.ToList();

    public BrushModelEditor BrushModels => _brushModels ??= new BrushModelEditor(Bsp);

    public PortReport Report { get; } = new();

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
