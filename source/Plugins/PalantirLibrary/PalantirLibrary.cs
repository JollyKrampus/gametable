using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PalantirLibrary.Api;
using PalantirLibrary.Sync;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace PalantirLibrary
{
    /// <summary>
    /// Palantír's play queue as a GameTable library, and the one bridge between GameTable and the
    /// house (house-of-order ADR 0039).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>In:</b> every play-queue row becomes a game whose GameId is the row's id. A game another
    /// library already brought in (Steam, Epic, GOG...) is linked to its row instead, by the Steam
    /// app id its launch target opens and then by exact title.
    /// </para>
    /// <para>
    /// <b>Out, one press or one real event at a time:</b> a status, score, tag or notes change made
    /// here; a session when a game GameTable launched stops; "Add to Palantír queue". Before every
    /// write the row is read again, and a field Troy changed on a Palantír screen since GameTable
    /// last read it is pulled rather than overwritten (<see cref="SyncPlanner"/>).
    /// </para>
    /// <para>
    /// <b>Never a deletion.</b> Removing a game here forgets the link; the row stays in the house.
    /// </para>
    /// </remarks>
    public class PalantirLibrary : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public static readonly Guid PluginId = Guid.Parse("5C7B1E2A-9D43-4F8E-B6A1-3E0F2D4C8A91");

        private const string NotificationId = "PalantirLibrary";

        private readonly LinkStore store;
        private readonly object syncGate = new object();
        private readonly ConcurrentDictionary<Guid, byte> applying = new ConcurrentDictionary<Guid, byte>();
        private Dictionary<Guid, EntryDetail> lastFetched = new Dictionary<Guid, EntryDetail>();

        // The play list's titles and the binned play rows', as last read, for the Xbox menu entry's
        // count: the main menu asks every time it opens and must not wait on the house. Null until
        // both have been read.
        private List<string> takenTitles;

        public override Guid Id { get; } = PluginId;

        public override string Name { get; } = "Palantír";

        internal PalantirLibrarySettingsViewModel SettingsModel { get; }

        public PalantirLibrary(IPlayniteAPI api) : base(api)
        {
            SettingsModel = new PalantirLibrarySettingsViewModel(this);
            store = new LinkStore(GetPluginUserDataPath());
            Properties = new LibraryPluginProperties { HasSettings = true };
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return SettingsModel;
        }

        public override UserControl GetSettingsView(bool firstRunView)
        {
            return new PalantirLibrarySettingsView { DataContext = SettingsModel };
        }

        // ------------------------------------------------------------------ in

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var client = NewClient();
            if (client == null)
            {
                return Enumerable.Empty<GameMetadata>();
            }

            using (client)
            {
                try
                {
                    var details = client.GetPlayQueue()
                        .Select(e => client.GetEntry(e.Id))
                        .Where(d => d?.Entry != null)
                        .ToList();

                    lastFetched = details.ToDictionary(d => d.Entry.Id);
                    var state = store.Load();
                    var linkedElsewhere = new HashSet<Guid>(state.Links
                        .Where(l => PlayniteApi.Database.Games.Get(l.Key)?.PluginId is Guid p && p != Id)
                        .Select(l => l.Value));

                    var games = new List<GameMetadata>();
                    foreach (var detail in details.Where(d => !linkedElsewhere.Contains(d.Entry.Id)))
                    {
                        games.Add(ToMetadata(detail, client));
                        if (!state.LastRead.ContainsKey(detail.Entry.Id))
                        {
                            state.LastRead[detail.Entry.Id] = Values(detail);
                        }
                    }

                    store.Save(state);
                    PlayniteApi.Notifications.Remove(NotificationId);
                    logger.Info($"Palantír: {details.Count} play rows read from {client.HouseUri}, {games.Count} offered.");
                    return games;
                }
                catch (Exception e)
                {
                    Report("Palantír's play queue could not be read", e);
                    return Enumerable.Empty<GameMetadata>();
                }
            }
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            Task.Run(() => SyncAll());
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            PlayniteApi.Database.Games.ItemUpdated += Games_ItemUpdated;
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            PlayniteApi.Database.Games.ItemUpdated -= Games_ItemUpdated;
        }

        // ------------------------------------------------------------------ out

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            var game = args.Game;
            var entry = store.Load().EntryFor(game.Id);
            var minutes = (int)Math.Round(args.ElapsedSeconds / 60.0, MidpointRounding.AwayFromZero);
            if (entry == null || minutes < 1)
            {
                return;
            }

            var playedOn = DateTime.Now.Date;
            Task.Run(() =>
            {
                var client = NewClient();
                if (client == null)
                {
                    return;
                }

                using (client)
                {
                    try
                    {
                        client.LogCountedSession(entry.Value, playedOn, minutes);
                        logger.Info($"Palantír: {minutes} minutes of \"{game.Name}\" logged to {entry.Value}.");
                    }
                    catch (Exception e)
                    {
                        Report($"{minutes} minutes of {game.Name} could not be logged to Palantír", e);
                    }
                }
            });
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (args.Games.Count != 1)
            {
                yield break;
            }

            var game = args.Games[0];
            var entry = store.Load().EntryFor(game.Id);
            if (entry == null)
            {
                yield return new GameMenuItem
                {
                    Description = "Add to Palantír queue",
                    MenuSection = "Palantír",
                    Action = _ => Task.Run(() => AddToQueue(game)),
                };
            }
            else
            {
                var client = NewClient(quiet: true);
                if (client != null)
                {
                    var url = client.EntryPageUrl(entry.Value);
                    client.Dispose();
                    yield return new GameMenuItem
                    {
                        Description = "Open in Palantír",
                        MenuSection = "Palantír",
                        Action = _ => Process.Start(url),
                    };
                }
            }
        }

        private void AddToQueue(Game game)
        {
            var client = NewClient();
            if (client == null)
            {
                return;
            }

            using (client)
            {
                try
                {
                    // An Xbox game takes Troy's word for where it is played, whichever door it comes through.
                    var platform = game.PluginId == XboxCandidates.XboxPluginId
                        ? XboxCandidates.PlatformWord(ToLibraryGame(game))
                        : game.Platforms?.FirstOrDefault()?.Name;
                    AddAndLink(client, game, platform, LaunchTargetOf(game));
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        NotificationId + game.Id, $"{game.Name} is on the Palantír play queue.", NotificationType.Info));
                }
                catch (Exception e)
                {
                    Report($"{game.Name} could not be added to Palantír", e);
                }
            }
        }

        /// <summary>
        /// Puts a game on the play queue and links it, keeping the house's answer as GameTable's first
        /// read of the new row, then settles the pair.
        /// </summary>
        private void AddAndLink(PalantirClient client, Game game, string platform, string launchTarget)
        {
            var added = client.AddGame(game.Name, platform, launchTarget);
            lock (syncGate)
            {
                var state = store.Load();
                state.Link(game.Id, added.Entry.Id);
                state.LastRead[added.Entry.Id] = Values(added);
                store.Save(state);
            }

            SyncOne(client, game.Id, added.Entry.Id, client.GetEntry(added.Entry.Id));
        }

        // ------------------------------------------------------------------ the Xbox games

        /// <summary>
        /// "Xbox games not on the play list (N)", while N is known and more than none. Counted from
        /// what was last read, because the main menu asks every time it opens.
        /// </summary>
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            var count = XboxCandidatesAgainst(takenTitles)?.Count ?? 0;
            if (count == 0)
            {
                return Enumerable.Empty<MainMenuItem>();
            }

            return new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    Description = $"Xbox games not on the play list ({count})",
                    MenuSection = "@Palantír",
                    Action = _ => XboxCandidatesView.Show(PlayniteApi, ReadXboxCandidates, AddXboxGame),
                },
            };
        }

        /// <summary>
        /// The window's list, read fresh: every Add is written from it, and a bin read at the last
        /// library update could offer a game he threw away since.
        /// </summary>
        private List<XboxCandidate> ReadXboxCandidates()
        {
            var client = NewClient(quiet: true);
            if (client == null)
            {
                throw new InvalidOperationException("no house address is set. Add it in Add-ons → Extensions settings → Libraries → Palantír.");
            }

            using (client)
            {
                var taken = XboxCandidates.TakenTitles(client.GetPlayQueue(), client.GetBinned());
                takenTitles = taken;
                return XboxCandidatesAgainst(taken);
            }
        }

        /// <returns>Null once the row is written and linked, or the sentence saying why it was not.</returns>
        private string AddXboxGame(XboxCandidate candidate)
        {
            var game = PlayniteApi.Database.Games.Get(candidate.GameId);
            if (game == null)
            {
                return $"{candidate.Title} is no longer in GameTable.";
            }

            var client = NewClient(quiet: true);
            if (client == null)
            {
                return "No house address is set. Add it in Add-ons → Extensions settings → Libraries → Palantír.";
            }

            using (client)
            {
                try
                {
                    // No launch target: a console game has nothing a PC can open, and the PC copy is
                    // started by its own library here.
                    AddAndLink(client, game, candidate.Platform, null);
                    logger.Info($"Palantír: \"{game.Name}\" added to the play list as {candidate.Platform}.");
                    return null;
                }
                catch (PalantirException e) when (e.Sentence != null)
                {
                    // The house's own refusal, such as a title already on the list, in its own words.
                    logger.Info($"Palantír: \"{game.Name}\" was not added: {e.Sentence}");
                    return e.Sentence;
                }
                catch (Exception e)
                {
                    logger.Error(e, $"{game.Name} could not be added to Palantír");
                    return $"{game.Name} could not be added to Palantír: {e.Message}";
                }
            }
        }

        /// <summary>The Xbox games to offer against those titles, or null while the titles are unknown.</summary>
        private List<XboxCandidate> XboxCandidatesAgainst(List<string> taken)
        {
            if (taken == null)
            {
                return null;
            }

            var games = PlayniteApi.Database.Games
                .Where(g => g.PluginId == XboxCandidates.XboxPluginId)
                .Select(ToLibraryGame)
                .ToList();
            return XboxCandidates.Find(games, store.Load(), taken);
        }

        private static LibraryGame ToLibraryGame(Game game)
        {
            return new LibraryGame
            {
                Id = game.Id,
                Name = game.Name,
                PluginId = game.PluginId,
                LibraryGameId = game.GameId,
                Platforms = game.Platforms?.Select(p => p.SpecificationId).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>(),
                Playtime = game.Playtime,
                Hidden = game.Hidden,
            };
        }

        private void Games_ItemUpdated(object sender, ItemUpdatedEventArgs<Game> args)
        {
            var state = store.Load();
            foreach (var change in args.UpdatedItems)
            {
                var game = change.NewData;
                if (applying.ContainsKey(game.Id) || state.EntryFor(game.Id) == null || !SharedFieldsChanged(change.OldData, game))
                {
                    continue;
                }

                var entryId = state.EntryFor(game.Id).Value;
                Task.Run(() =>
                {
                    var client = NewClient();
                    if (client == null)
                    {
                        return;
                    }

                    using (client)
                    {
                        try
                        {
                            SyncOne(client, game.Id, entryId, client.GetEntry(entryId));
                        }
                        catch (Exception e)
                        {
                            Report($"A change to {game.Name} could not be written to Palantír", e);
                        }
                    }
                });
            }
        }

        // ------------------------------------------------------------------ both ways

        /// <summary>
        /// After a library update: link what can be linked, then settle every linked pair.
        /// </summary>
        private void SyncAll()
        {
            var client = NewClient(quiet: true);
            if (client == null)
            {
                return;
            }

            using (client)
            {
                try
                {
                    var details = lastFetched.Count > 0
                        ? lastFetched.Values.ToList()
                        : client.GetPlayQueue().Select(e => client.GetEntry(e.Id)).Where(d => d?.Entry != null).ToList();
                    lastFetched = new Dictionary<Guid, EntryDetail>();

                    LinkAll(details);
                    RememberTakenTitles(client, details);

                    var byId = details.ToDictionary(d => d.Entry.Id);
                    foreach (var link in store.Load().Links.ToList())
                    {
                        if (PlayniteApi.Database.Games.Get(link.Key) == null)
                        {
                            Forget(link.Key);
                        }
                        else if (byId.TryGetValue(link.Value, out var detail))
                        {
                            SyncOne(client, link.Key, link.Value, detail);
                        }
                    }
                }
                catch (Exception e)
                {
                    Report("Palantír could not be brought up to date", e);
                }
            }
        }

        /// <summary>
        /// Keeps the play list's titles and the binned ones for the Xbox menu entry. A bin that cannot
        /// be read leaves the count unknown and the entry absent, rather than offering a game he threw
        /// away.
        /// </summary>
        private void RememberTakenTitles(PalantirClient client, List<EntryDetail> details)
        {
            try
            {
                takenTitles = XboxCandidates.TakenTitles(details.Select(d => d.Entry), client.GetBinned());
            }
            catch (Exception e)
            {
                takenTitles = null;
                logger.Warn(e, "Palantír: the trash could not be read, so the Xbox list waits for the next library update.");
            }
        }

        private void LinkAll(List<EntryDetail> details)
        {
            lock (syncGate)
            {
                var state = store.Load();
                var games = PlayniteApi.Database.Games.ToList();

                // One game to a row. A hidden copy is never linked again beside the copy that took
                // its row, and one an earlier GameTable did link again is taken off it here.
                state.UnlinkOwnCopiesBesideOthers(new HashSet<Guid>(games.Where(g => g.PluginId == Id).Select(g => g.Id)));

                // Every link made here is a game the row did not have, so each forgets what was last
                // read of the row (LinkAsNew). A copy imported a moment ago carries no notes at all,
                // because a library's metadata has nowhere to put them.
                foreach (var game in games.Where(g => g.PluginId == Id && !state.Links.ContainsKey(g.Id)))
                {
                    if (Guid.TryParse(game.GameId, out var entryId))
                    {
                        state.LinkOwnCopy(game.Id, entryId);
                    }
                }

                foreach (var game in games.Where(g => g.PluginId != Id && !state.Links.ContainsKey(g.Id)))
                {
                    var match = LinkMatcher.Match(new OtherGame
                    {
                        Id = game.Id,
                        Name = game.Name,
                        PluginId = game.PluginId,
                        LibraryGameId = game.GameId,
                    }, details);

                    if (match == null || state.GamesFor(match.Value).Any(g => games.FirstOrDefault(x => x.Id == g)?.PluginId != Id))
                    {
                        continue;
                    }

                    // The other library's copy is the one that launches and counts, so it takes the
                    // link. The Palantír-library copy is hidden, never removed.
                    foreach (var own in state.GamesFor(match.Value).ToList())
                    {
                        var duplicate = PlayniteApi.Database.Games.Get(own);
                        if (duplicate != null && !duplicate.Hidden)
                        {
                            duplicate.Hidden = true;
                            Apply(duplicate);
                        }

                        state.Unlink(own);
                    }

                    state.LinkAsNew(game.Id, match.Value);
                    logger.Info($"Palantír: linked \"{game.Name}\" to row {match.Value}.");
                }

                store.Save(state);
            }
        }

        /// <summary>One game and its row, settled field by field: his web typing wins.</summary>
        private void SyncOne(PalantirClient client, Guid gameId, Guid entryId, EntryDetail detail)
        {
            lock (syncGate)
            {
                var game = PlayniteApi.Database.Games.Get(gameId);
                if (game == null || detail?.Entry == null)
                {
                    return;
                }

                var state = store.Load();
                state.LastRead.TryGetValue(entryId, out var lastRead);
                var house = Values(detail);
                var mine = Values(game);
                var plan = SyncPlanner.Plan(lastRead, house, mine);
                var today = DateTime.Now.Date;
                var pulled = false;

                foreach (var step in plan.Where(s => s.Direction != SyncDirection.None))
                {
                    if (step.Conflict)
                    {
                        logger.Info($"Palantír: {step.Field} of \"{game.Name}\" changed on both sides; the house's value stands.");
                    }

                    if (step.Direction == SyncDirection.Pull)
                    {
                        PullInto(game, step.Field, house);
                        pulled = true;
                        continue;
                    }

                    switch (step.Field)
                    {
                        case SyncField.State:
                            client.SetState(entryId, mine.State, today);
                            break;
                        case SyncField.Rating:
                            client.SetRating(entryId, mine.Rating, today);
                            break;
                        case SyncField.Tags:
                            foreach (var tag in mine.Tags.Where(t => !house.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                            {
                                client.AddTag(entryId, tag);
                            }

                            foreach (var tag in house.Tags.Where(t => !mine.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                            {
                                client.RemoveTag(entryId, tag);
                            }

                            break;
                        case SyncField.Notes:
                            var form = UpdateEntryRequest.From(detail);
                            form.Notes = mine.Notes;
                            client.Update(entryId, form);
                            break;
                    }
                }

                if (pulled)
                {
                    Apply(game);
                }

                state.LastRead[entryId] = SyncPlanner.Settled(plan, house, mine);
                store.Save(state);
            }
        }

        private void PullInto(Game game, SyncField field, FieldValues house)
        {
            var db = PlayniteApi.Database;
            switch (field)
            {
                case SyncField.State:
                    var status = GameMapping.CompletionFor(house.State);
                    if (status != null)
                    {
                        game.CompletionStatusId = db.CompletionStatuses.Add(status).Id;
                    }

                    break;
                case SyncField.Rating:
                    game.UserScore = GameMapping.ScoreFor(house.Rating);
                    break;
                case SyncField.Tags:
                    game.TagIds = house.Tags.Select(t => db.Tags.Add(t).Id).ToList();
                    break;
                case SyncField.Notes:
                    game.Notes = house.Notes;
                    break;
            }
        }

        /// <summary>Writes a game this plugin changed, without hearing its own echo.</summary>
        private void Apply(Game game)
        {
            applying[game.Id] = 0;
            try
            {
                PlayniteApi.Database.Games.Update(game);
            }
            finally
            {
                applying.TryRemove(game.Id, out _);
            }
        }

        private void Forget(Guid gameId)
        {
            lock (syncGate)
            {
                var state = store.Load();
                state.Unlink(gameId);
                store.Save(state);
            }
        }

        // ------------------------------------------------------------------ shapes

        internal static FieldValues Values(EntryDetail detail)
        {
            return new FieldValues
            {
                State = detail.Entry.State,
                Rating = detail.Entry.Rating,
                Tags = detail.Entry.Tags?.ToList() ?? new List<string>(),
                Notes = GameMapping.NormaliseNotes(detail.Notes),
            };
        }

        private FieldValues Values(Game game)
        {
            return new FieldValues
            {
                State = GameMapping.StateFor(game.CompletionStatus?.Name),
                Rating = GameMapping.RatingFor(game.UserScore),
                Tags = game.Tags?.Select(t => t.Name).ToList() ?? new List<string>(),
                Notes = GameMapping.NormaliseNotes(game.Notes),
            };
        }

        private static bool SharedFieldsChanged(Game before, Game after)
        {
            return before.CompletionStatusId != after.CompletionStatusId
                || before.UserScore != after.UserScore
                || before.Notes != after.Notes
                || !new HashSet<Guid>(before.TagIds ?? new List<Guid>()).SetEquals(after.TagIds ?? new List<Guid>());
        }

        internal static GameMetadata ToMetadata(EntryDetail detail, PalantirClient client)
        {
            var entry = detail.Entry;
            var metadata = new GameMetadata
            {
                Name = entry.Title,
                GameId = entry.Id.ToString(),
                Description = detail.Synopsis,
                Source = new MetadataNameProperty("Palantír"),
                Links = new List<Link> { new Link("Palantír", client.EntryPageUrl(entry.Id)) },
                UserScore = GameMapping.ScoreFor(entry.Rating),
                Tags = new HashSet<MetadataProperty>((entry.Tags ?? new List<string>()).Select(t => new MetadataNameProperty(t))),
                Playtime = (ulong)Math.Max(0, (detail.Sessions ?? new List<PlaySession>())
                    .Where(s => s.Source != "GameTable")
                    .Sum(s => s.Minutes ?? 0)) * 60,
            };

            var status = GameMapping.CompletionFor(entry.State);
            if (status != null)
            {
                metadata.CompletionStatus = new MetadataNameProperty(status);
            }

            if (!string.IsNullOrWhiteSpace(entry.Platform))
            {
                metadata.Platforms = new HashSet<MetadataProperty> { new MetadataNameProperty(entry.Platform) };
            }

            if (client.FileUrl(entry.ArtworkKey) is string cover)
            {
                metadata.CoverImage = new MetadataFile(cover);
            }

            if (client.FileUrl(entry.BackgroundKey) is string background)
            {
                metadata.BackgroundImage = new MetadataFile(background);
            }

            if (DateTime.TryParse(detail.LastPlayedOn, out var lastPlayed))
            {
                metadata.LastActivity = lastPlayed;
            }

            if (!string.IsNullOrWhiteSpace(detail.LaunchTarget))
            {
                metadata.IsInstalled = true;
                metadata.GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Play",
                        IsPlayAction = true,
                        Type = GameMapping.IsAddress(detail.LaunchTarget) ? GameActionType.URL : GameActionType.File,
                        Path = detail.LaunchTarget.Trim(),
                    },
                };
            }

            return metadata;
        }

        private string LaunchTargetOf(Game game)
        {
            if (game.PluginId == LinkMatcher.SteamPluginId && !string.IsNullOrEmpty(game.GameId))
            {
                return $"steam://run/{game.GameId}";
            }

            var action = game.GameActions?.FirstOrDefault(a => a.IsPlayAction && a.Type == GameActionType.URL);
            return action == null ? null : PlayniteApi.ExpandGameVariables(game, action.Path);
        }

        private PalantirClient NewClient(bool quiet = false)
        {
            var url = SettingsModel.Settings.HouseUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                if (!quiet)
                {
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        NotificationId,
                        "Palantír: no house address is set. Add it in Add-ons → Extensions settings → Libraries → Palantír.",
                        NotificationType.Info,
                        () => OpenSettingsView()));
                }

                return null;
            }

            try
            {
                return new PalantirClient(url);
            }
            catch (ArgumentException e)
            {
                if (!quiet)
                {
                    Report("The Palantír house address is not usable", e);
                }

                return null;
            }
        }

        private void Report(string what, Exception e)
        {
            logger.Error(e, what);
            PlayniteApi.Notifications.Add(new NotificationMessage(
                NotificationId, $"{what}: {e.Message}", NotificationType.Error));
        }
    }
}
