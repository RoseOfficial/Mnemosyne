using System.Runtime.CompilerServices;
using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Core.Checklist;

/// <summary>What the look reads from one checklist, built the first time it is
/// asked for and kept with that checklist for as long as the checklist lives (a ConditionalWeakTable, so a checklist the cache replaces takes
/// its look with it, and nothing else holds it). The top groups hold the very Entry objects the sections list (Assembler.Place), and a search
/// keeps them, so a row's top group is found by reference in one dictionary lookup, never by scanning a list per row per frame. The tallies and
/// the groups with icons are counted over the whole checklist, so a search never changes a bar. Build it from the checklist
/// as assembled, never from a searched one: each search makes a new checklist.</summary>
public sealed class ChecklistLook
{
    private static readonly ConditionalWeakTable<Checklist, ChecklistLook> Kept = new();

    private readonly Dictionary<Entry, Tone> _placed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Category, Tally> _sections = new();
    private readonly Dictionary<(Category Category, string Group), Tally> _groups = new();
    private readonly HashSet<(Category Category, string Group)> _withIcons = new();

    private ChecklistLook(Checklist checklist)
    {
        Place(checklist.EndingSoon, Tone.EndingSoon);
        Place(checklist.Today, Tone.Today);
        Place(checklist.ThisWeek, Tone.ThisWeek);
        Place(checklist.Running, Tone.Running);
        foreach (var section in checklist.Sections)
        {
            _sections[section.Category] = Tones.TallyOf(section.Groups.SelectMany(g => g.Entries));
            foreach (var group in section.Groups)
            {
                _groups[(section.Category, group.Name)] = Tones.TallyOf(group.Entries);
                if (group.Entries.Any(e => e.Item.IconId != null)) _withIcons.Add((section.Category, group.Name));
            }
        }
    }

    /// <summary>The look of this checklist, built once and kept with it.</summary>
    public static ChecklistLook For(Checklist checklist) => Kept.GetValue(checklist, static built => new ChecklistLook(built));

    /// <summary>A row's tone (Tones.OfRow), for a row of this checklist or of a search of it.</summary>
    public Tone ToneOf(Entry entry) => Tones.OfRow(entry, _placed.GetValueOrDefault(entry, Tone.Neutral));

    /// <summary>A category's done/total over all its groups; nothing listed when the category is not in the checklist.</summary>
    public Tally Of(Category category) => _sections.GetValueOrDefault(category);

    /// <summary>A group's done/total.</summary>
    public Tally Of(Category category, string group) => _groups.GetValueOrDefault((category, group));

    /// <summary>Whether a row of the group has a game icon, so every row in it keeps the icon's square.</summary>
    public bool HasIcons(Category category, string group) => _withIcons.Contains((category, group));

    /// <summary>An entry lands in at most one top group (Assembler.Place).</summary>
    private void Place(IReadOnlyList<Entry> entries, Tone tone)
    {
        foreach (var entry in entries) _placed.TryAdd(entry, tone);
    }
}
