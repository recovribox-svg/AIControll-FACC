// ★ v0.3.17: per-fight numbers for tuning. ★ v0.3.18: also the observations the engine calibrates itself with
// (ValueModel): the party's damage per round (expected rounds, B2) and the enemies' real damage against the damage the
// engine predicted (the drop-chance model, N5), per completed round of this fight.
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
        /// <summary>★ v0.3.18: per round of this fight - damage the party dealt, damage the enemies dealt to the party,
        /// and the damage the engine predicted for the party at the round's first decision.</summary>
        private static readonly Dictionary<int, float> PartyByRound = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> EnemyByRound = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> PredictedByRound = new Dictionary<int, float>();
        private static float _lastDangerScale = 1f, _lastPartyPerRound;

        /// <summary>★ v0.3.20 (test4): the fight's latest danger calibration (ValueModel.DangerScale), for EnemyThreat.</summary>
        public static float LastDangerScale => _lastDangerScale;
        /// <summary>★ v0.3.19 (test8): per party member, the damage it took in each round of this fight.</summary>
        private static readonly Dictionary<string, Dictionary<int, float>> TakenByRound = new Dictionary<string, Dictionary<int, float>>();

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
            PartyByRound.Clear();
            EnemyByRound.Clear();
            PredictedByRound.Clear();
            TakenByRound.Clear();
            _lastDangerScale = 1f;
            _lastPartyPerRound = 0f;
        }

        /// <summary>
        /// ★ v0.3.18: the party's damage in the completed rounds of this fight (before <paramref name="round"/>), and how
        /// many rounds that is. Nothing before the fight's first decision.
        /// </summary>
        public static void PartyObserved(int round, out float damage, out int rounds)
        {
            damage = 0f;
            rounds = _started ? Math.Max(0, round - _firstRound) : 0;
            foreach (var pair in PartyByRound)
                if (pair.Key < round) damage += pair.Value;
        }

        /// <summary>
        /// ★ v0.3.18: the enemies' damage to the party and the engine's prediction for it, over the completed rounds
        /// since the first round with a prediction; <paramref name="roundsPredicted"/> counts the rounds predicted above 0.
        /// </summary>
        public static void DangerObserved(int round, out float observed, out float predicted, out int roundsPredicted)
        {
            observed = predicted = 0f;
            roundsPredicted = 0;
            int first = int.MaxValue;
            foreach (var pair in PredictedByRound)
            {
                if (pair.Key >= round) continue;
                predicted += pair.Value;
                if (pair.Value > 0f)
                {
                    roundsPredicted++;
                    first = Math.Min(first, pair.Key);
                }
            }
            if (roundsPredicted == 0) return;
            foreach (var pair in EnemyByRound)
                if (pair.Key >= first && pair.Key < round) observed += pair.Value;
        }

        /// <summary>
        /// ★ v0.3.19 (test8): the most damage <paramref name="unit"/> took in one round of the last three (this one
        /// included) - what is actually hitting it. The enemies focus the front line: Seelah went from 100 % to 45 % and
        /// from 64 % to 14 % in single rounds while the party-wide calibration had cut every prediction to 40 %.
        /// </summary>
        public static float RecentWorstTaken(UnitEntityData unit, int round)
        {
            if (unit == null || !TakenByRound.TryGetValue(unit.UniqueId, out var taken)) return 0f;
            float worst = 0f;
            foreach (var pair in taken)
                if (pair.Key <= round && pair.Key > round - 3) worst = Math.Max(worst, pair.Value);
            return worst;
        }

        /// <summary>★ v0.3.18: no prediction recorded for this round yet (the engine computes one at the round's first decision).</summary>
        public static bool NeedsPrediction(int round) => !PredictedByRound.ContainsKey(round);

        /// <summary>★ v0.3.18: the damage the enemies can deal to the party this round, as the engine predicts it (uncalibrated).</summary>
        public static void NotePrediction(int round, float damage) => PredictedByRound[round] = Math.Max(0f, damage);

        /// <summary>★ v0.3.18: what the engine used at its latest decision (for the fight-end line).</summary>
        public static void NoteCalibration(float partyPerRound, float dangerScale)
        {
            _lastPartyPerRound = partyPerRound;
            _lastDangerScale = dangerScale;
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
                    // The prior of TacticalEngine.BuildContext's ExpectedRounds (ValueModel).
                    float levels = ctx.Allies.Sum(a => (float)Math.Max(1, a.Descriptor?.Progression?.CharacterLevel ?? 1));
                    _partyPriorPerRound = ValueModel.PartyDamagePerRound(levels, 0f, 0f);
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
                             $"party damage {_partyDamage} = {_partyDamage / (float)rounds:F0}/round (prior {_partyPriorPerRound:F0}, " +
                             $"used at the end {_lastPartyPerRound:F0}); " +
                             $"enemy damage to the party {_enemyDamage} = {_enemyDamage / (float)rounds:F0}/round " +
                             $"(Threat of the enemies at the start {_threatAtStart:F0}; predicted {PredictedByRound.Values.Sum():F0} over " +
                             $"{PredictedByRound.Count(p => p.Value > 0f)} rounds, danger scale at the end x{_lastDangerScale:F2})");
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
            // ★ v0.3.20 (audit PERF-001): real time - decisions left out while a unit waited quietly for its cooldown.
            int quiet = timing == RealTime ? RealTimeTactics.TakeQuietSkips() : 0;
            Main.Log($"[Perf] {mode}: {sorted.Count} decisions, median {Percentile(0.5):F1} ms, 95% {Percentile(0.95):F1} ms, " +
                     $"slowest {timing.SlowestMs:F1} ms ({timing.Slowest}), total {sorted.Sum():F0} ms" +
                     (quiet > 0 ? $"; {quiet} skipped while waiting for a cooldown" : ""));
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
                int dealt = Math.Max(0, evt.Result);
                if (dealt == 0) return;
                int round = CombatMemory.Round;
                if (attacker.IsPlayerFaction)
                {
                    _partyDamage += dealt;
                    PartyByRound.TryGetValue(round, out float sum);
                    PartyByRound[round] = sum + dealt;
                }
                else if (target.IsPlayerFaction)
                {
                    _enemyDamage += dealt;
                    EnemyThreat.NoteDealt(attacker, dealt);   // ★ v0.3.20 (test4)
                    EnemyByRound.TryGetValue(round, out float sum);
                    EnemyByRound[round] = sum + dealt;
                    if (!TakenByRound.TryGetValue(target.UniqueId, out var taken))
                        TakenByRound[target.UniqueId] = taken = new Dictionary<int, float>();
                    taken.TryGetValue(round, out float mine);
                    taken[round] = mine + dealt;
                }
            }
            catch
            {
                // diagnostics must never disturb the game's damage rule
            }
        }
    }
}
