using NUnit.Framework;
using PalantirLibrary.Sync;

namespace PalantirLibrary.Tests
{
    [TestFixture]
    public class GameMappingTests
    {
        [TestCase("Queued", "Plan to Play")]
        [TestCase("Started", "Playing")]
        [TestCase("Finished", "Completed")]
        [TestCase("Garbage", "Abandoned")]
        public void EveryPalantirStateIsShownAsOneCompletionStatus_AndReadsBackAsItself(string state, string status)
        {
            Assert.AreEqual(status, GameMapping.CompletionFor(state));
            Assert.AreEqual(state, GameMapping.StateFor(status));
        }

        [TestCase("Beaten", "Finished")]
        [TestCase("Not Played", "Queued")]
        [TestCase("On Hold", null)]
        [TestCase("Played", null)]
        [TestCase(null, null)]
        public void AStatusPalantirHasNoWordFor_MapsToNothing_SoItIsNeverWrittenBack(string status, string state)
        {
            Assert.AreEqual(state, GameMapping.StateFor(status));
        }

        [TestCase(1, 20)]
        [TestCase(3, 60)]
        [TestCase(5, 100)]
        public void AStarIsTwentyPoints(int rating, int score)
        {
            Assert.AreEqual(score, GameMapping.ScoreFor(rating));
            Assert.AreEqual(rating, GameMapping.RatingFor(score));
        }

        [TestCase(null, null)]
        [TestCase(0, null)]
        [TestCase(1, 1)]
        [TestCase(49, 2)]
        [TestCase(50, 3)]
        [TestCase(87, 4)]
        [TestCase(100, 5)]
        public void AScoreIsRoundedToTheNearestStar_AndNoughtIsNoRating(int? score, int? rating)
        {
            Assert.AreEqual(rating, GameMapping.RatingFor(score));
        }

        [TestCase("steam://run/440", "440")]
        [TestCase("steam://rungameid/1245620", "1245620")]
        [TestCase("  STEAM://run/570  ", "570")]
        [TestCase("https://store.steampowered.com/app/440", null)]
        [TestCase(@"C:\Games\Doom\doom.exe", null)]
        [TestCase(null, null)]
        public void OnlyASteamRunTargetNamesASteamApp(string target, string appId)
        {
            Assert.AreEqual(appId, GameMapping.SteamAppId(target));
        }

        [TestCase("steam://run/440", true)]
        [TestCase("https://www.gog.com/game/x", true)]
        [TestCase("com.epicgames.launcher://apps/Fortnite?action=launch", true)]
        [TestCase(@"C:\Games\Doom\doom.exe", false)]
        [TestCase(@"D:/Emulators/retroarch.exe", false)]
        [TestCase(@"\\nas\games\game.exe", false)]
        [TestCase("doom.exe", false)]
        public void ALaunchTargetIsAnAddressOrAFile(string target, bool isAddress)
        {
            Assert.AreEqual(isAddress, GameMapping.IsAddress(target));
        }

        [Test]
        public void TitlesMatchTheWayAPersonReadsThem()
        {
            Assert.IsTrue(GameMapping.SameTitle("Elden  Ring ", "elden ring"));
            Assert.IsFalse(GameMapping.SameTitle("Death Stranding", "Death Stranding 2"));
        }

        [Test]
        public void BlankNotesAreNoNotes()
        {
            Assert.IsNull(GameMapping.NormaliseNotes("   "));
            Assert.AreEqual("a\nb", GameMapping.NormaliseNotes(" a\r\nb "));
        }
    }
}
