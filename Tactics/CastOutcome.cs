// ★ v0.3.15: did the game actually do something when the AI used an ability?
//
// In the 0.3.14 turn-based log (e62751a4, last fight) six Fire Blasts, a Cure Light Wounds scroll and four Cure Moderate
// Wounds potions all ended as "Success", yet the combat log shows nothing for them: no attack roll, no damage, no heal.
// The AI kept choosing them every turn. Some causes are found and fixed (an internal kinetic-blade "Fire Blast", see
// AbilityDiscovery); for anything else this is the safety net, in both combat modes:
//   - every ability the engine issues is followed until the game's execution of it ends (IAbilityExecutionProcessHandler);
//   - a touch spell (Cure X Wounds, Inflict X Wounds, potions of them) only counts once its touch has been delivered;
//   - "something happened" = any rule the game rolled for that caster meanwhile: an attack roll, damage, a heal, a saving
//     throw, a spell-resistance check, a buff applied, or a cast the game itself refused (spell failure, UMD) - those
//     are reported in the combat log and are not silent;
//   - an ability whose effect the mod can verify (damage, heal, debuff, control) that produced none of these is a silent
//     failure: logged, and after two in a fight by the same caster the ability is not chosen again until the fight ends.
// Abilities with effects the mod cannot see (summons, areas, teleports, utility) are never judged.
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;

