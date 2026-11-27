using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.70 features review: a spell prohibition prices a caster its own decisions have not measured yet.</summary>
    public sealed class V0370FeatureReviewTests
    {
        [Fact]
        public void UnmeasuredFullCasterLosesARoundOfItsLevel()
        {
            // A 9th-level wizard with 5th-level spells: full casting, a whole round of a 9th-level unit.
            Assert.Equal(ValueModel.PerRound(9), ValueModel.UnmeasuredSpellEdge(9, 5, usesWeapon: false), 4);
        }

        [Fact]
        public void PartialCastersAndWeaponUsersLoseLess()
        {
            // A 20th-level magus (6th-level spells, Pure Mage: no weapon attack) loses 6/10.5 of a round.
            Assert.Equal(ValueModel.PerRound(20) * 6f / 10.5f, ValueModel.UnmeasuredSpellEdge(20, 6, false), 3);
            // A paladin of 8 (2nd-level spells) who fights with a weapon: a fraction of that, halved.
            float paladin = ValueModel.UnmeasuredSpellEdge(8, 2, usesWeapon: true);
            Assert.True(paladin > 0f && paladin < ValueModel.UnmeasuredSpellEdge(8, 2, false));
            Assert.Equal(ValueModel.PerRound(8) * 2f / 4.5f * 0.5f, paladin, 3);
        }

        [Fact]
        public void NoSpellbookNoLoss()
        {
            Assert.Equal(0f, ValueModel.UnmeasuredSpellEdge(0, 0, false));
            Assert.Equal(0f, ValueModel.UnmeasuredSpellEdge(5, 0, false));
        }

        [Fact]
        public void TheEstimateBlocksARageThatWouldCostTheCasterItsSpells()
        {
            // Inspired Rage in the turn-based log: 33.7 over 4.4 rounds for the others; the Knight Commander counted 0.
            float edge = ValueModel.UnmeasuredSpellEdge(20, 6, false);
            Assert.True(SpellEdgeMemory.Loss(edge, 4.4f) > 33.7f);
        }
    }
}

namespace CompanionAI.Tests
{
    using CompanionAI_Pathfinder.Settings;
    using F = CompanionAI_Pathfinder.Settings.SuggestedSettingsRules.BuildFacts;

    /// <summary>★ issue #115: roles and ranges suggested from a build, checked on the companions' default builds.</summary>
    public sealed class SuggestedSettingsTests
    {
        private static void Expect(F facts, AIRole role, RangePreference range)
        {
            var s = SuggestedSettingsRules.Suggest(facts);
            Assert.Equal(role, s.Role);
            Assert.Equal(range, s.Range);
            Assert.False(string.IsNullOrEmpty(s.Why));
        }

        [Fact]
        public void SeelahPaladinWithShieldTanks() => Expect(new F { Shield = true }, AIRole.Tank, RangePreference.Melee);

        [Fact]
        public void ArchersKeepTheirDistance()
        {
            Expect(new F { RangedWeapon = true }, AIRole.DPS, RangePreference.Ranged);   // Lann, Wenduag, Arueshalae
            // A Thundercaller with a heavy crossbow, or an archer with a buckler, still shoots from range.
            Expect(new F { RangedWeapon = true, Shield = true }, AIRole.DPS, RangePreference.Ranged);
        }

        [Fact]
        public void CastersWithoutWeaponFeatsStayBack()
        {
            Expect(new F { FullCaster = true }, AIRole.DPS, RangePreference.Ranged);                 // Ember, Nenio
            Expect(new F { FullCaster = true, Divine = true }, AIRole.Support, RangePreference.Ranged); // Daeran, Sosiel
        }

        [Fact]
        public void WeaponTrainedDivineCasterSupportsFromMixedRange() =>
            Expect(new F { FullCaster = true, Divine = true, WeaponFeats = true }, AIRole.Support, RangePreference.Mixed); // Camellia

        [Fact]
        public void MartialsAndPartialCastersFightInMelee()
        {
            Expect(new F(), AIRole.DPS, RangePreference.Melee);                       // Greybor, Regill, Woljif, Ulbrig
            Expect(new F { WeaponFeats = true }, AIRole.DPS, RangePreference.Melee);
        }

        [Fact]
        public void BlastingKineticistIsRanged() => Expect(new F { BlastKineticist = true }, AIRole.DPS, RangePreference.Ranged);
    }
}

namespace CompanionAI.Tests
{
    using CompanionAI_Pathfinder.Tactics;

    /// <summary>★ v0.3.70 features3 review: real-time re-decision after losing HP (Wenduag, fight 8).</summary>
    public sealed class RealTimeReplanRulesTests
    {
        [Fact]
        public void WenduagsLossesTriggerTheRule()
        {
            // 100 max: 97 -> 34 (lost 63, below half) and 34 -> 9 (lost 25, below half) are both re-decided.
            Assert.True(RealTimeReplanRules.DropsAfterDamage(97, 34, 100));
            Assert.True(RealTimeReplanRules.DropsAfterDamage(34, 9, 100));
        }

        [Fact]
        public void SmallLossesOrHealthyUnitsKeepTheirCommand()
        {
            Assert.False(RealTimeReplanRules.DropsAfterDamage(100, 80, 100));   // a fifth lost
            Assert.False(RealTimeReplanRules.DropsAfterDamage(100, 60, 100));   // still above half
            Assert.False(RealTimeReplanRules.DropsAfterDamage(10, 10, 0));      // no max HP read: nothing lost
        }

        [Fact]
        public void AnyProvokingPartMakesTheActionProvoke()
        {
            Assert.True(RealTimeReplanRules.Provokes(castCheck: true, casterProvokes: false, rangedRoll: false));   // a spell
            Assert.True(RealTimeReplanRules.Provokes(false, false, rangedRoll: true));                              // a bomb, a shot
            Assert.True(RealTimeReplanRules.Provokes(false, casterProvokes: true, false));
            Assert.False(RealTimeReplanRules.Provokes(false, false, false));                                        // a melee attack
        }

        [Fact]
        public void TheDrowMagusBombIsDecidedAgainWhenTheVenomhodagsArrive()
        {
            // A Stun Bomb waiting to start, chosen with nobody near; a Venomhodag now engages her and can strike.
            Assert.True(RealTimeReplanRules.ReplanForProvoke(provokes: true, started: false, threatenedNow: true,
                                                             countedWhenChosen: false, canAttack: true));
            // Not when the engager was already priced, cannot make an attack of opportunity, the action does not provoke,
            // or the throw has started.
            Assert.False(RealTimeReplanRules.ReplanForProvoke(true, false, true, countedWhenChosen: true, true));
            Assert.False(RealTimeReplanRules.ReplanForProvoke(true, false, true, false, canAttack: false));
            Assert.False(RealTimeReplanRules.ReplanForProvoke(provokes: false, false, true, false, true));
            Assert.False(RealTimeReplanRules.ReplanForProvoke(true, started: true, true, false, true));
        }

        [Fact]
        public void AHealOnItselfOrAWalkAwayIsTheAnswerAndIsKept()
        {
            Assert.True(RealTimeReplanRules.KeepsAfterDamage(healsItself: true, walksAway: false));   // the potion
            Assert.True(RealTimeReplanRules.KeepsAfterDamage(healsItself: false, walksAway: true));   // out of the swarm
            Assert.False(RealTimeReplanRules.KeepsAfterDamage(false, false));                         // an attack chosen at full HP
        }
    }
}
