namespace Mnemosyne.Core.Readers;

/// <summary>One retainer as the client's roster holds it, in the client's own sorted order: its RetainerId, name, class (a
/// ClassJob row, 0 for a retainer with no class yet, taken to be right), level, gil, used inventory slots, and the venture's end as a unix
/// timestamp. Id is compared in memory by RosterGuard only and never reaches a file.</summary>
public readonly record struct RetainerFacts(ulong Id, string Name, int ClassJob, int Level, long Gil, int Items, uint VentureComplete);

/// <summary>Whether the retainer roster in the client is this character's. The roster is the only source of the retainer
/// rows and the venture rows, and nothing says the client clears it at logout. After a switch it may still hold the retainers of the
/// previous character until this character's own arrive, and stored under this character's record they would never be corrected. So a roster whose retainers
/// are exactly the ones last read under the previous character is not trusted until it changes, or until the manager has been seen not
/// ready for this character. A retainer belongs to one character, so the guard costs nothing on a real roster. The plugin's first
/// character has nothing to compare with and is trusted at once. The same lesson as RevocableGateState and ListLoadGuard:
/// state kept per character, reset by a switch. The ids stay in memory.
///
/// GetRetainerCount counts the retainer array and IsReady is a separate flag, so a ready roster with no retainers may be one whose
/// list has not arrived, at a login, a switch or a relog; read then, it would clear every retainer and venture row. It is believed only
/// once this login has shown this character's roster non-empty, when it is a dismissal. The login flag starts that proof over. The cost: a character
/// who dismissed its last retainer while the plugin was not running keeps its old rows until it hires one.
///
/// Comparing only against the roster the manager showed right before this character is not enough. Take a round trip
/// from A to a second character B and back to A in the same session, where the manager keeps A's roster through B's session because B has
/// no retainers of its own yet. B's carried roster is correctly distrusted, since it equals A's, but the guard also remembers it as what B
/// last saw. On the return to A, A's own true roster equals that same carried set, so a naive "not equal to what came right before" check
/// would never trust it again. The fix keeps each character's own last trusted, non-empty roster in memory too, and trusts a roster equal to
/// either the previous character's last seen set (unchanged) or this character's own last trusted set (new): only a character's own retainers can
/// equal its own last trusted roster, since a retainer belongs to one character.</summary>
public sealed class RosterGuard
{
    private ulong _owner;
    private HashSet<ulong>? _seen, _previous;
    private bool _trusted = true, _filled;
    // Each character's own last trusted, non-empty roster, so a round trip through another character who never replaced it does not
    // freeze this character's roster untrusted forever.
    private readonly Dictionary<ulong, HashSet<ulong>> _own = new();

    public bool Trust(ulong owner, bool login, bool ready, IReadOnlyCollection<ulong> retainerIds)
    {
        if (owner != _owner)
        {
            // The roster the manager showed while the previous character was logged in; none on the plugin's first character.
            _previous = _owner == 0 ? null : _seen;
            _owner = owner;
            _seen = null;
            _trusted = _previous == null;
            _filled = false;
        }
        if (login) _filled = false;
        if (!ready)
        {
            // The manager dropped its roster, so whatever it fills next belongs to this character. A roster that is not ready is never read.
            _trusted = true;
            return false;
        }
        var ids = new HashSet<ulong>(retainerIds.Where(id => id != 0));
        _seen = ids;
        if (!_trusted && (!ids.SetEquals(_previous!) || (_own.TryGetValue(owner, out var mine) && ids.SetEquals(mine)))) _trusted = true;
        if (!_trusted) return false;
        // A ready roster with no retainers may be a list that has not arrived. It is believed only once this login has shown this character's
        // roster non-empty: then it is a dismissal.
        if (ids.Count > 0)
        {
            _filled = true;
            _own[owner] = ids;
        }
        return _filled;
    }
}

/// <summary>Which of the roster's fields have shown data for this character since this login.</summary>
public readonly record struct RosterFields(bool Gil, bool Items, bool Levels);

/// <summary>Whether a retainer field's zero is real. The manager may report ready before a retainer's gil, items or level
/// arrive, and a zero read then would overwrite a real stored value, which no later read corrects until the bell. So a field's zero is
/// believed once any retainer of this roster has shown that field above zero since this login; until then it says nothing. A login starts
/// the proof over, a same-character relog included: a bell visit before the relog proves nothing about the fields the relog leaves empty.
/// The cost falls where every retainer truly holds zero of a field: those rows say nothing until one holds some. Kept per character, reset
/// by a switch and by a login. The reader calls Observe on every read, with an empty roster while RosterGuard does not trust it, so a login
/// read that finds the roster not ready still starts the proof over.</summary>
public sealed class RosterFill
{
    private ulong _owner;
    private bool _gil, _items, _levels;

    public RosterFields Observe(ulong owner, bool login, IEnumerable<RetainerFacts> roster)
    {
        if (owner != _owner || login)
        {
            _owner = owner;
            _gil = _items = _levels = false;
        }
        foreach (var retainer in roster)
        {
            _gil |= retainer.Gil > 0;
            _items |= retainer.Items > 0;
            _levels |= retainer.Level > 0;
        }
        return new RosterFields(_gil, _items, _levels);
    }
}
