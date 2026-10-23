// ★ v0.3.15: the save bonuses the game actually used, read from its saving throws.
//
// The save estimate (CombatMath.SaveFailChance) used the target's plain save stat. Saves against particular effects
// (+4 against mind-affecting, against poison, a mod's custom bonus, an "always succeeds" feature) are added by the game
// only inside the saving-throw rule, so the AI could not see them. Every RuleSavingThrow reports the bonus it really used
// (StatValue, with those contextual modifiers) and whether it passed automatically. What is kept is the difference to the
// plain stat at that moment, so later buffs and debuffs still count through the stat: per target and ability, plus per
// kind of creature (blueprint) for the next one of the same kind; cleared when the party's fight ends. Read-only.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities.Blueprints;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class SaveObserver : IGlobalRulebookHandler<RuleSavingThrow>
    {
        public sealed class Seen
        {
            /// <summary>Bonus the rule used minus the plain save stat at that moment.</summary>
            public int Delta;
            public bool AutoPass;
        }

        private static SaveObserver _instance;
        private static readonly Dictionary<string, Seen> ByUnit = new Dictionary<string, Seen>();
        private static readonly Dictionary<string, Seen> ByKind = new Dictionary<string, Seen>();
        private static readonly HashSet<string> Reported = new HashSet<string>();
        /// <summary>★ v0.3.19 (test9): ability → the save the game rolled against it (by anyone): kept for the session.</summary>
        private static readonly Dictionary<string, SavingThrowType> TypeSeen = new Dictionary<string, SavingThrowType>();

        /// <summary>
        /// ★ v0.3.19 (test9): the save the game rolls against <paramref name="ability"/>, as seen, or Unknown. Grease's save
        /// lives in its area buff's own actions, which the ability analysis does not read: every unit was "affected at
        /// 100 %" by it, a nimble rogue as surely as a paladin in plate.
        /// </summary>
        public static SavingThrowType SaveTypeOf(BlueprintAbility ability) =>
            ability != null && TypeSeen.TryGetValue(ability.AssetGuid.ToString(), out var type) ? type : SavingThrowType.Unknown;

        public static void Subscribe()
        {
            if (_instance != null) return;
            _instance = new SaveObserver();
            EventBus.Subscribe(_instance);
        }

        public static void Unsubscribe()
        {
            if (_instance == null) return;
            EventBus.Unsubscribe(_instance);
            _instance = null;
            Clear();
        }

        private sealed class FightTally
        {
            public string Caster, Ability;
            public int Saves, Passed, MinDc = int.MaxValue, MaxDc;
            public float Expected;
        }

        private static readonly Dictionary<string, FightTally> Fight = new Dictionary<string, FightTally>();

        private static void Tally(UnitEntityData caster, BlueprintAbility ability, RuleSavingThrow evt, float passChance)
        {
            string key = $"{caster.UniqueId}|{ability.AssetGuid}";
            if (!Fight.TryGetValue(key, out var t))
                Fight[key] = t = new FightTally { Caster = caster.CharacterName, Ability = ability.Name };
            t.Saves++;
            if (evt.IsPassed) t.Passed++;
            t.Expected += passChance;
            if (!evt.AutoPass)
            {
                t.MinDc = Math.Min(t.MinDc, evt.DifficultyClass);
                t.MaxDc = Math.Max(t.MaxDc, evt.DifficultyClass);
            }
        }

        /// <summary>★ v0.3.19 (release): the fight's saves against the party's abilities, one line each; then forgotten.</summary>
        public static void ReportFight()
        {
            try
            {
                foreach (var t in Fight.Values.OrderBy(x => x.Caster).ThenBy(x => x.Ability))
                    Main.Log($"[Save] {t.Caster}'s {t.Ability}: {t.Saves} saves, {t.Passed} passed (expected {t.Expected / t.Saves:P0})" +
                             (t.MaxDc > 0 ? $", DC {(t.MinDc == t.MaxDc ? $"{t.MaxDc}" : $"{t.MinDc}-{t.MaxDc}")}" : ""));
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Save] report: {ex.Message}");
            }
            Fight.Clear();
        }

        public static void Clear()
        {
            ByUnit.Clear();
            // ★ v0.3.18 (review): what one creature of a kind showed (a cultist under Resist Energy, a +4 contextual save)
            // was applied to every later creature of that blueprint for the whole session: kinds are learned per fight.
            ByKind.Clear();
            Reported.Clear();
        }

        private static string Key(string who, SavingThrowType type, BlueprintAbility ability) =>
            $"{who}|{type}|{ability?.AssetGuid}";

        /// <summary>What the game showed for <paramref name="target"/>'s saves of this type against this ability, or null.</summary>
        public static Seen Lookup(UnitEntityData target, SavingThrowType type, BlueprintAbility ability)
        {
            if (target == null || ability == null || ByUnit.Count == 0 && ByKind.Count == 0) return null;
            if (ByUnit.TryGetValue(Key(target.UniqueId, type, ability), out var seen)) return seen;
            string kind = target.Blueprint?.AssetGuid.ToString();
            return kind != null && ByKind.TryGetValue(Key(kind, type, ability), out seen) ? seen : null;
        }

        public void OnEventAboutToTrigger(RuleSavingThrow evt)
        {
        }

        public void OnEventDidTrigger(RuleSavingThrow evt)
        {
            try
            {
                if (evt == null || evt.WasAlreadyDead) return;
                var seenAbility = evt.Reason?.Ability?.Blueprint ?? evt.Reason?.Context?.SourceAbility;
                if (seenAbility != null && evt.Type != SavingThrowType.Unknown) TypeSeen[seenAbility.AssetGuid.ToString()] = evt.Type;
                var target = evt.Initiator;
                var caster = evt.Reason?.Caster;
                if (target == null || caster == null || !caster.IsPlayerFaction || !caster.IsEnemy(target)) return;
                var ability = evt.Reason?.Ability?.Blueprint ?? evt.Reason?.Context?.SourceAbility;
                if (ability == null) return;
                StatType statType = RuleSavingThrow.GetStatFromSavingThrow(evt.Type);
                int plain = target.Stats.GetStat(statType)?.ModifiedValue ?? evt.StatValue;
                // ★ v0.3.17: every save against the party's abilities, with the chance it had: tells bad luck from a wrong
                // estimate (Orvath cast Sleep on a Corrupted Mongrel Brute five times; the log could not say why it failed).
                int needed = evt.RollValueToPass;
                float passChance = evt.AutoPass ? 1f : Math.Max(0.05f, Math.Min(0.95f, (21 - needed) / 20f));
                // ★ v0.3.19 (release): each roll at verbose level only; the normal log gets one line per caster and ability
                // at the end of the fight (ReportFight) - saves made, passed, the chance expected and the DCs.
                Tally(caster, ability, evt, passChance);
                Main.Verbose($"[Save] {target.CharacterName} vs {caster.CharacterName}'s {ability.Name}: {evt.Type} " +
                         (evt.AutoPass ? "passes automatically" : $"{(int)evt.D20}+{evt.StatValue}{(evt.SuccessBonus != 0 ? $"+{evt.SuccessBonus}" : "")} vs DC {evt.DifficultyClass}") +
                         $" - {(evt.IsPassed ? "passed" : "failed")} (passes on {needed}+, {passChance:P0})");
                var seen = new Seen { Delta = evt.StatValue - plain, AutoPass = evt.AutoPass };
                ByUnit[Key(target.UniqueId, evt.Type, ability)] = seen;
                string kind = target.Blueprint?.AssetGuid.ToString();
                if (kind != null) ByKind[Key(kind, evt.Type, ability)] = seen;
                if ((seen.AutoPass || seen.Delta != 0) && Reported.Add(Key(target.UniqueId, evt.Type, ability)))
                    Main.Log($"[Save] {target.CharacterName}: {evt.Type} save against {ability.Name} " +
                             (seen.AutoPass ? "passes automatically" : $"{(seen.Delta > 0 ? "+" : "")}{seen.Delta} beyond its save stat") +
                             "; estimates use this");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Save] observer: {ex.Message}");
            }
        }
    }
}
