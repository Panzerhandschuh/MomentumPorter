namespace MomentumPorter.Assets.Sounds;

using MomentumPorter.Assets.Weapons;

/// <summary>
/// A game's surface properties (scripts/surfaceproperties*.txt): each surface's footstep sounds, following <c>base</c>
/// for the keys a surface doesn't set. Used to find which of another game's footsteps a Momentum surface gets.
/// </summary>
public sealed class SurfaceProperties
{
    public const string ManifestPath = "scripts/surfaceproperties_manifest.txt";

    private readonly Dictionary<string, KeyValuesNode> _surfaces = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Surfaces => _surfaces.Keys;

    /// <summary>
    /// Loads the files the manifest lists, in its order (a later definition of a surface replaces an earlier one, as in
    /// the engine), or scripts/surfaceproperties.txt if there's no manifest. Missing files are skipped.
    /// </summary>
    /// <param name="readText">A game file's text by game path, or null if the game doesn't have it</param>
    public static SurfaceProperties Load(Func<string, string?> readText)
    {
        var properties = new SurfaceProperties();
        List<string> files = ["scripts/surfaceproperties.txt"];
        if (readText(ManifestPath) is { } manifest)
        {
            try
            {
                files = KeyValuesNode.Parse(manifest, ManifestPath).FindAll("file").Where(x => !x.IsBlock).Select(x => x.Value!).ToList();
            }
            catch (FormatException)
            {
                // Keep the default file
            }
        }

        foreach (string file in files)
        {
            if (readText(Vpk.ContentPath.Normalize(file)) is { } text)
                properties.AddFile(text, file);
        }

        return properties;
    }

    public void AddFile(string text, string? fileName = null)
    {
        KeyValuesNode document;
        try
        {
            document = KeyValuesNode.ParseDocument(text, fileName);
        }
        catch (FormatException)
        {
            return;
        }

        foreach (KeyValuesNode surface in document.Children.Where(x => x.IsBlock))
            _surfaces[surface.Key] = surface;
    }

    public bool Contains(string surface) => _surfaces.ContainsKey(surface);

    /// <summary>
    /// The surface's <c>stepleft</c> or <c>stepright</c> sound, from the surface or its bases. Null if none sets it.
    /// </summary>
    public string? GetStep(string surface, bool left) => GetValue(surface, left ? "stepleft" : "stepright");

    /// <summary>A key's value on the surface or the nearest base that sets it.</summary>
    public string? GetValue(string surface, string key)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (string? name = surface; name is not null && seen.Add(name) && _surfaces.TryGetValue(name, out KeyValuesNode? node);)
        {
            if (node.GetString(key) is { Length: > 0 } value)
                return value;

            name = node.GetString("base");
        }

        return null;
    }

    /// <summary>The surfaces whose left (or right) footstep is the sound, e.g. every surface that uses <c>SolidMetal.StepLeft</c>.</summary>
    public IEnumerable<string> SurfacesUsing(string stepSound, bool left)
    {
        return _surfaces.Keys.Where(x => string.Equals(GetStep(x, left), stepSound, StringComparison.OrdinalIgnoreCase));
    }
}
