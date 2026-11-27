using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>
    /// ★ v0.3.70 features5 review: the drop model skips an ally whose chance a walk cannot change (PartyShift), and reads a
    /// list without an enumerator. Both must give the numbers they gave before.
    /// </summary>
    public sealed class V0370Features5ReviewTests
    {
        [Fact]
        public void AnAllyOutsideAnEnemysReachAndZoneIsNeverItsPick()
        {
            // PartyShift's skip rests on this: whatever the walker does to an enemy's weight sums, an ally in neither its
            // reach nor its wider zone has no chance to be picked by it, before or after.
            foreach (float reachSum in new[] { 0f, 0.3f, 1.7f })
                foreach (float zoneSum in new[] { 0f, 0.4f, 2.2f })
                    foreach (float weight in new[] { 0.05f, 0.5f, 0.95f })
                        Assert.Equal(0f, ValueModel.TargetChance(false, reachSum, false, zoneSum, weight));
        }

        [Fact]
        public void TheWalkerOnlyMattersThroughTheSums()
        {
            // An ally in the zone: the walker entering the enemy's reach takes the enemy off it (sum from 0 to > 0) - the
            // row is one the walk changes, and the ally is read again.
            float before = ValueModel.TargetChance(false, 0f, true, 1.2f, 0.6f);
            float after = ValueModel.TargetChance(false, 0.7f, true, 1.9f, 0.6f);
            Assert.True(before > 0f);
            Assert.Equal(0f, after);
        }

        [Fact]
        public void CombinedDropOverAListEqualsTheEnumerable()
        {
            var rng = new Random(20261127);
            for (int n = 0; n < 200; n++)
            {
                var threats = new List<(float chance, float drop)>();
                int count = rng.Next(0, 48);
                for (int i = 0; i < count; i++)
                    threats.Add(((float)rng.NextDouble() * 1.2f - 0.1f, (float)rng.NextDouble() * 1.2f - 0.1f));
                float list = ValueModel.CombinedDrop(threats);
                float enumerable = ValueModel.CombinedDrop(threats.AsEnumerable());
                Assert.Equal(enumerable, list);
            }
        }
    }
}
