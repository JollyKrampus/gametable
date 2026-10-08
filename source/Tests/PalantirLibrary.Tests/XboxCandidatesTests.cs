using NUnit.Framework;
using PalantirLibrary.Api;
using PalantirLibrary.Sync;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PalantirLibrary.Tests
{
    [TestFixture]
    public class XboxCandidatesTests
    {
        private static LibraryGame Xbox(string name, string gameId = "Microsoft.Game_8wekyb3d8bbwe", params string[] platforms)
        {
            return new LibraryGame
            {
                Id = Guid.NewGuid(),
                Name = name,
                PluginId = XboxCandidates.XboxPluginId,
                LibraryGameId = gameId,
                Platforms = platforms.ToList(),
            };
        }

        private static EntrySummary Row(string title, string queue = "Play")
        {
            return new EntrySummary { Id = Guid.NewGuid(), Title = title, Queue = queue };
        }

        private static List<string> Offered(
            IEnumerable<LibraryGame> games, LinkState links = null, IEnumerable<EntrySummary> play = null, IEnumerable<EntrySummary> binned = null)
        {
            return XboxCandidates.Find(games, links ?? new LinkState(), XboxCandidates.TakenTitles(play, binned))
                .Select(c => c.Title)
                .ToList();
        }

        [Test]
        public void AnXboxGameThePlayListDoesNotHave_IsOffered()
        {
            CollectionAssert.AreEqual(new[] { "Starfield" }, Offered(new[] { Xbox("Starfield") }));
        }

        [Test]
        public void AnXboxGameLinkedToARow_IsNotOffered()
        {
            var halo = Xbox("Halo Infinite");
            var links = new LinkState();
            links.Link(halo.Id, Guid.NewGuid());

            CollectionAssert.IsEmpty(Offered(new[] { halo }, links));
        }

        [Test]
        public void AnXboxGameWhoseTitleIsOnThePlayList_IsNotOffered_HoweverItIsWritten()
        {
            var play = new[] { Row("halo  infinite "), Row("Forza Horizon 5"), Row("Forza Horizon 5") };

            var offered = Offered(new[] { Xbox("Halo Infinite"), Xbox("Forza Horizon 5"), Xbox("Hi-Fi Rush") }, play: play);

            CollectionAssert.AreEqual(new[] { "Hi-Fi Rush" }, offered, "two rows under one title are still that title on the list");
        }

        [Test]
        public void AGameWhosePlayRowWasThrownAway_IsNotOfferedAgain_ButAnotherListsBinnedRowStopsNothing()
        {
            var binned = new[] { Row("Starfield"), Row("Fable", queue: "Read") };

            var offered = Offered(new[] { Xbox("Starfield"), Xbox("Fable") }, binned: binned);

            CollectionAssert.AreEqual(new[] { "Fable" }, offered);
        }

        [Test]
        public void OnlyTheXboxLibrarysGamesAreOffered_AndAHiddenOneIsNot()
        {
            var steam = Xbox("Portal 2");
            steam.PluginId = LinkMatcher.SteamPluginId;
            var hiddenApp = Xbox("YouTube", "CONSOLE_122001257_Application", "xbox_one");
            hiddenApp.Hidden = true;

            CollectionAssert.AreEqual(new[] { "Pentiment" }, Offered(new[] { steam, hiddenApp, Xbox("Pentiment") }));
        }

        [TestCase("CONSOLE_1717113201_DGame", "Xbox", "xbox_one")]
        [TestCase("CONSOLE_1297290144_Xbox360Game", "Xbox", "xbox360")]
        [TestCase("CONSOLE_2043073184_Application", "Xbox")]
        [TestCase("Microsoft.SeaofThieves_8wekyb3d8bbwe", "Xbox/PC", "pc_windows", "xbox_one", "xbox_series")]
        [TestCase("Microsoft.624F8B84B80_8wekyb3d8bbwe", "Xbox/PC", "pc_windows")]
        [TestCase("Microsoft.HalfMoon_8wekyb3d8bbwe", "Xbox/PC")]
        [TestCase("BethesdaSoftworks.ProjectGold_3275kfvn8vcwc", "Xbox", "xbox_series")]
        public void AConsoleTitleIsXbox_AndAGamePassOrStoreTitleIsXboxPc(string gameId, string word, params string[] platforms)
        {
            var game = Xbox("Any", gameId, platforms);

            Assert.AreEqual(word, XboxCandidates.PlatformWord(game));
            Assert.AreEqual(word == XboxCandidates.ConsoleWord, XboxCandidates.IsConsole(game));
        }

        [Test]
        public void EachOfferCarriesItsPlatformWord_AndThePlaytimePlayniteHas()
        {
            var halo = Xbox("Halo 3", "CONSOLE_1297287142_Xbox360Game", "xbox360");
            halo.Playtime = 90 * 60 + 30;
            var pentiment = Xbox("Pentiment", "Microsoft.Pentiment_8wekyb3d8bbwe", "pc_windows");

            var offers = XboxCandidates.Find(new[] { halo, pentiment }, new LinkState(), new List<string>());

            var console = offers.Single(c => c.Title == "Halo 3");
            Assert.AreEqual(halo.Id, console.GameId);
            Assert.AreEqual("Xbox", console.Platform);
            Assert.AreEqual(90, console.PlayedMinutes);
            var pc = offers.Single(c => c.Title == "Pentiment");
            Assert.AreEqual("Xbox/PC", pc.Platform);
            Assert.IsNull(pc.PlayedMinutes, "a game with no playtime shows none");
        }
    }
}
