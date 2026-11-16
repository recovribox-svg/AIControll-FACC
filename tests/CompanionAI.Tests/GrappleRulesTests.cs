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

        // ★ G2: a holder that can let go keeps a hold only while each pinned round pays.
        [Fact] public void AHolderThatCanLetGoKeepsOnlyTheRoundsThatPay()
        {
            // a weak victim (5) against a holder worth 34: each pinned round costs 34 - 5 - 8 = -21
            float bound = ValueModel.GrappleHoldValue(0.9f, 3f, 5f, 34f, 8f, 0.4f, 1f);
            float free = ValueModel.GrappleHoldValue(0.9f, 3f, 5f, 34f, 8f, 0.4f, 1f, true);
            Assert.True(bound < 0f);
            Assert.Equal(2f, free, 5);                       // only the victim's first, held turn: 5 x 0.4
            // a hold that pays is the same either way
            Assert.Equal(ValueModel.GrappleHoldValue(0.8f, 3f, 30f, 30f, 10f, 0.4f, 1f),
                         ValueModel.GrappleHoldValue(0.8f, 3f, 30f, 30f, 10f, 0.4f, 1f, true), 5);
        }

        [Fact] public void APinnedRoundWeighsTheVictimsTurnAndTheFreeHitAgainstTheHoldersTurn()
        {
            Assert.Equal(10f, ValueModel.GrappleRoundNet(30f, 30f, 10f, 1f), 5);
            Assert.Equal(-21f, ValueModel.GrappleRoundNet(5f, 34f, 8f, 1f), 5);
        }

        [Fact] public void KeepingAHoldIsWorthThisRoundAndLaterRoundsOnlyWhileTheyPay()
        {
            // pinned caster (40) held by a holder worth 28: this round 40 - 28, then 1.44 rounds of +24
            Assert.Equal(12f + 1.44f * 24f, ValueModel.GrappleKeepValue(0.8f, 3f, 40f, 28f, 12f, 1f, 1f), 4);
            // a pinned trash mob (10): this round 10 - 28 = -18, later rounds would cost too and are not counted: let go
            Assert.Equal(-18f, ValueModel.GrappleKeepValue(0.9f, 3f, 10f, 28f, 6f, 1f, 1f), 4);
            // held, not yet pinned (soft share): 40 x 0.4 - 28 = -12, but the later rounds pay: keep
            Assert.True(ValueModel.GrappleKeepValue(0.9f, 3f, 40f, 28f, 12f, 0.4f, 1f) > 0f);
            Assert.Equal(0f, ValueModel.GrappleKeepValue(0.9f, 0f, 40f, 28f, 12f, 1f, 1f));
        }

        // CombatManeuver values: None 0, Trip 1, Overrun 2, Disarm 3, Grapple 4, BullRush 5.
        [Fact] public void ManeuverImmunityBlocksItsOwnTypeOnly()
        {
            int swarm = ValueModel.ManeuverImmunityBit(4) | ValueModel.ManeuverImmunityBit(1);   // Grapple + Trip
            Assert.True(ValueModel.ManeuverBlocked(swarm, 4));
            Assert.True(ValueModel.ManeuverBlocked(swarm, 1));
            Assert.False(ValueModel.ManeuverBlocked(swarm, 5));
            Assert.False(ValueModel.ManeuverBlocked(0, 4));                                        // no immunity
        }

        [Fact] public void ManeuverImmunityOfTypeNoneBlocksEveryManeuver()
        {
            int all = ValueModel.ManeuverImmunityBit(0);
            for (int type = 1; type <= 12; type++) Assert.True(ValueModel.ManeuverBlocked(all, type));
            Assert.True(ValueModel.ManeuverBlocked(ValueModel.ManeuverImmunityBit(99), 4));        // unknown type: read as all
        }
    }
}
