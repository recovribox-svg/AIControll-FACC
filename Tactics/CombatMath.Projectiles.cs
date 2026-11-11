// ★ caster1 review: AbilityDeliverProjectile.Deliver repeats the payload once for each selected simple projectile.
// Read its actual array/rank rather than a spell-name/level table. No actor, command or delivery is created here.
using System;
using Kingmaker.UnitLogic.Mechanics;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class CombatMath
    {
        internal static int ProjectileDeliveries(AbilityProfile profile, MechanicsContext context)
        {
            var delivery = profile.SimpleProjectiles;
            // Native MagicHack replaces the delivery array with its one selected projectile. Its two payloads
            // are already in the fusion profile; never inherit either source spell's ray/missile count.
            // Controlled projectiles and Hand of the Apprentice retain their separate existing appraisal.
            if (delivery == null || profile.Fusion != null || delivery.ControlledProjectileHolderBuff != null ||
                delivery.IsHandOfTheApprentice) return 1;
            int available = delivery.Projectiles.Length;
            if (!delivery.UseMaxProjectilesCount) return available;
            if (context == null)
            {
                AbilityDiscovery.ReportOnce("projectile-rank|" + profile.Blueprint.AssetGuid,
                    () => $"[Math] {profile.Blueprint.name}: native projectile rank unavailable; retaining one-projectile appraisal");
                return 1;
            }
            // Native referenceArrayProxy.Take(context[rank]): no negative count and no delivery past the array.
            return Math.Max(0, Math.Min(available, context[delivery.MaxProjectilesCountRank]));
        }

        internal static bool ObserveProjectileRolls(AbilityProfile profile) => profile.Fusion == null &&
            profile.SimpleProjectiles?.NeedAttackRoll == true && profile.SimpleProjectiles.Projectiles.Length > 1;
    }
}
