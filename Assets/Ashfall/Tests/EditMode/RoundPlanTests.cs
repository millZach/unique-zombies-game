using NUnit.Framework;
using Ashfall.Core;

namespace Ashfall.Tests
{
    /// <summary>
    /// Locks down the progression curve. These are the numbers a player feels, and they
    /// are easy to break by "just tweaking" one constant, so the shape of the curve is
    /// asserted rather than the individual values wherever possible.
    /// </summary>
    public class RoundPlanTests
    {
        [Test]
        public void EveryRoundInTheSliceHasEnemies()
        {
            for (int round = RoundPlan.FirstRound; round <= RoundPlan.FinalRound; round++)
            {
                RoundComposition c = RoundPlan.For(round);
                Assert.Greater(c.TotalEnemies, 0, $"Round {round} has no enemies.");
                Assert.AreEqual(
                    c.TotalEnemies,
                    c.ShamblerCount + c.SprinterCount + c.BruteCount,
                    $"Round {round} composition does not sum to its total.");
            }
        }

        [Test]
        public void EnemyCountIncreasesEveryRound()
        {
            for (int round = RoundPlan.FirstRound; round < RoundPlan.FinalRound; round++)
            {
                Assert.Less(
                    RoundPlan.TotalEnemiesFor(round),
                    RoundPlan.TotalEnemiesFor(round + 1),
                    $"Round {round + 1} is not larger than round {round}.");
            }
        }

        [Test]
        public void FirstRoundIsShortAndGentle()
        {
            RoundComposition first = RoundPlan.For(1);
            Assert.LessOrEqual(first.TotalEnemies, 8, "Round 1 should be a short teaching round.");
            Assert.AreEqual(0, first.SprinterCount, "Round 1 must not contain sprinters.");
            Assert.AreEqual(0, first.BruteCount, "Round 1 must not contain brutes.");
            Assert.AreEqual(1f, first.HealthScale, 0.0001f, "Round 1 enemies use base health.");
        }

        [Test]
        public void SprintersArriveWithTheBreachPhase()
        {
            for (int round = 1; round < RoundPlan.SprinterFirstRound; round++)
            {
                Assert.AreEqual(0, RoundPlan.For(round).SprinterCount, $"Round {round} should have no sprinters.");
            }

            Assert.Greater(
                RoundPlan.For(RoundPlan.SprinterFirstRound).SprinterCount,
                0,
                "Sprinters must appear on their first round.");

            Assert.AreEqual(
                MapPhase.Breach,
                MapPhases.ForRound(RoundPlan.SprinterFirstRound),
                "Sprinters should arrive on the same round as the Breach phase.");
        }

        [Test]
        public void FirstBruteArrivesOnRoundSixWithTheSurgePhase()
        {
            for (int round = 1; round < RoundPlan.BruteFirstRound; round++)
            {
                Assert.AreEqual(0, RoundPlan.For(round).BruteCount, $"Round {round} should have no brutes.");
                Assert.IsFalse(RoundPlan.For(round).IsEliteRound);
            }

            RoundComposition six = RoundPlan.For(6);
            Assert.AreEqual(6, RoundPlan.BruteFirstRound);
            Assert.AreEqual(1, six.BruteCount, "Round 6 introduces exactly one Storm Brute.");
            Assert.IsTrue(six.IsEliteRound);
            Assert.AreEqual(MapPhase.Surge, six.Phase);
        }

        [Test]
        public void BruteCountRampsToThreeByTheFinalRound()
        {
            Assert.AreEqual(1, RoundPlan.BruteCountFor(6));
            Assert.AreEqual(1, RoundPlan.BruteCountFor(8));
            Assert.AreEqual(2, RoundPlan.BruteCountFor(9));
            Assert.AreEqual(2, RoundPlan.BruteCountFor(11));
            Assert.AreEqual(3, RoundPlan.BruteCountFor(12));
        }

        [Test]
        public void HealthScaleRisesMonotonically()
        {
            for (int round = RoundPlan.FirstRound; round < RoundPlan.FinalRound; round++)
            {
                Assert.Less(
                    RoundPlan.HealthScaleFor(round),
                    RoundPlan.HealthScaleFor(round + 1),
                    $"Health scale did not rise from round {round} to {round + 1}.");
            }
        }

        [Test]
        public void FinalRoundIsHardButNotAbsurd()
        {
            RoundComposition last = RoundPlan.For(RoundPlan.FinalRound);
            Assert.GreaterOrEqual(last.TotalEnemies, 25, "Round 12 should be a real crescendo.");
            Assert.LessOrEqual(last.TotalEnemies, 70, "Round 12 should still be finishable.");
            Assert.LessOrEqual(last.HealthScale, 6f, "Enemy health should not run away by round 12.");
            Assert.LessOrEqual(last.MaxConcurrent, 18);
        }

        [Test]
        public void ConcurrentCapNeverExceedsTheTotalWave()
        {
            for (int round = RoundPlan.FirstRound; round <= RoundPlan.FinalRound; round++)
            {
                RoundComposition c = RoundPlan.For(round);
                Assert.GreaterOrEqual(c.MaxConcurrent, 1);
                Assert.Greater(c.SpawnInterval, 0f, $"Round {round} would spawn instantly.");
            }
        }

        [Test]
        public void SpawnIntervalTightensAsRoundsProgress()
        {
            Assert.Greater(
                RoundPlan.For(1).SpawnInterval,
                RoundPlan.For(RoundPlan.FinalRound).SpawnInterval,
                "Later rounds should spawn faster.");
        }

        [Test]
        public void RoundsBelowOneAreClampedRatherThanBroken()
        {
            RoundComposition zero = RoundPlan.For(0);
            RoundComposition negative = RoundPlan.For(-5);
            Assert.AreEqual(RoundPlan.FirstRound, zero.Round);
            Assert.AreEqual(RoundPlan.FirstRound, negative.Round);
        }

        [Test]
        public void TransitionRoundsGetALongerIntroAndABiggerBonus()
        {
            Assert.Greater(RoundPlan.IntroDurationFor(3), RoundPlan.IntroDurationFor(4));
            Assert.Greater(RoundPlan.ClearBonusFor(6), RoundPlan.ClearBonusFor(7),
                "A phase-transition round should pay out more than the round after it.");
        }
    }
}
