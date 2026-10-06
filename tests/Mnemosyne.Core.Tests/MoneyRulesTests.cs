using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Readers;

namespace Mnemosyne.Core.Tests;

/// <summary>The Money readers' pure rules, each tried at and around its edge. The readers' own reads are checked in game; where
/// their readings land in the checklist is proven on the real catalog by Money_rows_are_information_never_attention_and_a_blank_one_is_not_listed
/// in CatalogBuilderTests.</summary>
public class MoneyRulesTests
{
    [Fact]
    public void A_carried_roster_is_trusted_only_once_it_changes_or_the_manager_was_seen_not_ready()
    {
        // After a switch the manager may still hold the roster of the previous character until this character's own arrives. A
        // retainer belongs to one character, so a roster equal to the one last read under the previous character is not this
        // character's. GetRetainerCount counts the retainer array and IsReady is a separate flag, so a ready roster with no
        // retainers may be one whose list has not arrived; it is believed only once this login has shown this character's roster
        // non-empty. The arguments are (content id, login, ready, retainer ids).
        var guard = new RosterGuard();
        Assert.True(guard.Trust(1, true, true, [11, 12]));        // the plugin's first character: nothing to compare with
        Assert.True(guard.Trust(1, false, true, [11]));           // a dismissal while the character plays is believed
        Assert.True(guard.Trust(1, false, true, []));             // and so is the character's last: this login has shown the character's roster
        Assert.False(guard.Trust(1, true, true, []));             // a relog, ready before the character's list arrives: kept
        Assert.True(guard.Trust(1, false, true, [11, 12]));       // the character's list arrives
        Assert.False(guard.Trust(2, true, true, [11, 12]));       // a switch while the manager still holds the first character's roster
        Assert.False(guard.Trust(2, false, true, [12, 11, 0]));   // neither order nor an empty slot makes it this character's
        Assert.False(guard.Trust(2, false, true, []));            // the carried roster cleared, this character's not yet arrived: kept
        Assert.True(guard.Trust(2, false, true, [21]));           // the character's own roster arrives
        Assert.True(guard.Trust(2, false, true, [21, 22]));       // and stays trusted as it changes (a new hire)
        Assert.False(guard.Trust(3, true, true, [21, 22]));       // the next switch carries the second character's
        Assert.False(guard.Trust(3, false, false, []));           // a roster that is not ready is never read
        Assert.True(guard.Trust(3, false, true, [31]));           // once the manager was seen not ready, what it fills is this character's
        Assert.False(guard.Trust(4, true, true, []));             // no retainers before this login has shown this character's: kept

        // A round trip. The first character (1) logs in, then a second character (2) logs in while the manager still shows the
        // first character's own roster (the second has none of its own yet), then the first character logs back in with that
        // same true roster. The second character's carried read must stay distrusted; the first character's return must be
        // trusted again, not frozen because its own roster happens to equal what the second character was last seen (falsely)
        // carrying.
        Assert.True(guard.Trust(1, true, true, [11, 12]));        // the first character's own roster, trusted and remembered
        Assert.False(guard.Trust(2, true, true, [11, 12]));       // the second character, still carrying the first character's roster: not its own
        Assert.True(guard.Trust(1, true, true, [11, 12]));        // back on the first character: its own roster is trusted again, not frozen
    }

