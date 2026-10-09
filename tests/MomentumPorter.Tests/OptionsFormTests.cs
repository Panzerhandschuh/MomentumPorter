namespace MomentumPorter.Tests;

using MomentumPorter.Options;
using MomentumPorter.UI.ViewModels.Shared;
using Xunit;

public class OptionsFormTests
{
    public enum Mode
    {
        Fast,
        Best,
    }

    public sealed class SampleOptions
    {
        [PortOption("Game", "Which game.")]
        public string? Game { get; set; }

        [PortOption("Enabled", "Turns it on.", Order = 1)]
        public bool Enabled { get; set; } = true;

        [PortOption("Count", "How many.", Order = 2, Min = 1, Max = 10)]
        public int Count { get; set; } = 3;

        [PortOption("Scale", "How big.", Group = "Textures", Order = 0)]
        public float Scale { get; set; } = 1.5f;

        [PortOption("Mode", "How.", Group = "Textures", Order = 1)]
        public Mode Mode { get; set; } = Mode.Best;

        [PortOption("Wad folder", "Where the WADs are.", Group = "Textures", Order = 2, Path = PathKind.Folder, Advanced = true)]
        public string? WadFolder { get; set; }

        [PortOption("Name", "Not nullable.", Group = "After", Order = 0)]
        public string Name { get; set; } = "x";

        [PortOption("Tweak", "Only for experts.", Group = "Experimental", Order = 0, Advanced = true)]
        public bool Tweak { get; set; }

        public bool NotAnOption { get; set; }
    }

    [Fact]
    public void BuildsFieldsFromTheAttributes()
    {
        var form = new OptionsFormViewModel(new SampleOptions());

        // Groups in the order the class declares them; a group of nothing but advanced options waits for the toggle
        Assert.Equal(["General", "Textures", "After"], form.Groups.Select(x => x.Name));
        Assert.Equal(8, form.Fields.Count());
        Assert.Null(form.Find(nameof(SampleOptions.NotAnOption)));

        Assert.IsType<TextOptionViewModel>(form.Find("Game"));
        Assert.IsType<BoolOptionViewModel>(form.Find("Enabled"));
        Assert.IsType<NumberOptionViewModel>(form.Find("Count"));
        Assert.IsType<NumberOptionViewModel>(form.Find("Scale"));
        Assert.IsType<EnumOptionViewModel>(form.Find("Mode"));
        var wad = Assert.IsType<TextOptionViewModel>(form.Find("WadFolder"));
        Assert.True(wad.CanBrowse);
        Assert.Equal("Where the WADs are.", wad.Help);

        OptionGroupViewModel textures = form.Groups.Single(x => x.Name == "Textures");
        Assert.Equal(["Scale", "Mode"], textures.Fields.Select(x => x.Name));
        Assert.All(form.Groups, x => Assert.True(x.ShowHeader));
    }

    [Fact]
    public void TheAdvancedToggleShowsTheAdvancedOptionsInline()
    {
        var form = new OptionsFormViewModel(new SampleOptions());
        Assert.True(form.HasAdvanced);
        Assert.DoesNotContain("WadFolder", form.VisibleFields.Select(x => x.Name));

        form.ShowAdvanced = true;

        Assert.Equal(["General", "Textures", "After", "Experimental"], form.Groups.Select(x => x.Name));
        Assert.Equal(["Scale", "Mode", "WadFolder"], form.Groups.Single(x => x.Name == "Textures").Fields.Select(x => x.Name));
        Assert.Equal(["Tweak"], form.Groups.Single(x => x.Name == "Experimental").Fields.Select(x => x.Name));

        // A filter and the toggle combine, and a hidden field keeps its value
        ((TextOptionViewModel)form.Find("WadFolder")!).Value = @"C:\wads";
        form.SetFilter(x => x.Info.Attribute.Group != "Textures");
        Assert.Equal(["General", "After", "Experimental"], form.Groups.Select(x => x.Name));
        form.ShowAdvanced = false;
        Assert.Equal(["General", "After"], form.Groups.Select(x => x.Name));
        Assert.Equal(@"C:\wads", ((SampleOptions)form.Options).WadFolder);
    }

    [Fact]
    public void AFormOfOneGroupHasNoHeading()
    {
        var form = new OptionsFormViewModel(new MapPortOptions());

        OptionGroupViewModel general = Assert.Single(form.Groups);
        Assert.False(general.ShowHeader);
        Assert.False(form.HasAdvanced);
        Assert.Equal([nameof(MapPortOptions.Compress), nameof(MapPortOptions.Pack)], general.Fields.Select(x => x.Name));
    }

    [Fact]
    public void WritesChangesToTheOptions()
    {
        var options = new SampleOptions();
        var form = new OptionsFormViewModel(options);

        ((BoolOptionViewModel)form.Find("Enabled")!).Value = false;
        ((EnumOptionViewModel)form.Find("Mode")!).Value = Mode.Fast;
        ((NumberOptionViewModel)form.Find("Scale")!).Value = 2.25m;
        ((TextOptionViewModel)form.Find("WadFolder")!).Value = @"C:\wads";

        Assert.False(options.Enabled);
        Assert.Equal(Mode.Fast, options.Mode);
        Assert.Equal(2.25f, options.Scale);
        Assert.Equal(@"C:\wads", options.WadFolder);

        // Clearing a nullable string stores null, a non-nullable one an empty string
        ((TextOptionViewModel)form.Find("WadFolder")!).Value = "";
        ((TextOptionViewModel)form.Find("Name")!).Value = null;
        Assert.Null(options.WadFolder);
        Assert.Equal("", options.Name);
    }

    [Fact]
    public void ClampsNumbersToTheirBounds()
    {
        var options = new SampleOptions();
        var count = (NumberOptionViewModel)new OptionsFormViewModel(options).Find("Count")!;

        Assert.Equal(1, count.Minimum);
        Assert.Equal(10, count.Maximum);
        Assert.True(count.IsInteger);

        count.Value = 50;
        Assert.Equal(10, options.Count);
        count.Value = 4.6m;
        Assert.Equal(5, options.Count);

        // Ints can't be empty
        count.Value = null;
        Assert.Equal(5, options.Count);
    }

    [Fact]
    public void ReloadsAfterTheOptionsChange()
    {
        var options = new SampleOptions();
        var form = new OptionsFormViewModel(options);

        options.Enabled = false;
        options.Game = "kzmod";
        form.Reload();

        Assert.False(((BoolOptionViewModel)form.Find("Enabled")!).Value);
        Assert.Equal("kzmod", ((TextOptionViewModel)form.Find("Game")!).Value);
    }
}
