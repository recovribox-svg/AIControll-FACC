// ★ v0.3.27 (test 5 log, author-approved): when a real-time walk counts as arrived for the "in range and still walking"
// stuck rule (GameInterface/RealTimeController.IsCommandStuck). Pure, unit-tested.
namespace CompanionAI_Pathfinder.Tactics
{
    public static class ArrivalRule
    {
        /// <summary>
        /// Slack past the arrival radius that still counts as in range. Seelah, pushing against a Vermlek's body 1.97 m from
        /// it (radius 2.01 m), wobbled between -0.04 and +0.04 m; each tick above 0 restarted the 2 s clock and the walk ran
        /// 6.6 s, until the no-progress rule ended it, while she lost 41 % of her HP.
        /// </summary>
        public const float Tolerance = 0.3f;

        /// <summary>
        /// The walk is in range: by the game's own test (<paramref name="gameSaysClose"/>: UnitCommand.IsUnitCloseEnough,
        /// mechanics distance and sight), or within <see cref="Tolerance"/> past its radius (<paramref name="gap"/>, metres
        /// beyond the arrival radius) with sight clear where the command needs it. Unusable input is not in range.
        /// </summary>
        public static bool InRange(bool gameSaysClose, float gap, bool sightOk)
        {
            if (gameSaysClose) return true;
            if (float.IsNaN(gap) || float.IsInfinity(gap)) return false;
            return gap <= Tolerance && sightOk;
        }
    }
}
