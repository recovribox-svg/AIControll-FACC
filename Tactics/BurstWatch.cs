// ★ burst aim (17 November 2026, author-approved A3-A5; docs/BURST_AIM_AND_TACTICS_PLAN.md §3.4-3.6): a ground-aimed
// burst once issued (TacticalEngine.Burst.cs). Until its cast goes off, real time re-checks its landing spot with fresh
// positions and cancels the cast when an ally the decision did not count will be caught, or no enemy will be left inside;
// the game spends the spell and the action only when the cast goes off (UnitUseAbility.OnAction, UnitCommandController),
// so the cancel is free. Once it has gone off, the time it lands is kept: the other AI units' walks avoid the spot until
// then (AoeDangerAnalyzer), and a turn-based turn waits for it (CustomBrainPatch.ReadyToEndTurn).
// ★ review follow-ups A-R1/A3/A-P1 (author-approved; docs/BURST_PATH_FORECAST.md): walks are read along the game's own
// path (TacticalEngine.ReadRoute); an ally that walks in is priced by its damage and by the decision's own friendly-control
// price (BurstPlan.AllyControl), its damage estimated once per watch; the units are read once per frame for every watch;
// a watch whose cast has not gone off is never dropped to make room, and each fight's peak counts are logged.
using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Utility;
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
            /// <summary>Each ally's damage estimate, read once for this watch (A-P1: not again at every re-check).</summary>
            public Dictionary<UnitEntityData, float> Damage;
            /// <summary>★ R2: re-checked before its cast goes off (real time, within the re-check cap). Every entry keeps its
            /// act and landing, whatever this says.</summary>
            public bool Recheck;
            /// <summary>★ evidence: when the decision expected the cast to go off (game seconds), and whether its landing was
            /// read.</summary>
            public double PredictedActAt = double.NaN;
            public bool LandingSeen;
            /// <summary>★ evidence (R3): allies that walked in with no control price from the decision, counted once.</summary>
            public HashSet<UnitEntityData> Unpriced;
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

        /// <summary>
        /// ★ R2 (docs/R_ITEMS_PLAN.md): the casts re-checked before they go off, at most. Each AI unit has at most one cast
        /// that has not gone off, so this is a bound on work, not a working limit. A burst past it keeps the checks of its
        /// decision; it is still tracked, so its landing spot is shared and a turn-based turn still waits for it.
        /// </summary>
        private const int MaxRechecked = 64;

        /// <summary>
        /// ★ R2: a memory guard on tracked bursts. An entry lives until its cast ends without going off or its burst lands,
        /// so the count is the bursts in flight. Nothing is ever dropped to make room; past this a burst is not tracked, and
        /// its landing spot is still shared (registered before admission).
        /// </summary>
        private const int MaxTracked = 256;
        private const int MaxLinesPerFight = 64;

        private static BurstWatch s_Instance;
        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly List<Refusal> Refusals = new List<Refusal>();
        private static readonly List<Cancel> Cancels = new List<Cancel>();
        private static int s_Lines;

        // A-P1: this fight's counts, logged when it ends - bursts watched, the most at once, the most units read for a
        // re-check, re-checks, and bursts that made room or went unwatched.
        private static int s_Watched, s_PeakWatches, s_PeakRoster, s_Rechecks, s_NotRechecked, s_Untracked;
        private static int s_RoutesOnPathAtStart, s_RoutesStraightAtStart, s_RoutesPursuingAtStart;

        // ★ evidence for the next playtest (docs/R_ITEMS_PLAN.md): how far off the decision's cast time was, where the anchor
        // stood when the burst landed, and allies that walked in with no control price from the decision (R3).
        private static int s_ActSamples, s_ActLate, s_Landings, s_AnchorInside, s_LandingsLate, s_UnpricedControl;
        private static double s_ActErrorSum, s_ActErrorAbsMax, s_AnchorDistanceSum;
        /// <summary>A landing read later than this after it landed is not used for the anchor's position.</summary>
        private const double LandingReadSeconds = 0.5d;

        // A-P1: the units a re-check reads, once per frame for every watch (in the game, alive, in combat).
        private static readonly List<UnitEntityData> Roster = new List<UnitEntityData>();
        private static int s_RosterFrame = -1;

        // A-R1: a walk read for a re-check (one at a time; the main thread only).
        private static readonly WalkRoute Route = new WalkRoute();

        // The native cast time of a running cast (UnitUseAbility.m_CastTime); read only, falls back to the plan's.
        private static readonly HarmonyLib.AccessTools.FieldRef<UnitUseAbility, float> CastTimeRef = ReadCastTimeRef();

        private static HarmonyLib.AccessTools.FieldRef<UnitUseAbility, float> ReadCastTimeRef()
        {
            try { return HarmonyLib.AccessTools.FieldRefAccess<UnitUseAbility, float>("m_CastTime"); }
            catch { return null; }
        }

        private static double Now => Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;

        /// <summary>The native cast time of a running cast (UnitUseAbility.m_CastTime), or <paramref name="fallback"/>.</summary>
        internal static float NativeCastTime(UnitUseAbility use, float fallback)
        {
            if (use == null || CastTimeRef == null) return fallback;
            try { return CastTimeRef(use); }
            catch { return fallback; }
        }

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

        /// <summary>A new fight: nothing carries over (the counts of a fight that raised no end are reported first).</summary>
        public static void Clear()
        {
            ReportFight();
            Entries.Clear();
            Refusals.Clear();
            Cancels.Clear();
            Roster.Clear();
            s_RosterFrame = -1;
            s_Lines = 0;
        }

        /// <summary>
        /// A-P1: one line of this fight's counts when it watched a burst - the evidence for the watch's capacity and the
        /// re-check's cost. The counts restart.
        /// </summary>
        public static void ReportFight()
        {
            if (s_Watched > 0 || s_Untracked > 0)
                Main.Log($"[Burst] fight summary: {s_Watched} burst(s) watched, at most {s_PeakWatches} at once; {s_Rechecks} re-check(s) " +
                         $"reading at most {s_PeakRoster} unit(s); walks read along the game's path " +
                         $"{TacticalEngine.RoutesOnPath - s_RoutesOnPathAtStart}, straight {TacticalEngine.RoutesStraight - s_RoutesStraightAtStart}" +
                         $" (of them, started attacks chasing their target {TacticalEngine.RoutesPursuing - s_RoutesPursuingAtStart})" +
                         (s_NotRechecked > 0 ? $"; {s_NotRechecked} burst(s) not re-checked (all {MaxRechecked} re-checks live)" : "") +
                         (s_Untracked > 0 ? $"; {s_Untracked} burst(s) not tracked ({MaxTracked} in flight)" : "") +
                         EvidenceSummary());
            s_Watched = s_PeakWatches = s_PeakRoster = s_Rechecks = s_NotRechecked = s_Untracked = 0;
            s_ActSamples = s_ActLate = s_Landings = s_AnchorInside = s_LandingsLate = s_UnpricedControl = 0;
            s_ActErrorSum = s_ActErrorAbsMax = s_AnchorDistanceSum = 0d;
            s_RoutesOnPathAtStart = TacticalEngine.RoutesOnPath;
            s_RoutesStraightAtStart = TacticalEngine.RoutesStraight;
            s_RoutesPursuingAtStart = TacticalEngine.RoutesPursuing;
        }

        /// <summary>★ evidence: the forecast against what happened, for the next playtest's calibration.</summary>
        private static string EvidenceSummary()
        {
            var text = new System.Text.StringBuilder();
            if (s_ActSamples > 0)
                text.Append($"; cast went off {s_ActErrorSum / s_ActSamples:+0.0;-0.0} s from the decision's forecast on average " +
                            $"(worst {s_ActErrorAbsMax:0.0} s, {s_ActLate} of {s_ActSamples} later)");
            if (s_Landings > 0)
                text.Append($"; anchor inside where it landed {s_AnchorInside} of {s_Landings}, " +
                            $"{s_AnchorDistanceSum / s_Landings:0.0} m from the point on average");
            if (s_LandingsLate > 0) text.Append($" ({s_LandingsLate} landing(s) read too late to place the anchor)");
            if (s_UnpricedControl > 0)
                text.Append($"; {s_UnpricedControl} ally walk-in(s) had no control price from the decision");
            return text.ToString();
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
                // ★ R2: the landing spot is shared with the other AI units' walks whether or not the burst is tracked.
                var profile = AbilityProfile.Get(plan.Ability);
                if (plan.RealTime && profile != null)
                    Analysis.AoeDangerAnalyzer.RegisterPendingBurst(plan.Point, plan.Radius, plan.Ability.Name, profile.DealsDamage,
                        profile.ControlStrength != ControlStrength.None, command, plan.Landing);
                if (Entries.Count >= MaxTracked)
                {
                    if (s_Untracked++ == 0)
                        Log($"[Burst] {unit.CharacterName}: {plan.Ability.Name} not tracked - {MaxTracked} bursts in flight; " +
                            "it keeps the checks of its decision and its landing spot is shared");
                    return;
                }
                // ★ R2: only the re-checks are capped; a burst past the cap keeps its act, landing and turn-based wait.
                bool recheck = plan.RealTime && Rechecked() < MaxRechecked;
                if (plan.RealTime && !recheck && s_NotRechecked++ == 0)
                    Log($"[Burst] {unit.CharacterName}: {plan.Ability.Name} not re-checked - all {MaxRechecked} re-checks are casts " +
                        "that have not gone off; it keeps the checks of its decision");
                Entries.Add(new Entry
                {
                    Unit = unit, Command = command, Plan = plan, NextCheck = Now + BurstRules.RecheckSeconds, Recheck = recheck,
                    PredictedActAt = plan.RealTime ? Now + plan.Walk + plan.Cast : double.NaN
                });
                s_Watched++;
                if (Entries.Count > s_PeakWatches) s_PeakWatches = Entries.Count;
                Log($"[Burst] {unit.CharacterName}: {plan.Ability.Name} aimed at the {plan.Describe()}; counted {plan.Enemies} " +
                    $"enem{(plan.Enemies == 1 ? "y" : "ies")}, {plan.Allies.Count} all{(plan.Allies.Count == 1 ? "y" : "ies")}");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Burst] track {unit.CharacterName}: {ex.Message}");
            }
        }

        /// <summary>★ R2: the casts re-checked now - real time, not gone off yet.</summary>
        private static int Rechecked()
        {
            int count = 0;
            foreach (var e in Entries)
                if (e.Recheck && double.IsNaN(e.LandsAt)) count++;
            return count;
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
                        e.LandingSeen = true;   // ★ evidence: nothing landed to place the anchor against
                        Analysis.AoeDangerAnalyzer.PendingBurstLandsIn(command, 0f);
                        return;
                    }
                    // ★ evidence: when the cast went off, against the decision's forecast (walk and cast time).
                    if (!double.IsNaN(e.PredictedActAt))
                    {
                        double error = Now - e.PredictedActAt;
                        s_ActSamples++;
                        s_ActErrorSum += error;
                        if (error > 0d) s_ActLate++;
                        if (Math.Abs(error) > s_ActErrorAbsMax) s_ActErrorAbsMax = Math.Abs(error);
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
            ObserveLandings();   // ★ evidence: turn-based landings are read while the turn waits
            return LandingPendingFor(unit, Now);
        }

        /// <summary>A burst of <paramref name="unit"/>'s that has gone off and lands after <paramref name="now"/>.</summary>
        internal static bool LandingPendingFor(UnitEntityData unit, double now)
        {
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
                if (e.Unit != unit || !e.Recheck) continue;   // ★ R2: past the cap, the decision's checks only
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
                float cast = cmd is UnitUseAbility use ? NativeCastTime(use, plan.Cast) : plan.Cast;
                remaining = Math.Max(0f, cast - cmd.TimeSinceStart) + plan.Delay + flight;
            }
            else
            {
                float walk = BurstRules.WalkSeconds(TacticalEngine.HorizontalDistance(unit.Position, plan.Point), plan.Range, unit.CurrentSpeedMps);
                remaining = BurstRules.LandingSeconds(walk, plan.Cast, plan.Delay, flight);
            }

            float newCost = 0f;
            List<string> caught = null;
            var roster = RosterNow();
            s_Rechecks++;
            if (roster.Count > s_PeakRoster) s_PeakRoster = roster.Count;
            bool casterRead = false;
            for (int i = 0; i <= roster.Count; i++)
            {
                // The roster, then the caster when it was not in it (a caster that is not in combat yet).
                var u = i < roster.Count ? roster[i] : casterRead ? null : unit;
                // Roster membership is cached, live state is not: a summon can leave or be torn down later in this frame.
                if (!CanReadRosterUnit(u, unit)) continue;
                if (u == unit) casterRead = true;
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
                var route = u.Descriptor.State.IsConscious ? TacticalEngine.ReadRoute(u, Route) : null;
                if (route != null) inside = route.MayBeInside(plan.Point.x, plan.Point.z, reach, remaining);
                else inside = Vector3.Distance(u.Position, plan.Point) <= reach;
                if (!inside || !profile.AreaReaches(plan.Ability, u)) continue;
                // ★ A3: its damage, and what the burst's control would cost it by the decision's own rule (AllyControl).
                float damage = DamageTo(e, u, profile);
                float control = 0f;
                bool priced = plan.AllyControl != null && plan.AllyControl.TryGetValue(u, out control);
                // ★ evidence (R3): an ally the decision did not price, for example one created since, walking into a burst
                // that applies a buff. Counted once per watch and ally; nothing about the verdict changes.
                if (!priced && profile.TargetBuffs.Count > 0 && (e.Unpriced ?? (e.Unpriced = new HashSet<UnitEntityData>())).Add(u))
                    s_UnpricedControl++;
                if (!(damage > 0f) && !(control > 0f)) continue;
                newAllies++;
                if (damage > 0f) newCost += u.HPLeft <= 0 ? Math.Max(0f, plan.Value) + 1f : BurstRules.AllyCost(damage, u.HPLeft);
                newCost += control;
                (caught ?? (caught = new List<string>())).Add(u.CharacterName);
            }
            if (caught != null) names = string.Join(", ", caught);
            // A plan that counted no enemy (its value came from elsewhere) is not cancelled for having none.
            return BurstRules.Judge(plan.AvoidFriendlyFire, newAllies, newCost, plan.Value, plan.Enemies > 0 ? enemies : 1);
        }

        /// <summary>A3/A-P1: an ally's damage from this burst, estimated once per watch.</summary>
        private static float DamageTo(Entry e, UnitEntityData ally, AbilityProfile profile)
        {
            var cache = e.Damage ?? (e.Damage = new Dictionary<UnitEntityData, float>());
            if (cache.TryGetValue(ally, out float damage)) return damage;
            damage = e.Plan.Damage;
            try
            {
                // ★ Fusion coverage C1: as the cast has it - aimed at the ground. A ground aim is the main target of no unit
                // unless the unit stands exactly on it with facing 0 (TargetWrapper.Equals). Plus the damage over time it
                // leaves on the ally.
                using (AbilityProfile.AimedAt(new TargetWrapper(e.Plan.Point)))
                    damage = CombatMath.EstimateAbilityDamage(e.Plan.Ability, profile, ally) +
                             TacticalEngine.PeriodicAllyDamage(e.Plan.Ability, profile, ally, CombatMath.AfflictionRounds);
            }
            catch { }
            cache[ally] = damage;
            return damage;
        }

        /// <summary>
        /// A-P1: the units a re-check reads - in the game, alive and in combat - gathered once per frame for every watch.
        /// </summary>
        private static List<UnitEntityData> RosterNow()
        {
            int frame = Time.frameCount;
            if (frame == s_RosterFrame) return Roster;
            s_RosterFrame = frame;
            Roster.Clear();
            foreach (var u in Game.Instance.State.Units)
            {
                if (u == null || !u.IsInGame || u.Descriptor?.State == null || u.Descriptor.State.IsDead) continue;
                if (!(u.CombatState?.IsInCombat ?? false)) continue;
                Roster.Add(u);
            }
            return Roster;
        }

        /// <summary>Revalidate retained references without another scene scan. The caster is also considered when it is
        /// not in combat yet; removed or incompletely initialised units cannot supply geometry/condition reads.</summary>
        private static bool CanReadRosterUnit(UnitEntityData unit, UnitEntityData caster)
        {
            if (unit == null || !unit.IsInGame || unit.Descriptor?.State == null || unit.Descriptor.State.IsDead) return false;
            return ReferenceEquals(unit, caster) || unit.CombatState?.IsInCombat == true;
        }

        /// <summary>Where <paramref name="unit"/> will be in <paramref name="seconds"/> on its own walk, and whether it walks.</summary>
        private static Vector3 Predict(UnitEntityData unit, float seconds, out bool walking)
        {
            var route = TacticalEngine.ReadRoute(unit, Route);   // ★ A-R1: along its own path
            walking = route != null;
            if (!walking) return unit.Position;
            route.At(seconds, out float x, out float y, out float z);
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Drops entries whose cast ended without its act being seen (it did not go off, or its act was missed), or whose
        /// burst has landed. ★ R2: nothing else ever removes an entry.
        /// </summary>
        private static void Prune()
        {
            ObserveLandings();
            double now = Now;
            Entries.RemoveAll(e => e.Command == null ||
                                   e.Command.IsFinished && double.IsNaN(e.LandsAt) ||
                                   !double.IsNaN(e.LandsAt) && now > e.LandsAt + BurstRules.LandingMarginSeconds);
        }

        /// <summary>
        /// ★ evidence: where each burst's anchor stood when it landed - inside the area (its radius and the anchor's body) or
        /// not, and how far from the point. Read once, only while the landing is fresh.
        /// </summary>
        private static void ObserveLandings()
        {
            if (Entries.Count == 0) return;
            double now = Now;
            foreach (var e in Entries)
            {
                if (e.LandingSeen || double.IsNaN(e.LandsAt) || now < e.LandsAt) continue;
                e.LandingSeen = true;
                var anchor = e.Plan.Anchor;
                if (now - e.LandsAt > LandingReadSeconds || anchor == null || !anchor.IsInGame) { s_LandingsLate++; continue; }
                float distance = TacticalEngine.HorizontalDistance(anchor.Position, e.Plan.Point);
                s_Landings++;
                s_AnchorDistanceSum += distance;
                if (distance <= e.Plan.Radius + anchor.Corpulence) s_AnchorInside++;
            }
        }
    }
}
