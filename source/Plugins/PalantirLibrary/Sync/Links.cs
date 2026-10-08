using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PalantirLibrary.Api;

namespace PalantirLibrary.Sync
{
    /// <summary>
    /// Which GameTable game is which Palantír row, and what GameTable last read of each row.
    /// Kept in the plugin's own data folder, never in the house.
    /// </summary>
    public class LinkState
    {
        /// <summary>GameTable game id → Palantír entry id.</summary>
        public Dictionary<Guid, Guid> Links { get; set; } = new Dictionary<Guid, Guid>();

        /// <summary>Palantír entry id → the four shared fields as GameTable last read them.</summary>
        public Dictionary<Guid, FieldValues> LastRead { get; set; } = new Dictionary<Guid, FieldValues>();

        public Guid? EntryFor(Guid gameId)
        {
            return Links.TryGetValue(gameId, out var entry) ? entry : (Guid?)null;
        }

        public IEnumerable<Guid> GamesFor(Guid entryId)
        {
            return Links.Where(l => l.Value == entryId).Select(l => l.Key);
        }

        public void Link(Guid gameId, Guid entryId)
        {
            Links[gameId] = entryId;
        }

        /// <summary>
        /// Links a game to a row it was not linked to, and forgets what GameTable last read of the row.
        /// </summary>
        /// <remarks>
        /// What was last read was read for the row's earlier game. Kept, it makes the newcomer's blank
        /// notes, score and tags look like edits made in GameTable, and the next sync would push them
        /// over Troy's: a machine may never overwrite an answer he gave. Forgotten, the next sync reads
        /// the row as never read, so the house is the truth and the game takes it all.
        /// </remarks>
        public void LinkAsNew(Guid gameId, Guid entryId)
        {
            if (EntryFor(gameId) == entryId)
            {
                return;
            }

            Links[gameId] = entryId;
            LastRead.Remove(entryId);
        }

        /// <summary>
        /// Links the Palantír library's own copy of a row, unless another game already has the row.
        /// </summary>
        /// <remarks>
        /// When another library's copy takes a row, the own copy is hidden and unlinked, and GameTable
        /// keeps it. Linked again beside the other copy, two games are settled against one row, and
        /// the one settled second still holds the older values and pushes them over his edits.
        /// </remarks>
        public void LinkOwnCopy(Guid gameId, Guid entryId)
        {
            if (!GamesFor(entryId).Any())
            {
                LinkAsNew(gameId, entryId);
            }
        }

        /// <summary>
        /// Takes the Palantír library's own copies off every row another game also has, and forgets
        /// what was read of those rows: what an earlier GameTable left behind when it linked a hidden
        /// copy again (<see cref="LinkOwnCopy"/>). The other copy is the one that launches and counts,
        /// and the next sync gives it the row as the house holds it.
        /// </summary>
        public void UnlinkOwnCopiesBesideOthers(ICollection<Guid> ownCopies)
        {
            var takenByOthers = new HashSet<Guid>(Links.Where(l => !ownCopies.Contains(l.Key)).Select(l => l.Value));
            foreach (var own in Links.Where(l => ownCopies.Contains(l.Key) && takenByOthers.Contains(l.Value)).ToList())
            {
                Links.Remove(own.Key);
                LastRead.Remove(own.Value);
            }
        }

        /// <summary>Forgets a game. The row in the house is not touched (rule 4).</summary>
        public void Unlink(Guid gameId)
        {
            Links.Remove(gameId);
        }
    }

    /// <summary>Loads and saves <see cref="LinkState"/> as one JSON file, written whole.</summary>
    public class LinkStore
    {
        private readonly string path;
        private readonly object gate = new object();

        public LinkStore(string directory)
        {
            path = Path.Combine(directory, "links.json");
        }

        public LinkState Load()
        {
            lock (gate)
            {
                if (!File.Exists(path))
                {
                    return new LinkState();
                }

                return JsonConvert.DeserializeObject<LinkState>(File.ReadAllText(path)) ?? new LinkState();
            }
        }

        public void Save(LinkState state)
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(state, Formatting.Indented));
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
        }
    }

    /// <summary>A game from another library, described only by what matching needs.</summary>
    public class OtherGame
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        /// <summary>The library it came from: Steam's plugin id makes <see cref="LibraryGameId"/> an app id.</summary>
        public Guid PluginId { get; set; }

        public string LibraryGameId { get; set; }
    }

    /// <summary>
    /// Finds the Palantír row a game from another library already is, so it is linked rather than
    /// imported twice.
    /// </summary>
    public static class LinkMatcher
    {
        /// <summary>Playnite's Steam library plugin.</summary>
        public static readonly Guid SteamPluginId = Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB");

        /// <summary>
        /// The row this game is, or null. A Steam game is matched first by the app id its row's
        /// launch target opens, which survives a renamed title; any game then by exact title.
        /// Two rows with the same title are a question, not a match, and nothing is linked.
        /// </summary>
        public static Guid? Match(OtherGame game, IEnumerable<EntryDetail> entries)
        {
            var rows = entries.Where(e => e?.Entry != null).ToList();

            if (game.PluginId == SteamPluginId && !string.IsNullOrEmpty(game.LibraryGameId))
            {
                var bySteam = rows.Where(e => GameMapping.SteamAppId(e.LaunchTarget) == game.LibraryGameId).ToList();
                if (bySteam.Count == 1)
                {
                    return bySteam[0].Entry.Id;
                }
            }

            var byTitle = rows.Where(e => GameMapping.SameTitle(e.Entry.Title, game.Name)).ToList();
            return byTitle.Count == 1 ? byTitle[0].Entry.Id : (Guid?)null;
        }
    }
}
