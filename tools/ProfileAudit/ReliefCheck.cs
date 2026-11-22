// ★ coverage5 (author request, docs/RELIEF_SPELLS.md): relief for hindered allies - dispels, removals and suppressions.
// Native contracts, the actual Remove Fear / Unbreakable Heart / Break Enchantment / Dispel Magic / Confusion graphs and the
// compiled routes. Inert native buff/context fields only: no actor, Game/Player, context constructor, Rulebook,
// dispel, cast or random roll.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Mechanics;
using System.Runtime.CompilerServices;

namespace ProfileAudit
{
    internal static class ReliefCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static List<MemberInfo> Body(Type type, string name) => BuffSuppressCheck.BodyWithLambdas(type, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("RELIEF FAILED: " + why); } }
            try
            {
                var engine = typeof(TacticalEngine);
                // --- native contracts ---
                var trigger = SaveBoostCheck.ReferencedMembers(typeof(RuleDispelMagic).GetMethod("OnTrigger", Hidden)).ToList();
                Check(!trigger.Any(m => m.Name == "IsEnemy" || m.Name == "IsAlly"),
                    "native: a dispel's check has no protection for a friendly buff (the party's own buffs can go first)");
                Check(Body(typeof(SuppressBuffs), "OnActivate").Any(m => m.Name == "ApplyChanges") &&
                      Body(typeof(UnitPartBuffSuppress), "ApplyChanges").Any(m => m.Name == "UpdateBuffSuppressionState"),
                    "native: a new suppressor re-checks the buffs already on its bearer");
                var suppressed = Body(typeof(UnitPartBuffSuppress), "IsSuppressed");
                Check(suppressed.Any(m => m.Name == "get_SpellDescriptor") && suppressed.Any(m => m.Name == "get_SpellSchool"),
                    "native: suppression reads the buff context's descriptor and school");

                // --- the actual graphs ---
                var buffs = loaded.OfType<BlueprintBuff>().ToList();
                var abilities = loaded.OfType<BlueprintAbility>().ToList();
                BlueprintBuff Buff(string name) => buffs.FirstOrDefault(b => b.name == name);
                BlueprintAbility Spell(string name) => abilities.FirstOrDefault(b => b.name == name);
                var fearRelief = Buff("RemoveFearBuff")?.GetComponent<SuppressBuffs>();
                Check(fearRelief != null && (fearRelief.Descriptor.Value & SpellDescriptor.Frightened) != 0 &&
                      (fearRelief.Descriptor.Value & SpellDescriptor.Shaken) != 0,
                    "actual Remove Fear: its buff suppresses Frightened and Shaken");
                var heart = Buff("UnbreakableHeartBuff")?.GetComponent<SuppressBuffs>();
                Check(heart != null && (heart.Descriptor.Value & SpellDescriptor.Confusion) != 0 &&
                      (heart.Descriptor.Value & SpellDescriptor.Compulsion) != 0 && (heart.Descriptor.Value & SpellDescriptor.Frightened) != 0,
                    "actual Unbreakable Heart: its buff suppresses Confusion, Compulsion (Dominate) and Frightened");
                var confusion = Buff("Confusion");
                var control = typeof(AbilityProfile).GetMethod("BuffControl", Hidden, null, new[] { typeof(BlueprintBuff) }, null);
                Check(confusion != null && control != null && control.Invoke(null, new object[] { confusion }).ToString() == "Hard",
                    "actual Confusion: a hard control of its bearer");
                var suppresses = engine.GetMethod("SuppressesOnRecipient", Hidden);
                bool Suppresses(string spell) => Spell(spell) is BlueprintAbility a &&
                    (bool)suppresses.Invoke(null, new object[] { AbilityProfile.Get(CasterPolicyCheck.Data(a)) });
                Check(Suppresses("RemoveFear") && Suppresses("UnbreakableHeart") && !Suppresses("Bless") && !Suppresses("Haste"),
                    "Remove Fear and Unbreakable Heart are read as suppressing; Bless and Haste are not");
                var breakEnchantment = Spell("BreakEnchantment");
                var breakDispel = breakEnchantment?.GetComponents<AbilityEffectRunAction>()
                    .SelectMany(r => r.Actions?.Actions ?? Array.Empty<Kingmaker.ElementsSystem.GameAction>())
                    .OfType<ContextActionDispelMagic>().FirstOrDefault();
                var unsupported = engine.GetMethod("DispelUnsupported", Hidden);
                Check(breakDispel != null && breakDispel.Schools.Contains(SpellSchool.Enchantment) && breakDispel.Schools.Contains(SpellSchool.Transmutation) &&
                      unsupported.Invoke(null, new object[] { breakDispel }) == null &&
                      breakEnchantment.GetComponent<AbilityTargetsAround>() != null,
                    "actual Break Enchantment: an area of allies, Enchantment and Transmutation effects, read by the dispel order");
                foreach (var name in new[] { "DispelMagicTarget", "DispelMagicGreaterTarget" })
                {
                    var spell = Spell(name);
                    var profile = spell != null ? AbilityProfile.Get(CasterPolicyCheck.Data(spell)) : null;
                    Check(profile != null && profile.DispelActions.Count > 0 &&
                          profile.DispelActions.All(d => unsupported.Invoke(null, new object[] { d }) == null),
                        $"actual {name}: its dispel is read by the dispel order (no fallback)");
                }

                // The actual point variant has no unit recipient. Run the production read with no actor: it must
                // return known zero before it can inspect a scoring anchor, context, or buff list.
                var point = Spell("DispelMagicPoint");
                var pointProfile = point != null ? AbilityProfile.Get(CasterPolicyCheck.Data(point)) : null;
                var unitDispel = engine.GetMethod("DispelTargetsUnits", Hidden);
                var greaterArea = Spell("DispelMagicGreaterArea");
                var greaterAreaProfile = greaterArea != null ? AbilityProfile.Get(CasterPolicyCheck.Data(greaterArea)) : null;
                Check(pointProfile != null && point.CanTargetPoint && !point.CanTargetSelf && !point.CanTargetFriends &&
                      !point.CanTargetEnemies && !(bool)unitDispel.Invoke(null, new object[] { pointProfile }),
                    "actual Dispel Magic Point has no unit-buff recipient");
                Check(greaterAreaProfile != null && greaterArea.GetComponent<AbilityTargetsAround>() != null &&
                      (bool)unitDispel.Invoke(null, new object[] { greaterAreaProfile }),
                    "actual Greater Dispel Magic Area retains its target-selector unit branch");
                var pointArgs = new object[] { null, null, pointProfile, null, null, null, 123f, null };
                Check(pointProfile != null && (bool)engine.GetMethod("TryDispelValue", Hidden).Invoke(null, pointArgs) &&
                      (float)pointArgs[6] == 0f,
                    "point dispel is known zero, with no unit-buff fallback or actor read");
                Check(Body(typeof(ContextActionDispelMagic), "RunAction").Any(m => m.Name == "TryDispelAreaEffect") &&
                      Body(typeof(ContextActionDispelMagic), "RunAction").Any(m => m.Name == "get_Unit"),
                    "native dispel branches between the target unit's buffs and point-containing area entities");

                var abandon = Spell("Vermlek_Ability_AbandonFlesh");
                var abandonProfile = abandon != null ? AbilityProfile.Get(CasterPolicyCheck.Data(abandon)) : null;
                Check(abandonProfile != null && abandonProfile.CasterRemovedBuffs.Count == 1 &&
                      abandonProfile.CasterRemovedBuffs[0].name == "Vermlek_Buff_FleshArmor" &&
                      !abandonProfile.RemovesAnything && abandonProfile.DispelActions.Count == 0 &&
                      !(bool)engine.GetMethod("CanRemove", Hidden).Invoke(null, new object[] { abandonProfile, null, false }),
                    "actual Abandon Flesh's caster-only removal is filtered, not an arbitrary target cleanse");
                var flesh = Buff("Vermlek_Buff_FleshArmor");
                var named = (Buff)RuntimeHelpers.GetUninitializedObject(typeof(Buff));
                typeof(Kingmaker.EntitySystem.EntityFact).GetField("<Blueprint>k__BackingField", Hidden).SetValue(named, flesh);
                var other = (Buff)RuntimeHelpers.GetUninitializedObject(typeof(Buff));
                typeof(Kingmaker.EntitySystem.EntityFact).GetField("<Blueprint>k__BackingField", Hidden).SetValue(other, confusion);
                var canRemove = engine.GetMethod("CanRemove", Hidden);
                Check(abandonProfile != null && flesh != null && confusion != null &&
                      (bool)canRemove.Invoke(null, new object[] { abandonProfile, named, true }) &&
                      !(bool)canRemove.Invoke(null, new object[] { abandonProfile, named, false }) &&
                      !(bool)canRemove.Invoke(null, new object[] { abandonProfile, other, true }),
                    "caster-only removal matches its named buff on the caster only, preserving a named self-cleanse");
                var removalCost = Body(engine, "CasterRemovalCost");
                Check(Calls(Body(engine, "ScoreAbility"), engine, "CasterRemovalCost") &&
                      Calls(removalCost, engine, "EnemyBuffShare") && Calls(removalCost, engine, "IsHeldState") &&
                      Calls(removalCost, typeof(RemovalRules), "HelpfulLoss"),
                    "caster removal loses active helpful output; existing held-state costs are not duplicated");

                var targetDispel = AbilityProfile.Get(Spell("DispelMagicTarget")).DispelActions.Single();
                Check(targetDispel.OneRollForAll &&
                      Body(typeof(ContextActionDispelMagic), "TryDispelBuff").Any(m => m.Name == "SetRollOverride"),
                    "native targeted Dispel Magic shares its d20 across attempts");
                var nativeTry = Body(typeof(ContextActionDispelMagic), "TryDispelBuff");
                Check(!nativeTry.Any(m => m.Name == "get_IsSuppressed"),
                    "native dispel eligibility does not exclude a wholly suppressed buff");

                // Match the native comparer on a complete, inert buff list with many equal-level ties. No actor/context
                // constructor or dispel executes; getters read only the seeded native fields.
                var comparerType = typeof(ContextActionDispelMagic).GetNestedTypes(Hidden)
                    .First(t => t.GetMethods(Hidden).Any(m => m.Name.StartsWith("<RunAction>") && m.GetParameters().Length == 2));
                var compareMethod = comparerType.GetMethods(Hidden).Single(m => m.Name.StartsWith("<RunAction>") && m.GetParameters().Length == 2);
                var nativeCompare = (Comparison<Buff>)Delegate.CreateDelegate(typeof(Comparison<Buff>),
                    RuntimeHelpers.GetUninitializedObject(comparerType), compareMethod);
                var inert = new List<Buff>();
                for (int i = 0; i < 80; i++)
                {
                    var buff = (Buff)RuntimeHelpers.GetUninitializedObject(typeof(Buff));
                    var context = (MechanicsContext)RuntimeHelpers.GetUninitializedObject(typeof(MechanicsContext));
                    var parameters = (AbilityParams)RuntimeHelpers.GetUninitializedObject(typeof(AbilityParams));
                    typeof(AbilityParams).GetField("<CasterLevel>k__BackingField", Hidden).SetValue(parameters, i % 4 + 10);
                    typeof(MechanicsContext).GetField("m_Params", Hidden).SetValue(context, parameters);
                    typeof(Buff).GetField("m_Context", Hidden).SetValue(buff, context);
                    inert.Add(buff);
                }
                var expectedOrder = inert.ToList(); expectedOrder.Sort(nativeCompare);
                var actualOrder = (List<Buff>)engine.GetMethod("DispelOrder", Hidden).Invoke(null, new object[] { inert });
                Check(actualOrder.SequenceEqual(expectedOrder), "the production dispel sort matches the native comparer, including ties");
                Check(!actualOrder.SequenceEqual(inert.OrderByDescending(b => b.Context.Params.CasterLevel)),
                    "the tie fixture detects the former stable-sort difference");

                // --- compiled routes ---
                var score = Body(engine, "ScoreAbility");
                Check(new[] { "TryDispelValue", "SuppressionRelief", "SuppressesOnRecipient", "RemovalRelief", "ReliefHorizon" }.All(n => Calls(score, engine, n)),
                    "ScoreAbility prices dispels, removals and suppressions by what they end");
                Check(new[] { "HasSomethingToDispel", "CanRemove", "TryDispelValue", "RemovalRelief", "FreeingValue" }
                      .All(n => Calls(Body(engine, n), engine, "DispelTargetsUnits")),
                    "fallback, filtering, dispel relief, removal relief and freeing share the unit-recipient guard");
                var dispel = Body(engine, "TryDispelValue");
                Check(Calls(dispel, engine, "DispelValue") && Calls(dispel, engine, "DispelEffectChance") && dispel.Any(m => m.Name == "TurnedAllies"),
                    "a dispel is valued on every unit it reaches in the game's order; dominated allies are the release's");
                var full = engine.GetMethods(Hidden).Where(m => m.Name == "DispelValue").OrderByDescending(m => m.GetParameters().Length).First();
                var order = SaveBoostCheck.ReferencedMembers(full).ToList();
                Check(new[] { "HarmWorth", "IsHarmfulOn", "AllyPerRound", "OfPartySide", "EnemyBuffShare" }.All(n => order.Any(m => m.Name == n)),
                    "on an ally, a harmful effect taken off is relief and the party's own buff a loss");
                Check(order.Any(m => m.Name == "OneRollForAll") && Calls(order, engine, "DispelOrder"),
                    "production forwards the shared-roll flag and sorts the full native buff input");
                var chance = Body(engine, "DispelEffectChance");
                Check(new[] { "EffectChance", "FusionPath", "DispelOutcomeShare", "FailedHitPoints", "PassedHitPoints", "NestedSaveFactor" }
                    .All(n => chance.Any(m => m.Name == n)), "a fused dispel uses its own save path and the actual HP survival of each outcome");
                var harm = Body(engine, "HarmWorth");
                Check(new[] { "OwnDebuffShare", "HeldRounds", "RoundSavePass", "ActiveRounds", "AllyPerRound" }.All(n => harm.Any(m => m.Name == n)),
                    "relief: the effect's control or penalty share of the ally's output, for the rounds it would still act");
                var suppression = Body(engine, "SuppressionRelief");
                Check(new[] { "SuppressorHits", "HarmWorth", "FreedWorth", "HoldsUnderControl" }.All(n => Calls(suppression, engine, n)),
                    "a suppression switches off what hinders the ally, or frees a dominated one, for as long as it lasts");
                Check(suppression.Any(m => m.Name == "Applies") && Calls(suppression, typeof(CombatMath), "BuffImmune") &&
                      Calls(suppression, engine, "TryFusionApplicationRounds"),
                    "suppression benefit requires an applicable, non-immune application and readable duration");
                Check(Body(engine, "RemovalRelief").Any(m => m.Name == "BuffsRemovedFrom") &&
                      Body(engine, "FreeingValue").Any(m => m.Name == "BuffsRemovedFrom"),
                    "removal relief and freeing use the actual guarded removal/filter reader");
                Check(Calls(Body(engine, "FreeingValue"), engine, "SuppressionRelief"),
                    "the release counts a suppression of the hold (Unbreakable Heart)");
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"RELIEF {checks} checks, {bad} failures");
            return lines;
        }
    }
}
