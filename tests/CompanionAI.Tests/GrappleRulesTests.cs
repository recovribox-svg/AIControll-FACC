using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ G1: the pure grapple rules (Tactics/ValueModel.Grapple.cs), against the game code they follow.</summary>
    public class GrappleRulesTests
    {
        [Fact] public void AHitThatGrapplesCutsTheRestOfTheRoutine()
        {
            // Claw, claw, bite (the bite does not grab: GriffonGrappleBuff names the claw's weapon type).
            var run = ValueModel.GrappleRun.Begin();
            Assert.Equal(1f, run.Next(0.8f, true, 0.9f), 5);
            Assert.Equal(0.28f, run.Next(0.8f, true, 0.9f), 5);
            Assert.Equal(0.0784f, run.Next(0.7f, false, 0.9f), 5);
            Assert.Equal(0.9216f, run.Starts, 5);
            Assert.Equal(1.3584f, run.Attacks, 4);
        }

        [Fact] public void WeaponsTheTriggerDoesNotNameNeverCutTheRoutine()
        {
            var run = ValueModel.GrappleRun.Begin();
            for (int i = 0; i < 4; i++) Assert.Equal(1f, run.Next(0.9f, false, 1f));
            Assert.Equal(0f, run.Starts);
            Assert.Equal(4f, run.Attacks);
        }

        [Fact] public void AFailedManeuverLetsTheRoutineGoOn()
        {
            var run = ValueModel.GrappleRun.Begin();
            run.Next(1f, true, 0f);
            Assert.Equal(1f, run.Next(1f, true, 0f));
            Assert.Equal(0f, run.Starts);
        }

        [Fact] public void TheHoldLastsWhileTheHolderRollsAndTheVictimStays()
        {
            Assert.Equal(0.72f, ValueModel.GrapplePersists(0.9f, 0.2f), 5);
            Assert.Equal(0f, ValueModel.GrapplePersists(0.9f, 1f));
            Assert.Equal(1f, ValueModel.GrapplePersists(2f, -1f));
        }

        [Fact] public void PinnedRoundsAreTheRoundsAfterTheFirstWithinTheHorizon()
        {
            Assert.Equal(1.44f, ValueModel.GrappleHeldRounds(0.8f, 3f), 5);   // 0.8 + 0.64
            Assert.Equal(1.12f, ValueModel.GrappleHeldRounds(0.8f, 2.5f), 5); // 0.8 + 0.64 x half a round
            Assert.Equal(0f, ValueModel.GrappleHeldRounds(0.8f, 1f));
            Assert.Equal(0f, ValueModel.GrappleHeldRounds(0f, 3f));
            Assert.Equal(2f, ValueModel.GrappleHeldRounds(1f, 3f));
        }

        [Fact] public void HoldingAnEqualEnemyIsWorthItsFirstTurnAndTheFreeAttacks()
        {
            // victim 30, holder 30, free hit 10, 1.44 pinned rounds: 30 x 0.4 + 1.44 x (30 + 10 - 30).
            float value = ValueModel.GrappleHoldValue(0.8f, 3f, 30f, 30f, 10f, 0.4f, 1f);
            Assert.Equal(12f + 14.4f, value, 4);
        }

        [Fact] public void HoldingAWeakEnemyCostsTheHolderMoreThanTheVictimLoses()
        {
            float value = ValueModel.GrappleHoldValue(0.9f, 3f, 5f, 34f, 8f, 0.4f, 1f);
            Assert.True(value < 0f);
        }

        [Fact] public void ADangerousVictimIsWorthPinning()
        {
            Assert.True(ValueModel.GrappleHoldValue(0.8f, 3f, 60f, 34f, 0f, 0.4f, 1f) >
                        ValueModel.GrappleHoldValue(0.8f, 3f, 30f, 34f, 0f, 0.4f, 1f));
        }

        [Fact] public void NoHorizonNoValueAndABrokenHoldKeepsOnlyTheFirstTurn()
        {
            Assert.Equal(0f, ValueModel.GrappleHoldValue(0.8f, 0f, 30f, 30f, 10f, 0.4f, 1f));
            Assert.Equal(12f, ValueModel.GrappleHoldValue(0f, 3f, 30f, 30f, 10f, 0.4f, 1f), 5);
            Assert.Equal(6f, ValueModel.GrappleHoldValue(0f, 0.5f, 30f, 30f, 10f, 0.4f, 1f), 5);
        }

        [Fact] public void ManeuverRollsFollowTheGamesNaturalOneAndTwenty()
        {
            Assert.Equal(0.05f, ValueModel.ManeuverRollChance(10, 40, false), 5);   // only a natural 20
            Assert.Equal(0.95f, ValueModel.ManeuverRollChance(30, 10, false), 5);   // a natural 1 fails
            Assert.Equal(1f, ValueModel.ManeuverRollChance(30, 10, true), 5);       // AlwaysChance
            Assert.Equal(0.5f, ValueModel.ManeuverRollChance(10, 21, false), 5);    // 11..20
        }

        [Fact] public void SkillChecksHaveNoAutomaticResults()
        {
            Assert.Equal(0.3f, ValueModel.SkillCheckChance(5, 20), 5);
            Assert.Equal(0f, ValueModel.SkillCheckChance(0, 40));
            Assert.Equal(1f, ValueModel.SkillCheckChance(30, 10));
        }
    }
}
