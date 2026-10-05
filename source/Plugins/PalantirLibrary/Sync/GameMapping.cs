using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PalantirLibrary.Sync
{
    /// <summary>
    /// How a Palantír row's words become GameTable's, and back. Pure: no Playnite runtime, no house.
    /// </summary>
    public static class GameMapping
    {
        /// <summary>Palantír's state → the GameTable completion status it is shown as.</summary>
        public static readonly IReadOnlyDictionary<string, string> CompletionForState =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Queued", "Plan to Play" },
                { "Started", "Playing" },
                { "Finished", "Completed" },
                { "Garbage", "Abandoned" },
            };

        /// <summary>
        /// A GameTable completion status → the Palantír state it means, for the statuses that mean
        /// one. "On Hold" and "Played" have no Palantír word, so they map to nothing and a change
        /// to either is not written back: the row keeps what it says.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> StateForCompletion =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Plan to Play", "Queued" },
                { "Not Played", "Queued" },
                { "Playing", "Started" },
                { "Completed", "Finished" },
                { "Beaten", "Finished" },
                { "Abandoned", "Garbage" },
            };

        private static readonly Regex steamRun = new Regex(
            @"^\s*steam://(?:run|rungameid|launch)/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string CompletionFor(string state)
        {
            return state != null && CompletionForState.TryGetValue(state, out var status) ? status : null;
        }

        public static string StateFor(string completionStatus)
        {
            return completionStatus != null && StateForCompletion.TryGetValue(completionStatus.Trim(), out var state)
                ? state
                : null;
        }

        /// <summary>One to five stars → GameTable's 0–100 user score: twenty points a star.</summary>
        public static int? ScoreFor(int? rating)
        {
            return rating.HasValue ? rating.Value * 20 : (int?)null;
        }

        /// <summary>
        /// A 0–100 user score → one to five stars, to the nearest star. No score, or a score of
        /// nought, is no rating: nought is how a cleared score arrives.
        /// </summary>
        public static int? RatingFor(int? score)
        {
            if (!score.HasValue || score.Value <= 0)
            {
                return null;
            }

            var stars = (int)Math.Round(score.Value / 20.0, MidpointRounding.AwayFromZero);
            return Math.Max(1, Math.Min(5, stars));
        }

        /// <summary>The Steam app a launch target opens, or null when it opens anything else.</summary>
        public static string SteamAppId(string launchTarget)
        {
            if (string.IsNullOrWhiteSpace(launchTarget))
            {
                return null;
            }

            var match = steamRun.Match(launchTarget);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Whether a launch target is an address to open (steam://, https://, com.epicgames...://)
        /// rather than a file to run. A drive letter or a UNC path is a file.
        /// </summary>
        public static bool IsAddress(string launchTarget)
        {
            if (string.IsNullOrWhiteSpace(launchTarget))
            {
                return false;
            }

            var text = launchTarget.Trim();
            if (Regex.IsMatch(text, @"^[A-Za-z]:[\\/]") || text.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return false;
            }

            return Regex.IsMatch(text, @"^[A-Za-z][A-Za-z0-9+.\-]*:");
        }

        /// <summary>Titles compared the way a person reads them: case, edges and doubled spaces ignored.</summary>
        public static bool SameTitle(string a, string b)
        {
            return a != null && b != null && string.Equals(NormaliseTitle(a), NormaliseTitle(b), StringComparison.OrdinalIgnoreCase);
        }

        public static string NormaliseTitle(string title)
        {
            return Regex.Replace(title ?? string.Empty, @"\s+", " ").Trim();
        }

        /// <summary>Two sets of labels hold the same words, whatever their order or case.</summary>
        public static bool SameTags(IEnumerable<string> a, IEnumerable<string> b)
        {
            var left = new HashSet<string>((a ?? Enumerable.Empty<string>()).Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase);
            return left.SetEquals((b ?? Enumerable.Empty<string>()).Select(t => t.Trim()));
        }

        /// <summary>Blank notes are no notes: null, empty and whitespace are the same thing.</summary>
        public static string NormaliseNotes(string notes)
        {
            return string.IsNullOrWhiteSpace(notes) ? null : notes.Replace("\r\n", "\n").Trim();
        }
    }
}
