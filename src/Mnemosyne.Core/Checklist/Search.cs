namespace Mnemosyne.Core.Checklist;

/// <summary>Filters every list by name, group, and category label. The badge is untouched.</summary>
public static class Search
{
    public static Checklist Filter(Checklist checklist, string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return checklist;
        bool Matches(Entry e) =>
            e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || e.Item.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || e.Item.Group.Contains(q, StringComparison.OrdinalIgnoreCase)
            || e.Item.Category.ToString().Contains(q, StringComparison.OrdinalIgnoreCase);

        var sections = checklist.Sections
            .Select(s => new Section(s.Category, s.Groups
                .Select(g => new EntryGroup(g.Name, g.Entries.Where(Matches).ToList()))
                .Where(g => g.Entries.Count > 0)
                .ToList()))
            .ToList();
        return new Checklist(
            checklist.Today.Where(Matches).ToList(),
            checklist.ThisWeek.Where(Matches).ToList(),
            checklist.EndingSoon.Where(Matches).ToList(),
            checklist.Running.Where(Matches).ToList(),
            sections, checklist.Badge, checklist.Stale);
    }
}
