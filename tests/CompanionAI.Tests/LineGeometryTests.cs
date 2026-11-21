using CompanionAI_Pathfinder.Tactics;
using Xunit;

namespace CompanionAI.Tests
{
    /// <summary>★ Fusion coverage C4: native WouldTargetPointLine on the ground plane, and the lined-up spot (Tactics/LineGeometry.cs).</summary>
    public class LineGeometryTests
    {
        // A 120 ft Lightning Bolt: 36.58 m, 5 ft wide (half 0.762 m), launched at the origin along +x.
        private const float Travel = 36.576f, Half = 0.762f;

        [Fact]
        public void CatchesInFrontWithinHalfWidthPlusBody()
        {
            Assert.True(LineGeometry.Catches(0f, 0f, 1f, 0f, Travel, Half, 10f, 0f, 0.5f));
            Assert.True(LineGeometry.Catches(0f, 0f, 1f, 0f, Travel, Half, 10f, 1.25f, 0.5f));     // 1.25 <= 0.762 + 0.5
            Assert.False(LineGeometry.Catches(0f, 0f, 1f, 0f, Travel, Half, 10f, 1.3f, 0.5f));
            Assert.False(LineGeometry.Catches(0f, 0f, 1f, 0f, Travel, Half, -0.5f, 0f, 0.5f));    // behind the launch point
            Assert.False(LineGeometry.Catches(0f, 0f, 1f, 0f, Travel, Half, 0f, 0f, 0.5f));       // along = 0: native "<= 0"
        }

        [Fact]
        public void LengthCountsTheBody()
        {
            Assert.True(LineGeometry.Catches(0f, 0f, 1f, 0f, Travel, Half, Travel + 0.4f, 0f, 0.5f));
            Assert.False(LineGeometry.Catches(0f, 0f, 1f, 0f, Travel, Half, Travel + 0.5f, 0f, 0.5f));  // native ">= travel + radius"
        }

        [Fact]
        public void TurnAndAngleAgree()
        {
            LineGeometry.Turn(1f, 0f, 10f, out float x, out float z);
            Assert.Equal(10f, LineGeometry.AngleBetween(1f, 0f, x, z), 3);
            LineGeometry.Turn(0f, 1f, -25f, out x, out z);
            Assert.Equal(-25f, LineGeometry.AngleBetween(0f, 1f, x, z), 3);
            Assert.Equal(1f, x * x + z * z, 4);
        }

        [Fact]
        public void BehindSpotLinesBothUp()
        {
            // Near at (5, 0), far at (10, 0): the spot is behind the near one on the -x side, nearest the caster at (0, 3).
            Assert.True(LineGeometry.BehindSpot(0f, 3f, 5f, 0f, 10f, 0f, 2f, 20f, out float sx, out float sz));
            Assert.Equal(0f, sx, 3);
            Assert.Equal(0f, sz, 3);
            LineGeometry.Direction(sx, sz, 10f, 0f, out float dx, out float dz);
            Assert.True(LineGeometry.Catches(sx + dx * 0.5f, sz + dz * 0.5f, dx, dz, Travel, Half, 5f, 0f, 0.5f));
            Assert.True(LineGeometry.Catches(sx + dx * 0.5f, sz + dz * 0.5f, dx, dz, Travel, Half, 10f, 0f, 0.5f));
            // A caster on the far side is held back at least minBack behind the near one.
            Assert.True(LineGeometry.BehindSpot(20f, 0f, 5f, 0f, 10f, 0f, 2f, 20f, out sx, out _));
            Assert.Equal(3f, sx, 3);
            // No room: the far one would be beyond the line's travel.
            Assert.False(LineGeometry.BehindSpot(0f, 0f, 5f, 0f, 10f, 0f, 3f, 2f, out _, out _));
        }

        [Fact]
        public void MaxBackKeepsTheFarOneInsideTheTravel()
        {
            float back = LineGeometry.MaxBack(15.24f, 0.5f, 6f, 0.5f, 0.5f);   // a 50 ft line, 6 m between them
            Assert.Equal(15.24f + 0.5f + 0.5f - 6f - 0.5f, back, 3);
            // Standing that far behind, the far one's distance from the launch point is under the travel plus its body.
            float along = back + 6f - 0.5f;
            Assert.True(along < 15.24f + 0.5f);
        }

        [Fact]
        public void LineUpMustBeatCastingFromHere()
        {
            float here = ValueModel.WalkedNet(10f, false, 1f, 0f, 0f);
            Assert.Equal(10f, here, 3);
            float up = ValueModel.WalkedNet(20f, true, 1f, 0.1f, 1f);           // 20 x 0.9 x 0.9 - 1
            Assert.Equal(15.2f, up, 3);
            Assert.True(ValueModel.LineUpWorthIt(here, up));
            Assert.False(ValueModel.LineUpWorthIt(here, ValueModel.WalkedNet(12.5f, true, 1f, 0f, 0f)));  // 11.25 < 10 x 1.15 + 0.5
            Assert.False(ValueModel.LineUpWorthIt(0f, 0.4f));                    // not for a sliver
            Assert.True(ValueModel.LineUpWorthIt(-3f, 1f));                      // here is worth nothing
            Assert.False(ValueModel.LineUpWorthIt(5f, float.NaN));
        }
    }
}
