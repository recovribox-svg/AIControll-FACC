// C07: one held charge is delivered by the first successful eligible melee attack, even in a full routine.
namespace CompanionAI_Pathfinder.Tactics
{
    public struct MagusChargeChance
    {
        private float missed, critical;
        private bool started;
        public float Hit => started ? 1f - missed : 0f;
        public float Critical => critical;
        public void Add(float hit, float confirmedCritical)
        {
            if (!started) { missed = 1f; started = true; }
            critical += missed * confirmedCritical;
            missed *= 1f - hit;
        }
    }
}