    [Fact]
    public void A_currency_zero_clears_only_when_it_was_read_and_gil_always_shows()
    {
        // A count is always data; a held count of zero clears the row, except gil's, which reads 0; a zero from a source that has not
        // answered says nothing, so the stored count survives.
        var now = Items.Now;
        Assert.Equal(new Reading("currency:28", new CounterValue(1_200, 2_000, now)), MoneyRules.Held("currency:28", 1_200, false, 2_000, false, now));
        Assert.Equal(new Reading("currency:28", null), MoneyRules.Held("currency:28", 0, true, 2_000, false, now));
        Assert.Null(MoneyRules.Held("currency:28", 0, false, 2_000, false, now));
        Assert.Equal(new Reading("currency:1", new CounterValue(0, 999_999_999, now)), MoneyRules.Held("currency:1", 0, true, 999_999_999, true, now));

        // The currency manager's own count for the items it holds, over its maximum only where it limits the item; its zero is read.
        var scrip = new CurrencyProbe(InManager: true, ManagerCount: 1_500, ManagerMax: 4_000, ManagerLimited: true, Held: 0, ContainerLoaded: false);
        Assert.Equal(new CounterValue(1_500, 4_000, now), MoneyRules.Currency("currency:41784", scrip, 4_000, false, now)!.Value);
        Assert.Equal(new CounterValue(1_500, 9_000, now), MoneyRules.Currency("currency:41784", scrip with { ManagerLimited = false }, 9_000, false, now)!.Value);
        Assert.Equal(new Reading("currency:21073", null), MoneyRules.Currency("currency:21073", scrip with { ManagerCount = 0 }, 65_000, false, now));

        // The inventory for everything else. Its zero is read only with the Currency container loaded and the manager filled, so the
        // inventory's zero never clears a currency the manager simply has not filled yet.
        var wolf = new CurrencyProbe(InManager: false, ManagerCount: 0, ManagerMax: 0, ManagerLimited: false, Held: 0, ContainerLoaded: true);
        Assert.Equal(new Reading("currency:25", null), MoneyRules.Currency("currency:25", wolf, 20_000, managerFilled: true, now));
        Assert.Null(MoneyRules.Currency("currency:25", wolf, 20_000, managerFilled: false, now));
        Assert.Null(MoneyRules.Currency("currency:25", wolf with { ContainerLoaded = false }, 20_000, managerFilled: true, now));
        Assert.Equal(new CounterValue(9_000, 20_000, now), MoneyRules.Currency("currency:25", wolf with { Held = 9_000, ContainerLoaded = false }, 20_000, false, now)!.Value);

        // A company's seals are over its rank's cap, and over the stack size only where the rank reads nothing.
        Assert.Equal(90_000, MoneyRules.SealCap(90_000, 200_000));
        Assert.Equal(200_000, MoneyRules.SealCap(0, 200_000));
    }

    [Fact]
    public void A_retainer_zero_is_believed_only_once_the_roster_proved_that_field()
    {
        // The roster may report ready before its gil and items arrive, and a zero then must not overwrite what is stored. A field's zero
        // is believed once any retainer of this roster has shown that field above zero since this login. The arguments are (content id,
        // login, roster).
        var now = Items.Now;
        var fill = new RosterFill();
        static RetainerFacts Retainer(ulong id, long gil, int items, int level) => new(id, "Mira", 17, level, gil, items, 0);
        var early = fill.Observe(1, false, [Retainer(11, 0, 0, 0), Retainer(12, 0, 0, 0)]);
        Assert.Equal(new RosterFields(false, false, false), early);
        Assert.Null(MoneyRules.Held("retainer:0:gil", 0, early.Gil, 999_999_999, true, now));                        // kept: the stored gil survives the login
        var filled = fill.Observe(1, false, [Retainer(11, 0, 0, 45), Retainer(12, 25_000, 0, 50)]);
        Assert.Equal(new RosterFields(true, false, true), filled);
        Assert.Equal(new CounterValue(0, 999_999_999, now), MoneyRules.Held("retainer:0:gil", 0, filled.Gil, 999_999_999, true, now)!.Value);   // a real zero now, and shown
        Assert.Null(MoneyRules.Held("retainer:0:slots", 0, filled.Items, 175, true, now));                           // no retainer has shown an item yet
        Assert.Equal(new RosterFields(true, false, true), fill.Observe(1, false, [Retainer(11, 0, 0, 45)]));         // the proof lasts the login
        Assert.Equal(new RosterFields(false, false, true), fill.Observe(1, true, [Retainer(11, 0, 0, 45)]));         // a relog starts over: gil's zero waits for a retainer to show gil again
        Assert.Equal(new RosterFields(false, false, false), fill.Observe(2, false, [Retainer(21, 0, 0, 0)]));        // a switch starts over
    }

