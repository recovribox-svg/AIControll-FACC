using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    public class GriffonAttackRulesTests
    {
        // Native delivery does not set ForceFullAttack or IsSingleAttack. Test its own charge/Pounce branches,
        // including TB ignored cooldown and RT movement, independently of the ability's action affordability.
        [Theory]
        [InlineData(false, true, false, false, false, false, false)]
        [InlineData(false, true, true, false, false, false, false)]
        [InlineData(true, true, false, false, false, false, true)]
        [InlineData(true, true, false, false, false, true, true)]
        [InlineData(true, true, false, false, true, false, false)]
        [InlineData(true, true, false, true, false, false, false)]
        [InlineData(true, true, true, true, true, true, true)]
        [InlineData(true, false, false, false, false, false, true)]
        [InlineData(true, false, false, false, true, false, true)]
        [InlineData(true, false, false, false, false, true, false)]
        [InlineData(true, false, false, true, false, false, false)]
        [InlineData(true, false, true, true, false, false, false)]
        public void NativeQueuedChargeRestrictions(bool pounce, bool turnBased, bool surprise, bool staggered,
                                                    bool movementRestricted, bool recentMove, bool expected)
        {
            Assert.Equal(expected, GriffonAttackRules.FullAttack(pounce, turnBased, surprise, staggered,
                movementRestricted, recentMove));
        }
    }
}
