namespace Mnemosyne.Core.Definitions;

/// <summary>One group of a category as the settings page's tick tree lists it: its name, and the key the ignore lists
/// and the roles store it under (ItemDefinition.GroupKey).</summary>
public sealed record CatalogGroup(string Name, string Key);

/// <summary>The set of every trackable item, built once at start. Immutable; lookups by id and by category.</summary>
public sealed class Catalog
{
    public static readonly Catalog Empty = new([]);

    private readonly Dictionary<string, ItemDefinition> _byId;
    private readonly Dictionary<Category, List<ItemDefinition>> _byCategory;
    private readonly Dictionary<Category, List<CatalogGroup>> _groups;

    public IReadOnlyList<ItemDefinition> Items { get; }

    public Catalog(IEnumerable<ItemDefinition> items)
    {
        Items = items.ToList();
        _byId = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        foreach (var item in Items)
        {
            if (!_byId.TryAdd(item.Id, item)) throw new ArgumentException($"duplicate catalog id '{item.Id}'");
            // A Window item with no dates lands in no top group and reads as neither upcoming nor past: the builder must
            // always supply a window, even the undated sentinels.
            if (item.Reset.Kind == ResetKind.Window && item.Availability == null)
                throw new ArgumentException($"catalog item '{item.Id}' has a Window reset rule and no availability");
            // CarriesOver suppresses the boundary zeroing that only a Daily or Weekly Counter ever runs. On any
            // other item the flag would change nothing while reading as a promise, so the builder is stopped here.
            if (item.CarriesOver && (item.Kind != ItemKind.Counter || item.Reset.Kind is not (ResetKind.Daily or ResetKind.Weekly)))
                throw new ArgumentException($"catalog item '{item.Id}' carries over but is not a periodic counter");
            // A recurring window needs a period to recur with and excludes fixed dates; one that closes before it opens is a
            // typo in an offset. Only a Timer ever reads "ready".
            if (item.Recurrence is { } recurs && (item.Availability != null || item.Reset.Kind is not (ResetKind.Daily or ResetKind.Weekly) || recurs.ClosesAfter <= recurs.OpensAfter))
                throw new ArgumentException($"catalog item '{item.Id}' has a recurring window it cannot have: it needs a Daily or Weekly rule, no fixed availability, and a close after its open");
            if (item.ReadyWhenPassed && item.Kind != ItemKind.Timer)
                throw new ArgumentException($"catalog item '{item.Id}' reads ready when passed but is not a Timer");
            // Only a Timer has a deadline to be warned of, and only a Timer with no reset rule has its window read (Place's
            // ResetKind.None arm; the others go through PlaceTimer and would ignore it); a window of zero or less would warn at the
            // deadline or never.
            if (item.RemindWithin is { } within && (item.Kind != ItemKind.Timer || item.Reset.Kind != ResetKind.None || within <= TimeSpan.Zero))
                throw new ArgumentException($"catalog item '{item.Id}' has a warning window it cannot have: it needs to be a Timer with no reset rule and the window above zero");
            // Only a Manual Counter reads Target; on anything else it would change nothing while reading as a promise. A number below 1
            // would throw in the assembler's clamp at assembly instead of here, so it is caught now.
            if (item.Target is { } target && (item.Kind != ItemKind.Counter || item.Freshness.Kind != FreshnessKind.Manual || target < 1))
                throw new ArgumentException($"catalog item '{item.Id}' has a number it cannot have: it needs to be a Manual counter and the number at least 1");
        }
        _byCategory = Items.GroupBy(i => i.Category)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.GroupOrder).ThenBy(i => i.Group, StringComparer.Ordinal).ThenBy(i => i.Order).ThenBy(i => i.Id, StringComparer.Ordinal).ToList());
        // Each category's groups, once, in the order a character's own list shows them (Assembler groups InCategory by
        // name), for the tick tree and the tick rule.
        _groups = _byCategory.ToDictionary(pair => pair.Key, pair => pair.Value.GroupBy(i => i.Group).Select(g => new CatalogGroup(g.Key, g.First().GroupKey)).ToList());
    }

    public ItemDefinition? TryGet(string id) => _byId.GetValueOrDefault(id);

    public IReadOnlyList<ItemDefinition> InCategory(Category category) =>
        _byCategory.TryGetValue(category, out var list) ? list : [];

    /// <summary>The category's groups, in the order a character's own list shows them.</summary>
    public IReadOnlyList<CatalogGroup> GroupsIn(Category category) =>
        _groups.TryGetValue(category, out var list) ? list : [];
}
