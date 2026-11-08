using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ v0.3.30 test 2: what an ability score feeds on a unit (Tactics/ValueModel.Stats.cs).</summary>
    public class StatUseTests
    {
        [Fact]
        public void WisdomOnAnyone_IsAThirdOfTheSaves()
        {
            var use = new ValueModel.StatUse();
            use.Add(ValueModel.StatTarget.Save, 1f);   // Will's base stat
            Assert.Equal(1f / 3f, use.Saves, 4);
            Assert.True(use.Any);
        }

        /// <summary>A paladin's Divine Grace: Charisma to all three saves (DerivativeStatBonus x3).</summary>
        [Fact]
        public void DivineGrace_IsAllThreeSaves()
        {
            var use = new ValueModel.StatUse();
            for (int i = 0; i < 3; i++) use.Add(ValueModel.StatTarget.Save, 1f);
            Assert.Equal(1f, use.Saves, 4);
        }

        /// <summary>A Zen Archer's Wisdom: the bow's attack stat, the monk's AC bonus and Will.</summary>
        [Fact]
        public void ZenArcherWisdom_IsAttackArmourAndWill()
        {
            var use = new ValueModel.StatUse();
            use.Add(ValueModel.StatTarget.Attack, 1f);
            use.Add(ValueModel.StatTarget.ArmorClass, 1f);
            use.Add(ValueModel.StatTarget.Save, 1f);
            Assert.Equal(1f, use.Attack, 4);
            Assert.Equal(1f, use.Armor, 4);
            Assert.Equal(1f / 3f, use.Saves, 4);
        }

        [Fact]
        public void IntelligenceOnAFighter_IsNothing() => Assert.False(new ValueModel.StatUse().Any);

        [Fact]
        public void TwoHandedStrength_IsOneAndAHalfDamage()
        {
            var use = new ValueModel.StatUse();
            use.Add(ValueModel.StatTarget.Damage, 1.5f);
            Assert.Equal(1.5f, use.Damage, 4);
        }

        [Fact]
        public void NonPositiveOrBrokenFactors_AddNothing()
        {
            var use = new ValueModel.StatUse();
            use.Add(ValueModel.StatTarget.Attack, 0f);
            use.Add(ValueModel.StatTarget.Attack, -1f);
            use.Add(ValueModel.StatTarget.Attack, float.NaN);
            use.Add(ValueModel.StatTarget.Attack, float.PositiveInfinity);
            Assert.False(use.Any);
        }

        [Fact]
        public void AModifierPointIsTwoScorePoints()
        {
            Assert.Equal(1f, ValueModel.ModifierGain(2f), 4);
            Assert.Equal(2f, ValueModel.ModifierGain(4f), 4);
            Assert.Equal(0f, ValueModel.ModifierGain(-2f), 4);
            Assert.Equal(0f, ValueModel.ModifierGain(float.NaN), 4);
        }
    }
}
