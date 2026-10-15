namespace MomentumPorter.Bsp;

using System.Numerics;
using LibBSP;

/// <summary>
/// Edits the collision trees of brush entity models (the "*N" models).
/// </summary>
public sealed class BrushModelEditor
{
    // LUMP_LEAFMINDISTTOWATER, which LibBSP doesn't parse
    private const int LeafMinDistToWaterLump = 46;

    private readonly BSP _bsp;
    private readonly Lump<Model> _models;
    private readonly Lump<Node> _nodes;
    private readonly Lump<Leaf> _leaves;
    private readonly NumList _leafBrushes;
    private readonly Lump<Brush> _brushes;
    private readonly Lump<BrushSide> _brushSides;

    // Lumps with one entry per leaf, which have to grow with the leaf lump. Each copies a leaf's entry to the end.
    private readonly List<Action<int>> _copyPerLeafEntry = [];

    public BrushModelEditor(BSP bsp)
    {
        _bsp = bsp;
        _models = bsp.Models;
        _nodes = bsp.Nodes;
        _leaves = bsp.Leaves;
        _leafBrushes = bsp.LeafBrushes;
        _brushes = bsp.Brushes;
        _brushSides = bsp.BrushSides;

        LumpInfo info = bsp[LeafMinDistToWaterLump];
        var minDistToWater = new NumList(bsp.Reader.ReadLump(info), NumList.DataType.UInt16, bsp, info);
        bsp.Lumps[LeafMinDistToWaterLump] = minDistToWater;

        AddPerLeafLump("LUMP_LEAFMINDISTTOWATER", minDistToWater, x => x);
        AddPerLeafLump("LUMP_LEAF_AMBIENT_INDEX", bsp.LeafAmbientIndices, x => new LeafAmbientIndex(x, bsp.LeafAmbientIndices));
        AddPerLeafLump("LUMP_LEAF_AMBIENT_INDEX_HDR", bsp.LeafAmbientIndicesHDR, x => new LeafAmbientIndex(x, bsp.LeafAmbientIndicesHDR));
        AddAmbientLightingLump("LUMP_LEAF_AMBIENT_LIGHTING", bsp.LeafAmbientLighting, bsp.LeafAmbientIndices);
        AddAmbientLightingLump("LUMP_LEAF_AMBIENT_LIGHTING_HDR", bsp.LeafAmbientLightingHDR, bsp.LeafAmbientIndicesHDR);
    }

    public int ModelCount => _models.Count;

    /// <summary>
    /// The brushes in a model's collision tree, sorted by brush index, with the first leaf each one was found in.
    /// </summary>
    public List<(int Brush, int Leaf)> GetBrushes(int model) => BrushGeometry.GetBrushes(_bsp, model);

    /// <summary>
    /// Gives each brush of a model its own model whose collision tree only contains that brush.
    /// The first brush keeps the original model. The new models copy the original's faces and bounds,
    /// so each one still draws the whole original entity (e.g. with showtriggers).
    /// </summary>
    /// <returns>The model for each brush, in brush index order</returns>
    public List<int> SplitModel(int model)
    {
        return SplitModel(model, GetBrushes(model).Select(x => (IReadOnlyList<int>)[x.Brush]).ToList());
    }

    /// <summary>
    /// Gives each group of a model's brushes its own model whose collision tree only contains those brushes, like
    /// <see cref="SplitModel(int)"/>. The first group keeps the original model.
    /// </summary>
    /// <param name="groups">Brush indices of the model, each in one group</param>
    /// <returns>The model for each group, in order</returns>
    public List<int> SplitModel(int model, IReadOnlyList<IReadOnlyList<int>> groups)
    {
        Dictionary<int, int> leaves = GetBrushes(model).ToDictionary(x => x.Brush, x => x.Leaf);

        var models = new List<int>(groups.Count);
        for (int i = 0; i < groups.Count; i++)
        {
            int node = AddBrushTree(groups[i].Select(x => (x, leaves[x])).ToList(), model);

            int newModel = model;
            if (i > 0)
            {
                _models.Add(new Model(_models[model], _models));
                newModel = _models.Count - 1;
            }

            // Lump objects wrap the lump's data, so this edits the model in place
            Model edited = _models[newModel];
            edited.HeadNodeIndex = node;
            models.Add(newModel);
        }

        return models;
    }

