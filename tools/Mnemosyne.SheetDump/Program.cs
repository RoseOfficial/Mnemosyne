using System.Text.Json;
using Lumina;
using Lumina.Data;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Storage;
using Mnemosyne.Interop.Sheets;

// Writes the reduced rows the plugin would build its catalog from, as the test fixture.
// Usage: Mnemosyne.SheetDump <path to game\sqpack> <output json path>
if (args.Length != 2)
{
    Console.Error.WriteLine("usage: Mnemosyne.SheetDump <sqpack path> <output json path>");
    return 2;
}

if (!Directory.Exists(args[0]))
{
    Console.Error.WriteLine($"sqpack folder not found: {args[0]}");
    return 2;
}
var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(args[1]));
if (outputDirectory != null) Directory.CreateDirectory(outputDirectory);

var gameData = new GameData(args[0], new LuminaOptions { DefaultExcelLanguage = Language.English });
if (!gameData.Repositories.TryGetValue("ffxiv", out var repo))
{
    Console.Error.WriteLine("the ffxiv repository was not found under the sqpack folder");
    return 3;
}
var version = repo.Version;
var rows = SheetReducer.Reduce(gameData.Excel, CatalogExtras.Load(), version, DateTimeOffset.UtcNow);
File.WriteAllText(args[1], JsonSerializer.Serialize(rows, Json.Options));
Console.WriteLine($"game {version}: {rows.Quests.Count} quests, {rows.ClassJobs.Count} jobs, {rows.AetherCurrentZones.Count} zones, {rows.BeastTribes.Count} tribes, {rows.GrandCompanies.Count} companies, {rows.AozActionCount} blue mage actions, {rows.Roulettes.Count} roulettes, {rows.HuntOrderTypes.Count} hunt bills, {rows.SatisfactionNpcs.Count} delivery clients, {rows.Tomestones.Count} weekly tomestone(s), {rows.ChallengeEntries.Count} challenge log entries, {rows.Lockouts.Count} raid lockout(s), {rows.TroveItems.Count} trove item(s), {rows.Duties.Count} duties, {rows.VariantSeries.Count} variant series, {rows.HuntingLogs.Count} hunting logs, {rows.PvPSeriesLevels.Count} PvP series levels, {rows.Currencies.Count} currencies, {rows.ContentTypes.Count} content types, {rows.PhantomJobs.Count} phantom jobs, {rows.CosmicClasses.Count} cosmic tools, {rows.IslandRanks.Count} island ranks, {rows.DeepDungeons.Count} deep dungeons, {rows.MainCommands.Count} main commands, {rows.Collections.Count} collection kinds, {rows.Logs.Count} logs, {rows.SightseeingLogs.Count} sightseeing blocks, {rows.AchievementCategories.Count} achievement categories, {rows.PvPRanks.Count} PvP ranks, {rows.ColosseumRanks.Count} Crystalline Conflict tiers, {rows.TomestoneItems.Count} tomestone items, {rows.Addons.Count} addon names, {rows.ArmoireRows.Count} armoire rows -> {args[1]}");
return 0;
