namespace MomentumPorter.Assets.Particles;

using MomentumPorter.Pipeline;

/// <summary>
/// Momentum's <c>particles/particles_manifest.txt</c>, which is the only way particle files get loaded. A pack that
/// needs a particle file Momentum doesn't list ships a copy of the manifest with the file added; a <c>!</c> in front
/// of a path precaches every system in it, as Momentum does for its weapon effects.
/// </summary>
public static class ParticleManifest
{
    public const string Path = "particles/particles_manifest.txt";

    /// <summary>Momentum's manifest with these files added, once each.</summary>
    public static string WithFiles(string momentumManifest, IEnumerable<string> files)
    {
        var missing = files
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => !momentumManifest.Contains(x.TrimStart('!'), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (missing.Count == 0)
            return momentumManifest;

        int close = momentumManifest.LastIndexOf('}');
        if (close < 0)
            throw new PortStepException($"Momentum's {Path} isn't a manifest");

        string added = string.Concat(missing.Select(x => $"    \"file\"        \"{x}\"\n"));
        return momentumManifest[..close].TrimEnd()
            + "\n\n    // Added by MomentumPorter\n" + added
            + momentumManifest[close..];
    }
}