    [Fact]
    public void A_retainer_level_reads_over_the_characters_own_class_level_and_a_retainer_with_no_class_keeps_only_its_name()
    {
        // A retainer's level is capped by the character's own level in the retainer's class, so at that level it reads done. The hand
        // file's level cap stands in when the character's level cannot be read, and the cap is never below the retainer's own level.
        var botanist = new ClassJobRow(17, "BTN", "botanist", 17, 0, 32, 17, 1, false, 0);
        const string Id = "retainer:0:level";
        Assert.Equal(new Reading(Id, new LevelValue(45, 90), Label: "Mira: Botanist"), MoneyRules.RetainerLevel(Id, "Mira", 17, botanist, 45, 90, 100, true));
        Assert.Equal(new LevelValue(90, 90), MoneyRules.RetainerLevel(Id, "Mira", 17, botanist, 90, 90, 100, false)!.Value);   // a level above 0 needs no proof
        Assert.Equal(new LevelValue(45, 100), MoneyRules.RetainerLevel(Id, "Mira", 17, botanist, 45, null, 100, true)!.Value);
        Assert.Equal(new LevelValue(45, 100), MoneyRules.RetainerLevel(Id, "Mira", 17, botanist, 45, 0, 100, true)!.Value);
        Assert.Equal(new LevelValue(52, 52), MoneyRules.RetainerLevel(Id, "Mira", 17, botanist, 52, 50, 100, true)!.Value);

        // No class yet (ClassJob 0, taken to be the client's no-class sentinel; not yet confirmed in game): the row clears and keeps
        // the retainer's name, once the roster has shown its levels.
        Assert.Equal(new Reading(Id, null, Label: "Mira"), MoneyRules.RetainerLevel(Id, "Mira", 0, null, 0, null, 100, true));
        Assert.Null(MoneyRules.RetainerLevel(Id, "Mira", 0, null, 0, null, 100, false));
        Assert.Null(MoneyRules.RetainerLevel(Id, "Mira", 17, null, 45, null, 100, true));                    // a class row that does not resolve says nothing

        // A retainer whose name has not arrived says nothing on the two clearing arms. A reading with no value and an empty label
        // would clear the retainer's stored name as well as the row (RecordUpdater.Name), so the row waits for the name.
        Assert.Null(MoneyRules.RetainerLevel(Id, "", 0, null, 0, null, 100, true));
        Assert.Null(MoneyRules.RetainerLevel(Id, "", 17, botanist, 0, 90, 100, true));
        Assert.Equal("Mira: Retainer Inventory", MoneyRules.RetainerLabel("Mira", "Retainer Inventory"));
        Assert.Equal("", MoneyRules.RetainerLabel("", "Retainer Inventory"));   // an empty name leaves the label off, not ": Retainer Inventory"
    }

    [Fact]
    public void A_live_read_wins_over_the_cache_for_the_rest_of_the_session_and_another_characters_cache_is_never_read()
    {
        // The item-search cache fills a storage row with no visit; the window's own count, once read, is not put back by a cache that
        // may lag it; a cache written for another content id is not this character's.
        var source = new StorageSource();
        Assert.Equal(40, source.Pick(1, null, new CacheCount(1, 40)));
        Assert.Null(source.Pick(1, null, new CacheCount(2, 55)));
        Assert.Null(source.Pick(1, null, null));
        Assert.Equal(42, source.Pick(1, 42, new CacheCount(1, 40)));
        Assert.Null(source.Pick(1, null, new CacheCount(1, 40)));
        Assert.Equal(43, source.Pick(1, 43, null));
        Assert.Equal(38, source.Pick(2, null, new CacheCount(2, 38)));   // a switch: the next character's cache is read again
    }

    [Fact]
    public void The_armoire_cache_counts_the_bits_of_the_storable_rows_and_nothing_past_its_words()
    {
        // CabinetItemUnlockBits is 125 words, 4,000 bits over the Cabinet row ids; bit i is word i / 32's bit i % 32 (the layout of the
        // client's other bit arrays; not yet confirmed in game for this one).
        var words = new uint[125];
        words[0] = 1u | 1u << 31;   // rows 0 and 31
        words[1] = 1u;              // row 32
        words[120] = 1u << 9;       // row 3,849, the last storable row
        Assert.Equal(4, MoneyRules.Stored([0u, 31u, 32u, 33u, 3849u], words));
        Assert.Equal(0, MoneyRules.Stored([4000u, 5047u], words));   // past the words: never counted, never a throw
        Assert.Equal(0, MoneyRules.Stored([0u], []));
    }
}
