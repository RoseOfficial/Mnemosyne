using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Core.Tests;

/// <summary>The icon numbers, from the fixture sheets. A job row carries its job's framed icon, 62100 plus the ClassJob row its name
/// comes from; a currency row, MGP's under Content among them, carries its item's Icon; every other row has none. The groups golden
/// does not print them, so it stays byte-identical.</summary>
public class IconTests
{
    private static readonly CatalogRows Rows = new FixtureCatalogSource().Load();
    private static readonly Catalog Built = CatalogBuilder.Build(Rows, CatalogExtras.Load());

    private static uint? IconOf(string id) => Built.Items.Single(i => i.Id == id).IconId;

    private static bool IsCurrency(ItemDefinition item) => item.Id.StartsWith(MoneyIds.CurrencyPrefix, StringComparison.Ordinal);

    [Fact]
    public void Every_job_row_carries_the_framed_icon_of_the_class_job_row_it_is_named_after()
    {
        // The ClassJob sheet has no icon column, and the game numbers its framed job icons 62100 plus the ClassJob row. The row a job
        // item is named after gives the icon: Paladin (19) for the Gladiator's experience, Monk
        // (20) for the Pugilist's, Summoner (27) for the Arcanist's, which Scholar shares, Carpenter (8), a crafter with no job above it, and Blue
        // Mage (36) and Beastmaster (43), which have no base class.
        Assert.Equal(62119u, IconOf("job:1"));
        Assert.Equal(62120u, IconOf("job:0"));
        Assert.Equal(62127u, IconOf("job:18"));
        Assert.Equal(62108u, IconOf("job:7"));
        Assert.Equal(62136u, IconOf("job:25"));
        Assert.Equal(62143u, IconOf("job:32"));
        var jobs = Built.InCategory(Category.Jobs).Where(i => i.Id.StartsWith("job:", StringComparison.Ordinal)).ToList();
        Assert.Equal(33, jobs.Count);
        Assert.All(jobs, job => Assert.InRange(job.IconId!.Value, CatalogBuilder.JobIconBase + 1, CatalogBuilder.JobIconBase + 99));
    }

    [Fact]
    public void Every_currency_row_carries_its_items_icon_mgp_under_content_included()
    {
        // The Item sheet's Icon of the currency's item: MGP's row is a currency row too, so the Totals page can draw its icon. The
        // fixture carries the Icon of each of its 77 currencies, read from the game data it was dumped from.
        Assert.Equal(77, Rows.Currencies.Count);
        Assert.All(Rows.Currencies, currency => Assert.NotEqual(0u, currency.Icon));
        Assert.Equal(65002u, IconOf(MoneyIds.CurrencyId(MoneyIds.Gil)));
        Assert.Equal(65023u, IconOf(MoneyIds.CurrencyId(28)));          // Poetics, a tomestone
        Assert.Equal(65054u, IconOf(MoneyIds.CurrencyId(21073)));       // Ixali Oaknot, an allied society's currency
        Assert.Equal(65025u, IconOf(MoneyIds.CurrencyId(CatalogBuilder.MgpItemId)));
        Assert.Equal(Category.Content, Built.Items.Single(i => i.Id == MoneyIds.CurrencyId(CatalogBuilder.MgpItemId)).Category);
        var currencies = Built.Items.Where(IsCurrency).ToList();
        Assert.Equal(57, currencies.Count);
        Assert.All(currencies, currency => Assert.NotNull(currency.IconId));
    }

    [Fact]
    public void Every_other_row_has_no_icon_and_a_currency_the_rows_give_no_icon_builds_none()
    {
        // A row with no icon draws a blank square. The Blue Mage spells and the Grand Company rows in Jobs, the retainer and storage
        // rows in Money, and every quest, duty and reset have none. Rows from an older dump carry no "icon" on a currency, which reads
        // as 0, and build a currency row with none rather than icon 0.
        Assert.All(Built.Items.Where(i => !i.Id.StartsWith("job:", StringComparison.Ordinal) && !IsCurrency(i)), item => Assert.Null(item.IconId));
        Assert.Null(IconOf("blu:spells"));

        var older = Rows with { Currencies = Rows.Currencies.Select(c => c with { Icon = 0 }).ToList() };
        var built = CatalogBuilder.Build(older, CatalogExtras.Load());
        Assert.All(built.Items.Where(IsCurrency), item => Assert.Null(item.IconId));
    }
}
