// ★ v0.3.17: per-fight numbers for tuning - diagnostics only, nothing here changes a decision.
//
// docs/AI_TUNING_AUDIT.md, phase 0:
//   [Perf]  how long the tactical engine takes per decision (count, median, 95th percentile, slowest), per combat mode.
//           Nothing was measured before; optimisation waits for these numbers.
//   [Calib] how long the fight really lasted against the engine's expected rounds at its start, and the damage the
//           party and the enemies really dealt per round against what the estimate assumes (4 x levels for the party,
//           CombatMath.Threat for the enemies). Turn-based logs showed fights lasting about twice the estimate (B2).
// Reported and cleared when the party's fight ends.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules.Damage;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class FightStats : IGlobalRulebookHandler<RuleDealDamage>
    {
        private sealed class Timing
        {
            public readonly List<double> Ms = new List<double>();
            public double SlowestMs;
            public string Slowest;
        }

        private static FightStats _instance;
        private static readonly Timing TurnBased = new Timing();
        private static readonly Timing RealTime = new Timing();

        private static bool _started;
        private static bool _startedRealTime;
        private static int _firstRound, _lastRound;
        private static float _expectedAtStart, _partyPriorPerRound, _threatAtStart;
        private static int _enemiesAtStart;
        private static long _partyDamage, _enemyDamage;

        public static void Subscribe()
        {
            if (_instance != null) return;
            _instance = new FightStats();
            EventBus.Subscribe(_instance);
        }

        public static void Unsubscribe()
        {
            if (_instance == null) return;
            EventBus.Unsubscribe(_instance);
            _instance = null;
            Clear();
        }

        public static void Clear()
        {
            TurnBased.Ms.Clear();
            TurnBased.SlowestMs = 0d;
            TurnBased.Slowest = null;
            RealTime.Ms.Clear();
            RealTime.SlowestMs = 0d;
            RealTime.Slowest = null;
            _started = false;
            _partyDamage = _enemyDamage = 0;
        }

        /// <summary>One engine decision took <paramref name="ms"/> milliseconds.</summary>
        public static void RecordDecision(UnitEntityData unit, double ms, string chosen)
        {
            var timing = CombatMemory.IsRealTime ? RealTime : TurnBased;
            timing.Ms.Add(ms);
            if (ms > timing.SlowestMs)
            {
                timing.SlowestMs = ms;
                timing.Slowest = $"{unit?.CharacterName}: {chosen ?? "nothing"}";
            }
        }

        /// <summary>A decision's context: the first one of the fight sets the estimates, every one moves the last round.</summary>
        public static void NoteContext(TacticalContext ctx)
        {
            if (ctx == null) return;
            try
            {
                if (!_started)
                {
                    _started = true;
                    _startedRealTime = CombatMemory.IsRealTime;
                    _firstRound = _lastRound = ctx.Round;
                    _expectedAtStart = ctx.ExpectedRounds;
                    // The same assumption as TacticalEngine.BuildContext's ExpectedRounds.
                    float levels = ctx.Allies.Sum(a => (float)Math.Max(1, a.Descriptor?.Progression?.CharacterLevel ?? 1));
                    _partyPriorPerRound = Math.Max(10f, levels * 4f);
                    _threatAtStart = ctx.Enemies.Sum(CombatMath.Threat);
                    _enemiesAtStart = ctx.Enemies.Count;
                }
                _lastRound = Math.Max(_lastRound, ctx.Round);
                if (ctx.Round < _firstRound) _firstRound = ctx.Round;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Calib] {ex.Message}");
            }
        }

        /// <summary>The party's fight ended: log the numbers, then forget them.</summary>
        public static void Report()
        {
            try
            {
                ReportTiming("turn-based", TurnBased);
                ReportTiming("real-time", RealTime);
                if (_started)
                {
                    int rounds = Math.Max(1, _lastRound - _firstRound + 1);
                    string unit = _startedRealTime ? "6-second spans" : "rounds";
                    Main.Log($"[Calib] fight lasted {rounds} {unit} ({(_startedRealTime ? "RT" : "R")}{_firstRound}-{_lastRound}), " +
                             $"{_enemiesAtStart} enemies at the start; expected rounds at the start {_expectedAtStart:F1}; " +
                             $"party damage {_partyDamage} = {_partyDamage / (float)rounds:F0}/round (estimate assumes {_partyPriorPerRound:F0}); " +
                             $"enemy damage to the party {_enemyDamage} = {_enemyDamage / (float)rounds:F0}/round " +
                             $"(Threat of the enemies at the start {_threatAtStart:F0})");
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Calib] report: {ex.Message}");
            }
            Clear();
        }

        private static void ReportTiming(string mode, Timing timing)
        {
            if (timing.Ms.Count == 0) return;
            var sorted = timing.Ms.OrderBy(x => x).ToList();
            double Percentile(double q) => sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * (sorted.Count - 1) + 0.5))];
            Main.Log($"[Perf] {mode}: {sorted.Count} decisions, median {Percentile(0.5):F1} ms, 95% {Percentile(0.95):F1} ms, " +
                     $"slowest {timing.SlowestMs:F1} ms ({timing.Slowest}), total {sorted.Sum():F0} ms");
        }

        public void OnEventAboutToTrigger(RuleDealDamage evt)
        {
        }

        public void OnEventDidTrigger(RuleDealDamage evt)
        {
            try
            {
                if (evt == null || evt.IsFake || !_started) return;
                var attacker = evt.Initiator;
                var target = evt.Target;
                if (attacker == null || target == null || !attacker.IsEnemy(target)) return;
                if (attacker.IsPlayerFaction) _partyDamage += Math.Max(0, evt.Result);
                else if (target.IsPlayerFaction) _enemyDamage += Math.Max(0, evt.Result);
            }
            catch
            {
                // diagnostics must never disturb the game's damage rule
            }
        }
    }
}
