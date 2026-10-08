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
    private readonly Lump<PlaneBSP> _planes;

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
        _planes = bsp.Planes;

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
    public List<(int Brush, int Leaf)> GetBrushes(int model)
    {
        var brushes = new SortedDictionary<int, int>();
        var visited = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(_models[model].HeadNodeIndex);

        while (stack.Count > 0)
        {
            int node = stack.Pop();
            if (!visited.Add(node))
                continue;

            if (node < 0)
            {
                int leafIndex = -1 - node;
                Leaf leaf = _leaves[leafIndex];
                for (int i = 0; i < leaf.NumMarkBrushIndices; i++)
                    brushes.TryAdd((int)_leafBrushes[leaf.FirstMarkBrushIndex + i], leafIndex);

                continue;
            }

            stack.Push(_nodes[node].Child1Index);
            stack.Push(_nodes[node].Child2Index);
        }

        return brushes.Select(x => (x.Key, x.Value)).ToList();
    }

    /// <summary>
    /// Gives each brush of a model its own model whose collision tree only contains that brush.
    /// The first brush keeps the original model. The new models copy the original's faces and bounds,
    /// so each one still draws the whole original entity (e.g. with showtriggers).
    /// </summary>
    /// <returns>The model for each brush, in brush index order</returns>
    public List<int> SplitModel(int model)
    {
        List<(int Brush, int Leaf)> brushes = GetBrushes(model);

        var models = new List<int>(brushes.Count);
        for (int i = 0; i < brushes.Count; i++)
        {
            int node = AddSingleBrushTree(brushes[i].Brush, brushes[i].Leaf, model);

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

    // A single node whose sides both lead to a leaf holding only the brush. The node's plane doesn't matter then.
    private int AddSingleBrushTree(int brush, int templateLeaf, int model)
    {
        (Vector3 mins, Vector3 maxs) = GetBrushBounds(brush) ?? (_models[model].Minimums, _models[model].Maximums);
        mins = ToShortBounds(mins, MathF.Floor);
        maxs = ToShortBounds(maxs, MathF.Ceiling);

        // Leaves reference their brushes with 16 bit indices in most versions
        if (_leafBrushes.Type == NumList.DataType.UInt16 && _leafBrushes.Count > ushort.MaxValue)
            throw new InvalidOperationException("Too many leaf brushes to add another");

        _leafBrushes.Add(brush);

        // Keep the contents, cluster, area and water data of a leaf the brush was already in
        var leaf = new Leaf(_leaves[templateLeaf], _leaves)
        {
            Minimums = mins,
            Maximums = maxs,
            FirstMarkFaceIndex = 0,
            NumMarkFaceIndices = 0,
            FirstMarkBrushIndex = _leafBrushes.Count - 1,
            NumMarkBrushIndices = 1,
        };
        _leaves.Add(leaf);
        int leafIndex = _leaves.Count - 1;

        foreach (Action<int> copyEntry in _copyPerLeafEntry)
            copyEntry(templateLeaf);

        var node = new Node(new byte[Node.GetStructLength(_bsp.MapType, _nodes.LumpInfo.version)], _nodes)
        {
            PlaneIndex = _brushSides[_brushes[brush].FirstSideIndex].PlaneIndex,
            Child1Index = -1 - leafIndex,
            Child2Index = -1 - leafIndex,
            Minimums = mins,
            Maximums = maxs,
            AreaIndex = leaf.Area,
        };
        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    // Built from the brush's axial planes, which compiled brushes always have (as bevels if nothing else)
    private (Vector3 Mins, Vector3 Maxs)? GetBrushBounds(int brush)
    {
        Brush brushData = _brushes[brush];

        float?[] mins = new float?[3];
        float?[] maxs = new float?[3];
        for (int side = brushData.FirstSideIndex; side < brushData.FirstSideIndex + brushData.NumSides; side++)
        {
            PlaneBSP plane = _planes[_brushSides[side].PlaneIndex];
            Vector3 normal = plane.Normal;

            for (int axis = 0; axis < 3; axis++)
            {
                if (normal[axis] == 1 && normal.LengthSquared() == 1)
                    maxs[axis] = plane.Distance;
                else if (normal[axis] == -1 && normal.LengthSquared() == 1)
                    mins[axis] = -plane.Distance;
            }
        }

        if (mins.Any(x => x is null) || maxs.Any(x => x is null))
            return null;

        return (new Vector3(mins[0]!.Value, mins[1]!.Value, mins[2]!.Value), new Vector3(maxs[0]!.Value, maxs[1]!.Value, maxs[2]!.Value));
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
