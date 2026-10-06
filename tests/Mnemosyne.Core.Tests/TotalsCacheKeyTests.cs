using Mnemosyne.Core.Checklist;

namespace Mnemosyne.Core.Tests;

/// <summary>The key Plugin.TotalsFor caches its last-built Totals lines on, so they are not rebuilt every frame the page is open. Equal
/// keys mean nothing that could move a line has moved: the same tracker Version (every record change bumps it), the same settings
/// Version, and the same UTC minute (which also covers a holder going stale). Pure: it reads nothing but what it is given, as
/// BoardCacheKeyTests pins for the All tasks board.</summary>
public class TotalsCacheKeyTests
{
    [Fact]
    public void The_same_inputs_give_an_equal_key_and_a_moved_tracker_version_settings_version_or_a_new_minute_each_differ()
    {
        var key = TotalsCacheKey.For(trackerVersion: 3, settingsVersion: 1, Items.Now);

        Assert.Equal(key, TotalsCacheKey.For(trackerVersion: 3, settingsVersion: 1, Items.Now));                 // the same inputs
        Assert.Equal(key, TotalsCacheKey.For(trackerVersion: 3, settingsVersion: 1, Items.Now.AddSeconds(30)));  // still the same minute

        Assert.NotEqual(key, TotalsCacheKey.For(trackerVersion: 4, settingsVersion: 1, Items.Now));               // the tracker version moved
        Assert.NotEqual(key, TotalsCacheKey.For(trackerVersion: 3, settingsVersion: 2, Items.Now));               // the settings version moved
        Assert.NotEqual(key, TotalsCacheKey.For(trackerVersion: 3, settingsVersion: 1, Items.Now.AddMinutes(1))); // a new minute
    }
}
