using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

public class ChecklistCacheTests
{
    [Fact]
    public void A_change_to_one_record_does_not_invalidate_anothers_slot()
    {
        var cache = new ChecklistCache();
        var aria = Items.Record(1);
        var kestrel = Items.Record(2, "Kestrel Vane");
        var first = cache.Get(Items.Catalog(), aria, new Settings(), 1, 1, Items.Now);
        var kept = cache.Get(Items.Catalog(), kestrel, new Settings(), 1, 1, Items.Now);

        // Aria's record moved on; Kestrel's did not, so Kestrel's slot must be the very same object.
        aria.Values[Items.Roulette.Id] = new DoneValue(true, "daily:2026-09-13");
        Assert.NotSame(first, cache.Get(Items.Catalog(), aria, new Settings(), 2, 1, Items.Now));
        Assert.Same(kept, cache.Get(Items.Catalog(), kestrel, new Settings(), 1, 1, Items.Now));
    }

    [Fact]
    public void A_settings_edit_and_a_new_minute_each_miss_the_cache()
    {
        var cache = new ChecklistCache();
        var record = Items.Record();
        var first = cache.Get(Items.Catalog(), record, new Settings(), 1, 1, Items.Now);
        Assert.Same(first, cache.Get(Items.Catalog(), record, new Settings(), 1, 1, Items.Now + TimeSpan.FromSeconds(30)));
        Assert.NotSame(first, cache.Get(Items.Catalog(), record, new Settings(), 1, 2, Items.Now));
        Assert.NotSame(first, cache.Get(Items.Catalog(), record, new Settings(), 1, 1, Items.Now + TimeSpan.FromMinutes(1)));
    }
}
