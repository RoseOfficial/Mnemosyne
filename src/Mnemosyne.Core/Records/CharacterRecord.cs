using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mnemosyne.Core.Records;

/// <summary>One character's stored state. One file per record. Mutable, owned by the Tracker; the window reads it.</summary>
public sealed class CharacterRecord
{
    /// <summary>A record may hold a "gate" value, a kind an older build does not know. A build that meets a
    /// higher version leaves the file untouched and says so; without the bump it would have quarantined the file as
    /// corrupt, because an unknown value kind throws inside the deserializer.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ulong ContentId { get; set; }
    public string Name { get; set; } = "";
    public string HomeWorld { get; set; } = "";
    public string DataCenter { get; set; } = "";
    public string? DisplayName { get; set; }
    public bool Hidden { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    /// <summary>Category name to the instant its reader last completed.</summary>
    public Dictionary<string, DateTimeOffset> CategoryReadAt { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Item id to the instant a reader last read that item, for items whose freshness is per item.</summary>
    public Dictionary<string, DateTimeOffset> ItemReadAt { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Item id to a name only this character can supply. The catalog is global, so a venture slot is "Venture:
    /// retainer 3" there and the character says who is in slot 3. Stored locally like every
    /// other value in this file and sent nowhere.</summary>
    public Dictionary<string, string> ItemNames { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Reminder key to the occasion it last fired for: a period key for the daily reset, an instant for an event's
    /// close or a journal's expiry. Kept on the record rather than in plugin memory so a relog inside the same period stays
    /// quiet, which is the whole point of an opt-in reminder.</summary>
    public Dictionary<string, string> Reminded { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, ItemValue> Values { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, ObservedValue> Observed { get; set; } = new(StringComparer.Ordinal);
    public DateTimeOffset? HousingLastEntered { get; set; }
    public HashSet<string> Ignored { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The id of the character's role, or null for none. An id this PC does not have behaves as no
    /// role and is kept, so a settings import can bring the role back. Not written while null, so a record with no role saves as before; an
    /// older build carries it through Extra, so there is no schema bump.</summary>
    public string? Role { get; set; }

    /// <summary>One point per UTC calendar day, "yyyy-MM-dd" to the character's own gil plus its
    /// retainers' gil, the last amount seen that day. Null until the first point, so a record with no history writes no "gilHistory" and saves
    /// exactly as before (Json.Options leaves a null out); an older build carries it through Extra, so there is no schema bump. GilPoints reads
    /// and writes it; nothing trims it.</summary>
    public Dictionary<string, long>? GilHistory { get; set; }

    [JsonIgnore] public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
    /// <summary>The identifier Lifestream's /li command takes to switch characters.</summary>
    [JsonIgnore] public string NameAtWorld => $"{Name}@{HomeWorld}";

    /// <summary>Keys this build does not know, carried through a load and a save untouched.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
