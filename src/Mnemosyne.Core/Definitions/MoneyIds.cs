namespace Mnemosyne.Core.Definitions;

/// <summary>The ids the Money rows are built under and read by, and the sheet rows that name them, in one place, so the
/// reducer, the builder and the readers cannot drift apart, as CollectionIds serves the Collections rows.</summary>
public static class MoneyIds
{
    /// <summary>Every currency row is currency:&lt;item id&gt;, the id MGP has carried in Content since it was added.</summary>
    public const string CurrencyPrefix = "currency:";

    public static string CurrencyId(uint itemId) => CurrencyPrefix + itemId;

    /// <summary>Gil is item 1, and the Storm, Serpent and Flame Seals are items 20 to 22, read with GetCompanySeals for the
    /// Maelstrom, the Order of the Twin Adder and the Immortal Flames, GrandCompany rows 1 to 3, in that order (Lumina's
    /// GrandCompanyRank: its Maelstrom, Serpents and Flames columns, in that order; items 20 to 22 are named "Storm Seal",
    /// "Serpent Seal" and "Flame Seal").</summary>
    public const uint Gil = 1, FirstSeal = 20, LastSeal = 22;

    /// <summary>The GrandCompany row a seal item belongs to, 1 to 3.</summary>
    public static byte SealCompany(uint sealItemId) => (byte)(sealItemId - FirstSeal + 1);

    /// <summary>The seal item of a GrandCompany row, 1 to 3, the inverse of SealCompany: the Maelstrom's Storm Seals, the Twin Adder's Serpent
    /// Seals, the Immortal Flames' Flame Seals (the summary above). Null for row 0, no company, and for any other row. The Company seals
    /// column reads it.</summary>
    public static uint? SealItem(uint companyRow) => companyRow is >= 1 and <= LastSeal - FirstSeal + 1 ? FirstSeal + companyRow - 1 : null;

    /// <summary>The roster holds ten retainers, the same ten slots the venture rows use.</summary>
    public const int RetainerSlots = 10;

    /// <summary>retainer:&lt;slot&gt;:level, :gil and :slots, the slot being the client's sorted index, as the venture rows'
    /// is; never RetainerId.</summary>
    public const string RetainerPrefix = "retainer:", Level = "level", RetainerGil = "gil", Slots = "slots";

    public static string RetainerId(int slot, string field) => $"{RetainerPrefix}{slot}:{field}";

    /// <summary>The ten retainer-gil ids, built once. Holdings.RetainerGil reads this instead of
    /// formatting RetainerId(slot, RetainerGil) itself on every call.</summary>
    public static readonly IReadOnlyList<string> RetainerGilIds = Enumerable.Range(0, RetainerSlots).Select(slot => RetainerId(slot, RetainerGil)).ToList();

    /// <summary>reset:venture:&lt;slot&gt;, the Resets timer of each roster slot, the same ten slots. Named here so the
    /// builder and the Ventures ready column cannot drift apart.</summary>
    public const string VenturePrefix = "reset:venture:";

    public static string VentureId(int slot) => VenturePrefix + slot;

    /// <summary>The six storage rows.</summary>
    public const string Bags = "storage:bags", Armoury = "storage:armoury", Saddlebag = "storage:saddlebag", PremiumSaddlebag = "storage:saddlebag:premium",
        Dresser = "storage:dresser", Armoire = "storage:armoire";

    /// <summary>The MainCommand rows that name the Currency group and three storage rows in the client's language: Inventory 10,
    /// Armoury Chest 25, Currency 66 and Chocobo Saddlebag 77.</summary>
    public const uint InventoryCommand = 10, ArmouryChestCommand = 25, CurrencyCommand = 66, SaddlebagCommand = 77;

    public static readonly IReadOnlyList<uint> Commands = [InventoryCommand, ArmouryChestCommand, CurrencyCommand, SaddlebagCommand];

    /// <summary>The Addon rows that name the other groups, rows and retainer field words: Retainer 532, Gil 830, Right
    /// Saddlebag 885, Allagan Tomestones 3500, Retainer Inventory 3731, Armoire 3734, Glamour Dresser 3735, Societal Currency 5751 and Storage
    /// 17580.</summary>
    public const uint RetainerAddon = 532, GilAddon = 830, RightSaddlebagAddon = 885, TomestonesAddon = 3500, RetainerInventoryAddon = 3731,
        ArmoireAddon = 3734, GlamourDresserAddon = 3735, SocietalCurrencyAddon = 5751, StorageAddon = 17580;

    public static readonly IReadOnlyList<uint> Addons =
        [RetainerAddon, GilAddon, RightSaddlebagAddon, TomestonesAddon, RetainerInventoryAddon, ArmoireAddon, GlamourDresserAddon, SocietalCurrencyAddon, StorageAddon];
}
