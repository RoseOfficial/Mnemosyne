using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Checklist;

/// <summary>Assemble when something changed, not every frame. A hit needs the same state version and the same
/// UTC minute, so relative times and reset boundaries move on their own.
/// Both keys are per character: a record's own version moves only when that record changed, so one character's login or
/// edit no longer re-assembles the other thirteen. The settings version and the minute are still shared.
/// Assembling the whole catalog costs about a
/// millisecond per character, so a player with a dozen alts would drop a frame every minute. BeginFrame caps how many slots
/// one frame may assemble; past the budget a character keeps the checklist it already has, which is at most a minute out of
/// date in its relative times, and is refreshed on a later frame. A character with no slot at all is always assembled, since
/// there is nothing else to draw for it.</summary>
public sealed class ChecklistCache
{
    private sealed class Slot(int recordVersion, int settingsVersion, long minute, Checklist checklist)
    {
        public int RecordVersion { get; } = recordVersion;
        public int SettingsVersion { get; } = settingsVersion;
        public long Minute { get; } = minute;
        public Checklist Checklist { get; } = checklist;
        public MatrixRow? Row { get; set; }
    }

    private readonly Dictionary<ulong, Slot> _cache = new();
    private int _budget = int.MaxValue;

    /// <summary>Starts a frame in which at most <paramref name="budget"/> stale slots are re-assembled.</summary>
    public void BeginFrame(int budget) => _budget = budget;

    public Checklist Get(Catalog catalog, CharacterRecord record, Settings settings, int recordVersion, int settingsVersion, DateTimeOffset now) =>
        SlotFor(catalog, record, settings, recordVersion, settingsVersion, now).Checklist;

    /// <summary>The matrix row, built once per cache slot from the cached checklist.</summary>
    public MatrixRow GetRow(Catalog catalog, CharacterRecord record, Settings settings, int recordVersion, int settingsVersion, DateTimeOffset now)
    {
        var slot = SlotFor(catalog, record, settings, recordVersion, settingsVersion, now);
        return slot.Row ??= Summariser.Summarise(catalog, record, settings, now, slot.Checklist);
    }

    public void Remove(ulong contentId) => _cache.Remove(contentId);

    private Slot SlotFor(Catalog catalog, CharacterRecord record, Settings settings, int recordVersion, int settingsVersion, DateTimeOffset now)
    {
        var minute = now.ToUnixTimeSeconds() / 60;
        var hit = _cache.GetValueOrDefault(record.ContentId);
        if (hit != null && hit.RecordVersion == recordVersion && hit.SettingsVersion == settingsVersion && hit.Minute == minute) return hit;
        if (hit != null && _budget <= 0) return hit;
        _budget--;
        var slot = new Slot(recordVersion, settingsVersion, minute, Assembler.Assemble(catalog, record, settings, now));
        _cache[record.ContentId] = slot;
        return slot;
    }
}
