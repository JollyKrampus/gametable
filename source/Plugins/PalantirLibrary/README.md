# The Palantír library

GameTable's bridge to the house. Palantír's play queue appears as a GameTable library, and the
edits and playtime made in GameTable go back to it. The decision behind it is house-of-order
ADR 0039; the house-side runbook is in `docs/operations.md`, under *GameTable against the server*.

## Setting it up

Add-ons → Extensions settings → Libraries → **Palantír** → *House address*. Use the same address
the house's desktop `remote.json` carries, for example `https://hoo-ville.<tailnet>.ts.net`. There
is no login: the tailnet is the authentication, as it is for every head of the house. Then update
the library (F5).

## What moves

| Palantír | GameTable |
|---|---|
| title | name |
| platform | platform |
| tags | tags |
| notes | notes (both ways) |
| synopsis | description |
| cover / background | cover / background, from the house's `/api/files/` |
| launch target | the Play action: an address (`steam://`, `https://`...) opens; a path runs |
| rating 1–5 | user score, rating × 20 (both ways, to the nearest star) |
| `Queued` / `Started` / `Finished` / `Garbage` | Plan to Play / Playing / Completed / Abandoned (both ways; Beaten → Finished, Not Played → Queued, On Hold and Played are not written back) |
| typed sessions | imported playtime |

**Out, one real event at a time:**

- **A game stops:** the minutes it ran are logged to its row as a session marked `GameTable`. The
  house shows it as *counted by GameTable*, and the house's total adds it to the minutes typed by
  hand.
- **You change status, score, tags or notes on a linked game:** the change is written to the row.
- **Right-click → Palantír → Add to Palantír queue:** puts a game from any library on the queue and
  links it. *Open in Palantír* opens a linked row's page.
- **Extensions → Palantír → Xbox games not on the play list (N):** see below.

## Read before write

Before every write the row is read again. A field changed on a Palantír screen since GameTable last
read it is **pulled into GameTable rather than overwritten**: his typing on the web or the phone
wins. What GameTable last read is kept in `links.json` in the plugin's data folder
(`%AppData%\GameTable\ExtensionsData\5c7b1e2a-9d43-4f8e-b6a1-3e0f2d4c8a91`).

## Linking, not duplicating

A game another library brought in, such as Steam, is linked to its row instead of being imported
twice:

1. First by the Steam app id its row's `steam://run/<id>` opens.
2. Then by exact title.

Two rows with the same title are left alone. When a Steam copy is linked, the Palantír-library copy
of that row is **hidden, not removed**.

**A newly linked game starts from the row.** Whenever a game is linked to a row it was not linked to
(a Steam copy taking the row, or the row's own copy on its first import), GameTable forgets what it
last read of that row. The first sync then reads the row as new: the house's status, score, tags and
notes come into the game, and nothing of the game's is pushed. Before this, the newcomer's blank
notes, score and tags read as edits made in GameTable and were written over the row's.

## Xbox games not on the play list

Troy asked (2026-10-08) for his Xbox games, on the PC (Game Pass, the Microsoft Store) and on the
console, to reach the play list through GameTable. They come in through Playnite's Xbox library
add-on; turn on its *Import Xbox console games* for the console ones, which are the games he has
**played** (Xbox title history), not everything he owns. They go onto the list one press at a time,
never by themselves.

**Extensions → Palantír → Xbox games not on the play list (N)** opens a window of every Xbox game
GameTable has that is:

- not linked to a row;
- not under a title a play row already has, compared as linking compares titles (case, edges and
  doubled spaces ignored), one row or two;
- not under the title of a **play row in Palantír's trash** (`GET /api/palantir/trash`). Throwing a
  row away is his answer about the game, so it is not offered back to him, the same rule the house's
  own Steam list keeps;
- not hidden in GameTable.

Each line shows the title, the platform word, the playtime Playnite holds, and an **Add** button.
Add writes the row the way *Add to Palantír queue* does (`POST /api/palantir/entries`), with no
launch target, links the Xbox game to it, and keeps the house's answer as GameTable's first read of
the row. The platform words are Troy's: **Xbox** for a console title (a GameId of
`CONSOLE_{titleId}_{mediaItemType}`, or an Xbox console as its only kind of platform) and **Xbox/PC**
for a Game Pass or Store title, as his existing rows already write it. *Add to Palantír queue* on an Xbox
game uses the same words.

The entry appears once a library update has read both the play list and the trash, and only while N
is more than none: absent, not empty. The window reads both again when it opens, because every Add
is written from it. A title the house already holds is refused by the house, and the window shows
the house's sentence and adds nothing.

**Known gaps:**

- **Apps.** The add-on imports console apps (YouTube, Netflix...) beside console games. It drops
  non-games from the PC titles by the title's type, but not from the console ones, and the type is
  not kept on the game; whether the `mediaItemType` in a console GameId tells them apart is not
  verified. Hide an app in GameTable and it leaves the list.
- **The live-service games.** The play list as GameTable reads it leaves out the live-service list
  while the house keeps that list closed, so one of those games can be offered. Add is then refused
  by the house, in its words.
- **Near titles.** "Halo MCC" and "Halo: The Master Chief Collection" are two titles here, so the
  second can be offered beside the first.

## Never a deletion

Removing a game in GameTable forgets the link; the row stays in the house (Palantír rule 4). The
client has no route that deletes or trashes a row. The only `DELETE` it sends takes a tag off a row,
and a test pins that. It reads the trash (`GetBinned`, named for the bin so that test can go on
refusing any method named for the trash), and does nothing else to it.

## The wire

`Api/PalantirDtos.cs` is a hand-written copy of the house's contracts, because this plugin is
.NET Framework 4.6.2 and the house is net10. The house pins every name used here in
`tests/Palantir.Api.Tests/GameTableContractTests.cs`. If one changes there, change it here in the
same week. The trash answers with the same row (`EntrySummaryDto`) a queue does; the house pins its
names on a queue row, not yet on the `/trash` route itself.
