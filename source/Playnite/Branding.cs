using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace Playnite
{
    /// <summary>
    /// GameTable's identity: every place this fork differs from upstream Playnite in what it is
    /// called, where it keeps its data and how it finds itself.
    /// </summary>
    /// <remarks>
    /// GameTable is Playnite (MIT, Josef Nemec) forked as the play queue's head for Palantír. The
    /// namespaces, the SDK and every add-on contract stay Playnite's, so community add-ons load
    /// unchanged and a <c>git merge upstream/master</c> conflicts here and almost nowhere else.
    /// Anything that would let GameTable and a real Playnite on the same PC see each other's data,
    /// lock or pipe, or let an upstream update replace GameTable, has to be named here.
    /// </remarks>
    public static class Branding
    {
        /// <summary>The product name shown to the user.</summary>
        public const string ProductName = "GameTable";

        /// <summary>Whose work this is built on, as the About box and the licence say.</summary>
        public const string UpstreamName = "Playnite";

        /// <summary>One line of credit, required in spirit by the MIT notice and kept in fact.</summary>
        public const string BasedOn = "Based on Playnite by Josef Nemec (MIT).";

        /// <summary>The folder under %AppData% and %TEMP%. Not Playnite's, so both can be installed.</summary>
        public const string DataFolderName = "GameTable";

        /// <summary>The URI scheme registered for this install: gametable://playnite/start/&lt;id&gt;.</summary>
        /// <remarks>
        /// Only the scheme changes. <see cref="PlayniteUriHandler.ParseUri"/> ignores the scheme and
        /// reads the source from the host part, so every playnite:// path still works after it.
        /// Registering playnite:// as well would take it from a real Playnite on the same PC.
        /// </remarks>
        public const string UriScheme = "gametable";

        /// <summary>The HKCU\Software\Classes key holding <see cref="UriScheme"/>.</summary>
        public const string UriRegistryKey = "GameTable";

        /// <summary>The single-instance mutex. Playnite's own is "PlayniteInstaceMutex".</summary>
        public const string InstanceMutexName = "GameTableInstanceMutex";

        /// <summary>The named pipe a second launch hands its arguments to.</summary>
        public const string PipeEndpoint = "net.pipe://localhost/GameTablePipe";

        /// <summary>The shortcut name in the Startup folder when "launch when you start your computer" is on.</summary>
        public const string StartupShortcutName = "GameTable.lnk";

        /// <summary>
        /// Whether a crash report may be uploaded. Never: Playnite's diagnostics service is Josef
        /// Nemec's, and a GameTable crash is not his to read. The package is saved and shown instead.
        /// </summary>
        public static readonly bool UploadsDiagnostics = false;

        /// <summary>
        /// Strings that name the upstream project rather than this app, and so keep "Playnite".
        /// </summary>
        public static readonly HashSet<string> UpstreamStringKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "LOCSettingsCreateLocalization",
            "LOCPatreonDevelopMessage",
            "LOCDBCorruptionCrashMessage",
        };

        private static readonly Regex productWord = new Regex(@"\bPlaynite\b", RegexOptions.Compiled);

        /// <summary>
        /// One user-facing string with the product's name in it, renamed. The word "Playnite" only,
        /// whole and capitalised, so playnite:// paths, file names and API names are untouched.
        /// </summary>
        public static string Rebrand(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return productWord.Replace(text, ProductName);
        }

        /// <summary>
        /// Renames the product in every string of a localization dictionary, in place, leaving the
        /// keys in <see cref="UpstreamStringKeys"/> alone.
        /// </summary>
        /// <remarks>
        /// Done once at load rather than in the forty translation files, so a Crowdin sync upstream
        /// merges without a conflict and every language is renamed, not only English.
        /// </remarks>
        public static void ApplyTo(ResourceDictionary dictionary)
        {
            if (dictionary == null)
            {
                return;
            }

            foreach (var key in dictionary.Keys.Cast<object>().ToList())
            {
                if (key is string name && UpstreamStringKeys.Contains(name))
                {
                    continue;
                }

                if (dictionary[key] is string text && text.Contains(UpstreamName))
                {
                    dictionary[key] = Rebrand(text);
                }
            }
        }
    }
}
