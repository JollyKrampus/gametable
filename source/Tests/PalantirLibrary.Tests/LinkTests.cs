using NUnit.Framework;
using PalantirLibrary.Api;
using PalantirLibrary.Sync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PalantirLibrary.Tests
{
    [TestFixture]
    public class LinkTests
    {
        private static EntryDetail Row(Guid id, string title, string target = null)
        {
            return new EntryDetail { Entry = new EntrySummary { Id = id, Title = title }, LaunchTarget = target };
        }

        [Test]
        public void ASteamGameIsMatchedByTheAppItsRowOpens_BeforeItsTitle()
        {
            var tf2 = Guid.NewGuid();
            var rows = new List<EntryDetail>
            {
                Row(tf2, "tf2 with the boys", "steam://run/440"),
                Row(Guid.NewGuid(), "Team Fortress 2"),
            };

            var match = LinkMatcher.Match(new OtherGame
            {
                Name = "Team Fortress 2",
                PluginId = LinkMatcher.SteamPluginId,
                LibraryGameId = "440",
            }, rows);

            Assert.AreEqual(tf2, match);
        }

        [Test]
        public void AnyOtherGameIsMatchedByExactTitle()
        {
            var hades = Guid.NewGuid();
            var match = LinkMatcher.Match(
                new OtherGame { Name = "hades", PluginId = Guid.NewGuid(), LibraryGameId = "x" },
                new[] { Row(hades, "Hades"), Row(Guid.NewGuid(), "Hades II") });

            Assert.AreEqual(hades, match);
        }

        [Test]
        public void TwoRowsWithTheSameTitleAreAQuestion_AndNothingIsLinked()
        {
            var match = LinkMatcher.Match(
                new OtherGame { Name = "Dune", PluginId = Guid.NewGuid() },
                new[] { Row(Guid.NewGuid(), "Dune"), Row(Guid.NewGuid(), "Dune") });

            Assert.IsNull(match);
        }

        [Test]
        public void LinksAndWhatWasLastRead_SurviveARestart()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PalantirLibraryTests", Guid.NewGuid().ToString());
            try
            {
                var store = new LinkStore(dir);
                var game = Guid.NewGuid();
                var entry = Guid.NewGuid();
                var state = store.Load();
                state.Link(game, entry);
                state.LastRead[entry] = new FieldValues { State = "Started", Rating = 4, Tags = new List<string> { "couch" } };
                store.Save(state);
                store.Save(state);

                var reloaded = new LinkStore(dir).Load();

                Assert.AreEqual(entry, reloaded.EntryFor(game));
                Assert.AreEqual(4, reloaded.LastRead[entry].Rating);
                CollectionAssert.AreEqual(new[] { "couch" }, reloaded.LastRead[entry].Tags);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Test]
        public void ForgettingAGame_OnlyForgetsTheLink()
        {
            var state = new LinkState();
            var game = Guid.NewGuid();
            var entry = Guid.NewGuid();
            state.Link(game, entry);
            state.LastRead[entry] = new FieldValues();

            state.Unlink(game);

            Assert.IsNull(state.EntryFor(game));
            Assert.IsTrue(state.LastRead.ContainsKey(entry), "the row and what was read of it are not GameTable's to throw away");
        }

        [Test]
        public void AGameLinkedToARowItWasNot_TakesTheRowsAnswers_AndPushesNoneOfItsBlanks()
        {
            // What GameTable read of the row for the row's own copy, before a Steam copy arrived.
            var row = Guid.NewGuid();
            var house = new FieldValues { State = "Finished", Rating = 5, Notes = "beat it with Sam", Tags = new List<string> { "couch" } };
            var state = new LinkState();
            state.Link(Guid.NewGuid(), row);
            state.LastRead[row] = house.Clone();

            var steamCopy = Guid.NewGuid();
            state.LinkAsNew(steamCopy, row);
            state.LastRead.TryGetValue(row, out var lastRead);
            var plan = SyncPlanner.Plan(lastRead, house, new FieldValues());

            Assert.AreEqual(row, state.EntryFor(steamCopy));
            Assert.IsFalse(plan.Any(s => s.Direction == SyncDirection.Push), "a machine never overwrites an answer he gave");
            Assert.IsTrue(plan.All(s => s.Direction == SyncDirection.Pull), "the house is the truth, and the copy takes it all");
        }

        [Test]
        public void LinkingAGameToTheRowItAlreadyHas_KeepsWhatWasRead()
        {
            var state = new LinkState();
            var game = Guid.NewGuid();
            var row = Guid.NewGuid();
            state.Link(game, row);
            state.LastRead[row] = new FieldValues { Rating = 4 };

            state.LinkAsNew(game, row);

            Assert.AreEqual(4, state.LastRead[row].Rating);
        }

        [Test]
        public void AHiddenCopy_IsNotLinkedAgain_BesideTheCopyThatTookItsRow()
        {
            var row = Guid.NewGuid();
            var steamCopy = Guid.NewGuid();
            var hiddenCopy = Guid.NewGuid();
            var state = new LinkState();
            state.Link(steamCopy, row);
            state.LastRead[row] = new FieldValues { Rating = 5 };

            state.LinkOwnCopy(hiddenCopy, row);

            Assert.IsNull(state.EntryFor(hiddenCopy));
            CollectionAssert.AreEqual(new[] { steamCopy }, state.GamesFor(row));
            Assert.AreEqual(5, state.LastRead[row].Rating, "nothing about the row changed");
        }

        [Test]
        public void ARowsOwnCopy_IsLinked_WhileNoOtherGameHasTheRow()
        {
            var row = Guid.NewGuid();
            var ownCopy = Guid.NewGuid();
            var state = new LinkState();

            state.LinkOwnCopy(ownCopy, row);

            Assert.AreEqual(row, state.EntryFor(ownCopy));
        }

        [Test]
        public void ARowLeftWithTwoGames_KeepsTheOtherLibrarysCopy_AndStartsAgainFromTheHouse()
        {
            var crowded = Guid.NewGuid();
            var alone = Guid.NewGuid();
            var steamCopy = Guid.NewGuid();
            var hiddenCopy = Guid.NewGuid();
            var aloneCopy = Guid.NewGuid();
            var state = new LinkState();
            state.Link(steamCopy, crowded);
            state.Link(hiddenCopy, crowded);
            state.Link(aloneCopy, alone);
            state.LastRead[crowded] = new FieldValues();
            state.LastRead[alone] = new FieldValues();

            state.UnlinkOwnCopiesBesideOthers(new HashSet<Guid> { hiddenCopy, aloneCopy });

            CollectionAssert.AreEqual(new[] { steamCopy }, state.GamesFor(crowded));
            Assert.IsFalse(state.LastRead.ContainsKey(crowded), "the copy that stays takes the row as the house holds it");
            Assert.AreEqual(alone, state.EntryFor(aloneCopy));
            Assert.IsTrue(state.LastRead.ContainsKey(alone));
        }
    }
}
