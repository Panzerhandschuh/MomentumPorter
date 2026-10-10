namespace MomentumPorter.Tests;

using System.Text;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;
using MomentumPorter.Tools;
using ValvePak;
using Xunit;

public class BspPackGamesTests
{
    [Fact]
    public void WritesAConfigBspPackCanRead()
    {
        using var temp = new TempFolder();
        string steam = temp.Combine("Steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        var tool = new BspPackTool(temp.Combine("bspPack", BspPackTool.ExecutableName));
        Directory.CreateDirectory(tool.ToolDirectory);
        string excluded = temp.Combine("kz", "materials", "a.vmt");

        tool.WriteConfig(new BspPackGame("Kreedz Climbing", temp.Combine("games", "kzmod"), [excluded]), steam);

        Assert.Equal(temp.Combine("games", "kzmod"), tool.ReadConfiguredGameFolder());
        string[] config = File.ReadAllLines(tool.ConfigPath);
        Assert.DoesNotContain(config, string.IsNullOrWhiteSpace);
        Assert.Equal(steam, config[^1]);

        List<string> resources = File.ReadAllLines(tool.ResourceConfigPath).Where(x => !x.StartsWith('#')).ToList();
        Assert.Equal(resources.IndexOf("[ExcludeFiles]") + 1, resources.IndexOf(excluded));
        Assert.DoesNotContain(resources, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void LeavesOutSteamWhenItIsMissing()
    {
        using var temp = new TempFolder();
        var tool = new BspPackTool(temp.Combine(BspPackTool.ExecutableName));

        tool.WriteConfig(new BspPackGame("Kreedz Climbing", temp.Combine("kzmod"), []), temp.Combine("NoSteam"));

        Assert.DoesNotContain("[SteamPath]", File.ReadAllLines(tool.ConfigPath));
    }

    [Fact]
    public void ExtractsVpksOnceWithTheFirstVpkWinning()
    {
        using var temp = new TempFolder();
        string first = WriteVpk(temp.Combine("first.vpk"), ("materials/a.vmt", "first"));
        string second = WriteVpk(temp.Combine("second.vpk"), ("materials/a.vmt", "second"), ("sound/b.wav", "b"));
        string destination = temp.Combine("vpk");

        var log = new List<string>();
        var recordingLog = new DelegateLog((_, message) => log.Add(message));
        BspPackGames.ExtractVpks([first, second], destination, "Test", recordingLog, default);

        Assert.Equal("first", File.ReadAllText(Path.Combine(destination, "materials", "a.vmt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(destination, "sound", "b.wav")));
        Assert.False(Directory.Exists(destination + ".partial"));

        log.Clear();
        BspPackGames.ExtractVpks([first, second], destination, "Test", recordingLog, default);
        Assert.Empty(log);

        // A changed VPK extracts again
        WriteVpk(second, ("sound/c.wav", "c"));
        File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddMinutes(1));
        BspPackGames.ExtractVpks([first, second], destination, "Test", recordingLog, default);
        Assert.NotEmpty(log);
        Assert.True(File.Exists(Path.Combine(destination, "sound", "c.wav")));
        Assert.False(File.Exists(Path.Combine(destination, "sound", "b.wav")));
    }

    [Fact]
    public void FindsLooseFilesAVpkShadows()
    {
        using var temp = new TempFolder();
        string content = temp.Combine("kz");
        WriteFile(Path.Combine(content, "materials", "Rock", "shadowed.vmt"));
        WriteFile(Path.Combine(content, "materials", "rock", "own.vmt"));
        WriteFile(Path.Combine(content, "maps", "materials", "rock", "shadowed.vmt"));
        string vpk = WriteVpk(temp.Combine("hl2_misc_dir.vpk"), ("materials/rock/shadowed.vmt", "hl2"));

        List<string> shadowed = BspPackGames.FindShadowedLooseFiles(content, [vpk]);

        Assert.Equal([Path.Combine(content, "materials", "Rock", "shadowed.vmt")], shadowed);
    }

    [Fact]
    public void SetsUpKreedzClimbing()
    {
        using var temp = new TempFolder();
        string install = temp.Combine("KreedzClimbing");
        string content = Path.Combine(install, "kz");
        WriteVpk(Path.Combine(content, "kz_misc_dir.vpk"), multiChunk: true, ("models/kz/thing.mdl", "kz"));
        WriteVpk(Path.Combine(install, "hl2", "hl2_misc_dir.vpk"), ("materials/rock.vmt", "hl2"));
        WriteFile(Path.Combine(content, "materials", "rock.vmt"));
        WriteFile(Path.Combine(content, "materials", "kz_only.vmt"));
        string momentum = temp.CreateMomentumInstall();
        WriteFile(Path.Combine(momentum, "bin", "win64", "bspzip.exe"));
        var games = new BspPackGames(temp.Combine("bspPack"));

        BspPackGame game = games.Prepare("kzmod", content, momentum, new SteamLocator(steamRoot: null), NullLog.Instance);

        Assert.Equal(Path.Combine(games.RootDirectory, "kzmod"), game.GameFolder);
        string gameInfo = File.ReadAllText(Path.Combine(game.GameFolder, "gameinfo.txt"));
        string extracted = Path.Combine(game.GameFolder, "vpk");
        Assert.True(gameInfo.IndexOf($"\"{extracted}\"", StringComparison.Ordinal) < gameInfo.IndexOf($"\"{content}\"", StringComparison.Ordinal));
        Assert.Equal("kz", File.ReadAllText(Path.Combine(extracted, "models", "kz", "thing.mdl")));
        Assert.Equal([Path.Combine(content, "materials", "rock.vmt")], game.ExcludeFiles);

        // bspPack runs <game folder>/../bin/win64/bspzip.exe
        Assert.True(File.Exists(Path.Combine(game.GameFolder, "..", "bin", "win64", "bspzip.exe")));

        // Again, with the junction and the extraction already there
        games.Prepare("kzmod", content, momentum, new SteamLocator(steamRoot: null), NullLog.Instance);
    }

    [Fact]
    public void FailsWithoutMomentumsBspZip()
    {
        using var temp = new TempFolder();
        var games = new BspPackGames(temp.Combine("bspPack"));

        var ex = Assert.Throws<PortStepException>(() => games.Prepare("kzmod", null, temp.CreateMomentumInstall(), new SteamLocator(steamRoot: null), NullLog.Instance));
        Assert.Contains("bspzip", ex.Message);
    }

    [Fact]
    public void FailsForGamesItDoesNotKnow()
    {
        using var temp = new TempFolder();
        string momentum = temp.CreateMomentumInstall();
        WriteFile(Path.Combine(momentum, "bin", "win64", "bspzip.exe"));

        var ex = Assert.Throws<PortStepException>(() => new BspPackGames(temp.Combine("bspPack")).Prepare("tf2", null, momentum, new SteamLocator(steamRoot: null), NullLog.Instance));
        Assert.Contains("config.ini", ex.Message);
    }

    private static string WriteVpk(string path, params (string Path, string Data)[] files) => WriteVpk(path, false, files);

    private static string WriteVpk(string path, bool multiChunk, params (string Path, string Data)[] files)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var package = new Package();
        foreach ((string file, string data) in files)
            package.AddFile(file, Encoding.UTF8.GetBytes(data), multiChunk);
        package.Write(path);
        return path;
    }

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Path.GetFileName(path));
    }
}
