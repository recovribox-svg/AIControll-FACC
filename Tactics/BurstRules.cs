// ★ burst aim (17 November 2026): the pure rules for aiming an area burst that can hurt allies at the ground where its
// anchor enemy will be when the burst lands. No game types: the game side is TacticalEngine.Burst.cs and BurstWatch.cs.
// Native contracts (docs/BURST_AIM_AND_TACTICS_PLAN.md §3.1):
// - AbilityTargetsAround.Select picks its victims when the burst is delivered: around the anchor unit's eye position at that
//   moment, or around a fixed point;
// - AbilityDeliverDelay waits DelaySeconds, then delivers on the main target; a projectile aimed at a unit follows it;
// - UnitUseAbility goes off 2 s after the cast starts (6 s for a full-round spell not from an item, 0 for a wand or an
//   Immediate animation); turn-based caps it at 6 s;
// - the spell and the action are spent when the cast goes off (UnitUseAbility.OnAction, UnitCommandController), so a cast
//   cancelled before that costs nothing.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class BurstRules
    {
        /// <summary>The native cast time of a real-time standard spell (UnitUseAbility.m_CastTime).</summary>
        public const float StandardCastSeconds = 2f;

        /// <summary>The native real-time cast time of a full-round spell not cast from an item.</summary>
        public const float FullRoundCastSeconds = 6f;

        /// <summary>Half of the window around the landing time in which an ally's walk is tested.</summary>
        public const float WindowHalfSeconds = 0.5f;

        /// <summary>Samples of an ally's walk in that window.</summary>
        public const int WindowSamples = 5;

        /// <summary>How fast the uncertainty of a predicted position grows: this share of the distance walked.</summary>
        public const float UncertaintyShare = 0.25f;

        /// <summary>The slowest speed assumed for a unit with a walk of its own (CurrentSpeedMps can read 0 at rest).</summary>
        public const float MinWalkSpeed = 1.5f;

        /// <summary>Seconds between two re-checks of a cast that has not gone off.</summary>
        public const float RecheckSeconds = 0.25f;

        /// <summary>Cancels allowed per caster and ability within <see cref="CancelWindowSeconds"/>.</summary>
        public const int MaxCancels = 2;

        public const float CancelWindowSeconds = 6f;

        /// <summary>After a cancel, the same ability is not aimed within this distance of the same point for this long.</summary>
        public const float RefusalMeters = 1.5f;

        public const float RefusalSeconds = 1.5f;

        /// <summary>A landing spot stays a danger for the other units this long after the landing time.</summary>
        public const float LandingMarginSeconds = 0.3f;

        /// <summary>The longest a landing spot is kept for the other units.</summary>
        public const float MaxPendingSeconds = 12f;

        /// <summary>Lead shorter than this is not worth moving the point for (the enemy stands, or nearly).</summary>
        public const float MinLeadMeters = 0.5f;

        /// <summary>
        /// The native cast time (UnitUseAbility): what elapses from the cast starting to it going off. Turn-based counts 0:
        /// nobody else moves during the caster's turn.
        /// </summary>
        public static float CastSeconds(bool realTime, bool fullRound, bool fromItem, bool wand, bool immediate)
        {
            if (!realTime || wand || immediate) return 0f;
            return fullRound && !fromItem ? FullRoundCastSeconds : StandardCastSeconds;
        }

        /// <summary>A projectile's flight over <paramref name="distance"/> metres: none without a projectile.</summary>
        public static float FlightSeconds(float distance, float speed, float minTime)
        {
            if (!(speed > 0f) || !(distance > 0f) || float.IsInfinity(distance)) return 0f;
            return Math.Max(Math.Max(0f, minTime), distance / speed);
        }

        /// <summary>From the decision to the burst landing: the walk, the cast, the delay and the flight.</summary>
        public static float LandingSeconds(float walk, float cast, float delay, float flight) =>
            Math.Max(0f, walk) + Math.Max(0f, cast) + Math.Max(0f, delay) + Math.Max(0f, flight);

        /// <summary>The seconds a walk of <paramref name="distance"/> beyond <paramref name="range"/> takes.</summary>
        public static float WalkSeconds(float distance, float range, float speed)
        {
            float beyond = distance - range;
            if (!(beyond > 0f)) return 0f;
            return beyond / Math.Max(MinWalkSpeed, speed);
        }

        /// <summary>How far a unit with a walk of <paramref name="walkLength"/> metres gets in <paramref name="seconds"/>.</summary>
        public static float Travel(float walkLength, float speed, float seconds)
        {
            if (!(walkLength > 0f) || !(seconds > 0f)) return 0f;
            return Math.Min(walkLength, Math.Max(MinWalkSpeed, speed) * seconds);
        }

        /// <summary>The uncertainty of a position predicted <paramref name="seconds"/> ahead for a walking unit.</summary>
        public static float Uncertainty(bool walking, float speed, float seconds)
        {
            if (!walking || !(seconds > 0f)) return 0f;
            return UncertaintyShare * Math.Max(MinWalkSpeed, speed) * seconds;
        }

        /// <summary>An enemy counts as hit only when it is surely inside: within the reach less the uncertainty.</summary>
        public static bool SurelyInside(float distance, float reach, float uncertainty) => distance <= reach - Math.Max(0f, uncertainty);

        /// <summary>An ally counts as caught when it may be inside: within the reach plus the uncertainty.</summary>
        public static bool MaybeInside(float distance, float reach, float uncertainty) => distance <= reach + Math.Max(0f, uncertainty);

        /// <summary>
        /// Where a unit walking from (fx, fz) to (tx, tz) at <paramref name="speed"/> is after <paramref name="seconds"/>, on the
        /// straight line, stopping at the end of its walk.
        /// </summary>
        public static void At(float fx, float fz, float tx, float tz, float speed, float seconds, out float x, out float z)
        {
            float dx = tx - fx, dz = tz - fz;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            float travel = Travel(length, speed, seconds);
            if (!(length > 0f) || !(travel > 0f))
            {
                x = fx;
                z = fz;
                return;
            }
            float share = travel / length;
            x = fx + dx * share;
            z = fz + dz * share;
        }

        /// <summary>
        /// A unit walking from (fx, fz) to (tx, tz) may be within <paramref name="reach"/> of (cx, cz) when the burst lands:
        /// at any of <see cref="WindowSamples"/> moments in the window of <see cref="WindowHalfSeconds"/> around
        /// <paramref name="landing"/>, with the uncertainty of its predicted position.
        /// </summary>
        public static bool WalkMayBeInside(float fx, float fz, float tx, float tz, float speed, float cx, float cz, float reach,
                                           float landing)
        {
            float from = Math.Max(0f, landing - WindowHalfSeconds);
            float to = Math.Max(from, landing + WindowHalfSeconds);
            for (int i = 0; i < WindowSamples; i++)
            {
                float t = WindowSamples == 1 ? landing : from + (to - from) * i / (WindowSamples - 1);
                At(fx, fz, tx, tz, speed, t, out float x, out float z);
                float dx = x - cx, dz = z - cz;
                if (MaybeInside((float)Math.Sqrt(dx * dx + dz * dz), reach, Uncertainty(true, speed, t))) return true;
            }
            return false;
        }

        /// <summary>The outcome of a re-check before the cast goes off.</summary>
        public enum Verdict
        {
            Keep,
            /// <summary>Avoid Friendly Fire is on and an ally the plan did not count is now caught.</summary>
            AllyCaught,
            /// <summary>The allies now caught cost more than the cast was worth.</summary>
            AllyCostsMore,
            /// <summary>No enemy is inside any more: the spell would be wasted.</summary>
            NothingInside
        }

        /// <summary>
        /// The re-check's verdict from what changed since the plan: allies caught that it did not count, what they cost
        /// (twice the damage they take, as in the scoring), the cast's planned value and the enemies now surely inside.
        /// </summary>
        public static Verdict Judge(bool avoidFriendlyFire, int newAllies, float newAllyCost, float plannedValue, int enemiesInside)
        {
            if (newAllies > 0 && avoidFriendlyFire) return Verdict.AllyCaught;
            if (newAllies > 0 && newAllyCost >= Math.Max(0f, plannedValue)) return Verdict.AllyCostsMore;
            if (enemiesInside <= 0) return Verdict.NothingInside;
            return Verdict.Keep;
        }

        /// <summary>Another cancel is allowed after <paramref name="recentCancels"/> within the window.</summary>
        public static bool MayCancel(int recentCancels) => recentCancels < MaxCancels;

        /// <summary>The cost of hurting an ally, as the scoring counts it: twice the damage it takes, at most its HP.</summary>
        public static float AllyCost(float damage, float hpLeft) => damage > 0f ? 2f * Math.Min(damage, Math.Max(0f, hpLeft)) : 0f;
    }
}
