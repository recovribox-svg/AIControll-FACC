// C07: exported native modes/touches, supplementary stock hidden buffs, scalar preview restoration and command flags.
// Does not initialize an actor, run a command, trigger an attack/cast, spend a resource or test scene navigation.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;

namespace ProfileAudit
{
    internal static class MagusCheck
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("MAGUS FAILED: " + why); } }
            try
            {
                var all = loaded.ToList();
                var mechanics = typeof(TacticalEngine).Assembly.GetType("CompanionAI_Pathfinder.Tactics.MagusMechanics", true);
                var kind = mechanics.GetMethod("Kind", Hidden);
                var weaponDelivery = mechanics.GetMethod("WeaponDeliversTouch", Hidden);
                foreach (var test in new[] { ("StandardLongsword", false, false, false), ("StandardLongsword", true, false, true),
                    ("StandardLongsword", true, true, false), ("Claw1d4", false, false, true), ("Bite1d6", false, false, true),
                    ("StandardShortbow", true, false, false) })
                {
                    var weapon = all.OfType<BlueprintItemWeapon>().FirstOrDefault(b => b.name == test.Item1);
                    if (weapon == null) { lines.Add("MAGUS weapon fixture outside scope: " + test.Item1); continue; }
                    Check((bool)weaponDelivery.Invoke(null, new object[] { weapon, test.Item2, test.Item3 }) == test.Item4,
                        $"actual {test.Item1}: native held-touch delivery with Spellstrike={test.Item2}, empty hand={test.Item3}");
                }
                foreach (var pair in new[] { ("SpellCombatAbility", "Combat"), ("SpellStrikeAbility", "Strike") })
                {
                    var bp = all.OfType<BlueprintActivatableAbility>().FirstOrDefault(b => b.name == pair.Item1);
                    if (bp == null) { lines.Add("MAGUS mode fixture outside scope: " + pair.Item1); continue; }
                    Check(kind.Invoke(null, new object[] { bp }).ToString() == pair.Item2, "actual native mode " + pair.Item1);
                    var clone = Clone(bp);
                    clone.ComponentsArray = clone.ComponentsArray.Concat(new[] { Bare<ActivatableAbilityResourceLogic>() }).ToArray();
                    Check(kind.Invoke(null, new object[] { clone }).ToString() == "None", "paid modified mode remains outside free native contract");
                }
                var touch = all.OfType<BlueprintAbility>().FirstOrDefault(b => b.name == "ShockingGraspCast");
                var ray = all.OfType<BlueprintAbility>().FirstOrDefault(b => b.name == "ScorchingRay");
                var plain = mechanics.GetMethod("PlainTouch", Hidden);
                if (touch != null && ray != null)
                {
                    Check((bool)plain.Invoke(null, new object[] { CasterPolicyCheck.Data(touch), AbilityProfile.Get(touch) }),
                        "actual Shocking Grasp source is a native physical touch");
                    Check(!(bool)plain.Invoke(null, new object[] { CasterPolicyCheck.Data(ray), AbilityProfile.Get(ray) }),
                        "actual Scorching Ray is not a held melee touch");
                }

                var strike = all.OfType<BlueprintBuff>().FirstOrDefault(b => b.name == "MagusSpellStrikeBuff");
                var combat = all.OfType<BlueprintBuff>().FirstOrDefault(b => b.name == "MagusSpellCombatCheckerBuff");
                if (strike != null && combat != null)
                {
                    var extra = mechanics.GetMethod("StrikeExtra", Hidden);
                    var args = new object[] { strike, 0 };
                    Check((bool)extra.Invoke(null, args) && (int)args[1] == 1, "actual hidden native Spellstrike buff adds one non-Haste attack");
                    var changed = Clone(strike);
                    changed.ComponentsArray = changed.ComponentsArray.Select(Clone).ToArray();
                    changed.ComponentsArray[0].GetType().GetField("Haste").SetValue(changed.ComponentsArray[0], true);
                    Check(!(bool)extra.Invoke(null, new object[] { changed, 0 }), "different extra-attack stacking shape is withheld");
                    var stacked = Clone(strike);
                    stacked.Stacking = StackingType.Stack;
                    Check(!(bool)extra.Invoke(null, new object[] { stacked, 0 }), "a stackable extra-attack buff is not treated as one replacement buff");
                    var read = mechanics.GetMethod("ReadCombat", Hidden);
                    var model = read.Invoke(null, new object[] { combat });
                    Check((bool)model.GetType().GetField("Known", Hidden).GetValue(model), "actual hidden conditional combat checker graph recognized");
                    var parts = (IList)model.GetType().GetField("Parts", Hidden).GetValue(model);
                    Check(parts.Count == 2, "actual combat checker has two archetype-selected typed penalties");
                    var guards = new List<bool>();
                    foreach (var part in parts)
                    {
                        var rule = part.GetType().GetField("Part", Hidden).GetValue(part);
                        Check((int)rule.GetType().GetField("AttackBonus").GetValue(rule) == -2,
                            "penalty amount is read from the native child component");
                        var conditions = (Array)part.GetType().GetField("Guards", Hidden).GetValue(part);
                        Check(conditions.Length == 1, "each native penalty has one owning-feature guard");
                        if (conditions.Length == 1) guards.Add((bool)conditions.GetValue(0).GetType().GetField("Present", Hidden).GetValue(conditions.GetValue(0)));
                    }
                    Check(guards.Contains(true) && guards.Contains(false), "native mutually exclusive feature branches remain distinct");
                    var unrelated = Clone(combat);
                    unrelated.ComponentsArray = unrelated.ComponentsArray.Concat(new[] { Bare<Kingmaker.UnitLogic.FactLogic.AddStatBonus>() }).ToArray();
                    var unknown = read.Invoke(null, new object[] { unrelated });
                    Check(!(bool)unknown.GetType().GetField("Known", Hidden).GetValue(unknown), "additional unmodeled combat effect is withheld");
                    var stackedCombat = Clone(combat);
                    stackedCombat.Stacking = StackingType.Stack;
                    var stacking = read.Invoke(null, new object[] { stackedCombat });
                    Check(!(bool)stacking.GetType().GetField("Known", Hidden).GetValue(stacking), "a stackable combat penalty is not priced as a replacement checker");
                }
                else lines.Add("MAGUS supplementary stock hidden-buff fixtures outside this scope (use the separate BubblePrints fixture run)");

                var partData = new UnitPartMagus();
                partData.SpellCombat.Unlock(); partData.Spellstrike.Unlock();
                partData.SpellCombat.Active = partData.Spellstrike.Active = false;
                var hands = Bare<HandsEquipmentSet>();
                var grip = typeof(HandsEquipmentSet).GetField("m_GripType", Hidden);
                grip.SetValue(hands, GripType.TwoHanded);
                var preview = mechanics.GetNestedType("Preview", Hidden);
                IDisposable Preview(bool c, bool s) => (IDisposable)Activator.CreateInstance(preview, Hidden, null,
                    new object[] { partData, c, s, hands }, null);
                using (Preview(true, false))
                {
                    Check(partData.SpellCombat.Active && !partData.Spellstrike.Active, "preview sets unlocked scalar features only");
                    Check(Convert.ToInt32(grip.GetValue(hands)) == 0, "preview models native one-field grip preference reset");
                    using (Preview(false, true))
                        Check(!partData.SpellCombat.Active && partData.Spellstrike.Active, "nested feature preview is isolated");
                    Check(partData.SpellCombat.Active && !partData.Spellstrike.Active, "inner preview restores outer native flags");
                }
                Check(!partData.SpellCombat.Active && !partData.Spellstrike.Active && (GripType)grip.GetValue(hands) == GripType.TwoHanded,
                    "outer preview restores both features and original grip");
                try { using (Preview(true, true)) { throw new InvalidOperationException("intentional fixture exit"); } }
                catch (InvalidOperationException) { }
                Check(!partData.SpellCombat.Active && !partData.Spellstrike.Active && (GripType)grip.GetValue(hands) == GripType.TwoHanded,
                    "exception exit restores native scalar state");

                var configure = typeof(TacticalEngine).GetMethod("ConfigureMagusFollowup", Hidden);
                foreach (bool full in new[] { false, true })
                {
                    var attack = Bare<UnitAttack>();
                    configure.Invoke(null, new object[] { attack, full, TimeSpan.FromSeconds(99) });
                    var commandType = typeof(Kingmaker.UnitLogic.Commands.Base.UnitCommand);
                    bool unbounded = (bool)commandType.GetField("m_IgnoreCooldown", Hidden).GetValue(attack);
                    var deadline = commandType.GetField("m_IgnoreCooldownBeforeTime", Hidden).GetValue(attack);
                    Check(!unbounded && deadline is TimeSpan until && until == TimeSpan.FromSeconds(99) &&
                        attack.ForceFullAttack == full && attack.IsSingleAttack == !full,
                        full ? "native Spell Combat uses explicit full flags" : "native Spellstrike-only follow-up stays a single attack");
                }
                var caster = CasterPolicyCheck.Data(touch ?? ray);
                var other = CasterPolicyCheck.Data(touch ?? ray);
                var recipient = Bare<UnitEntityData>();
                var quoteType = typeof(CombatMath).GetNestedType("MagusRollScope", Hidden);
                var current = typeof(CombatMath).GetField("s_MagusRoll", Hidden);
                var quote = new WeaponAttackEstimate { TouchHitChance = 0.75f, TouchCritChance = 0.15f };
                IDisposable Quote(AbilityData a, WeaponAttackEstimate q) => (IDisposable)Activator.CreateInstance(quoteType, Hidden, null,
                    new object[] { a, recipient, q, true }, null);
                using (Quote(caster, quote))
                {
                    Check(Math.Abs(CombatMath.SpellAttackHitChance(null, recipient, caster) - 0.75f) < 0.00001f,
                        "payload uses the quoted eligible weapon-hit probability before any native actor query");
                    var critical = typeof(CombatMath).GetMethod("AbilityCritFactor", Hidden);
                    Check(Math.Abs((float)critical.Invoke(null, new object[] { caster, null, recipient }) - 1.2f) < 0.00001f,
                        "Magus x2 spell critical uses first successful weapon hit, not the weapon's x3/x4 multiplier");
                    Check(!(bool)quoteType.GetMethod("Matches", Hidden).Invoke(current.GetValue(null), new object[] { other, recipient }),
                        "distinct AbilityData identity cannot reuse a payload scope");
                    using (Quote(other, null)) Check(CombatMath.SpellAttackHitChance(null, recipient, other) == 1f,
                        "guaranteed-hit appraisal does not inherit an outer weapon probability");
                    Check(Math.Abs(CombatMath.SpellAttackHitChance(null, recipient, caster) - 0.75f) < 0.00001f, "nested scalar scope restores outer quote");
                }
                Check(current.GetValue(null) == null, "no scalar payload quote survives its scope");

                void Calls(Type owner, string method, MethodBase target, string why)
                {
                    var caller = owner.GetMethod(method, Hidden);
                    Check(SaveBoostCheck.ReferencedMembers(caller).Any(m => m.Module == target.Module && m.MetadataToken == target.MetadataToken), why);
                }
                Calls(typeof(TacticalEngine), "Execute", typeof(UnitUseAbility).GetMethod("CreateCastCommand", new[] { typeof(AbilityData), typeof(TargetWrapper) }),
                    "production issue path uses the game's native command factory");
                Calls(typeof(TacticalEngine), "ValidateMagusIssue", typeof(UnitEntityData).GetMethod("PreparedSpellCombat"), "final issue validates native combat opportunity");
                Calls(typeof(TacticalEngine), "ValidateMagusIssue", typeof(UnitEntityData).GetMethod("PreparedSpellStrike"), "final issue validates native strike opportunity");
                Calls(typeof(TacticalEngine), "MagusFullCast", typeof(UnitPartMagus).GetProperty("CanUseSpellCombat").GetMethod,
                    "a new cast uses native equipment eligibility rather than requiring an old RT opportunity clock");
                Calls(typeof(CombatMath), "MagusProjectedAttacks", typeof(Kingmaker.RuleSystem.Rules.RuleCalculateAttacksCount).GetMethod("AddExtraAttacks"),
                    "extra-attack forecast uses native calculation-result operation");
                Calls(typeof(TacticalEngine), "MagusStateChanged", typeof(CombatMath).GetMethod("InfusionStateChanged", Hidden),
                    "scalar feature/grip previews invalidate existing context/condition/SR caches");
            }
            catch (Exception ex) { bad++; lines.Add("MAGUS FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"MAGUS {checks} checks, failures {bad}; native metadata, scalar lifetimes, explicit flags and compiled paths; actors/casts/commands NOT RUN");
            return lines;
        }
        private static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        private static T Clone<T>(T original)
        {
            var clone = RuntimeHelpers.GetUninitializedObject(original.GetType());
            for (var type = original.GetType(); type != null; type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    field.SetValue(clone, field.GetValue(original));
            return (T)clone;
        }
    }
}
