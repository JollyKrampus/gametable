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

## Never a deletion

Removing a game in GameTable forgets the link; the row stays in the house (Palantír rule 4). The
client has no route that deletes or trashes a row. The only `DELETE` it sends takes a tag off a row,
and a test pins that.

## The wire

`Api/PalantirDtos.cs` is a hand-written copy of the house's contracts, because this plugin is
.NET Framework 4.6.2 and the house is net10. The house pins every name used here in
`tests/Palantir.Api.Tests/GameTableContractTests.cs`. If one changes there, change it here in the
same week.
