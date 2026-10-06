using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Core.Readers;

/// <summary>Whether a character has unlocked a Resets row, as pure rules over reads the caller supplies, so every
/// rule is tested without a client. The rule the reader applies to each answer is the hunt-bill rule made general
/// ("What a locked item does to a stored value"): true reads the row as before; false is "this character has no such thing"
/// and clears whatever was stored; null is "could not be judged" and leaves the stored value alone. Every gate here is
/// monotonic — quests, duty unlocks, levels and society ranks only rise — so a row that clears cannot flap back.</summary>
public static class ResetGates
{
    /// <summary>Miner, Botanist and Fisher: the classes whose highest level opens the treasure-map row.</summary>
    public static readonly IReadOnlyList<uint> GathererClassJobs = [16, 17, 18];

    /// <summary>Carpenter to Culinarian: the classes whose lowest level opens the Grand Company supply half (the
    /// per-class half taken to be right; not yet confirmed in game).</summary>
    public static readonly IReadOnlyList<uint> CrafterClassJobs = [8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>Whether one challenge-log entry is open to this character: its unlock link or quest, and its level
    /// requirement against the highest class or job level. A level requirement with no level to read cannot be judged.</summary>
    public static bool? ChallengeEntryOpen(bool unlocked, int levelUnlock, int? highestLevel)
    {
        if (!unlocked) return false;
        if (levelUnlock <= 0) return true;
        return highestLevel is { } level ? level >= levelUnlock : null;
    }

    /// <summary>Whether one roulette is open under its hand-file rule, over its member duties and two per-duty reads.
    /// No gate at all is open: a roulette the hand file does not know is read as it was before the gates, and the builder
    /// has said so. A member rule over an empty member list is null, because the sheet, not the character, is what has
    /// nothing to say. UnlockedAtLeast stops at the count; the all-rules stop at the first miss.</summary>
    public static bool? RouletteOpen(RouletteGate? gate, IReadOnlyList<uint> members, Func<uint, bool> unlocked, Func<uint, bool> completed, bool isBattleMentor)
    {
        if (gate == null) return true;
        switch (gate.Rule)
        {
            case RouletteRule.BattleMentor:
                return isBattleMentor;
            case RouletteRule.Completed:
                if (gate.ContentIds is not { Count: > 0 } duties) return null;
                foreach (var duty in duties) if (!completed(duty)) return false;
                return true;
            case RouletteRule.UnlockedAtLeast:
            {
                if (members.Count == 0) return null;
                var found = 0;
                foreach (var member in members) if (unlocked(member) && ++found >= gate.Count) return true;
                return false;
            }
            case RouletteRule.UnlockedAll:
                if (members.Count == 0) return null;
                foreach (var member in members) if (!unlocked(member)) return false;
                return true;
            case RouletteRule.CompletedAll:
                if (members.Count == 0) return null;
                foreach (var member in members) if (!unlocked(member) || !completed(member)) return false;
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(gate), gate.Rule, "unknown roulette rule");
        }
    }

    /// <summary>The weekly total a character can actually reach: the client's own cap (used plus remaining, twelve today)
    /// or the sum of the unlocked clients' weekly deliveries, whichever is lower, so a one-client character's row can read
    /// done at six rather than never at twelve. Null when no client is unlocked, which clears the total row.</summary>
    public static int? DeliveryTotalCap(int readCap, IEnumerable<int> unlockedClientCaps)
    {
        var any = false;
        var sum = 0;
        foreach (var cap in unlockedClientCaps)
        {
            any = true;
            sum += cap;
        }
        return any ? Math.Min(readCap, sum) : null;
    }

    /// <summary>The least-wrong rule: the weekly tomestone is reachable once some combat job that is not a limited
    /// job stands at the level cap, because the tomestone drops only from level-cap content. Crafters and gatherers earn
    /// none, and a limited job cannot enter the content that pays. The cost of the rule: a character at the cap who has not
    /// yet reached the story that opens the paying duties sees a row it cannot fill, until it does. The rejected alternative,
    /// "the tomestone is known", would hide the row from every newly capped character until its first drop.</summary>
    public static bool TomestoneOpen(IEnumerable<ClassJobRow> jobs, Func<ClassJobRow, int> level, int levelCap) =>
        jobs.Any(j => j.DohDolIndex == -1 && !j.IsLimitedJob && level(j) >= levelCap);

    /// <summary>The treasure-map row is open once the highest gatherer can work the node that yields the lowest map.</summary>
    public static bool MapOpen(IEnumerable<int> gathererLevels, int gatherLevel) =>
        gathererLevels.Any(l => l >= gatherLevel);
}
