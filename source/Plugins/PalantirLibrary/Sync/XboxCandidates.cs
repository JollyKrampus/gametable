using PalantirLibrary.Api;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PalantirLibrary.Sync
{
    /// <summary>A game in GameTable's database, described only by what the Xbox list needs.</summary>
    public class LibraryGame
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid PluginId { get; set; }

        /// <summary>
        /// The library's own id. The Xbox add-on writes a package family name for a PC title and
        /// CONSOLE_{titleId}_{mediaItemType} for a console one.
        /// </summary>
        public string LibraryGameId { get; set; }

        /// <summary>
        /// Playnite's platform specification ids (pc_windows, xbox_one...), because a platform's name
        /// is his to rename and its specification id is not.
        /// </summary>
        public List<string> Platforms { get; set; } = new List<string>();

        /// <summary>Seconds, as Playnite keeps it.</summary>
        public ulong Playtime { get; set; }

        public bool Hidden { get; set; }
    }

    /// <summary>One Xbox game GameTable has and Palantír's play list does not.</summary>
    public class XboxCandidate
    {
        public Guid GameId { get; set; }
        public string Title { get; set; }

        /// <summary>The platform the row is written with: <see cref="XboxCandidates.ConsoleWord"/> or <see cref="XboxCandidates.PcWord"/>.</summary>
        public string Platform { get; set; }

        /// <summary>The minutes Playnite holds for it, or null when it holds none.</summary>
        public int? PlayedMinutes { get; set; }
    }

    /// <summary>
    /// The Xbox games GameTable has that Palantír's play list does not: what "Xbox games not on the
    /// play list" offers, one press each and never by itself. Pure: no Playnite runtime, no house.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A game is not offered when it is linked to a row, when a play row already has its title (one
    /// row or two: the house refuses a second row under a title it holds), or when a play row in
    /// Palantír's trash has its title. Throwing a row away is an answer Troy gave about the game, so
    /// it is not offered back to him; the house's own Steam list keeps the same rule.
    /// </para>
    /// <para>
    /// A game hidden in GameTable is not offered either. The Xbox add-on imports console apps beside
    /// console games and keeps nothing that says which is which, so hiding one is how it leaves.
    /// </para>
    /// </remarks>
    public static class XboxCandidates
    {
        /// <summary>Playnite's Xbox library add-on (BuiltinExtension.XboxLibrary).</summary>
        public static readonly Guid XboxPluginId = Guid.Parse("7E4FBB5E-2AE3-48D4-8BA0-6B30E7A4E287");

        /// <summary>Troy's platform word for a game played on his Xbox console.</summary>
        public const string ConsoleWord = "Xbox";

        /// <summary>Troy's platform word for a Game Pass or Microsoft Store game on the PC, as his rows already write it.</summary>
        public const string PcWord = "Xbox/PC";

        private const string ConsoleIdPrefix = "CONSOLE_";

        private const string PcPlatform = "pc_windows";

        private static readonly HashSet<string> consolePlatforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "xbox", "xbox360", "xbox_one", "xbox_series",
        };

        /// <summary>
        /// The titles an Xbox game is not offered under: every play row's, and every binned play row's.
        /// The trash holds every list's rows, and only the play list's are about a game.
        /// </summary>
        public static List<string> TakenTitles(IEnumerable<EntrySummary> playRows, IEnumerable<EntrySummary> binned)
        {
            var thrownAway = (binned ?? Enumerable.Empty<EntrySummary>())
                .Where(r => r != null && string.Equals(r.Queue, "Play", StringComparison.OrdinalIgnoreCase));

            return (playRows ?? Enumerable.Empty<EntrySummary>())
                .Concat(thrownAway)
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Title))
                .Select(r => r.Title)
                .ToList();
        }

        public static List<XboxCandidate> Find(IEnumerable<LibraryGame> games, LinkState links, IEnumerable<string> takenTitles)
        {
            var taken = new HashSet<string>((takenTitles ?? Enumerable.Empty<string>()).Select(GameMapping.TitleKey), StringComparer.Ordinal);

            return (games ?? Enumerable.Empty<LibraryGame>())
                .Where(g => g != null && g.PluginId == XboxPluginId && !g.Hidden && !string.IsNullOrWhiteSpace(g.Name))
                .Where(g => links.EntryFor(g.Id) == null && !taken.Contains(GameMapping.TitleKey(g.Name)))
                .Select(g => new XboxCandidate
                {
                    GameId = g.Id,
                    Title = g.Name,
                    Platform = PlatformWord(g),
                    PlayedMinutes = g.Playtime >= 60 ? (int)Math.Min((ulong)int.MaxValue, g.Playtime / 60) : (int?)null,
                })
                .OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string PlatformWord(LibraryGame game)
        {
            return IsConsole(game) ? ConsoleWord : PcWord;
        }

        /// <summary>
        /// Whether a game is a console title: by the id the add-on gives every console title, and
        /// otherwise by an Xbox console being its only kind of platform, which is his edit if the
        /// add-on did not write it. Everything else the add-on imports is a PC title.
        /// </summary>
        public static bool IsConsole(LibraryGame game)
        {
            if (game.LibraryGameId != null && game.LibraryGameId.StartsWith(ConsoleIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var platforms = game.Platforms ?? new List<string>();
            return platforms.Any(p => p != null && consolePlatforms.Contains(p))
                && !platforms.Any(p => string.Equals(p, PcPlatform, StringComparison.OrdinalIgnoreCase));
        }
    }
}
