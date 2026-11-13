// ★ v0.3.50 review2: Per-evaluation bounds on the custom leap's expensive native reads. No actor or game dependency.
namespace CompanionAI_Pathfinder.Tactics
{
    public struct GriffonQueryBudget
    {
        public const int MaxLandingAttempts = 8, MaxWeaponQuotes = 4;
        public int LandingAttempts { get; private set; }
        public int WeaponQuotes { get; private set; }
        public bool Exhausted => LandingAttempts >= MaxLandingAttempts || WeaponQuotes >= MaxWeaponQuotes;

        public bool TryLanding()
        {
            if (Exhausted) return false;
            LandingAttempts++;
            return true;
        }

        public bool TryWeaponQuote()
        {
            if (WeaponQuotes >= MaxWeaponQuotes || WeaponQuotes >= LandingAttempts) return false;
            WeaponQuotes++;
            return true;
        }
    }
}
