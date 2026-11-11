// Actual native MagicHackData and exported payload/delivery blueprints. No actor setup, Rulebook execution or casts.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ProfileAudit
{
    internal static class FusionCheck
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FUSION FAILED: " + why); } }
            try
            {
                var all = loaded.ToList(); var spells = all.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => spells.FirstOrDefault(a => a.name == name);
                var holder = Spell("MagicHackDefaultSlot1");
                if (holder == null) { failures = 0; return new List<string> { "FUSION SKIPPED: native MagicHack slot outside this scope" }; }
                var fire = Spell("Fireball"); var lightning = Spell("LightningBolt"); var slow = Spell("Slow"); var confusion = Spell("ConfusionSpell") ?? Spell("Confusion");
                Check(fire != null && lightning != null && slow != null && confusion != null, "actual four payload fixtures exist");
                if (fire == null || lightning == null || slow == null || confusion == null) throw new InvalidOperationException("required payload fixture missing");
                AbilityData Fuse(BlueprintAbility first, BlueprintAbility second, BlueprintAbility delivery, SavingThrowType save, int level = 3)
                {
                    var a = CasterPolicyCheck.Data(holder);
                    a.MagicHackData = new MagicHackData { Spell1 = first, Spell2 = second, DeliverBlueprint = delivery,
                        SavingThrowType = save, SpellLevel = level, SpellSchool = SpellSchool.Evocation };
                    return a;
                }
                string Unsupported(AbilityProfile p) => (string)typeof(AbilityProfile).GetProperty("FusionUnsupported", Hidden).GetValue(p);
                var fused = Fuse(fire, lightning, fire, SavingThrowType.Reflex);
                var profile = AbilityProfile.Get(fused);
                Check(Unsupported(profile) == null, "Fireball/Lightning Bolt direct native payload supported: " + Unsupported(profile));
                Check(profile.Damage.Count == AbilityProfile.Get(fire).Damage.Count + AbilityProfile.Get(lightning).Damage.Count, "both ordered damage payloads retained");
                Check(profile.Blueprint == fire && profile.IsAoE && profile.SelectsTargets && !profile.NeedsAttackRoll, "selected burst geometry, no invented attack roll");
                Check(!holder.CanTargetEnemies && profile.Blueprint.CanTargetEnemies, "empty slot flags are replaced by actual native delivery flags");
                Check(profile.Save == SavingThrowType.Reflex, "chosen shared save retained");
                Check(ReferenceEquals(profile, AbilityProfile.Get(fused)), "unchanged native fusion reuses its profile");
                var twice = AbilityProfile.Get(Fuse(fire, fire, fire, SavingThrowType.Reflex));
                Check(twice.Damage.Count == 2 * AbilityProfile.Get(fire).Damage.Count, "two applications of one source retain both native payloads");
                string before = AbilityDiscovery.Key(fused), ledger = CombatMemory.AbilityKey(fused);
                fused.MagicHackData.SavingThrowType = SavingThrowType.Will;
                var changed = AbilityProfile.Get(fused);
                Check(!ReferenceEquals(profile, changed) && changed.Save == SavingThrowType.Will && profile.Save == SavingThrowType.Reflex,
                    "editing a save invalidates metadata without mutating the earlier snapshot");
                Check(before != AbilityDiscovery.Key(fused) && ledger != CombatMemory.AbilityKey(fused), "edited fusion does not inherit old discovery/failure/cast keys");
                fused.MagicHackData.DeliverBlueprint = lightning;
                var line = AbilityProfile.Get(fused);
                Check(!ReferenceEquals(line, changed) && line.Blueprint == lightning && Unsupported(line) == "line delivery geometry",
                    "delivery change invalidates metadata; unsupported line footprint cannot bypass friendly-fire safety");
                var cc = AbilityProfile.Get(Fuse(slow, confusion, slow, SavingThrowType.Will));
                Check(Unsupported(cc) == null, "Slow/Confusion control payload supported: " + Unsupported(cc));
                Check(cc.TargetBuffs.Count >= 2 && cc.ControlStrength != ControlStrength.None && cc.Save == SavingThrowType.Will, "both real control effects retained with one selected save");
                var mixed = AbilityProfile.Get(Fuse(fire, slow, fire, SavingThrowType.Will));
                Check(Unsupported(mixed) == null && mixed.Damage.Count > 0 && mixed.TargetBuffs.Count > 0 && mixed.Save == SavingThrowType.Will,
                    "damage/control mixture uses selected delivery and selected save");
                var shocking = Spell("ShockingGraspCast");
                var touchPayload = AbilityProfile.Get(Fuse(shocking, fire, fire, SavingThrowType.Reflex));
                Check(Unsupported(touchPayload) == null && touchPayload.Damage.Count == 2 && !touchPayload.NeedsAttackRoll,
                    "source touch payload uses native sticky fallback; selected burst requires no invented touch roll");

                var factor = typeof(CombatMath).GetMethod("FusionDamageFactor", Hidden);
                var damage = profile.Damage.OfType<ContextActionDealDamage>().First();
                float Scale(AbilityProfile p, Kingmaker.ElementsSystem.GameAction a, float fail, bool evade = false, bool improved = false) =>
                    (float)factor.Invoke(null, new object[] { p, a, fail, evade, improved });
                Check(damage.HalfIfSaved, "actual Fireball damage uses native HalfIfSaved");
                Check(Math.Abs(Scale(profile, damage, 0.2f) - 0.6f) < 0.00001f, "single shared save: full on failure, half on success");
                Check(Math.Abs(Scale(profile, damage, 0.2f, true) - 0.2f) < 0.00001f, "Evasion removes passed-save half");
                Check(Math.Abs(Scale(profile, damage, 0.2f, true, true) - 0.1f) < 0.00001f, "Improved Evasion halves failed-save damage");
                Check(Scale(touchPayload, touchPayload.Damage.OfType<ContextActionDealDamage>().First(d => !d.HalfIfSaved), 0.2f) == 1f,
                    "source touch damage without HalfIfSaved remains unconditional on the shared save");
                var enervation = Spell("Enervation");
                var drainProfile = AbilityProfile.Get(Fuse(enervation, fire, fire, SavingThrowType.Reflex, 4));
                var drain = drainProfile.Damage.OfType<ContextActionDealDamage>().First(d => !d.HalfIfSaved);
                Check(Scale(drainProfile, drain, 0.05f) == 1f, "unconditional native negative levels gain no invented save gate");
                var disintegration = Spell("Disintegrate");
                var split = AbilityProfile.Get(Fuse(disintegration, fire, fire, SavingThrowType.Fortitude, 6));
                var nativePath = typeof(AbilityProfile).GetMethod("FusionPath", Hidden);
                var failed = split.Damage.First(a => nativePath.Invoke(split, new object[] { a }).ToString() == "Failed");
                var passed = split.Damage.First(a => nativePath.Invoke(split, new object[] { a }).ToString() == "Passed");
                Check(Math.Abs(Scale(split, failed, 0.2f) - 0.2f) < 0.00001f && Math.Abs(Scale(split, passed, 0.2f) - 0.8f) < 0.00001f,
                    "actual Disintegrate's different damage branches are weighted on the same save (its extra buff remains outside supported casts)");

                var scope = typeof(CombatMath).GetMethod("MemoizeResistance", Hidden);
                var remember = typeof(CombatMath).GetMethod("RememberFusionDamageChance", Hidden);
                var chance = typeof(CombatMath).GetMethod("FusionDamageChance", Hidden);
                var invalidate = typeof(CombatMath).GetMethod("InvalidateResistanceMemo", Hidden);
                var recipient = (Kingmaker.EntitySystem.Entities.UnitEntityData)RuntimeHelpers.GetUninitializedObject(typeof(Kingmaker.EntitySystem.Entities.UnitEntityData));
                using ((IDisposable)scope.Invoke(null, null))
                {
                    var positive = new List<KeyValuePair<Kingmaker.ElementsSystem.GameAction, float>> { new KeyValuePair<Kingmaker.ElementsSystem.GameAction, float>(damage, 12f) };
                    remember.Invoke(null, new object[] { fused, recipient, profile, positive, 0.2f, true, false });
                    Check(Math.Abs((float)chance.Invoke(null, new object[] { fused, recipient, profile }) - 0.2f) < 0.00001f,
                        "Conservative viability receives native failed-save probability against Evasion without re-discounting damage");
                    using ((IDisposable)scope.Invoke(null, null))
                        Check((float)chance.Invoke(null, new object[] { fused, recipient, profile }) == 1f, "nested forecast cannot reuse outer fusion chance");
                    Check(Math.Abs((float)chance.Invoke(null, new object[] { fused, recipient, profile }) - 0.2f) < 0.00001f, "outer chance restored after nested forecast");
                    invalidate.Invoke(null, null);
                    Check((float)chance.Invoke(null, new object[] { fused, recipient, profile }) == 1f, "state preview invalidates fusion chance memo");
                }
                Check((float)chance.Invoke(null, new object[] { fused, recipient, profile }) == 1f, "no cached fusion chance outlives the evaluation");

                var bookBlueprint = all.OfType<BlueprintSpellbook>().First(b => b.name == "MagicDeceiverSpellbook");
                var book = (Spellbook)RuntimeHelpers.GetUninitializedObject(typeof(Spellbook));
                typeof(Spellbook).GetField("Blueprint").SetValue(book, bookBlueprint);
                typeof(AbilityData).GetField("m_CachedSpellbook", Hidden).SetValue(fused, book);
                var evaluate = typeof(CombatMath).GetMethod("EvaluateDamageDice", Hidden);
                var dice = new ContextDiceValue { DiceType = DiceType.D6,
                    DiceCountValue = new ContextValue { ValueType = ContextValueType.Simple, Value = 137 },
                    BonusValue = new ContextValue { ValueType = ContextValueType.Simple, Value = 2 } };
                var limits = bookBlueprint.GetComponent<MagicHackSpellbookComponent>();
                for (int level = 1; level <= 6; level++)
                {
                    fused.MagicHackData.SpellLevel = level;
                    typeof(AbilityData).GetField("m_OverrideSpellLevel", Hidden).SetValue(fused, (int?)level);
                    float result = (float)evaluate.Invoke(null, new object[] { fused, dice, null, 20, false, false });
                    Check(Math.Abs(result - (Math.Min(137, limits.GetMaxDamageDicesForAction(level)) * 3.5f + 2)) < 0.00001f,
                        "level " + level + " uses actual spellbook's per-action dice cap, keeps flat bonus");
                }
                var unknown = Fuse(fire, lightning, fire, SavingThrowType.Reflex);
                unknown.MagicHackData.AdditionalAoeBlueprint = lightning;
                Check(Unsupported(AbilityProfile.Get(unknown)) == "additional area delivery", "unknown extra delivery remains explicit fallback");
                var partial = CasterPolicyCheck.Data(holder); partial.MagicHackData = new MagicHackData();
                Check(Unsupported(AbilityProfile.Get(partial)) != null, "incomplete native data is withheld safely");
            }
            catch (Exception ex) { bad++; lines.Add("FUSION FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"FUSION {checks} checks, failures {bad}; real payloads, native save/dice fields and metadata mutation; no actor setup, Rulebook or command execution");
            return lines;
        }
    }
}