    private void AddPerLeafLump<T>(string name, IList<T>? lump, Func<T, T> copy)
    {
        if (lump is null || lump.Count == 0)
            return;

        if (lump.Count != _leaves.Count)
            throw new InvalidDataException($"{name} has {lump.Count} entries but there are {_leaves.Count} leaves");

        _copyPerLeafEntry.Add(leaf => lump.Add(copy(lump[leaf])));
    }

    // The engine only goes through the index lump for version 1 ambient lighting with a non-empty index lump.
    // Otherwise (e.g. older maps) there's one sample per leaf.
    private void AddAmbientLightingLump(string name, Lump<LeafAmbientLighting>? lighting, Lump<LeafAmbientIndex>? indices)
    {
        if (lighting is null || (lighting.LumpInfo.version == 1 && indices is { Count: > 0 }))
            return;

        AddPerLeafLump(name, lighting, x => new LeafAmbientLighting(x, lighting));
    }

    // A single node whose sides both lead to a leaf holding only the brushes. The node's plane doesn't matter then.
    private int AddBrushTree(List<(int Brush, int Leaf)> brushes, int model)
    {
        Vector3 mins = new(float.MaxValue), maxs = new(float.MinValue);
        foreach ((int brush, _) in brushes)
        {
            (Vector3 brushMins, Vector3 brushMaxs) = BrushGeometry.GetBrushBounds(_bsp, brush) ?? (_models[model].Minimums, _models[model].Maximums);
            mins = Vector3.Min(mins, brushMins);
            maxs = Vector3.Max(maxs, brushMaxs);
        }

        mins = ToShortBounds(mins, MathF.Floor);
        maxs = ToShortBounds(maxs, MathF.Ceiling);

        // Leaves reference their brushes with 16 bit indices in most versions
        if (_leafBrushes.Type == NumList.DataType.UInt16 && _leafBrushes.Count + brushes.Count > ushort.MaxValue + 1)
            throw new InvalidOperationException("Too many leaf brushes to add more");

        int firstLeafBrush = _leafBrushes.Count;
        foreach ((int brush, _) in brushes)
            _leafBrushes.Add(brush);

        // Keep the contents, cluster, area and water data of a leaf the first brush was already in
        int templateLeaf = brushes[0].Leaf;
        int firstBrush = brushes[0].Brush;
        var leaf = new Leaf(_leaves[templateLeaf], _leaves)
        {
            Minimums = mins,
            Maximums = maxs,
            FirstMarkFaceIndex = 0,
            NumMarkFaceIndices = 0,
            FirstMarkBrushIndex = firstLeafBrush,
            NumMarkBrushIndices = brushes.Count,
        };
        _leaves.Add(leaf);
        int leafIndex = _leaves.Count - 1;

        foreach (Action<int> copyEntry in _copyPerLeafEntry)
            copyEntry(templateLeaf);

        var node = new Node(new byte[Node.GetStructLength(_bsp.MapType, _nodes.LumpInfo.version)], _nodes)
        {
            PlaneIndex = _brushSides[_brushes[firstBrush].FirstSideIndex].PlaneIndex,
            Child1Index = -1 - leafIndex,
            Child2Index = -1 - leafIndex,
            Minimums = mins,
            Maximums = maxs,
            AreaIndex = leaf.Area,
        };
        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    // Leaf and node bounds are shorts in most versions
    private static Vector3 ToShortBounds(Vector3 bounds, Func<float, float> round)
    {
        return Vector3.Clamp(
            new Vector3(round(bounds.X), round(bounds.Y), round(bounds.Z)),
            new Vector3(short.MinValue),
            new Vector3(short.MaxValue)
        );
    }
}
