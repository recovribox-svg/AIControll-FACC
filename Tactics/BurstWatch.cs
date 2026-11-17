// ★ burst aim (17 November 2026, author-approved A3-A5; docs/BURST_AIM_AND_TACTICS_PLAN.md §3.4-3.6): a ground-aimed
// burst once issued (TacticalEngine.Burst.cs). Until its cast goes off, real time re-checks its landing spot with fresh
// positions and cancels the cast when an ally the decision did not count will be caught, or no enemy will be left inside;
// the game spends the spell and the action only when the cast goes off (UnitUseAbility.OnAction, UnitCommandController),
// so the cancel is free. Once it has gone off, the time it lands is kept: the other AI units' walks avoid the spot until
// then (AoeDangerAnalyzer), and a turn-based turn waits for it (CustomBrainPatch.ReadyToEndTurn).
using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class BurstWatch : IUnitCommandActHandler
    {
        private sealed class Entry
        {
            public UnitEntityData Unit;
            public UnitCommand Command;
            public TacticalEngine.BurstPlan Plan;
            public double NextCheck;
            public bool StartSeen;
            public double LandsAt = double.NaN;
            public bool LimitNoted;
        }

        private struct Refusal
        {
            public UnitEntityData Unit;
            public BlueprintAbility Blueprint;
            public Vector3 Point;
            public double Until;
        }

        private struct Cancel
        {
            public UnitEntityData Unit;
            public BlueprintAbility Blueprint;
            public double At;
        }

        private const int MaxEntries = 32;
        private const int MaxLinesPerFight = 64;

        private static BurstWatch s_Instance;
        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly List<Refusal> Refusals = new List<Refusal>();
        private static readonly List<Cancel> Cancels = new List<Cancel>();
        private static int s_Lines;

        // The native cast time of a running cast (UnitUseAbility.m_CastTime); read only, falls back to the plan's.
        private static readonly HarmonyLib.AccessTools.FieldRef<UnitUseAbility, float> CastTimeRef = ReadCastTimeRef();

        private static HarmonyLib.AccessTools.FieldRef<UnitUseAbility, float> ReadCastTimeRef()
        {
            try { return HarmonyLib.AccessTools.FieldRefAccess<UnitUseAbility, float>("m_CastTime"); }
            catch { return null; }
        }

        private static double Now => Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;

        public static void Subscribe()
        {
            if (s_Instance != null) return;
            s_Instance = new BurstWatch();
            EventBus.Subscribe(s_Instance);
        }

        public static void Unsubscribe()
        {
            if (s_Instance == null) return;
            EventBus.Unsubscribe(s_Instance);
            s_Instance = null;
            Clear();
        }

        /// <summary>A new fight: nothing carries over.</summary>
        public static void Clear()
        {
            Entries.Clear();
            Refusals.Clear();
            Cancels.Clear();
            s_Lines = 0;
        }

        private static void Log(string line)
        {
            if (s_Lines >= MaxLinesPerFight) return;
            if (++s_Lines == MaxLinesPerFight) line += " (further [Burst] lines this fight are not logged)";
            Main.Log(line);
        }

        /// <summary>A ground-aimed burst was issued: watch it, and share its landing spot with the other AI units.</summary>
        internal static void Track(UnitEntityData unit, UnitCommand command, TacticalEngine.BurstPlan plan)
        {
            if (unit == null || command == null || plan == null) return;
            try
            {
                Prune();
                if (Entries.Count >= MaxEntries) Entries.RemoveAt(0);
                Entries.Add(new Entry { Unit = unit, Command = command, Plan = plan, NextCheck = Now + BurstRules.RecheckSeconds });
                var profile = AbilityProfile.Get(plan.Ability);
                if (plan.RealTime && profile != null)
                    Analysis.AoeDangerAnalyzer.RegisterPendingBurst(plan.Point, plan.Radius, plan.Ability.Name, profile.DealsDamage,
                        profile.ControlStrength != ControlStrength.None, command, plan.Landing);
                Log($"[Burst] {unit.CharacterName}: {plan.Ability.Name} aimed at the {plan.Describe()}; counted {plan.Enemies} " +
                    $"enem{(plan.Enemies == 1 ? "y" : "ies")}, {plan.Allies.Count} all{(plan.Allies.Count == 1 ? "y" : "ies")}");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Burst] track {unit.CharacterName}: {ex.Message}");
            }
        }

        public void HandleUnitCommandDidAct(UnitCommand command)
        {
            if (command == null || Entries.Count == 0) return;
            try
            {
                foreach (var e in Entries)
                {
                    if (!ReferenceEquals(e.Command, command) || !double.IsNaN(e.LandsAt)) continue;
                    // A cast that failed when it went off (OnAction: concentration, target, charges) delivers nothing.
                    if (command.Result == UnitCommand.ResultType.Fail)
                    {
                        e.LandsAt = Now;
                        Analysis.AoeDangerAnalyzer.PendingBurstLandsIn(command, 0f);
                        return;
                    }
                    var profile = AbilityProfile.Get(e.Plan.Ability);
                    float flight = profile != null ? TacticalEngine.FlightSecondsTo(profile, e.Unit.EyePosition, e.Plan.Point) : 0f;
                    float delay = profile?.DeliverDelaySeconds ?? e.Plan.Delay;
                    e.LandsAt = Now + delay + flight;
                    // A long walk can outlast the first estimate; the spot is shared again until it lands.
                    if (!Analysis.AoeDangerAnalyzer.PendingBurstLandsIn(command, delay + flight) && e.Plan.RealTime && profile != null)
                        Analysis.AoeDangerAnalyzer.RegisterPendingBurst(e.Plan.Point, e.Plan.Radius, e.Plan.Ability.Name, profile.DealsDamage,
                            profile.ControlStrength != ControlStrength.None, command, delay + flight);
                    return;
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Burst] act: {ex.Message}");
            }
        }

        /// <summary>
        /// A5: <paramref name="unit"/>'s own burst has gone off and not landed yet (turn-based: its turn waits for it).
        /// </summary>
        public static bool PendingLanding(UnitEntityData unit)
        {
            if (unit == null || Entries.Count == 0) return false;
            double now = Now;
            foreach (var e in Entries)
                if (e.Unit == unit && !double.IsNaN(e.LandsAt) && now < e.LandsAt) return true;
            return false;
        }

        /// <summary>After a cancel, the same ability is not aimed close to the same point for a moment.</summary>
        internal static bool Refused(UnitEntityData unit, AbilityData ability, Vector3 point)
        {
            if (Refusals.Count == 0 || unit == null || ability?.Blueprint == null) return false;
            double now = Now;
            Refusals.RemoveAll(r => now > r.Until);
            foreach (var r in Refusals)
                if (r.Unit == unit && r.Blueprint == ability.Blueprint && TacticalEngine.HorizontalDistance(r.Point, point) <= BurstRules.RefusalMeters)
                    return true;
            return false;
        }

        /// <summary>
        /// A3 (real time): <paramref name="unit"/>'s ground-aimed burst should be cancelled before its cast goes off. The
        /// caller interrupts <paramref name="command"/> (nothing is spent before the cast goes off) and decides again.
        /// </summary>
        public static bool ShouldCancel(UnitEntityData unit, out UnitCommand command, out string why)
        {
            command = null;
            why = null;
            if (unit == null || Entries.Count == 0 || !CombatMemory.IsRealTime) return false;
            Prune();
            double now = Now;
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e.Unit != unit) continue;
                var cmd = e.Command;
                if (cmd.IsFinished || cmd.IsActed) continue;
                bool started = cmd.IsStarted;
                bool due = now >= e.NextCheck || started && !e.StartSeen;
                if (!due) continue;
                e.StartSeen |= started;
                e.NextCheck = now + BurstRules.RecheckSeconds;
                try
                {
                    var verdict = Evaluate(e, out int newAllies, out string names, out int enemies);
                    if (verdict == BurstRules.Verdict.Keep) continue;
                    var blueprint = e.Plan.Ability.Blueprint;
                    Cancels.RemoveAll(c => now - c.At > BurstRules.CancelWindowSeconds);
                    int recent = 0;
                    foreach (var c in Cancels) if (c.Unit == unit && c.Blueprint == blueprint) recent++;
                    if (!BurstRules.MayCancel(recent))
                    {
                        if (!e.LimitNoted)
                        {
                            e.LimitNoted = true;
                            Log($"[Burst] {unit.CharacterName}: {e.Plan.Ability.Name} kept - {Reason(verdict, names)}, but it was " +
                                $"cancelled {recent} times in {BurstRules.CancelWindowSeconds:0} s");
                        }
                        continue;
                    }
                    Cancels.Add(new Cancel { Unit = unit, Blueprint = blueprint, At = now });
                    Refusals.Add(new Refusal { Unit = unit, Blueprint = blueprint, Point = e.Plan.Point, Until = now + BurstRules.RefusalSeconds });
                    Entries.RemoveAt(i);
                    command = cmd;
                    why = $"{e.Plan.Ability.Name} cancelled before casting ({Reason(verdict, names)}; " +
                          $"{(started ? "while casting" : "on the way")}, {enemies} enem{(enemies == 1 ? "y" : "ies")} left inside)";
                    return true;
                }
                catch (Exception ex)
                {
                    Main.Verbose($"[Burst] re-check {unit.CharacterName}: {ex.Message}");
                }
            }
            return false;
        }

        private static string Reason(BurstRules.Verdict verdict, string names)
        {
            switch (verdict)
            {
                case BurstRules.Verdict.AllyCaught: return $"{names} walks into it";
                case BurstRules.Verdict.AllyCostsMore: return $"{names} would cost more than it is worth";
                case BurstRules.Verdict.NothingInside: return "no enemy left where it lands";
                default: return verdict.ToString();
            }
        }

        /// <summary>
        /// The re-check: the time left until the burst lands, the enemies surely inside then, and the allies the decision did
        /// not count that may be inside then - each with the damage it would take (their cost, as in the scoring).
        /// </summary>
        private static BurstRules.Verdict Evaluate(Entry e, out int newAllies, out string names, out int enemies)
        {
            newAllies = 0;
            enemies = 0;
            names = null;
            var unit = e.Unit;
            var plan = e.Plan;
            var cmd = e.Command;
            var profile = AbilityProfile.Get(plan.Ability);
            if (profile == null) return BurstRules.Verdict.Keep;
            float flight = TacticalEngine.FlightSecondsTo(profile, unit.EyePosition, plan.Point);
            float remaining;
            if (cmd.IsStarted)
            {
                float cast = plan.Cast;
                if (CastTimeRef != null && cmd is UnitUseAbility use)
                {
                    try { cast = CastTimeRef(use); } catch { }
                }
                remaining = Math.Max(0f, cast - cmd.TimeSinceStart) + plan.Delay + flight;
            }
            else
            {
                float walk = BurstRules.WalkSeconds(TacticalEngine.HorizontalDistance(unit.Position, plan.Point), plan.Range, unit.CurrentSpeedMps);
                remaining = BurstRules.LandingSeconds(walk, plan.Cast, plan.Delay, flight);
            }

            float newCost = 0f;
            List<string> caught = null;
            foreach (var u in Game.Instance.State.Units)
            {
                if (u == null || !u.IsInGame || u.Descriptor?.State == null || u.Descriptor.State.IsDead) continue;
                if (u != unit && !(u.CombatState?.IsInCombat ?? false)) continue;
                float reach = plan.Radius + u.Corpulence;
                if (unit.IsEnemy(u))
                {
                    if (!u.Descriptor.State.IsConscious) continue;
                    var at = Predict(u, remaining, out bool walking);
                    if (BurstRules.SurelyInside(Vector3.Distance(at, plan.Point), reach, BurstRules.Uncertainty(walking, u.CurrentSpeedMps, remaining)) &&
                        profile.AreaReaches(plan.Ability, u))
                        enemies++;
                    continue;
                }
                if (!u.IsPlayerFaction && u != unit) continue;
                if (plan.Allies.Contains(u)) continue;
                bool inside;
                var walk = u.Descriptor.State.IsConscious ? TacticalEngine.WalkClaim(u) : null;
                if (walk.HasValue)
                {
                    var from = u.Position;
                    inside = BurstRules.WalkMayBeInside(from.x, from.z, walk.Value.x, walk.Value.z, u.CurrentSpeedMps,
                                                        plan.Point.x, plan.Point.z, reach, remaining);
                }
                else inside = Vector3.Distance(u.Position, plan.Point) <= reach;
                if (!inside || !profile.AreaReaches(plan.Ability, u)) continue;
                float damage = plan.Damage;
                try { damage = CombatMath.EstimateAbilityDamage(plan.Ability, profile, u); }
                catch { }
                if (!(damage > 0f)) continue;
                newAllies++;
                newCost += u.HPLeft <= 0 ? Math.Max(0f, plan.Value) + 1f : BurstRules.AllyCost(damage, u.HPLeft);
                (caught ?? (caught = new List<string>())).Add(u.CharacterName);
            }
            if (caught != null) names = string.Join(", ", caught);
            // A plan that counted no enemy (its value came from elsewhere) is not cancelled for having none.
            return BurstRules.Judge(plan.AvoidFriendlyFire, newAllies, newCost, plan.Value, plan.Enemies > 0 ? enemies : 1);
        }

        /// <summary>Where <paramref name="unit"/> will be in <paramref name="seconds"/> on its own walk, and whether it walks.</summary>
        private static Vector3 Predict(UnitEntityData unit, float seconds, out bool walking)
        {
            var from = unit.Position;
            var walk = TacticalEngine.WalkClaim(unit);
            walking = walk.HasValue;
            if (!walking) return from;
            BurstRules.At(from.x, from.z, walk.Value.x, walk.Value.z, unit.CurrentSpeedMps, seconds, out float x, out float z);
            return new Vector3(x, from.y, z);
        }

        /// <summary>Drops entries whose cast ended without going off, or whose burst has landed.</summary>
        private static void Prune()
        {
            double now = Now;
            Entries.RemoveAll(e => e.Command == null ||
                                   e.Command.IsFinished && !e.Command.IsActed ||
                                   !double.IsNaN(e.LandsAt) && now > e.LandsAt + BurstRules.LandingMarginSeconds);
        }
    }
}
