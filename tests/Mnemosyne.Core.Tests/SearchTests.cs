using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

public class SearchTests
{
    [Fact]
    public void Search_matches_name_group_and_category_and_keeps_the_badge()
    {
        var c = Assembler.Assemble(Items.Catalog(), SummariserTests.Aria(), new Settings(), Items.Now);
        var moon = Search.Filter(c, "moonfire");
        Assert.Single(moon.EndingSoon);
        Assert.Single(moon.Sections.Single(s => s.Category == Category.Events).Groups);
        Assert.Equal(c.Badge, moon.Badge);

        var tanks = Search.Filter(c, "TANKS");
        Assert.Equal(["job:1"], tanks.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries).Select(e => e.Item.Id));

        var money = Search.Filter(c, "money");
        Assert.Equal(["timer:venture:1"], money.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries).Select(e => e.Item.Id));

        Assert.Same(c, Search.Filter(c, "  "));
    }
}
