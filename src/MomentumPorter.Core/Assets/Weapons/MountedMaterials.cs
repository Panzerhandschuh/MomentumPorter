namespace MomentumPorter.Assets.Weapons;

using System.Text;
using System.Text.RegularExpressions;
using MomentumPorter.Assets.Models;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;

/// <summary>
/// Keeps a mounted game's model on its own materials when another game Momentum mounts (or Momentum itself) ships a
/// material at the same path: CS:S's <c>v_knife_t.mdl</c> uses <c>models/weapons/v_models/knife_t/knife_t</c>, which
/// CS:GO ships too for its own knife, laid out differently, and only one of the two can load. Such materials are copied
/// into the pack under <c>materials/porter/&lt;game&gt;/</c> with their textures, and the model gets those folders in front of
/// its <c>$cdmaterials</c> (<see cref="StudioModelPatcher.PrependMaterialDirectories"/>), so it finds its own copy first
/// and the other game's model keeps the original path.
/// </summary>
public sealed partial class MountedMaterials(GameContent content, IReadOnlyList<GameContent> others, string? momentumGameDirectory, string gameId, IPackSink sink)
{
    // Material path (under materials/, no extension) -> who else ships it, null if nobody; each is checked and copied once
    private readonly Dictionary<string, string?> _clashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder under <c>materials/</c> the pack's copies go in.</summary>
    public string Prefix => $"porter/{gameId}/";

    /// <summary>
    /// Copies the model's clashing materials into the pack and returns the folders to put in front of its
    /// <c>$cdmaterials</c>, in its own order and format; empty when none of its materials clash.
    /// </summary>
    public IReadOnlyList<string> Redirect(PortJobContext context, string model, StudioModelHeader header)
    {
        var directories = new List<string>();
        var clashing = new List<string>();
        foreach (string directory in header.TextureDirectories)
        {
            bool redirected = false;
            foreach (string texture in header.Textures)
            {
                string material = MaterialName(directory.Trim('/', '\\') + "/" + texture.Trim('/', '\\'));
                if (Clash(context, material) is { } other)
                {
                    redirected = true;
                    clashing.Add($"{material} ({other})");
                }
            }

            if (redirected)
                directories.Add(Prefix.Replace('/', '\\') + directory.TrimStart('/', '\\'));
        }

        if (clashing.Count > 0)
        {
            context.Report.Change($"Materials copied into the pack because another game ships them too: {string.Join(", ", clashing)}");
            context.Log.Info($"{model} uses materials another game Momentum mounts ships at the same path; its own copies are in the pack under materials/{Prefix}");
        }

        return directories;
    }

    /// <summary>The model file with the folders from <see cref="Redirect"/> in front of its <c>$cdmaterials</c>.</summary>
    public byte[] Patch(PortJobContext context, string model, byte[] mdl)
    {
        StudioModelHeader header;
        try
        {
            header = StudioModelHeader.Read(mdl);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            context.Log.Info($"Couldn't read {model}'s materials to check them against the other mounted games: {ex.Message}");
            return mdl;
        }

        IReadOnlyList<string> directories = Redirect(context, model, header);
        return directories.Count == 0 ? mdl : StudioModelPatcher.PrependMaterialDirectories(mdl, directories);
    }

    /// <summary>Who else ships the material or one of its textures, after copying it into the pack; null if nobody.</summary>
    private string? Clash(PortJobContext context, string material)
    {
        if (_clashes.TryGetValue(material, out string? known))
            return known;

        _clashes[material] = null;
        if (content.Read($"materials/{material}.vmt") is not { } vmt)
            return null;

        string text = Encoding.Latin1.GetString(vmt);
        var textures = TextureReferences(text).Distinct().Where(x => content.Exists($"materials/{x}.vtf")).ToList();
        string? other = FindClash($"materials/{material}.vmt", vmt)
            ?? textures.Select(x => $"materials/{x}.vtf").Select(x => FindClash(x, content.Read(x))).FirstOrDefault(x => x is not null);
        if (other is null)
            return null;

        var renamed = textures.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string rewritten = Token().Replace(text, match =>
        {
            string value = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Value;
            return renamed.Contains(MaterialName(value)) ? $"\"{Prefix}{MaterialName(value)}\"" : match.Value;
        });
        sink.AddFile($"materials/{Prefix}{material}.vmt", Encoding.Latin1.GetBytes(rewritten));
        foreach (string texture in textures)
        {
            if (content.Read($"materials/{texture}.vtf") is { } vtf)
                sink.AddFile($"materials/{Prefix}{texture}.vtf", vtf);
        }

        if (Include().Match(text) is { Success: true } include)
            context.Report.Warn($"materials/{material}.vmt includes {include.Groups["path"].Value}, which isn't copied with it, so it may still show another game's copy");

        return _clashes[material] = other;
    }

    /// <summary>Who else ships the file with different contents: another mounted game, or Momentum; null if nobody.</summary>
    private string? FindClash(string path, byte[]? data)
    {
        if (data is null)
            return null;

        if (momentumGameDirectory is not null)
        {
            string loose = Path.Combine(momentumGameDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(loose) && !File.ReadAllBytes(loose).AsSpan().SequenceEqual(data))
                return "Momentum";
        }

        foreach (GameContent game in others)
        {
            if (game.Read(path) is { } theirs && !theirs.AsSpan().SequenceEqual(data))
                return game.Name;
        }

        return null;
    }

    /// <summary>The values in a .vmt that could be texture paths ($basetexture, $bumpmap, $envmapmask...).</summary>
    private static IEnumerable<string> TextureReferences(string vmt)
    {
        foreach (Match match in Token().Matches(vmt))
        {
            string value = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Value;
            if (value.Length > 0 && !value.StartsWith('$') && !value.StartsWith('%') && !value.StartsWith("//", StringComparison.Ordinal) && value.IndexOfAny(['[', '{', ' ']) < 0)
                yield return MaterialName(value);
        }
    }

    /// <summary>A material or texture path as it's looked up: under materials/, forward slashes, lower case, no extension.</summary>
    private static string MaterialName(string path)
    {
        string normalized = ContentPath.Normalize(path.Trim()).ToLowerInvariant();
        if (normalized.StartsWith("materials/", StringComparison.Ordinal))
            normalized = normalized["materials/".Length..];

        return normalized.EndsWith(".vtf", StringComparison.Ordinal) || normalized.EndsWith(".vmt", StringComparison.Ordinal) ? normalized[..^4] : normalized;
    }

    [GeneratedRegex("""
        "(?<quoted>[^"\r\n]*)"|[^\s"{}]+
        """)]
    private static partial Regex Token();

    [GeneratedRegex("""
        "?include"?\s+"?(?<path>[^"\s]+)
        """, RegexOptions.IgnoreCase)]
    private static partial Regex Include();
}
