using System;
using System.Collections.Generic;
using System.Linq;

namespace PalantirLibrary.Sync
{
    /// <summary>
    /// The four fields both sides edit, as one side holds them at one moment.
    /// </summary>
    public class FieldValues
    {
        /// <summary>Palantír's word: Queued, Started, Finished, Garbage. Null is "no opinion".</summary>
        public string State { get; set; }

        /// <summary>One to five, or null.</summary>
        public int? Rating { get; set; }

        public List<string> Tags { get; set; } = new List<string>();

        public string Notes { get; set; }

        public FieldValues Clone()
        {
            return new FieldValues
            {
                State = State,
                Rating = Rating,
                Tags = Tags?.ToList() ?? new List<string>(),
                Notes = Notes,
            };
        }
    }

    public enum SyncDirection
    {
        /// <summary>Both sides agree with what was last read; nothing moves.</summary>
        None,

        /// <summary>The house changed since GameTable last read it: GameTable takes the house's value.</summary>
        Pull,

        /// <summary>Only GameTable changed: the house takes GameTable's value.</summary>
        Push,
    }

    public enum SyncField
    {
        State,
        Rating,
        Tags,
        Notes,
    }

    /// <summary>One field's decision.</summary>
    public class FieldSync
    {
        public SyncField Field { get; set; }
        public SyncDirection Direction { get; set; }

        /// <summary>True when both sides changed and the house's value won.</summary>
        public bool Conflict { get; set; }
    }

    /// <summary>
    /// Read before write: which way each field moves, given what GameTable last read from the
    /// house, what the house says now, and what GameTable says now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>His typing wins.</b> A field that changed in the house since GameTable last read it
    /// came from Troy at a Palantír screen, and it is pulled into GameTable even when GameTable
    /// changed it too. A field only GameTable changed is pushed. A field neither changed stays.
    /// </para>
    /// <para>
    /// GameTable's "no opinion" (a completion status Palantír has no word for, like On Hold) is
    /// never pushed: it would overwrite a state with nothing.
    /// </para>
    /// </remarks>
    public static class SyncPlanner
    {
        public static List<FieldSync> Plan(FieldValues lastRead, FieldValues house, FieldValues game)
        {
            if (house == null)
            {
                throw new ArgumentNullException(nameof(house));
            }

            if (game == null)
            {
                throw new ArgumentNullException(nameof(game));
            }

            // Never read before: the house is the truth, and GameTable takes it all.
            if (lastRead == null)
            {
                return new List<FieldSync>
                {
                    Decide(SyncField.State, true, !SameState(house.State, game.State), false),
                    Decide(SyncField.Rating, true, house.Rating != game.Rating, false),
                    Decide(SyncField.Tags, true, !GameMapping.SameTags(house.Tags, game.Tags), false),
                    Decide(SyncField.Notes, true, !SameNotes(house.Notes, game.Notes), false),
                };
            }

            return new List<FieldSync>
            {
                Decide(SyncField.State,
                    houseChanged: !SameState(lastRead.State, house.State),
                    differs: !SameState(house.State, game.State),
                    gameChanged: game.State != null && !SameState(lastRead.State, game.State)),
                Decide(SyncField.Rating,
                    houseChanged: lastRead.Rating != house.Rating,
                    differs: house.Rating != game.Rating,
                    gameChanged: lastRead.Rating != game.Rating),
                Decide(SyncField.Tags,
                    houseChanged: !GameMapping.SameTags(lastRead.Tags, house.Tags),
                    differs: !GameMapping.SameTags(house.Tags, game.Tags),
                    gameChanged: !GameMapping.SameTags(lastRead.Tags, game.Tags)),
                Decide(SyncField.Notes,
                    houseChanged: !SameNotes(lastRead.Notes, house.Notes),
                    differs: !SameNotes(house.Notes, game.Notes),
                    gameChanged: !SameNotes(lastRead.Notes, game.Notes)),
            };
        }

        /// <summary>What GameTable will have read once a plan is carried out.</summary>
        public static FieldValues Settled(List<FieldSync> plan, FieldValues house, FieldValues game)
        {
            var settled = house.Clone();
            foreach (var step in plan.Where(s => s.Direction == SyncDirection.Push))
            {
                switch (step.Field)
                {
                    case SyncField.State: settled.State = game.State; break;
                    case SyncField.Rating: settled.Rating = game.Rating; break;
                    case SyncField.Tags: settled.Tags = game.Tags?.ToList() ?? new List<string>(); break;
                    case SyncField.Notes: settled.Notes = game.Notes; break;
                }
            }

            return settled;
        }

        private static FieldSync Decide(SyncField field, bool houseChanged, bool differs, bool gameChanged)
        {
            var step = new FieldSync { Field = field, Direction = SyncDirection.None };
            if (!differs)
            {
                return step;
            }

            if (houseChanged)
            {
                step.Direction = SyncDirection.Pull;
                step.Conflict = gameChanged;
            }
            else if (gameChanged)
            {
                step.Direction = SyncDirection.Push;
            }

            return step;
        }

        private static bool SameState(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameNotes(string a, string b)
        {
            return string.Equals(GameMapping.NormaliseNotes(a), GameMapping.NormaliseNotes(b), StringComparison.Ordinal);
        }
    }
}
