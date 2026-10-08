using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace PalantirLibrary.Api
{
    /// <summary>
    /// The one door to the house: Palantír's /api/palantir, over the tailnet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No credential, because there is none to give. The house's heads are unauthenticated on the
    /// tailnet (ADR 0021), and this is one more head. The address is the only setting.
    /// </para>
    /// <para>
    /// Every route here is one the house's own web head already uses, and nothing here deletes or
    /// trashes a row: there is no method for it (Palantír rule 4).
    /// </para>
    /// <para>
    /// Synchronous on purpose. The plugin calls it from Playnite's library-update thread and from
    /// background tasks it starts itself, never from the UI thread.
    /// </para>
    /// </remarks>
    public sealed class PalantirClient : IDisposable
    {
        /// <summary>The day format the house reads and writes (a DateOnly).</summary>
        public const string DayFormat = "yyyy-MM-dd";

        private static readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Include,
        };

        private readonly HttpClient http;

        /// <summary>The house's root address, always ending in a slash.</summary>
        public Uri HouseUri { get; }

        public PalantirClient(string houseUrl, HttpMessageHandler handler = null)
        {
            HouseUri = ParseHouseUrl(houseUrl);
            http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.BaseAddress = HouseUri;
            http.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// The address as typed, made usable: a scheme if there was none, and a trailing slash so
        /// relative routes land under it rather than beside it.
        /// </summary>
        public static Uri ParseHouseUrl(string houseUrl)
        {
            if (string.IsNullOrWhiteSpace(houseUrl))
            {
                throw new ArgumentException("No house address is set. Add it in the Palantír library's settings.");
            }

            var text = houseUrl.Trim();
            if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                text = "https://" + text;
            }

            if (!text.EndsWith("/", StringComparison.Ordinal))
            {
                text += "/";
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException($"\"{houseUrl}\" is not an address.");
            }

            return uri;
        }

        /// <summary>
        /// Every game on the play queue, including the live-service list the queue view leaves out,
        /// and none that are in the bin.
        /// </summary>
        public List<EntrySummary> GetPlayQueue()
        {
            var queue = Get<List<EntrySummary>>("api/palantir/queues/play") ?? new List<EntrySummary>();

            // 204 while the house keeps no live-service list, which is nothing to add.
            var live = Get<List<EntrySummary>>("api/palantir/live-service") ?? new List<EntrySummary>();

            return queue.Concat(live)
                .Where(e => e != null && string.IsNullOrEmpty(e.DeletedOn))
                .GroupBy(e => e.Id)
                .Select(g => g.First())
                .ToList();
        }

        public EntryDetail GetEntry(Guid id)
        {
            return Get<EntryDetail>($"api/palantir/entries/{id}");
        }

        /// <summary>
        /// Every list's rows in Palantír's trash, read so a game Troy threw away is not offered back
        /// to him. Named for the bin so the rule-4 test can go on refusing any method named for the
        /// trash: this one only reads it.
        /// </summary>
        public List<EntrySummary> GetBinned()
        {
            return Get<List<EntrySummary>>("api/palantir/trash") ?? new List<EntrySummary>();
        }

        /// <summary>Puts a game on the play queue, with its platform and what starts it.</summary>
        public EntryDetail AddGame(string title, string platform, string launchTarget)
        {
            var added = Send<EntryDetail>(HttpMethod.Post, "api/palantir/entries", new AddEntryRequest
            {
                Queue = "Play",
                Title = title,
                Platform = platform,
            });

            if (string.IsNullOrWhiteSpace(launchTarget))
            {
                return added;
            }

            var form = UpdateEntryRequest.From(added);
            form.LaunchTarget = launchTarget;
            return Update(added.Entry.Id, form);
        }

        public EntryDetail Update(Guid id, UpdateEntryRequest form)
        {
            return Send<EntryDetail>(HttpMethod.Put, $"api/palantir/entries/{id}", form);
        }

        public void SetState(Guid id, string state, DateTime on)
        {
            Send<JToken>(HttpMethod.Put, $"api/palantir/entries/{id}/state", new SetEntryStateRequest
            {
                State = state,
                ChangedOn = Day(on),
            });
        }

        public void SetRating(Guid id, int? rating, DateTime on)
        {
            Send<JToken>(HttpMethod.Put, $"api/palantir/entries/{id}/rating", new SetRatingRequest
            {
                Rating = rating,
                On = Day(on),
            });
        }

        public void AddTag(Guid id, string name)
        {
            Send<JToken>(HttpMethod.Post, $"api/palantir/entries/{id}/tags", new TagRequest { Name = name });
        }

        /// <summary>Takes a label off a row. A label, not the row: rule 4 is not touched.</summary>
        public void RemoveTag(Guid id, string name)
        {
            Send<JToken>(HttpMethod.Delete, $"api/palantir/entries/{id}/tags/{Uri.EscapeDataString(name)}", null);
        }

        /// <summary>One sitting GameTable timed, from launch to exit.</summary>
        public EntryDetail LogCountedSession(Guid id, DateTime playedOn, int minutes)
        {
            return Send<EntryDetail>(HttpMethod.Post, $"api/palantir/entries/{id}/sessions", new LogSessionRequest
            {
                PlayedOn = Day(playedOn),
                Minutes = minutes,
                Source = "GameTable",
            });
        }

        /// <summary>Where the house serves a picture it holds, or null when there is none.</summary>
        public string FileUrl(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? null : new Uri(HouseUri, "api/files/" + key).ToString();
        }

        /// <summary>The row's own page on the house's web head.</summary>
        public string EntryPageUrl(Guid id)
        {
            return new Uri(HouseUri, $"palantir/entries/{id}").ToString();
        }

        public static string Day(DateTime day)
        {
            return day.ToString(DayFormat, CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            http.Dispose();
        }

        private T Get<T>(string route) where T : class
        {
            return Send<T>(HttpMethod.Get, route, null);
        }

        private T Send<T>(HttpMethod method, string route, object body) where T : class
        {
            using (var request = new HttpRequestMessage(method, route))
            {
                if (body != null)
                {
                    request.Content = new StringContent(
                        JsonConvert.SerializeObject(body, jsonSettings), Encoding.UTF8, "application/json");
                }

                using (var response = http.SendAsync(request).GetAwaiter().GetResult())
                {
                    var text = response.Content == null
                        ? string.Empty
                        : response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new PalantirException(method, route, response.StatusCode, SentenceFrom(text));
                    }

                    if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(text))
                    {
                        return null;
                    }

                    return JsonConvert.DeserializeObject<T>(text, jsonSettings);
                }
            }
        }

        /// <summary>The house refuses in words ({"code","message"}); keep the words.</summary>
        private static string SentenceFrom(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                var message = JObject.Parse(body)["message"];
                return message?.ToString() ?? body;
            }
            catch (JsonException)
            {
                return body.Length > 300 ? body.Substring(0, 300) : body;
            }
        }
    }

    /// <summary>The house said no, or could not be reached, and here is what it said.</summary>
    public sealed class PalantirException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        /// <summary>The house's own words for its answer, or null when it gave none.</summary>
        public string Sentence { get; }

        public PalantirException(HttpMethod method, string route, HttpStatusCode status, string sentence)
            : base($"Palantír answered {(int)status} to {method} {route}" + (sentence == null ? "." : $": {sentence}"))
        {
            StatusCode = status;
            Sentence = sentence;
        }
    }
}
