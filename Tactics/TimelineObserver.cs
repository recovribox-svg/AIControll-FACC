// ★ evidence for the next playtest (20 November 2026; docs/R_ITEMS_PLAN.md, R1 and R4). Read only: what the game did,
// against what the engine forecast. Nothing here changes a decision. One summary line per topic per fight:
// - [Timing] (real time): party weapon attacks - command start to the first swing, and the gap between swings by routine
//   size, melee and ranged apart; and each command issued on an enemy whose cast damage can break: the forecast first hit
//   and cast end against the actual ones, and whether the cast broke (a failed RuleCheckConcentration ends it,
//   UnitUseAbility.FailIfConcentrationCheckFailed) or went off (its command acts).
// - [Flank] (both modes): the flank the engine forecast for a melee attack (CombatMath.PredictFlank) against the game's own
//   answer at the roll (RuleAttackRoll.TargetIsFlanked, UnitCombatState.IsFlanked); forecasts by a second engager (the
//   F1 projection) apart, with the height between the two units when they were wrong.
// Bounded: per command and per attacker, at most MaxRecords forecasts a fight; scalar tallies only.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class TimelineObserver : IGlobalRulebookHandler<RuleAttackRoll>, IGlobalRulebookHandler<RuleDealDamage>,
                                           IGlobalRulebookHandler<RuleCheckConcentration>, IUnitCommandActHandler
    {
        private const int MaxRecords = 32;

        private static TimelineObserver s_Instance;

        // --- attack timing (R4) ---
        private sealed class AttackTrack
        {
            public int Swings;
            public int LastIndex = -1;
            public float LastAt;
        }
        private static ConditionalWeakTable<UnitAttack, AttackTrack> s_Attacks = new ConditionalWeakTable<UnitAttack, AttackTrack>();
        private static readonly Tally FirstMelee = new Tally(), FirstRanged = new Tally(), GapRanged = new Tally();
        /// <summary>Melee gaps by routine size (1..6, the last one for six or more).</summary>
        private static readonly Tally[] GapMelee = { new Tally(), new Tally(), new Tally(), new Tally(), new Tally(), new Tally() };

        // --- break forecasts (R4) ---
        private sealed class BreakRecord
        {
            public UnitEntityData Attacker, Enemy;
            public UnitUseAbility Cast;
            public double IssuedAt;
            public float PredictedHit = float.NaN, PredictedCastLeft;
            public bool Credited;
            public double HitAt = double.NaN, EndedAt = double.NaN;
            public bool Broken, WentOff;
        }
        private static readonly List<BreakRecord> Breaks = new List<BreakRecord>();
        private static int s_BreaksSkipped;

        // --- flank forecasts (R1) ---
        private sealed class FlankRecord
        {
            public UnitEntityData Target;
            public UnitCommand Command;
            public bool Predicted, BySecond;
        }
        private static readonly Dictionary<UnitEntityData, FlankRecord> Flanks = new Dictionary<UnitEntityData, FlankRecord>();
        private static int s_FlankAgree, s_FlankFalse, s_FlankFalseBySecond, s_FlankMissed, s_FlankBySecond;
        private static float s_FlankFalseHeightMax;

        private sealed class Tally
        {
            public int N;
            public double Sum, Min = double.MaxValue, Max;
            public void Add(double v)
            {
                N++;
                Sum += v;
                if (v < Min) Min = v;
                if (v > Max) Max = v;
            }
            public void Clear() { N = 0; Sum = Max = 0d; Min = double.MaxValue; }
            public string Text() => N == 0 ? "none" : $"{Sum / N:0.00} s (n={N}, {Min:0.00}-{Max:0.00})";
        }

        private static double Now => Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;

        public static void Subscribe()
        {
            if (s_Instance != null) return;
            s_Instance = new TimelineObserver();
            EventBus.Subscribe(s_Instance);
        }

        public static void Unsubscribe()
        {
            if (s_Instance == null) return;
            EventBus.Unsubscribe(s_Instance);
            s_Instance = null;
            Clear();
        }

        /// <summary>A new fight: the counts of a fight that raised no end are reported first.</summary>
        public static void Clear()
        {
            ReportFight();
        }

        /// <summary>
        /// A command the engine issued: a break forecast when it attacks an enemy whose cast damage can break (real time),
        /// and a flank forecast for a melee weapon attack (both modes).
        /// </summary>
        internal static void NoteIssued(UnitEntityData unit, Candidate c, UnitCommand command)
        {
            if (s_Instance == null || unit == null || c?.Target == null || command == null || !unit.IsEnemy(c.Target)) return;
            try
            {
                if (c.Kind == CandidateKind.WeaponAttack) NoteFlank(unit, c, command);
                if (CombatMemory.IsRealTime && (c.Kind == CandidateKind.WeaponAttack || c.Kind == CandidateKind.Ability)) NoteBreak(unit, c);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Timing] note {unit.CharacterName}: {ex.Message}");
            }
        }

        private static void NoteFlank(UnitEntityData unit, Candidate c, UnitCommand command)
        {
            var weapon = unit.GetFirstWeapon();
            if (weapon?.Blueprint == null || !weapon.Blueprint.IsMelee) return;
            bool predicted = CombatMath.ForecastFlank(unit, c.Target, weapon, out bool bySecond);
            Flanks[unit] = new FlankRecord { Target = c.Target, Command = command, Predicted = predicted, BySecond = bySecond };
        }

        private static void NoteBreak(UnitEntityData unit, Candidate c)
        {
            var enemy = c.Target;
            if (!(enemy.Commands?.Standard is UnitUseAbility use) || !use.IsRunning || use.IsActed || use.Cutscene) return;
            if (use.Ability?.Blueprint == null || !use.Ability.CanBeInterruptedWithDamage) return;
            if (Breaks.Count >= MaxRecords) { s_BreaksSkipped++; return; }
            var record = new BreakRecord
            {
                Attacker = unit, Enemy = enemy, Cast = use, IssuedAt = Now,
                PredictedCastLeft = BurstWatch.NativeCastTime(use, BurstRules.StandardCastSeconds) - use.TimeSinceStart,
                Credited = c.Label != null && (c.Label.Contains("[breaks ") || c.Label.Contains("disrupt"))
            };
            float distance = TacticalEngine.HorizontalDistance(unit.Position, enemy.Position);
            if (c.Kind == CandidateKind.WeaponAttack)
            {
                var weapon = unit.GetFirstWeapon();
                bool ranged = weapon?.Blueprint?.IsRanged == true;
                record.PredictedHit = DisruptRules.WeaponHitSeconds(0, c.DisruptWalkSeconds, ranged, distance, 0f);
            }
            else if (c.Ability != null && !c.NeedsMove && !c.MoveFirst)
            {
                // The same rule as AbilityDisruptBonus, for a cast made where the caster stands.
                var profile = AbilityProfile.Get(c.Ability);
                if (profile == null || !profile.DealsDamage) return;
                float lag = profile.DeliverDelaySeconds + TacticalEngine.FlightSecondsTo(profile, unit.Position, enemy.Position);
                record.PredictedHit = DisruptRules.AbilityHitSeconds(0f, TacticalEngine.CastSecondsOf(c.Ability, true), lag);
            }
            Breaks.Add(record);
        }

        public void OnEventAboutToTrigger(RuleAttackRoll evt) { }

        public void OnEventDidTrigger(RuleAttackRoll evt)
        {
            try
            {
                var attacker = evt?.Initiator;
                var target = evt?.Target;
                if (attacker == null || target == null) return;
                bool opportunity = evt.RuleAttackWithWeapon?.IsAttackOfOpportunity == true;
                if (!opportunity) NoteFirstHit(attacker, target);
                if (!opportunity && Flanks.Count > 0 && Flanks.TryGetValue(attacker, out var flank) && flank.Target == target &&
                    evt.Weapon?.Blueprint?.IsMelee == true)
                {
                    Flanks.Remove(attacker);
                    bool actual = evt.TargetIsFlanked || target.CombatState?.IsFlanked == true;
                    if (flank.BySecond) s_FlankBySecond++;
                    if (flank.Predicted == actual) s_FlankAgree++;
                    else if (flank.Predicted)
                    {
                        s_FlankFalse++;
                        if (flank.BySecond)
                        {
                            s_FlankFalseBySecond++;
                            s_FlankFalseHeightMax = Math.Max(s_FlankFalseHeightMax, Math.Abs(attacker.Position.y - target.Position.y));
                        }
                    }
                    else s_FlankMissed++;
                }
                if (!opportunity && CombatMemory.IsRealTime && attacker.IsPlayerFaction &&
                    attacker.Commands?.Standard is UnitAttack attack && attack.IsStarted && !attack.IsFinished)
                    NoteSwing(attack, evt.Weapon?.Blueprint?.IsRanged == true);
            }
            catch { }
        }

        private static void NoteSwing(UnitAttack attack, bool ranged)
        {
            var track = s_Attacks.GetOrCreateValue(attack);
            int index = attack.GetAttackIndex();
            if (index == track.LastIndex) return;   // another roll of the same attack (a reroll, Cleave, images)
            float at = attack.TimeSinceStart;
            if (track.Swings == 0) (ranged ? FirstRanged : FirstMelee).Add(at);
            else if (ranged) GapRanged.Add(at - track.LastAt);
            else GapMelee[Math.Max(1, Math.Min(GapMelee.Length, attack.AllAttacks.Count)) - 1].Add(at - track.LastAt);
            track.Swings++;
            track.LastIndex = index;
            track.LastAt = at;
        }

        public void OnEventAboutToTrigger(RuleDealDamage evt) { }

        public void OnEventDidTrigger(RuleDealDamage evt)
        {
            try
            {
                if (Breaks.Count > 0 && evt?.Initiator != null && evt.Target != null) NoteFirstHit(evt.Initiator, evt.Target);
            }
            catch { }
        }

        private static void NoteFirstHit(UnitEntityData attacker, UnitEntityData target)
        {
            if (Breaks.Count == 0) return;
            foreach (var r in Breaks)
                if (r.Attacker == attacker && r.Enemy == target && double.IsNaN(r.HitAt) && double.IsNaN(r.EndedAt)) r.HitAt = Now;
        }

        public void OnEventAboutToTrigger(RuleCheckConcentration evt) { }

        public void OnEventDidTrigger(RuleCheckConcentration evt)
        {
            try
            {
                if (Breaks.Count == 0 || evt?.Initiator == null || evt.Success) return;
                foreach (var r in Breaks)
                {
                    if (r.Enemy != evt.Initiator || !double.IsNaN(r.EndedAt)) continue;
                    if (r.Cast.IsActed) continue;   // a check after the cast went off changes nothing (MakeConcentrationCheck)
                    r.Broken = true;
                    r.EndedAt = Now;
                }
            }
            catch { }
        }

        public void HandleUnitCommandDidAct(UnitCommand command)
        {
            if (Breaks.Count == 0 || command == null) return;
            foreach (var r in Breaks)
            {
                if (!ReferenceEquals(r.Cast, command) || !double.IsNaN(r.EndedAt)) continue;
                r.WentOff = command.Result != UnitCommand.ResultType.Fail;
                r.EndedAt = Now;
            }
        }

        /// <summary>The fight's evidence lines, then the tallies restart.</summary>
        public static void ReportFight()
        {
            try
            {
                if (FirstMelee.N + FirstRanged.N > 0)
                {
                    var gaps = new StringBuilder();
                    for (int i = 0; i < GapMelee.Length; i++)
                        if (GapMelee[i].N > 0)
                            gaps.Append($"; {(i + 1 == GapMelee.Length ? $"{i + 1}+" : $"{i + 1}")}-attack routines " +
                                        $"{GapMelee[i].Text()} apart (forecast {DisruptRules.SwingSpacing(i + 1, false):0.00} s)");
                    Main.Log($"[Timing] fight summary: party weapon attacks - first melee swing {FirstMelee.Text()} after the start " +
                             $"(forecast {DisruptRules.FirstSwingSeconds:0.00} s), first ranged {FirstRanged.Text()}{gaps}" +
                             (GapRanged.N > 0 ? $"; ranged attacks {GapRanged.Text()} apart (forecast {DisruptRules.NextSwingSeconds:0.00} s when chained)" : ""));
                }
                if (Breaks.Count > 0)
                {
                    int credited = 0, hitInTime = 0, broken = 0, wentOff = 0, other = 0;
                    var hitError = new Tally();
                    var castError = new Tally();
                    foreach (var r in Breaks)
                    {
                        if (r.Credited) credited++;
                        if (r.Broken) broken++;
                        else if (r.WentOff) wentOff++;
                        else other++;
                        if (!double.IsNaN(r.HitAt) && (double.IsNaN(r.EndedAt) || r.HitAt <= r.EndedAt)) hitInTime++;
                        if (!double.IsNaN(r.HitAt) && !float.IsNaN(r.PredictedHit)) hitError.Add(r.HitAt - r.IssuedAt - r.PredictedHit);
                        if (r.WentOff) castError.Add(r.EndedAt - r.IssuedAt - r.PredictedCastLeft);
                    }
                    Main.Log($"[Timing] fight summary: {Breaks.Count} attack(s) issued on a casting enemy ({credited} credited a break); " +
                             $"first attack resolved before the cast ended {hitInTime}; broken {broken}, went off {wentOff}, ended otherwise {other}; " +
                             $"actual first attack minus forecast {SignedText(hitError)}; actual cast end minus forecast {SignedText(castError)}" +
                             (s_BreaksSkipped > 0 ? $" ({s_BreaksSkipped} more not recorded)" : ""));
                }
                int flanks = s_FlankAgree + s_FlankFalse + s_FlankMissed;
                if (flanks > 0)
                    Main.Log($"[Flank] fight summary: {flanks} melee attack(s) compared with the game at the roll - forecast right {s_FlankAgree}, " +
                             $"forecast flanked but not {s_FlankFalse} ({s_FlankFalseBySecond} of them by a second engager" +
                             (s_FlankFalseBySecond > 0 ? $", height between the units up to {s_FlankFalseHeightMax:0.0} m" : "") +
                             $"), flanked but not forecast {s_FlankMissed}; second-engager forecasts {s_FlankBySecond}");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Timing] report: {ex.Message}");
            }
            FirstMelee.Clear(); FirstRanged.Clear(); GapRanged.Clear();
            foreach (var t in GapMelee) t.Clear();
            s_Attacks = new ConditionalWeakTable<UnitAttack, AttackTrack>();
            Breaks.Clear();
            s_BreaksSkipped = 0;
            Flanks.Clear();
            s_FlankAgree = s_FlankFalse = s_FlankFalseBySecond = s_FlankMissed = s_FlankBySecond = 0;
            s_FlankFalseHeightMax = 0f;
        }

        private static string SignedText(Tally t) =>
            t.N == 0 ? "none" : $"{t.Sum / t.N:+0.00;-0.00} s (n={t.N}, {t.Min:+0.00;-0.00} to {t.Max:+0.00;-0.00})";
    }
}
