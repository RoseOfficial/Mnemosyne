using Mnemosyne.Core.Readers;

namespace Mnemosyne.Core.Tests;

/// <summary>The Collections readers' pure rules, each tried at and around its edge. The readers' own reads are checked in
/// game; where their readings land in the checklist is proven on the real catalog by Collections_rows_are_information_never_attention
/// in CatalogBuilderTests.</summary>
public class CollectionRulesTests
{
    private readonly record struct Print(ulong Owner, int Fish, int Recipes);

    [Fact]
    public void A_loaded_list_is_trusted_only_after_it_was_seen_unloaded_for_the_same_character()
    {
        // A list already loaded at the first read may be the previous character's, carried across a switch, or one loaded before the
        // plugin reloaded; either way it was never seen requested for this character, so it is not counted for this character.
        var guard = new ListLoadGuard();
        Assert.False(guard.Trust(1, true));    // loaded at the first read: not this character's to count
        Assert.False(guard.Trust(1, false));   // not loaded: nothing to read, and now seen so
        Assert.True(guard.Trust(1, true));     // this character's own visit
        Assert.True(guard.Trust(1, true));     // and it stays trusted while loaded
        Assert.False(guard.Trust(2, true));    // a switch with the list still loaded: the previous character's list
        Assert.False(guard.Trust(2, true));
        Assert.False(guard.Trust(2, false));   // the client drops or requests it again
        Assert.True(guard.Trust(2, true));     // this character's own
        Assert.False(guard.Trust(1, true));    // back to the first character: seen unloaded under 2, never under 1 since
    }

    [Fact]
    public void A_polled_read_runs_only_when_its_fingerprint_moved()
    {
        // A full read of the logs or the achievement lists costs thousands of checks, so the tick reads in full only when a
        // cheap fingerprint moved; any other trigger always reads, and the fingerprint names the character.
        var print = new Print(1, 10, 200);
        Assert.True(CollectionRules.ReadNow(ReaderTrigger.Interval, ReaderTrigger.Interval, null, print));                           // never read yet
        Assert.False(CollectionRules.ReadNow(ReaderTrigger.Interval, ReaderTrigger.Interval, print, print));                         // a tick that changed nothing
        Assert.True(CollectionRules.ReadNow(ReaderTrigger.Interval, ReaderTrigger.Interval, print, print with { Fish = 11 }));        // a fish caught
        Assert.True(CollectionRules.ReadNow(ReaderTrigger.Interval, ReaderTrigger.Interval, print, print with { Owner = 2 }));       // a character switch
        Assert.True(CollectionRules.ReadNow(ReaderTrigger.Login, ReaderTrigger.Interval, print, print));                             // a login always reads
        Assert.True(CollectionRules.ReadNow(ReaderTrigger.Interval | ReaderTrigger.LevelOrJob, ReaderTrigger.Interval, print, print)); // a class can open its log
        Assert.False(CollectionRules.ReadNow(ReaderTrigger.Window, ReaderTrigger.Interval | ReaderTrigger.Window, print, print));    // the lists poll a window fire too
    }

    [Fact]
    public void A_log_row_beyond_the_clients_array_is_not_caught_and_is_counted_apart()
    {
        // BitArray.Get throws past its bit count, so a fish row a patch adds before the client structs catch up would
        // disable the reader for the session. Read through a bound instead: the row is not caught, and it is named apart.
        bool? Bits(int index) => index is >= 0 and < 1522 ? index % 2 == 0 : null;   // a stand-in array bounded at 1,522 bits, every even bit set
        var (owned, beyond) = CollectionRules.Owned([2u, 3u, 4u, 1600u], 0, Bits);
        Assert.Equal(2, owned);                   // 2 and 4 caught, 3 not
        Assert.Equal([1600u], beyond);            // past the array: not caught, and said once by the reader
        // A spearfishing row's bit is its id minus 20000; a row below 20000 falls outside the array too.
        var (spearfish, outside) = CollectionRules.Owned([20002u, 20003u, 19999u], 20000, Bits);
        Assert.Equal(1, spearfish);
        Assert.Equal([19999u], outside);
        Assert.Equal(0, CollectionRules.Owned([], 0, Bits).Owned);

        // The fish array's 191 bytes hold the last log fish, row 1,522, which a read bounded by the declared BitCount (1,522) refuses.
        var fish = new byte[191];
        fish[1522 / 8] = 1 << (1522 % 8);
        var (last, past) = CollectionRules.Owned([1522u, 1528u], 0, CollectionRules.Bits(fish));
        Assert.Equal(1, last);
        Assert.Equal([1528u], past);
    }
}
