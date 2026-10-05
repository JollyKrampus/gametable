using NUnit.Framework;
using PalantirLibrary.Sync;
using System.Collections.Generic;
using System.Linq;

namespace PalantirLibrary.Tests
{
    [TestFixture]
    public class SyncPlannerTests
    {
        private static FieldValues V(string state = "Started", int? rating = 3, string notes = "n", params string[] tags)
        {
            return new FieldValues { State = state, Rating = rating, Notes = notes, Tags = tags.ToList() };
        }

        private static SyncDirection Way(List<FieldSync> plan, SyncField field)
        {
            return plan.Single(s => s.Field == field).Direction;
        }

        [Test]
        public void NeverReadBefore_TheHouseIsTheTruth_AndEveryFieldThatDiffersIsPulled()
        {
            var plan = SyncPlanner.Plan(null, V("Finished", 5, "house", "couch"), V("Started", 2, "mine"));

            Assert.IsTrue(plan.All(s => s.Direction == SyncDirection.Pull));
        }

        [Test]
        public void OnlyGameTableChanged_SoItIsPushed()
        {
            var plan = SyncPlanner.Plan(V(), V(), V("Finished", 5, "beat it", "couch"));

            Assert.AreEqual(SyncDirection.Push, Way(plan, SyncField.State));
            Assert.AreEqual(SyncDirection.Push, Way(plan, SyncField.Rating));
            Assert.AreEqual(SyncDirection.Push, Way(plan, SyncField.Notes));
            Assert.AreEqual(SyncDirection.Push, Way(plan, SyncField.Tags));
        }

        [Test]
        public void OnlyTheHouseChanged_SoItIsPulled()
        {
            var plan = SyncPlanner.Plan(V(), V("Finished"), V());

            Assert.AreEqual(SyncDirection.Pull, Way(plan, SyncField.State));
            Assert.AreEqual(SyncDirection.None, Way(plan, SyncField.Rating));
        }

        [Test]
        public void HisTypingWins_WhenBothSidesChangedTheSameField()
        {
            var plan = SyncPlanner.Plan(V(rating: 3), V(rating: 4), V(rating: 1));
            var rating = plan.Single(s => s.Field == SyncField.Rating);

            Assert.AreEqual(SyncDirection.Pull, rating.Direction);
            Assert.IsTrue(rating.Conflict);
        }

        [Test]
        public void AStatusPalantirHasNoWordFor_IsNeverPushed_ButAHouseChangeIsStillPulled()
        {
            var unchanged = SyncPlanner.Plan(V("Started"), V("Started"), V(state: null));
            Assert.AreEqual(SyncDirection.None, Way(unchanged, SyncField.State));

            var houseMoved = SyncPlanner.Plan(V("Started"), V("Finished"), V(state: null));
            Assert.AreEqual(SyncDirection.Pull, Way(houseMoved, SyncField.State));
        }

        [Test]
        public void TagsAreASet_OrderAndCaseAreNotAChange()
        {
            var plan = SyncPlanner.Plan(
                V(tags: new[] { "couch", "co-op" }),
                V(tags: new[] { "Co-op", "couch" }),
                V(tags: new[] { "COUCH", "co-op" }));

            Assert.AreEqual(SyncDirection.None, Way(plan, SyncField.Tags));
        }

        [Test]
        public void BlankAndMissingNotesAreTheSame()
        {
            var plan = SyncPlanner.Plan(V(notes: null), V(notes: "  "), V(notes: ""));

            Assert.AreEqual(SyncDirection.None, Way(plan, SyncField.Notes));
        }

        [Test]
        public void WhatIsSettled_IsTheHouseWithEveryPushApplied()
        {
            var house = V("Started", 3, "house");
            var game = V("Finished", 3, "house");
            var plan = SyncPlanner.Plan(V("Started", 3, "house"), house, game);

            var settled = SyncPlanner.Settled(plan, house, game);

            Assert.AreEqual("Finished", settled.State);
            Assert.AreEqual(3, settled.Rating);
            Assert.AreEqual("house", settled.Notes);
        }
    }
}
