# Mnemosyne

![Downloads](https://img.shields.io/github/downloads/RoseOfficial/Mnemosyne/total)

Remembers what every character has done, so you don't have to.

Mnemosyne is a progress checklist for all your FFXIV characters. It keeps a record of each character you log into, works out what is
left to do on each one, and shows it in one window: your characters down the left, one character's checklist on the right, with the
number of things still to do beside each name.

## What it does

- **Seven categories, read from the game:** story, jobs, events, dailies and weeklies, content, collections and money. Each character's
  checklist shows what is due today, this week and soon, then every category in full.
- **My tasks:** add your own tasks for anything the game cannot tell Mnemosyne (daily, weekly, one-off, or every few hours), for every
  character, a role, or the characters you pick, and tick them by hand. A task can count up to a number, such as three runs a day.
- **All tasks:** everything still to do on any character in one list, most urgent first, with how many characters still owe each task.
  Pin a task, raise or lower its priority, snooze it until the next reset, or hide it.
- **All characters:** a table of every character, sortable by any column: the main scenario, jobs at the level cap, dailies and
  weeklies left, gil, ventures ready, bag space and company seals.
- **Totals:** each currency added up across your characters, with who holds how much, and a graph of your gil over time.
- **Roles:** make a role that says what to track (a retainer mule might track only money and timers) and give it to a character. What
  the role leaves out is still listed, dimmed, but never counted.
- **Attention, kept quiet:** one chat line after login with the number of things to do, and the same number in the server info bar.
  Reminders for the daily reset, events ending, Wondrous Tails, house visits and My tasks before reset are all off until you switch
  them on. Nothing is said in chat during a duty, a cutscene or combat.
- **Export and import:** save everything to one file, and load it on another PC. Each character keeps whichever copy was seen last.

## What it does not do

- It never talks to the network: no Lodestone, no server of its own, nothing uploaded. It knows only what your game client shows it.
- It never writes to the game or sends anything to the server. The one exception is the optional switch button (below), which asks
  the Lifestream plugin to change character when you click it.
- It records only your own characters, never other players.
- A character appears only after you log into it once with Mnemosyne running. Its data is as fresh as its last login; resets are
  worked out from the clock, so a daily done yesterday shows as due again after the reset without logging in.
- Some rows can only be read while a game window is open. They show "needs:" and the window's name, such as "needs: Achievements",
  until you open that window once on that character.
- The Mini and Jumbo Cactpot are counted only while Mnemosyne is running: a ticket bought with it switched off is never seen.
- The interface is in English only.

## Privacy

Mnemosyne stores character data only on this PC. It is never uploaded anywhere.

Everything lives in Dalamud's configuration folder for the plugin, `%APPDATA%\XIVLauncher\pluginConfigs\Mnemosyne`: one file per
character, named by its content id, and `settings.json`. An export file and a `/mnemo dump` file hold your characters' names, worlds
and content ids, so share them only with someone you trust.

## Installation

Mnemosyne ships through the Olympus plugin repository. In Dalamud, open Settings → Experimental → Custom Plugin Repositories, add

    https://raw.githubusercontent.com/RoseOfficial/Olympus/main/repo.json

save, then install Mnemosyne from the plugin installer. If you already use Olympus, the repository is already there and Mnemosyne
simply appears in the list.

To build a copy yourself instead, run a Release build of `src/Mnemosyne/Mnemosyne.csproj`; the ready-to-install zip lands at
`src/Mnemosyne/bin/Release/Mnemosyne/latest.zip`.

## The switch button (optional)

With the Lifestream plugin (version 2.5.3.0 or later) installed and enabled, each character gets a switch button that logs you over
to it. Mnemosyne hands Lifestream its `/li Name@World` command on your click and does nothing else. That form of the command is not in
Lifestream's own help and may change in a later version of Lifestream; without Lifestream, the button simply does not appear. The
button is disabled in a duty, in combat and during a cutscene.

## Using it

- `/mnemo` opens and closes the window; `/mnemo settings` opens the settings (also the cog in the window's title bar).
- `/mnemo <name>` opens the window on the character whose name starts with `<name>`.
- `/mnemo help` lists the commands.
- Right-click a character for rename, hide, forget and role. Right-click a row to ignore it on this character or everywhere.
- Right-click a line in All tasks to pin, prioritise, snooze or hide it.
- Roles and My tasks are made on the settings page.

## Troubleshooting

`/mnemo dump`, typed while logged in, writes a text file to the configuration folder with the logged-in character's record, its
checklist, All tasks, Totals and the plugin's own state. It holds your characters' names and content ids: read it before you share it,
and share it only with someone you trust. Mnemosyne keeps the five newest dumps and removes older ones.

A problem reading or writing a file is shown on the settings page, under General.

Report a problem at https://github.com/RoseOfficial/Mnemosyne/issues. Read the note on `/mnemo dump` above before you attach one.

## Keeping up with the game

The dates of seasonal events and a few values the game data does not state (such as the level cap) ship inside the plugin. A new
seasonal event, Moogle Treasure Trove or expansion therefore arrives with a plugin update; until then, the new event is simply not
listed.

## Licence

MIT. See [LICENSE](LICENSE).
