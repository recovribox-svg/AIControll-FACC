// ★ v0.3.51: AbilityGriffinAttack queues an IsCharge attack with IgnoreCooldown, not an ordinary full-round attack.
namespace CompanionAI_Pathfinder.Tactics
{
    internal static class GriffonAttackRules
    {
        // UnitAttack.InitAttacks / IsFullAttackRestricted, for the native delivery's default single/full flags.
        // TB IgnoreCooldown bypasses spent actions, but not restricted movement. Its surprise-Pounce branch
        // precedes both restrictions. RT retains the native recent-move restriction. No future cooldown credit.
        internal static bool FullAttack(bool pounce, bool turnBased, bool surprise, bool staggered,
                                         bool movementRestricted, bool recentMove) =>
            pounce && (turnBased && surprise || !staggered && !(turnBased ? movementRestricted : recentMove));
    }
}