namespace CompanionAI_Pathfinder.Tactics
{
    public sealed class CastOutcome :
        IAbilityExecutionProcessHandler,
        IUnitBuffHandler,
        IGlobalRulebookHandler<RuleCastSpell>,
        IGlobalRulebookHandler<RuleAttackRoll>,
        IGlobalRulebookHandler<RuleDealDamage>,
        IGlobalRulebookHandler<RuleHealDamage>,
        IGlobalRulebookHandler<RuleSavingThrow>,
        IGlobalRulebookHandler<RuleSpellResistanceCheck>
    {
        private sealed class Pending
        {
            public UnitEntityData Caster;
            public UnitEntityData Target;
            public string Name;
            public string Key;
            public BlueprintAbility Blueprint;
            /// <summary>Touch spells: the delivery ability (the held charge) whose execution carries the effect.</summary>
            public BlueprintAbility TouchDelivery;
            public UnitCommand Command;
            public double IssuedAt;
            public bool Evidence;
            public bool ChargeStageDone;
            /// <summary>★ v0.3.17: judged as a silent failure (damage, heal, harmful buff); others are only watched.</summary>
            public bool Verifiable;
            /// <summary>★ v0.3.17: an enemy the ability was aimed at; whether any rule touched it is logged (diagnostics).</summary>
            public bool WatchTarget;
            /// <summary>★ v0.3.17: reaching nothing on the target counts as a failure against that target (not for abilities
            /// whose effect comes from their own logic later, such as Charge's attack).</summary>
            public bool BlockOnNoReach;
            /// <summary>★ v0.3.17 (test4): an area: reaching others is a success (it may be centred on an immune enemy on
            /// purpose, to catch more of the others); only reaching no one counts against the aim.</summary>
            public bool Area;
            /// <summary>★ v0.3.17 (test5): the buffs the ability is meant to apply; only those on the target count as it
            /// reaching the target (a hidden marker the game adds does not). Null: any buff counts.</summary>
            public HashSet<BlueprintBuff> Intended;
            public AbilityData Ability;
            public bool TargetReached;
            public HashSet<string> Reached;
        }

        /// <summary>Game seconds after the command ended without the execution ending: the game dropped the cast.</summary>
        private const double SettleAfterSeconds = 6d;
        public const int FailuresBeforeBlock = 2;

        private static CastOutcome _instance;
        private static readonly Dictionary<string, Pending> ByCaster = new Dictionary<string, Pending>();
        /// <summary>"caster|ability|target" → silent failures this fight (target "-" for the caster itself or a point).</summary>
        private static readonly Dictionary<string, int> Failures = new Dictionary<string, int>();

        public static void Subscribe()
        {
            if (_instance != null) return;
            _instance = new CastOutcome();
            EventBus.Subscribe(_instance);
        }

        public static void Unsubscribe()
        {
            if (_instance == null) return;
            EventBus.Unsubscribe(_instance);
            _instance = null;
            Clear();
        }

        /// <summary>The fight ended: what failed in it may work in the next one.</summary>
        public static void Clear()
        {
            ByCaster.Clear();
            Failures.Clear();
            StuckCharges.Clear();
        }

        private static double Now => Game.Instance?.TimeController?.GameTime.TotalSeconds ?? 0d;

        /// <summary>
        /// Per target: an enemy immune to Color Spray (no save rolled, no buff) must not block Color Spray against others.
        /// </summary>
        private static string FailureKey(UnitEntityData caster, BlueprintAbility blueprint, UnitEntityData target) =>
            $"{caster?.UniqueId}|{blueprint?.AssetGuid}|{(target == null || target == caster ? "-" : target.UniqueId)}";

        /// <summary>The touch-delivery ability of a touch spell (what a held charge is), or null.</summary>
        private static BlueprintAbility TouchDeliveryOf(BlueprintAbility blueprint)
        {
            try { return blueprint?.StickyTouch?.TouchDeliveryAbility; }
            catch { return null; }
        }

        /// <summary>
        /// Follow an ability the engine just issued. <paramref name="verifiable"/>: its profile names an effect the game
        /// would report (damage, heal, buff, control); others are not judged.
        /// </summary>
        public static void Begin(UnitEntityData caster, AbilityData ability, UnitEntityData target, UnitCommand command, bool verifiable,
                                 bool watchTarget = false, bool blockOnNoReach = false, bool area = false,
                                 IEnumerable<BlueprintBuff> intendedBuffs = null)
        {
            if (caster == null || ability?.Blueprint == null) return;
            // ★ v0.3.17: an ability aimed at an enemy is also watched when its effect is not judged (a Sleep burst), only
            // to log whether the game did anything to that enemy.
            watchTarget = watchTarget && target != null && target != caster && caster.IsEnemy(target);
            if (!verifiable && !watchTarget)
            {
                ByCaster.Remove(caster.UniqueId);
                return;
            }
            ByCaster[caster.UniqueId] = new Pending
            {
                Caster = caster,
                Target = target,
                Name = ability.Name,
                Key = FailureKey(caster, ability.Blueprint, target),
                Blueprint = ability.Blueprint,
                TouchDelivery = TouchDeliveryOf(ability.Blueprint),
                Command = command,
                IssuedAt = Now,
                Verifiable = verifiable,
                WatchTarget = watchTarget,
                BlockOnNoReach = watchTarget && blockOnNoReach,
                Area = area,
                Intended = intendedBuffs != null && intendedBuffs.Any() ? new HashSet<BlueprintBuff>(intendedBuffs) : null,
                Ability = ability
            };
        }

        /// <summary>The unit was given another kind of order: the last ability is no longer followed.</summary>
        public static void Forget(UnitEntityData caster)
        {
            if (caster != null) ByCaster.Remove(caster.UniqueId);
        }

        /// <summary>
        /// The ability is not chosen this fight on that target: it did nothing twice there for this caster (a touch spell
        /// also when its charge did nothing).
        /// </summary>
        public static bool IsBlocked(UnitEntityData caster, AbilityData ability, UnitEntityData target)
        {
            if (caster == null || ability?.Blueprint == null || Failures.Count == 0) return false;
            if (Failures.TryGetValue(FailureKey(caster, ability.Blueprint, target), out int n) && n >= FailuresBeforeBlock) return true;
            var delivery = TouchDeliveryOf(ability.Blueprint);
            return delivery != null && Failures.TryGetValue(FailureKey(caster, delivery, target), out n) && n >= FailuresBeforeBlock;
        }

        /// <summary>The touch spell whose charge the unit is holding now (UnitPartTouch), or null.</summary>
        public static BlueprintAbility HeldChargeOf(UnitEntityData unit) => unit == null ? null : HeldCharge(unit);

        /// <summary>
        /// ★ v0.3.15: casting <paramref name="ability"/> would throw away the charge the caster already holds for the same
        /// touch spell (a new cast replaces it: UnitPartTouch.Init). The held charge is the free option.
        /// </summary>
        public static bool WouldReplaceHeldCharge(UnitEntityData caster, AbilityData ability, UnitEntityData target)
        {
            if (ability?.Blueprint == null || ability.StickyTouch != null) return false;
            var delivery = TouchDeliveryOf(ability.Blueprint);
            if (delivery == null || delivery != HeldCharge(caster)) return false;
            // Only when the charge can reach that target itself; otherwise the new cast is the only way.
            try
            {
                var held = caster.Get<UnitPartTouch>()?.Ability?.Data;
                return held != null && (target == null || held.CanTarget(new TargetWrapper(target)));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Called before each decision of <paramref name="caster"/>: judges a cast whose execution never reported an end
        /// (the command ended long ago), and a touch spell whose charge is still held although its target was the caster
        /// (a potion's touch is delivered at once).
        /// </summary>
        public static void Settle(UnitEntityData caster)
        {
            if (caster == null) return;
            if (StuckCharges.Remove(caster.UniqueId)) ClearStuckCharge(caster);
            if (!ByCaster.TryGetValue(caster.UniqueId, out var p)) return;
            bool commandOver = p.Command == null || p.Command.IsFinished;
            if (!commandOver) return;
            if (p.Evidence)
            {
                Finish(p, true, null);
                return;
            }
            if (p.TouchDelivery != null && p.ChargeStageDone)
            {
                var held = HeldCharge(caster);
                if (held == p.TouchDelivery && (p.Target == null || p.Target == caster))
                {
                    Main.Log("[Cast] diagnostics: " + TouchState(caster));
                    Finish(p, false, "its touch was never delivered (the charge is still held)");
                    return;
                }
                // A charge held for someone else is delivered by a later command (the engine delivers held charges first);
                // still held two rounds later, it was not.
                if (held == p.TouchDelivery)
                {
                    if (Now - p.IssuedAt > 2 * SettleAfterSeconds)
                        Finish(p, false, "its touch was never delivered to " + (p.Target?.CharacterName ?? "the target"));
                    return;
                }
            }
            if (Now - p.IssuedAt > SettleAfterSeconds)
                Finish(p, false, "the game never finished executing it");
        }

        private static BlueprintAbility HeldCharge(UnitEntityData unit)
        {
            try { return unit.Get<UnitPartTouch>()?.Ability?.Blueprint; }
            catch { return null; }
        }

        /// <summary>
        /// ★ v0.3.15: the held touch charge as the game sees it, for the log: the root cause of potions and cure spells
        /// that heal nothing is not known yet, and this is what tells it apart (a charge from an earlier fight or a
        /// loaded save, one the game refuses to deliver to its holder and why, a missing fact).
        /// </summary>
        private static string TouchState(UnitEntityData unit)
        {
            try
            {
                var part = unit?.Get<UnitPartTouch>();
                if (part == null) return $"{unit?.CharacterName}: no held touch charge";
                var ability = part.Ability;
                var data = ability?.Data;
                string self = "?";
                try { self = data == null ? "no data" : data.CanTarget(new TargetWrapper(unit)) ? "yes" : "no: " + data.CantTargetReason(new TargetWrapper(unit)); }
                catch (Exception ex) { self = "unreadable: " + ex.Message; }
                double age = (Game.Instance.TimeController.GameTime - part.AppearTime).TotalSeconds;
                bool inFacts = ability != null && unit.Facts.Contains(ability);
                return $"{unit.CharacterName} holds {(ability == null ? "no ability (empty charge)" : ability.Blueprint?.name)}, " +
                       $"from {data?.SourceItemForTouch?.Name ?? "a spell"}, {age:0.#} s old, cast this round={part.IsCastedInThisRound}, " +
                       $"fact on the unit={inFacts}, active={ability?.Active}, auto-cast order queued={part.AutoCastCommand != null && unit.Commands.Contains(part.AutoCastCommand)}, " +
                       $"can touch self={self}";
            }
            catch (Exception ex)
            {
                return $"{unit?.CharacterName}: touch state unreadable - {ex.Message}";
            }
        }

        /// <summary>
        /// ★ v0.3.15: party members already holding a touch charge when a fight starts. The game removes charges when a
        /// unit leaves combat, so one here came from a loaded save or survived the last fight.
        /// </summary>
        public static void ReportHeldCharges()
        {
            try
            {
                foreach (var unit in Game.Instance.Player.Party)
                    if (unit?.Get<UnitPartTouch>() != null)
                        Main.Log("[Cast] at fight start: " + TouchState(unit));
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Cast] held charges: {ex.Message}");
            }
        }

        /// <summary>Units whose held charge could not be delivered to themselves: cleared before their next decision.</summary>
        private static readonly HashSet<string> StuckCharges = new HashSet<string>();

        /// <summary>
        /// ★ v0.3.15: a touch charge that the game cannot deliver to its holder. In the 0.3.14 real-time log (9c5c6df2)
        /// Camellia's Cure Moderate Wounds potion healed nothing and left the potion's touch charge behind; touching
        /// herself with it did nothing either and the charge stayed, and Seelah drank eight potions in a row the same way.
        /// The charge is part of the save (UnitPartTouch), so the state followed the party from turn-based into real
        /// time. Removed the way the game's own cleanup does it (RemoveUnitPartTouchOnAllCompanions, and leaving combat):
        /// the next potion or spell then starts from a fresh charge.
        /// </summary>
        private static void ClearStuckCharge(UnitEntityData unit)
        {
            try
            {
                var part = unit.Get<UnitPartTouch>();
                if (part == null) return;
                string name = part.Ability?.Name ?? "touch spell";
                var data = part.Ability?.Data;
                unit.Remove<UnitPartTouch>();
                if (data != null && !unit.Brain.IsAutoUseAbility(data)) unit.CombatState.ManualTarget = null;
                Main.Log($"[Cast] {unit.CharacterName}: removed the held {name} charge the game could not deliver to them");
            }
            catch (Exception ex)
            {
                Main.Log($"[Cast] {unit.CharacterName}: held touch charge could not be removed - {ex.Message}");
            }
        }

        private static void Finish(Pending p, bool success, string why)
        {
            ByCaster.Remove(p.Caster.UniqueId);
            bool missedTarget = p.WatchTarget && !p.TargetReached;
            if (missedTarget && p.Area && p.Reached != null && p.Reached.Count > 0)
            {
                // An area centred on an enemy it does not affect, for the others in it: that worked.
                Main.Log($"[Cast] {p.Caster.CharacterName}: {p.Name} centred on {p.Target?.CharacterName}, reached {string.Join(", ", p.Reached)}");
                missedTarget = false;
            }
            if (missedTarget)
            {
                // ★ v0.3.17: nothing the game rolled touched the enemy it was aimed at - a condition the profile cannot
                // read (a creature-type or level limit), an immunity it does not see. Twice: not aimed at that enemy again
                // this fight (IsBlocked is per target). Orvath cast Sleep on a Corrupted Mongrel Brute over its Hit Dice
                // limit five times (log 06aebf2d).
                int count = 0;
                if (p.BlockOnNoReach)
                {
                    Failures.TryGetValue(p.Key, out count);
                    Failures[p.Key] = ++count;
                }
                int level = 0;
                try { level = p.Target?.Descriptor?.Progression?.CharacterLevel ?? 0; } catch { }
                Main.Log($"[Cast] {p.Caster.CharacterName}: {p.Name} did nothing to its target {p.Target?.CharacterName} " +
                         $"(level {level}) - no attack roll, save, spell resistance, damage or effect on it" +
                         (p.Reached != null && p.Reached.Count > 0 ? $" (it reached {string.Join(", ", p.Reached)})" : "") +
                         (p.BlockOnNoReach ? $" ({count}/{FailuresBeforeBlock}{(count >= FailuresBeforeBlock ? "; not aimed at it again this fight" : "")})" : ""));
                string note = p.Ability != null && p.Target != null ? AbilityProfile.Get(p.Ability)?.HitDiceNote(p.Ability, p.Target) : null;
                if (note != null) Main.Log($"[Cast] {p.Name} on {p.Target.CharacterName}: {note}");
                if (p.BlockOnNoReach) return;   // counted once for this cast
            }
            // Only effects the mod can verify count as silent failures; the rest was watched for the log alone.
            if (success || !p.Verifiable) return;
            // A held charge's failure counts for the touch spell as a whole: the next scroll or potion of it fails alike.
            string key = p.TouchDelivery != null ? FailureKey(p.Caster, p.TouchDelivery, p.Target) : p.Key;
            Failures.TryGetValue(key, out int n);
            Failures[key] = ++n;
            Main.Log($"[Cast] {p.Caster.CharacterName}: {p.Name} did nothing in the game - {why} ({n}/{FailuresBeforeBlock})" +
                     (n >= FailuresBeforeBlock ? "; not chosen again this fight" : ""));
            // Its own charge is left on a caster who meant to touch themselves (a potion, a self-cast cure).
            if (p.Target == null || p.Target == p.Caster)
            {
                var held = HeldCharge(p.Caster);
                if (held != null && (held == p.Blueprint || held == p.TouchDelivery)) StuckCharges.Add(p.Caster.UniqueId);
            }
        }

        private static Pending For(UnitEntityData unit)
        {
            if (unit == null || ByCaster.Count == 0) return null;
            return ByCaster.TryGetValue(unit.UniqueId, out var p) ? p : null;
        }

        private static void Mark(UnitEntityData caster)
        {
            var p = For(caster);
            if (p != null) p.Evidence = true;
        }

        /// <summary>★ v0.3.17: a rule of <paramref name="caster"/>'s ability touched <paramref name="unit"/> (target diagnostics).</summary>
        private static void MarkReached(UnitEntityData caster, UnitEntityData unit, MechanicsContext source = null)
        {
            var p = For(caster);
            if (p == null || !p.WatchTarget || unit == null) return;
            // ★ v0.3.17 (test3): an effect of another lasting area of the same caster is not this cast reaching the
            // target. The Cultist Champion stood in Orvath's Grease; its saves against the Grease counted as Sleep
            // reaching it, so seven Sleeps that never touched it were never caught (log 9a7bb1f9).
            if (FromOtherArea(source, p)) return;
            if (unit == p.Target) p.TargetReached = true;
            else if (unit != caster) (p.Reached ?? (p.Reached = new HashSet<string>())).Add(unit.CharacterName);
        }

        /// <summary>The rule comes from a lasting area that this cast did not create.</summary>
        private static bool FromOtherArea(MechanicsContext context, Pending p)
        {
            try
            {
                for (var c = context; c != null; c = c.ParentContext)
                    if (c.AssociatedBlueprint is BlueprintAbilityAreaEffect)
                        return c.SourceAbility != p.Blueprint;
            }
            catch { }
            return false;
        }

        public void HandleExecutionProcessStart(AbilityExecutionContext context)
        {
        }

        public void HandleExecutionProcessEnd(AbilityExecutionContext context)
        {
            try
            {
                var p = For(context?.MaybeCaster);
                if (p == null) return;
                var bp = context.AbilityBlueprint;
                if (p.TouchDelivery != null)
                {
                    if (bp == p.Blueprint) p.ChargeStageDone = true;
                    else if (bp == p.TouchDelivery) Finish(p, p.Evidence, "its touch was delivered without any effect");
                    return;
                }
                if (bp == p.Blueprint || context.Ability?.ConvertedFrom?.Blueprint == p.Blueprint)
                    Finish(p, p.Evidence, "it ran without an attack roll, damage, heal, save or buff");
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Cast] observer: {ex.Message}");
            }
        }

        public void HandleBuffDidAdded(Buff buff)
        {
            try
            {
                Mark(buff?.Context?.MaybeCaster);
                var pending = For(buff?.Context?.MaybeCaster);
                // ★ v0.3.17 (test5): only the effect the ability is meant to apply counts as reaching the target.
                if (pending?.Intended == null || (buff?.Blueprint != null && pending.Intended.Contains(buff.Blueprint)))
                    MarkReached(buff?.Context?.MaybeCaster, buff?.Owner?.Unit, buff?.Context);
            }
            catch { }
        }

        public void HandleBuffDidRemoved(Buff buff)
        {
        }

        public void OnEventAboutToTrigger(RuleCastSpell evt)
        {
        }

        public void OnEventDidTrigger(RuleCastSpell evt)
        {
            if (evt == null || evt.Success) return;
            try
            {
                // Refused by a rule (spell failure, UMD, counterspell): the combat log says so; not a silent failure.
                if (evt.IsSpellFailed || evt.IsArcaneSpellFailed || evt.IsUMDFailed || evt.ForceFail)
                {
                    // ★ v0.3.17: a failed Use Magic Device check is in the combat log only as a skill roll; the wand or
                    // scroll seemed to do nothing (Orvath's wand of Cure Light Wounds). The game keeps the charge then.
                    if (evt.IsUMDFailed && evt.UseMagicDeviceCheck != null)
                        Main.Log($"[Cast] {evt.Initiator?.CharacterName}: Use Magic Device failed for {evt.Spell?.Name} - " +
                                 $"rolled {evt.UseMagicDeviceCheck.RollResult} vs DC {evt.UseMagicDeviceCheck.DC} (the item keeps its charge)");
                    Mark(evt.Initiator);
                    var refused = For(evt.Initiator);
                    if (refused != null) refused.WatchTarget = false;   // nothing reached the target, and the log said why
                    return;
                }
                // Refused without a word: RuleCastSpell returns before casting when the spell "cannot target" its target
                // (its error report is not in the log). This is how a potion's touch on its drinker failed.
                var p = For(evt.Initiator);
                var bp = evt.Spell?.Blueprint;
                if (p != null && bp != null && (bp == p.Blueprint || bp == p.TouchDelivery))
                {
                    // The game's own explanation (the text its suppressed error report would carry).
                    string reason = null;
                    try { reason = evt.SpellTarget != null ? evt.Spell.CantTargetReason(evt.SpellTarget) : null; } catch { }
                    Finish(p, false, "the game refused it without a message (" +
                                     (evt.SpellTarget?.Unit?.CharacterName ?? "that target") + ": " +
                                     (string.IsNullOrEmpty(reason) ? "no target reason given" : reason) + ")");
                    Main.Log("[Cast] diagnostics: " + TouchState(evt.Initiator));
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Cast] observer: {ex.Message}");
            }
        }

        public void OnEventAboutToTrigger(RuleAttackRoll evt)
        {
        }

        public void OnEventDidTrigger(RuleAttackRoll evt)
        {
            if (evt != null && !evt.IsFake)
            {
                Mark(evt.Initiator);
                MarkReached(evt.Initiator, evt.Target, evt.Reason?.Context);
            }
        }

        public void OnEventAboutToTrigger(RuleDealDamage evt)
        {
        }

        public void OnEventDidTrigger(RuleDealDamage evt)
        {
            if (evt != null && !evt.IsFake)
            {
                Mark(evt.Initiator);
                MarkReached(evt.Initiator, evt.Target, evt.Reason?.Context);
            }
        }

        public void OnEventAboutToTrigger(RuleHealDamage evt)
        {
        }

        public void OnEventDidTrigger(RuleHealDamage evt)
        {
            if (evt != null)
            {
                Mark(evt.Initiator);
                MarkReached(evt.Initiator, evt.Target, evt.Reason?.Context);
            }
        }

        public void OnEventAboutToTrigger(RuleSavingThrow evt)
        {
        }

        public void OnEventDidTrigger(RuleSavingThrow evt)
        {
            if (evt != null)
            {
                Mark(evt.Reason?.Caster);
                MarkReached(evt.Reason?.Caster, evt.Initiator, evt.Reason?.Context);
            }
        }

        public void OnEventAboutToTrigger(RuleSpellResistanceCheck evt)
        {
        }

        public void OnEventDidTrigger(RuleSpellResistanceCheck evt)
        {
            if (evt != null)
            {
                Mark(evt.Initiator);
                // ★ v0.3.17 (test5): the game checks spell resistance for every target of the spell before anything else
                // (AbilityExecutionProcess.DoApplyEffect), also targets without spell resistance and ones the effect
                // then skips (Sleep's Hit Dice limit). Only a resisted spell is an outcome on the target; Sleep on the
                // Cultist Champion counted as "reached" through this check, every cast.
                if (evt.IsSpellResisted) MarkReached(evt.Initiator, evt.Target, evt.Context);
            }
        }
    }
}
