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

        public static void Clear()
        {
            ByUnit.Clear();
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
                var target = evt.Initiator;
                var caster = evt.Reason?.Caster;
                if (target == null || caster == null || !caster.IsPlayerFaction || !caster.IsEnemy(target)) return;
                var ability = evt.Reason?.Ability?.Blueprint ?? evt.Reason?.Context?.SourceAbility;
                if (ability == null) return;
                StatType statType = RuleSavingThrow.GetStatFromSavingThrow(evt.Type);
                int plain = target.Stats.GetStat(statType)?.ModifiedValue ?? evt.StatValue;
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
