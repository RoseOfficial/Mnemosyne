using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Checklist;

public enum EntryState { Done, NotDone, Upcoming, Past, Unknown }

/// <summary>One item as the character sees it. Marker is "" or one of: "assumed", "as seen", "as of last visit to &lt;gate&gt;",
/// "needs: &lt;window&gt;", "visit: &lt;place&gt;", "not read yet", "quests done; which return is unknown", "quests done";
/// a Counter may carry "&lt;marker&gt;, assumed". AtCap is set for a Counter at its cap. Label is the per-character name
/// from the record, when the catalog can only number the item; Name is what to draw. Detail is a second line under the
/// value, used for progress toward the next level or rank. OffFor is the name of the character's role when that role switches
/// the item off: the row is listed, dimmed with "off for &lt;role&gt;", and in no top group; null
/// otherwise. Count is a Counter row's count and cap as its words read them, its reset already applied, so the look's bar
/// agrees with its value; null on every other row. No text form prints it.</summary>
public sealed record Entry(ItemDefinition Item, ItemValue? Value, EntryState State, string Display, bool Assumed, string Marker, bool AtCap = false, string Label = "", string Detail = "", string? OffFor = null,
    Meter? Count = null)
{
    public bool IsDone => State == EntryState.Done;

    /// <summary>A stored lock: the character has not unlocked this item, so the row is blank and is attention nowhere. The window
    /// draws it dimmed with "not unlocked yet", so a lock never looks like unfinished work.</summary>
    public bool Locked => Value is GateValue { Locked: true };

    /// <summary>The item's name as this character sees it: the record's own name when it has one, else the catalog's.</summary>
    public string Name => Label.Length > 0 ? Label : Item.Name;
}

public sealed record EntryGroup(string Name, IReadOnlyList<Entry> Entries);

public sealed record Section(Category Category, IReadOnlyList<EntryGroup> Groups);

/// <summary>What one character sees. Running holds the running events that are not done and not yet ending soon,
/// in end order; unlike the other three top groups it is not counted in the badge.</summary>
public sealed record Checklist(
    IReadOnlyList<Entry> Today,
    IReadOnlyList<Entry> ThisWeek,
    IReadOnlyList<Entry> EndingSoon,
    IReadOnlyList<Entry> Running,
    IReadOnlyList<Section> Sections,
    int Badge,
    bool Stale)
{
    public static readonly Checklist None = new([], [], [], [], [], 0, false);
}
