namespace MomentumPorter.Tests;

using MomentumPorter.Steam;
using Xunit;

public class SteamLocatorTests
{
    [Fact]
    public void ParsesLibraryPaths()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                }
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                }
            }
            """;

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamLocator.ParseLibraryPaths(vdf).ToList());
    }

    [Fact]
    public void ParsesAppManifest()
    {
        const string acf = """
            "AppState"
            {
                "appid"		"240"
                "name"		"Counter-Strike: Source"
                "installdir"		"Counter-Strike Source"
            }
            """;

        Dictionary<string, string> pairs = SteamLocator.ParseVdfPairs(acf);
        Assert.Equal("Counter-Strike Source", pairs["installdir"]);
        Assert.Equal("240", pairs["appid"]);
    }

    [Fact]
    public void FindsGamesByManifestAndFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));
        try
        {
            string steamapps = Path.Combine(root, "steamapps");
            Directory.CreateDirectory(Path.Combine(steamapps, "common", "Counter-Strike Source", "cstrike"));
            Directory.CreateDirectory(Path.Combine(steamapps, "common", "KreedzClimbing", "kz"));
            File.WriteAllText(Path.Combine(steamapps, "appmanifest_240.acf"), "\"AppState\" { \"installdir\" \"Counter-Strike Source\" }");
            File.WriteAllText(Path.Combine(steamapps, "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\" \"" + root.Replace("\\", "\\\\") + "\" } }");

            var locator = new SteamLocator(root);

            GameLocation? css = locator.Find(KnownGame.CounterStrikeSource);
            Assert.NotNull(css);
            Assert.Equal(Path.Combine(steamapps, "common", "Counter-Strike Source", "cstrike"), css.ContentDirectory);

            GameLocation? kz = locator.Find(KnownGame.KreedzClimbing);
            Assert.NotNull(kz);
            Assert.Equal(Path.Combine(steamapps, "common", "KreedzClimbing", "kz"), kz.ContentDirectory);

            Assert.Null(locator.Find(KnownGame.Quake3));
            Assert.Single(locator.Libraries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
