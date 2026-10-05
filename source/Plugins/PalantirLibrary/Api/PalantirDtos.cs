using System;
using System.Collections.Generic;

namespace PalantirLibrary.Api
{
    // Hand-written copies of the few records GameTable reads from and sends to Palantír's
    // /api/palantir. GameTable is .NET Framework 4.6.2 and cannot reference the house's
    // Palantir.Contracts (net10), so these are matched by NAME, and the house pins every one of
    // these names in tests/Palantir.Api.Tests/GameTableContractTests.cs. Change both in the same week.
    //
    // Days cross as "yyyy-MM-dd" strings, exactly as the house writes a DateOnly.

    /// <summary>One play-queue row as a list shows it (Palantír's EntrySummaryDto).</summary>
    public class EntrySummary
    {
        public Guid Id { get; set; }
        public string Queue { get; set; }
        public string Title { get; set; }
        public int? Priority { get; set; }
        public string State { get; set; }
        public string Platform { get; set; }
        public int? Rating { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public string CoverImagePath { get; set; }
        public string ArtworkKey { get; set; }
        public string BackgroundKey { get; set; }
        public string LogoKey { get; set; }
        public string DeletedOn { get; set; }
    }

    /// <summary>One sitting at one game (Palantír's PlaySessionDto).</summary>
    public class PlaySession
    {
        public Guid Id { get; set; }
        public string PlayedOn { get; set; }
        public int? Minutes { get; set; }
        public string Notes { get; set; }
        public int Sequence { get; set; }

        /// <summary>"Typed" or "GameTable".</summary>
        public string Source { get; set; }
    }

    /// <summary>One row in full (Palantír's EntryDetailDto), the fields GameTable uses.</summary>
    public class EntryDetail
    {
        public EntrySummary Entry { get; set; }
        public string Notes { get; set; }
        public string ReleaseNote { get; set; }
        public string WhereToWatch { get; set; }
        public string Company { get; set; }
        public string Format { get; set; }
        public string Shelf { get; set; }
        public string LaunchTarget { get; set; }
        public string Synopsis { get; set; }
        public List<PlaySession> Sessions { get; set; } = new List<PlaySession>();
        public int TotalPlayMinutes { get; set; }
        public string LastPlayedOn { get; set; }
    }

    public class AddEntryRequest
    {
        public string Queue { get; set; }
        public string Title { get; set; }
        public int? Priority { get; set; }
        public string Notes { get; set; }
        public string Platform { get; set; }
        public string ReleaseNote { get; set; }
    }

    /// <summary>
    /// The whole edit form. Every field is sent, read back from the row a moment earlier, because
    /// the house treats a missing field as cleared.
    /// </summary>
    public class UpdateEntryRequest
    {
        public string Title { get; set; }
        public int? Priority { get; set; }
        public string Notes { get; set; }
        public string Platform { get; set; }
        public string ReleaseNote { get; set; }
        public string Author { get; set; }
        public string Format { get; set; }
        public string Shelf { get; set; }
        public string WhereToWatch { get; set; }
        public string CoverImagePath { get; set; }
        public string LaunchTarget { get; set; }
        public string Kind { get; set; }
        public int? ReleaseYear { get; set; }
        public string Company { get; set; }

        /// <summary>The form exactly as the row has it now, ready for one field to change.</summary>
        public static UpdateEntryRequest From(EntryDetail detail)
        {
            return new UpdateEntryRequest
            {
                Title = detail.Entry.Title,
                Priority = detail.Entry.Priority,
                Notes = detail.Notes,
                Platform = detail.Entry.Platform,
                ReleaseNote = detail.ReleaseNote,
                Format = detail.Format,
                Shelf = detail.Shelf,
                WhereToWatch = detail.WhereToWatch,
                CoverImagePath = detail.Entry.CoverImagePath,
                LaunchTarget = detail.LaunchTarget,
                Company = detail.Company,
            };
        }
    }

    public class SetEntryStateRequest
    {
        public string State { get; set; }
        public string ChangedOn { get; set; }
    }

    public class SetRatingRequest
    {
        public int? Rating { get; set; }
        public string On { get; set; }
    }

    public class TagRequest
    {
        public string Name { get; set; }
    }

    public class LogSessionRequest
    {
        public string PlayedOn { get; set; }
        public int? Minutes { get; set; }
        public string Notes { get; set; }
        public string Source { get; set; }
    }
}
