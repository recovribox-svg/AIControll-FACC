using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class SaveBoostTests
    {
        [Theory]
        [InlineData(5, 20, 1)]     // DC is still below the natural-1 floor after the boost.
        [InlineData(5, 20, 20)]    // Cross the floor only after the actual DC rises far enough.
        [InlineData(21, 10, 1)]
        [InlineData(30, 10, 1)]    // The natural-20 cap cannot be raised.
        [InlineData(40, 10, 3)]
        public void DcBoost_MatchesEnumeratedD20Outcomes(int dc, int save, int boost)
        {
            int failuresBefore = 0, failuresAfter = 0;
            for (int roll = 1; roll <= 20; roll++)
            {
                if (roll == 1 || roll != 20 && roll + save < dc) failuresBefore++;
                if (roll == 1 || roll != 20 && roll + save < dc + boost) failuresAfter++;
            }
            float before = ValueModel.SpellSaveFailChance(dc, save, persistent: false);
            float after = ValueModel.SpellSaveFailChance(dc + boost, save, persistent: false);
            Assert.Equal(failuresBefore / 20f, before, 5);
            Assert.Equal(failuresAfter / 20f, after, 5);
        }

        [Fact]
        public void DcBoost_DoesNotIncreaseSpellResistanceOrAttackChances()
        {
            // Ten failed saves become eleven. Only those outcomes change even with several independent defenses.
            const float defenses = 0.5f * 0.4f;
            float before = ValueModel.SpellSaveFailChance(21, 10, false);
            float after = ValueModel.SpellSaveFailChance(22, 10, false);
            float share = ValueModel.SaveBoostShare(before, after);
            Assert.Equal(0.1f, share, 5);
            Assert.Equal(11f / 20f * defenses, before * defenses * (1f + share), 5);
        }

        [Fact]
        public void PersistentDcBoost_MatchesFourHundredRollPairs()
        {
            int failures = 0;
            for (int first = 1; first <= 20; first++)
                for (int second = 1; second <= 20; second++)
                    if (first == 1 || first != 20 && first + 10 < 22 ||
                        second == 1 || second != 20 && second + 10 < 22)
                        failures++;
            float before = ValueModel.SpellSaveFailChance(21, 10, true);
            float after = ValueModel.SpellSaveFailChance(22, 10, true);
            Assert.Equal(0.75f, before, 5);
            Assert.Equal(failures / 400f, after, 5);
            Assert.Equal(0.7975f, after, 5); // Adding .05 to the already Persistent chance would incorrectly give .80.
        }

        [Fact]
        public void PersistentAtNaturalTwentyCap_CannotGainAnotherDcPoint()
        {
            float before = ValueModel.SpellSaveFailChance(40, 10, true);
            float after = ValueModel.SpellSaveFailChance(41, 10, true);
            Assert.Equal(0.9975f, before, 5);
            Assert.Equal(0f, ValueModel.SaveBoostShare(before, after), 5);
        }

        [Theory]
        [InlineData(false, false, false, 0.5f, 0.55f, 0.1f)]
        [InlineData(true, false, false, 0.75f, 0.775f, 1f / 30f)]
        [InlineData(true, true, false, 0.5f, 0.55f, 0.1f)]
        [InlineData(true, true, true, 0.25f, 0.275f, 0.1f)]
        public void DamageGain_UsesActualSavedAndFailedDamageFractions(bool half, bool evasion, bool improved,
                                                                     float expectedBefore, float expectedAfter, float expectedShare)
        {
            float before = ValueModel.SaveEffectFactor(0.5f, half, evasion, improved);
            float after = ValueModel.SaveEffectFactor(0.55f, half, evasion, improved);
            Assert.Equal(expectedBefore, before, 5);
            Assert.Equal(expectedAfter, after, 5);
            Assert.Equal(expectedShare, ValueModel.SaveBoostShare(before, after), 5);
        }

        [Fact]
        public void AutomaticSuccess_DcDoesNotGiveTheEffectAChance()
        {
            float before = ValueModel.SpellSaveFailChance(21, 10, true, autoPass: true);
            float after = ValueModel.SpellSaveFailChance(100, 10, true, autoPass: true);
            Assert.Equal(0f, before);
            Assert.Equal(0f, after);
            Assert.Equal(0f, ValueModel.SaveBoostShare(before, after));
            Assert.Equal(0f, ValueModel.SaveBoostShare(ValueModel.SaveEffectFactor(before, true),
                                                    ValueModel.SaveEffectFactor(after, true)));
        }

        [Fact]
        public void NoImprovement_HasNoMarginalValue()
        {
            Assert.Equal(0f, ValueModel.SaveBoostShare(0.5f, 0.5f));
            Assert.Equal(0f, ValueModel.SaveBoostShare(0.5f, 0.4f));
        }

        [Fact]
        public void DcBoost_ChangesBothOrdinarySaveAndShadowDisbelief()
        {
            float failBefore = ValueModel.SpellSaveFailChance(21, 10, false);
            float failAfter = ValueModel.SpellSaveFailChance(22, 10, false);
            float before = failBefore * ValueModel.ShadowEffectFactor(failBefore, 0.2f);
            float after = failAfter * ValueModel.ShadowEffectFactor(failAfter, 0.2f);
            Assert.Equal(0.3f, before, 5);
            Assert.Equal(0.352f, after, 5);
            Assert.Equal(0.17333333f, ValueModel.SaveBoostShare(before, after), 5);
        }

        [Fact]
        public void ShadowWithoutOrdinarySave_StillGainsFromDisbeliefDc()
        {
            // The ordinary spell factor is1: only the separate disbelief Will save changes.
            float before = ValueModel.ShadowEffectFactor(ValueModel.SpellSaveFailChance(21, 10, false), 0.2f);
            float after = ValueModel.ShadowEffectFactor(ValueModel.SpellSaveFailChance(22, 10, false), 0.2f);
            Assert.Equal(0.6f, before, 5);
            Assert.Equal(0.64f, after, 5);
            Assert.Equal(1f / 15f, ValueModel.SaveBoostShare(before, after), 5);
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(0f)] // Existing reader treats a nonpositive reality factor as no shadow reduction.
        public void FullRealityOrNonpositiveSettings_NoDisbeliefGain(float reality)
        {
            float before = ValueModel.ShadowEffectFactor(0.5f, reality);
            float after = ValueModel.ShadowEffectFactor(0.55f, reality);
            Assert.Equal(1f, before);
            Assert.Equal(1f, after);
            Assert.Equal(0f, ValueModel.SaveBoostShare(before, after));
        }

        [Fact]
        public void ShadowDisbelief_PersistentUsesAdjustedWillDc()
        {
            float before = ValueModel.ShadowEffectFactor(ValueModel.SpellSaveFailChance(21, 10, true), 0.2f);
            float after = ValueModel.ShadowEffectFactor(ValueModel.SpellSaveFailChance(22, 10, true), 0.2f);
            Assert.Equal(0.8f, before, 5);
            Assert.Equal(0.838f, after, 5);
            Assert.Equal(0.0475f, ValueModel.SaveBoostShare(before, after), 5);
        }

        [Fact]
        public void ShadowDisbelief_AtWillCapDoesNotGain()
        {
            float before = ValueModel.ShadowEffectFactor(ValueModel.SpellSaveFailChance(40, 10, false), 0.2f);
            float after = ValueModel.ShadowEffectFactor(ValueModel.SpellSaveFailChance(41, 10, false), 0.2f);
            Assert.Equal(0.96f, before, 5);
            Assert.Equal(0f, ValueModel.SaveBoostShare(before, after));
        }

        // ★ v0.3.30 competence2 (PR #20 review): free Persistent through the same save-effect factor as a DC boost.
        [Fact]
        public void Persistent_SaveNegates_GainsTheSecondRoll()
        {
            Assert.Equal(0.75f, ValueModel.PersistentFail(0.5f), 5);
            Assert.Equal(0.5f, ValueModel.PersistentBoostShare(0.5f, halfOnSave: false), 5);   // .50 -> .75
        }

        [Fact]
        public void Persistent_HalfDamage_IsNotAFlatHalf()
        {
            // Ordinary half damage: .75 -> .875 of the damage, +16.7 % (the old flat half said +25 %).
            Assert.Equal(0.875f / 0.75f - 1f, ValueModel.PersistentBoostShare(0.5f, halfOnSave: true), 4);
            // Evasion takes nothing on a pass: .50 -> .75, +50 % (the old flat half said +25 %).
            Assert.Equal(0.5f, ValueModel.PersistentBoostShare(0.5f, halfOnSave: true, evasion: true), 4);
            // Improved Evasion: .25 -> .375, also +50 %.
            Assert.Equal(0.5f, ValueModel.PersistentBoostShare(0.5f, halfOnSave: true, evasion: true, improved: true), 4);
        }

        [Fact]
        public void Persistent_NoGainWhenTheSaveCannotChange()
        {
            Assert.Equal(0f, ValueModel.PersistentBoostShare(0f, halfOnSave: false));
            Assert.Equal(0f, ValueModel.PersistentBoostShare(1f, halfOnSave: true));
            Assert.Equal(0f, ValueModel.PersistentBoostShare(float.NaN, halfOnSave: false));
        }

        [Fact]
        public void AutomaticWillSuccess_ShadowOnlyDcDoesNotGain()
        {
            float before = ValueModel.ShadowEffectFactor(ValueModel.SpellSaveFailChance(21, 10, true, autoPass: true), 0.2f);
            float after = ValueModel.ShadowEffectFactor(ValueModel.SpellSaveFailChance(100, 10, true, autoPass: true), 0.2f);
            Assert.Equal(0.2f, before, 5);
            Assert.Equal(0f, ValueModel.SaveBoostShare(before, after));
        }
    }
}
