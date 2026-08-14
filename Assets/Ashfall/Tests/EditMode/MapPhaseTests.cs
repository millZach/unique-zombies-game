using NUnit.Framework;
using UnityEngine;
using Ashfall.Core;

namespace Ashfall.Tests
{
    /// <summary>
    /// The phase schedule is the spine of the vertical slice's pacing: rounds 3, 6, 9
    /// and 12 have to change the station, and nothing else may.
    /// </summary>
    public class MapPhaseTests
    {
        [Test]
        public void PhaseBoundariesAreAtRoundsOneThreeSixNineAndTwelve()
        {
            CollectionAssert.AreEqual(new[] { 1, 3, 6, 9, 12 }, MapPhases.StartRounds);
        }

        [Test]
        public void EachRoundMapsToTheExpectedPhase()
        {
            Assert.AreEqual(MapPhase.Standby, MapPhases.ForRound(1));
            Assert.AreEqual(MapPhase.Standby, MapPhases.ForRound(2));
            Assert.AreEqual(MapPhase.Breach, MapPhases.ForRound(3));
            Assert.AreEqual(MapPhase.Breach, MapPhases.ForRound(5));
            Assert.AreEqual(MapPhase.Surge, MapPhases.ForRound(6));
            Assert.AreEqual(MapPhase.Surge, MapPhases.ForRound(8));
            Assert.AreEqual(MapPhase.Blackout, MapPhases.ForRound(9));
            Assert.AreEqual(MapPhase.Blackout, MapPhases.ForRound(11));
            Assert.AreEqual(MapPhase.Meridian, MapPhases.ForRound(12));
        }

        [Test]
        public void PhaseNeverGoesBackwards()
        {
            var previous = MapPhase.Standby;
            for (int round = 1; round <= 40; round++)
            {
                MapPhase current = MapPhases.ForRound(round);
                Assert.GreaterOrEqual((int)current, (int)previous, $"Phase regressed at round {round}.");
                previous = current;
            }
        }

        [Test]
        public void PhaseHoldsAtMeridianBeyondTheSlice()
        {
            Assert.AreEqual(MapPhase.Meridian, MapPhases.ForRound(13));
            Assert.AreEqual(MapPhase.Meridian, MapPhases.ForRound(99));
        }

        [Test]
        public void OnlyTheScheduledRoundsAreTransitions()
        {
            for (int round = 1; round <= 20; round++)
            {
                bool expected = round == 1 || round == 3 || round == 6 || round == 9 || round == 12;
                Assert.AreEqual(expected, MapPhases.IsTransitionRound(round), $"Round {round}.");
            }
        }

        [Test]
        public void EveryPhaseHasADisplayNameAndAHeadline()
        {
            for (int i = 0; i < MapPhases.Count; i++)
            {
                var phase = (MapPhase)i;
                Assert.IsNotEmpty(MapPhases.DisplayName(phase), $"{phase} has no display name.");
                Assert.IsNotEmpty(MapPhases.Headline(phase), $"{phase} has no headline.");
            }
        }

        [Test]
        public void PhaseTintWalksFromAmberToTeal()
        {
            Color standby = MapPhases.Tint(MapPhase.Standby);
            Color meridian = MapPhases.Tint(MapPhase.Meridian);

            Assert.Greater(standby.r, standby.b, "Standby should read warm.");
            Assert.Greater(meridian.b, meridian.r, "Meridian should read cold.");

            // And the intermediate phases should interpolate, not jump around.
            float previousWarmth = float.MaxValue;
            for (int i = 0; i < MapPhases.Count; i++)
            {
                Color tint = MapPhases.Tint((MapPhase)i);
                float warmth = tint.r - tint.b;
                Assert.Less(warmth, previousWarmth + 0.0001f, $"Phase {i} is not cooler than the one before it.");
                previousWarmth = warmth;
            }
        }

        [Test]
        public void PlayableStatesAllowControlAndTerminalStatesDoNot()
        {
            Assert.IsTrue(GameState.Combat.IsPlayable());
            Assert.IsTrue(GameState.RoundIntro.IsPlayable());
            Assert.IsTrue(GameState.RoundClear.IsPlayable());
            Assert.IsFalse(GameState.Boot.IsPlayable());
            Assert.IsFalse(GameState.Defeat.IsPlayable());
            Assert.IsFalse(GameState.RunComplete.IsPlayable());

            Assert.IsTrue(GameState.Defeat.IsRunOver());
            Assert.IsTrue(GameState.RunComplete.IsRunOver());
            Assert.IsFalse(GameState.Combat.IsRunOver());
        }
    }
}
