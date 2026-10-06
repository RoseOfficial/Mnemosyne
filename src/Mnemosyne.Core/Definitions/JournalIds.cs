namespace Mnemosyne.Core.Definitions;

/// <summary>The journal row ids that decide which quests are kept and which block each kept row lands in, in one place.
/// Two files consume them and must agree: the sheet reduction decides which rows are kept, and the catalog builder decides
/// which block each kept row lands in. Widening one alone is silent — kept rows that become no item, or a block whose rows
/// were never kept — so neither file carries a journal id of its own. The one journal number that stays in
/// <see cref="CatalogBuilder"/> is the Rising genre, which renames a group for display and decides nothing about keeping or
/// blocking. Every id here is a game fact with its source.</summary>
public static class JournalIds
{
    /// <summary>A quest is MSQ when its journal section is 0 or 1; the fifteen MSQ categories are already story order.</summary>
    public const int MsqSectionMax = 1;

    /// <summary>Chronicles of a New Era, journal section 2, one category per arc.</summary>
    public const uint ChroniclesFirst = 16, ChroniclesLast = 31;

    /// <summary>Hildibrand sidequests: one category holding six genres.</summary>
    public const uint Hildibrand = 55;

    /// <summary>Class and job quests, categories 86 to 94, holding genres from <see cref="ClassQuestGenreFirst"/>.</summary>
    public const uint ClassQuestsFirst = 86, ClassQuestsLast = 94;

    /// <summary>The first class and job quest genre; the builder's group order is the offset from it.</summary>
    public const uint ClassQuestGenreFirst = 156;

    /// <summary>Role quests: one category holding genres from <see cref="RoleQuestGenreFirst"/> to 233.</summary>
    public const uint RoleQuests = 95;

    /// <summary>The first role quest genre; the builder's group order is the offset from it.</summary>
    public const uint RoleQuestGenreFirst = 217;

    /// <summary>Seasonal events.</summary>
    public const uint SeasonalEvents = 97;

    /// <summary>Collaboration quests. A genre, not a category: it sits under category 98, which is not kept.</summary>
    public const uint CollaborationGenre = 252;

    /// <summary>Sidequest storylines, journal section 3, less <see cref="Hildibrand"/> (category 55), which is its own
    /// block.</summary>
    private static readonly uint[] Storylines = [54, 56, 57, 58];

    /// <summary>Whether a journal category is one of the four sidequest-storyline blocks.</summary>
    public static bool IsStorylineCategory(uint categoryId) => Array.IndexOf(Storylines, categoryId) >= 0;

    /// <summary>Whether a quest is kept for its journal category: Chronicles, the sidequest storylines including
    /// Hildibrand, the class, job and role quests, and seasonal events.</summary>
    public static bool IsKeptCategory(uint categoryId) =>
        categoryId is (>= ChroniclesFirst and <= ChroniclesLast) or (>= ClassQuestsFirst and <= ClassQuestsLast) or RoleQuests or Hildibrand or SeasonalEvents
        || IsStorylineCategory(categoryId);
}
