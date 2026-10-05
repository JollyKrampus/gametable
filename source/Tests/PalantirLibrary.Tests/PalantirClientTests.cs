using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PalantirLibrary.Api;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PalantirLibrary.Tests
{
    /// <summary>A house that answers from a table and remembers what it was asked.</summary>
    internal class FakeHouse : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Path, string Body)> Asked = new List<(HttpMethod, string, string)>();
        public readonly Dictionary<string, (HttpStatusCode Status, string Body)> Answers =
            new Dictionary<string, (HttpStatusCode, string)>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            var path = request.RequestUri.AbsolutePath;
            Asked.Add((request.Method, path, body));

            var key = request.Method.Method + " " + path;
            if (!Answers.TryGetValue(key, out var answer))
            {
                answer = (HttpStatusCode.OK, "{}");
            }

            return new HttpResponseMessage(answer.Status)
            {
                Content = new StringContent(answer.Body ?? string.Empty, Encoding.UTF8, "application/json"),
            };
        }
    }

    [TestFixture]
    public class PalantirClientTests
    {
        private static readonly Guid Tf2 = Guid.Parse("11111111-1111-1111-1111-111111111111");

        [TestCase("hoo-ville.tail1234.ts.net", "https://hoo-ville.tail1234.ts.net/")]
        [TestCase("https://hoo-ville.tail1234.ts.net", "https://hoo-ville.tail1234.ts.net/")]
        [TestCase(" http://localhost:5000/ ", "http://localhost:5000/")]
        public void TheHouseAddressIsMadeUsable(string typed, string expected)
        {
            Assert.AreEqual(expected, PalantirClient.ParseHouseUrl(typed).ToString());
        }

        [Test]
        public void NoHouseAddressIsRefusedInWords()
        {
            var e = Assert.Throws<ArgumentException>(() => PalantirClient.ParseHouseUrl("  "));
            StringAssert.Contains("No house address", e.Message);
        }

        [Test]
        public void ThePlayQueueIncludesTheLiveServiceList_AndNothingInTheBin()
        {
            var house = new FakeHouse();
            var live = Guid.NewGuid();
            var binned = Guid.NewGuid();
            house.Answers["GET /api/palantir/queues/play"] = (HttpStatusCode.OK,
                $"[{{\"id\":\"{Tf2}\",\"title\":\"TF2\",\"state\":\"Queued\"}},{{\"id\":\"{binned}\",\"title\":\"x\",\"deletedOn\":\"2026-09-20\"}}]");
            house.Answers["GET /api/palantir/live-service"] = (HttpStatusCode.OK,
                $"[{{\"id\":\"{live}\",\"title\":\"Destiny 2\",\"state\":\"Started\"}},{{\"id\":\"{Tf2}\",\"title\":\"TF2\"}}]");

            var queue = new PalantirClient("http://house/", house).GetPlayQueue();

            CollectionAssert.AreEquivalent(new[] { Tf2, live }, queue.Select(e => e.Id));
        }

        [Test]
        public void NoLiveServiceList_204_IsNothingToAdd()
        {
            var house = new FakeHouse();
            house.Answers["GET /api/palantir/queues/play"] = (HttpStatusCode.OK, $"[{{\"id\":\"{Tf2}\"}}]");
            house.Answers["GET /api/palantir/live-service"] = (HttpStatusCode.NoContent, "");

            Assert.AreEqual(1, new PalantirClient("http://house/", house).GetPlayQueue().Count);
        }

        [Test]
        public void ACountedSession_IsSentExactlyAsTheHousePinsIt()
        {
            var house = new FakeHouse();

            new PalantirClient("http://house/", house).LogCountedSession(Tf2, new DateTime(2026, 10, 2, 21, 30, 0), 95);

            var (method, path, body) = house.Asked.Single();
            Assert.AreEqual(HttpMethod.Post, method);
            Assert.AreEqual($"/api/palantir/entries/{Tf2}/sessions", path);
            var json = JObject.Parse(body);
            Assert.AreEqual("2026-10-02", (string)json["playedOn"]);
            Assert.AreEqual(95, (int)json["minutes"]);
            Assert.AreEqual("GameTable", (string)json["source"]);
        }

        [Test]
        public void StateRatingAndTags_GoToTheirOwnRoutes()
        {
            var house = new FakeHouse();
            var client = new PalantirClient("http://house/", house);
            var day = new DateTime(2026, 10, 2);

            client.SetState(Tf2, "Finished", day);
            client.SetRating(Tf2, 4, day);
            client.AddTag(Tf2, "couch co-op");
            client.RemoveTag(Tf2, "couch co-op");

            Assert.AreEqual(HttpMethod.Put, house.Asked[0].Method);
            Assert.AreEqual($"/api/palantir/entries/{Tf2}/state", house.Asked[0].Path);
            Assert.AreEqual("Finished", (string)JObject.Parse(house.Asked[0].Body)["state"]);
            Assert.AreEqual("2026-10-02", (string)JObject.Parse(house.Asked[0].Body)["changedOn"]);
            Assert.AreEqual($"/api/palantir/entries/{Tf2}/rating", house.Asked[1].Path);
            Assert.AreEqual(4, (int)JObject.Parse(house.Asked[1].Body)["rating"]);
            Assert.AreEqual(HttpMethod.Post, house.Asked[2].Method);
            Assert.AreEqual(HttpMethod.Delete, house.Asked[3].Method);
            Assert.AreEqual($"/api/palantir/entries/{Tf2}/tags/couch%20co-op", house.Asked[3].Path);
        }

        [Test]
        public void NothingTheClientSendsCanDeleteOrTrashARow()
        {
            // Rule 4: the only DELETE it knows takes a label off a row.
            var house = new FakeHouse();
            var client = new PalantirClient("http://house/", house);
            client.RemoveTag(Tf2, "x");

            Assert.IsTrue(house.Asked.Where(a => a.Method == HttpMethod.Delete).All(a => a.Path.Contains("/tags/")));
            var methods = typeof(PalantirClient).GetMethods().Select(m => m.Name).ToList();
            Assert.IsFalse(methods.Any(m => m.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("Trash", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void TheEditFormIsSentWhole_SoNoFieldIsCleared()
        {
            var house = new FakeHouse();
            house.Answers[$"PUT /api/palantir/entries/{Tf2}"] = (HttpStatusCode.OK, $"{{\"entry\":{{\"id\":\"{Tf2}\"}}}}");
            var detail = new EntryDetail
            {
                Entry = new EntrySummary { Id = Tf2, Title = "TF2", Platform = "PC/Steam", Priority = 2, CoverImagePath = @"C:\covers\tf2.png" },
                Notes = "old",
                LaunchTarget = "steam://run/440",
                ReleaseNote = "2007",
                Company = "with the boys",
            };

            var form = UpdateEntryRequest.From(detail);
            form.Notes = "new";
            new PalantirClient("http://house/", house).Update(Tf2, form);

            var json = JObject.Parse(house.Asked.Single().Body);
            Assert.AreEqual("TF2", (string)json["title"]);
            Assert.AreEqual("new", (string)json["notes"]);
            Assert.AreEqual("PC/Steam", (string)json["platform"]);
            Assert.AreEqual(2, (int)json["priority"]);
            Assert.AreEqual("steam://run/440", (string)json["launchTarget"]);
            Assert.AreEqual(@"C:\covers\tf2.png", (string)json["coverImagePath"]);
            Assert.AreEqual("2007", (string)json["releaseNote"]);
            Assert.AreEqual("with the boys", (string)json["company"]);
        }

        [Test]
        public void ARefusalCarriesTheHousesOwnSentence()
        {
            var house = new FakeHouse();
            house.Answers[$"POST /api/palantir/entries/{Tf2}/sessions"] = (HttpStatusCode.BadRequest,
                "{\"code\":\"validation\",\"message\":\"Only a game has play sessions.\"}");

            var e = Assert.Throws<PalantirException>(() =>
                new PalantirClient("http://house/", house).LogCountedSession(Tf2, DateTime.Today, 10));

            StringAssert.Contains("Only a game has play sessions.", e.Message);
            Assert.AreEqual(HttpStatusCode.BadRequest, e.StatusCode);
        }

        [Test]
        public void ARowBecomesAGame_WithItsPicturesStatusScoreAndPlayButton()
        {
            var client = new PalantirClient("http://house/", new FakeHouse());
            var detail = new EntryDetail
            {
                Entry = new EntrySummary
                {
                    Id = Tf2, Title = "Team Fortress 2", State = "Started", Rating = 4, Platform = "PC/Steam",
                    Tags = new List<string> { "couch" }, ArtworkKey = "ab12.jpg", BackgroundKey = "cd34.jpg",
                },
                LaunchTarget = "steam://run/440",
                Synopsis = "Nine classes.",
                LastPlayedOn = "2026-10-01",
                Sessions = new List<PlaySession>
                {
                    new PlaySession { Minutes = 30, Source = "Typed" },
                    new PlaySession { Minutes = 90, Source = "GameTable" },
                    new PlaySession { Minutes = null, Source = "Typed" },
                },
            };

            var game = PalantirLibrary.ToMetadata(detail, client);

            Assert.AreEqual(Tf2.ToString(), game.GameId);
            Assert.AreEqual("Team Fortress 2", game.Name);
            Assert.AreEqual("Playing", ((MetadataNameProperty)game.CompletionStatus).Name);
            Assert.AreEqual(80, game.UserScore);
            Assert.AreEqual("http://house/api/files/ab12.jpg", game.CoverImage.Path);
            Assert.AreEqual("http://house/api/files/cd34.jpg", game.BackgroundImage.Path);
            Assert.AreEqual("PC/Steam", ((MetadataNameProperty)game.Platforms.Single()).Name);
            Assert.AreEqual(new DateTime(2026, 10, 1), game.LastActivity);
            Assert.AreEqual(30UL * 60, game.Playtime, "only typed minutes: GameTable counts its own sessions itself");
            Assert.IsTrue(game.IsInstalled);
            var play = game.GameActions.Single();
            Assert.IsTrue(play.IsPlayAction);
            Assert.AreEqual(GameActionType.URL, play.Type);
            Assert.AreEqual("steam://run/440", play.Path);
            Assert.AreEqual($"http://house/palantir/entries/{Tf2}", game.Links.Single().Url);
        }

        [Test]
        public void ARowWithAnExe_LaunchesAFile_AndARowWithNoTargetHasNoPlayButton()
        {
            var client = new PalantirClient("http://house/", new FakeHouse());
            var exe = PalantirLibrary.ToMetadata(new EntryDetail
            {
                Entry = new EntrySummary { Id = Tf2, Title = "Doom" },
                LaunchTarget = @"C:\Games\Doom\doom.exe",
            }, client);
            var none = PalantirLibrary.ToMetadata(new EntryDetail
            {
                Entry = new EntrySummary { Id = Guid.NewGuid(), Title = "Zelda", Platform = "Nintendo Switch 2" },
            }, client);

            Assert.AreEqual(GameActionType.File, exe.GameActions.Single().Type);
            Assert.IsFalse(none.IsInstalled);
            Assert.IsNull(none.GameActions);
            Assert.IsNull(none.CoverImage);
        }
    }
}
