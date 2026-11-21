// ★ Fusion coverage C1 (docs/FUSION_MAIN_TARGET_DOT.md): Molten Orb's main-target guard and the damage over time a fused
// cast leaves (Molten Orb's and Incendiary Runes' burning, Acid Arrow's acid). Native contracts in the supplied assembly,
// the actual graphs and the compiled routes. Actor-free: no Game/Player, Rulebook, cast, buff or Unity timing.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.UnitLogic.Mechanics.Conditions;

namespace ProfileAudit
{
    internal static class FusionDotCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static List<MemberInfo> Body(Type type, string name) => BuffSuppressCheck.BodyWithLambdas(type, name);
        private static bool Calls(IEnumerable<MemberInfo> body, Type type, string name) =>
            body.Any(m => m.DeclaringType == type && m.Name == name);
        private static bool CallsNamed(IEnumerable<MemberInfo> body, string name) => body.Any(m => m.Name == name);
        /// <summary>Every overload's body (BaseDamage.AddModifier has three).</summary>
        private static List<MemberInfo> Overloads(Type type, string name) => type.GetMethods(Hidden | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == name).SelectMany(m => SaveBoostCheck.ReferencedMembers(m)).ToList();

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FUSION-DOT FAILED: " + why); } }
            try
            {
                // --- 1. native contracts ---
                var mainTarget = Body(typeof(ContextConditionIsMainTarget), "CheckCondition");
                Check(CallsNamed(mainTarget, "get_MainTarget") && CallsNamed(mainTarget, "get_Target") && CallsNamed(mainTarget, "Equals"),
                    "native: 'is the main target' compares the cast's main target with the recipient (TargetWrapper.Equals)");
                Check(Calls(Body(typeof(AbilityExecutionContext), "get_MainTarget"), typeof(AbilityExecutionContext), "get_ClickedTarget"),
                    "native: a cast's main target is its clicked target, fixed for the cast");
                var targetEquals = typeof(Kingmaker.Utility.TargetWrapper).GetMethods(Hidden)
                    .Single(m => m.Name == "Equals" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Kingmaker.Utility.TargetWrapper));
                var equalsBody = SaveBoostCheck.ReferencedMembers(targetEquals).ToList();
                Check(CallsNamed(equalsBody, "get_Unit") && CallsNamed(equalsBody, "get_Point") && CallsNamed(equalsBody, "get_Orientation"),
                    "native: a target compares by unit, else by point and facing - a ground aim matches a unit only at its exact spot and facing");
                Check(CallsNamed(Body(typeof(MechanicsContext), "get_SavingThrow"), "get_Current"),
                    "native: a context's saving throw is the current save scope (ContextData<SavingThrowData>), unset in a round tick");
                var saveRun = Body(typeof(ContextActionSavingThrow), "RunAction");
                Check(CallsNamed(saveRun, "get_MagicHackData") && CallsNamed(saveRun, "get_SavingThrow") &&
                      saveRun.Any(m => m.Name == "MakeSaveAlsoForMagicHack"),
                    "native: a fused cast's inner save reuses the root save only inside its scope; a tick rolls its own");
                var damageInfo = Body(typeof(ContextActionDealDamage), "GetDamageInfo");
                Check(Calls(damageInfo, typeof(MagicHackSpellbookComponent), "GetMaxDamageDicesForAction") &&
                      CallsNamed(damageInfo, "get_AbilityContext") && damageInfo.Any(m => m.Name == "IgnoreUnitModifiers"),
                    "native: a fused cast's dice cap reads the ability of the context's root, and the damage carries IgnoreUnitModifiers");
                Check(Calls(Body(typeof(MechanicsContext), "get_SourceAbilityContext"), typeof(MechanicsContext), "get_Root"),
                    "native: a buff's context roots in the cast that applied it (SourceAbilityContext = Root)");
                Check(CallsNamed(Overloads(typeof(BaseDamage), "AddModifier"), "get_IgnoreModifiers") &&
                      !CallsNamed(Body(typeof(BaseDamage), "SetReductionBecauseResistance"), "get_IgnoreModifiers"),
                    "native: IgnoreModifiers stops flat damage bonuses only, not resistance");
                var attach = Body(typeof(Buff), "OnAttach");
                Check(CallsNamed(attach, "get_TickTime") && CallsNamed(attach, "get_TurnStartTime") && CallsNamed(attach, "set_NextTickTime"),
                    "native: a buff's first tick is one interval after it lands (turn-based: after the turn's start)");
                Check(typeof(AddFactContextActions).IsAssignableFrom(typeof(AddBuffActions)) &&
                      typeof(Kingmaker.Controllers.Units.ITickEachRound).IsAssignableFrom(typeof(AddFactContextActions)),
                    "native: AddBuffActions is the round-tick component AddFactContextActions");

                // --- 2. the actual graphs ---
                var all = loaded.OfType<BlueprintAbility>().ToList();
                var buffs = loaded.OfType<BlueprintBuff>().ToList();
                BlueprintAbility Spell(string name) => all.First(b => b.name == name);
                BlueprintBuff Buff(string guid) => buffs.SingleOrDefault(b => b.AssetGuid.ToString() == guid);
                var holder = all.Single(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                AbilityData Fuse(string a, string b, SavingThrowType save)
                {
                    var data = CasterPolicyCheck.Data(holder);
                    data.MagicHackData = new MagicHackData { Spell1 = Spell(a), Spell2 = Spell(b), DeliverBlueprint = Spell(a),
                        SavingThrowType = save, SpellSchool = SpellSchool.Evocation, SpellLevel = 4 };
                    return data;
                }
                var engine = typeof(TacticalEngine);
                var math = typeof(CombatMath);
                var withheld = engine.GetMethod("FusionWithheld", Hidden);
                var model = engine.GetMethod("FusionOutcomeModel", Hidden);
                var applications = typeof(AbilityProfile).GetProperty("BuffApplications", Hidden);
                var buffPath = typeof(AbilityProfile).GetMethod("FusionBuffPath", Hidden);
                var guards = typeof(AbilityProfile).GetProperty("FusionIndependentGuards", Hidden);
                var reason = typeof(AbilityProfile).GetField("FusionOutcomeMetadataReason", Hidden);
                var carrier = math.GetMethod("IsPeriodicDamageCarrier", Hidden);
                var planOf = math.GetMethod("PeriodicPlanOf", Hidden);
                Check(withheld != null && model != null && applications != null && buffPath != null && guards != null && carrier != null &&
                      planOf != null, "production admission, carrier and plan contracts exist");

                bool IsCarrier(BlueprintBuff b) => b != null && (bool)carrier.Invoke(null, new object[] { b });
                bool Supported(BlueprintBuff b) => b != null && (bool)planOf.Invoke(null, new object[] { b }).GetType()
                    .GetProperty("Supported", Hidden).GetValue(planOf.Invoke(null, new object[] { b }));
                var moltenBurn = Buff("ad9a6a7ee08ce73469dff703a17f8934");
                var runesBurn = Buff("e69fd2ebb67749968f79519f6dc5a5f1");
                var acid = Buff("e52b0f098512c0b4388ef42764c51bdf");
                Check(moltenBurn != null && runesBurn != null && acid != null, "Molten Orb's burning, Incendiary Runes' burning and Acid Arrow's acid are loaded");
                Check(Supported(moltenBurn) && IsCarrier(moltenBurn),
                    "actual Molten Orb burning (1d6 fire a round, IgnoreUnitModifiers) is read and is a periodic damage carrier");
                Check(Supported(runesBurn) && IsCarrier(runesBurn),
                    "actual Incendiary Runes burning (1d6 fire, Reflex each round ends it, DC from the context's save) is a carrier");
                Check(Supported(acid) && IsCarrier(acid), "actual Acid Arrow acid (2d4 a round) is a periodic damage carrier");
                Check(ReferenceEquals(planOf.Invoke(null, new object[] { acid }), planOf.Invoke(null, new object[] { acid })),
                    "plans are read once per blueprint");

                // Not carriers: more than damage in a tick, more than round actions, or dice the cast's ranks decide.
                BlueprintBuff With(BlueprintComponent[] components) => new BlueprintBuff { ComponentsArray = components };
                AddFactContextActions Ticking(params GameAction[] actions) => new AddFactContextActions {
                    Activated = new ActionList(), Deactivated = new ActionList(), NewRound = new ActionList { Actions = actions } };
                ContextActionDealDamage Fire(ContextValue count) => new ContextActionDealDamage {
                    DamageType = new DamageTypeDescription { Type = DamageType.Energy, Energy = Kingmaker.Enums.Damage.DamageEnergyType.Fire },
                    Value = new ContextDiceValue { DiceType = DiceType.D6, DiceCountValue = count, BonusValue = 0 } };
                Check(IsCarrier(With(new BlueprintComponent[] { Ticking(Fire(1)) })), "a synthetic 1d6-a-round buff is a carrier");
                Check(!IsCarrier(With(new BlueprintComponent[] { Ticking(Fire(1)), new AddCondition { Condition = Kingmaker.UnitLogic.UnitCondition.Staggered } })),
                    "a carrier that also staggers is not damage only");
                Check(!IsCarrier(With(new BlueprintComponent[] { Ticking(Fire(1), new ContextActionApplyBuff()) })),
                    "a tick that applies a child buff is not damage only");
                Check(!IsCarrier(With(new BlueprintComponent[] { Ticking(Fire(new ContextValue { ValueType = ContextValueType.Rank })) })),
                    "dice the cast's ranks decide are not priced as fixed");
                // ★ coverage4 (C6a): a rank the buff's own config reads from the caster level is a fixed read of the cast.
                ContextRankConfig Rank(AbilityRankType type, ContextRankBaseValueType from)
                {
                    var config = new ContextRankConfig();
                    typeof(ContextRankConfig).GetField("m_Type", Hidden).SetValue(config, type);
                    typeof(ContextRankConfig).GetField("m_BaseValueType", Hidden).SetValue(config, from);
                    return config;
                }
                var ranked = new ContextValue { ValueType = ContextValueType.Rank, ValueRank = AbilityRankType.DamageDice };
                Check(IsCarrier(With(new BlueprintComponent[] { Ticking(Fire(ranked)), Rank(AbilityRankType.DamageDice, ContextRankBaseValueType.CasterLevel) })),
                    "dice from the buff's own caster-level rank are a carrier");
                Check(!IsCarrier(With(new BlueprintComponent[] { Ticking(Fire(ranked)), Rank(AbilityRankType.DamageDice, ContextRankBaseValueType.StatBonus) })),
                    "dice from a stat-based rank (someone's state) are not");
                Check(!IsCarrier(With(new BlueprintComponent[] { Ticking(Fire(ranked)), Rank(AbilityRankType.DamageBonus, ContextRankBaseValueType.CasterLevel) })),
                    "a rank config of another rank type does not read the dice");
                var caustic = Buff("02a4d7303f0eeee408d7c7e5908ba339");
                Check(caustic != null && Supported(caustic) && IsCarrier(caustic),
                    "actual Caustic Eruption acid (1d6 per two caster levels a round, at most 10) is a periodic damage carrier");
                var tickReader = engine.GetMethod("PeriodicCarrierDamage", Hidden);
                var rankRead = math.GetMethod("PeriodicRank", Hidden);
                Check(tickReader != null && rankRead != null &&
                      BuffSuppressCheck.BodyWithLambdas(engine, "PeriodicCarrierDamage").Any(m => m.Name == "PeriodicRank") &&
                      SaveBoostCheck.ReferencedMembers(rankRead).Any(m => m.Name == "RecalculateRanks") &&
                      SaveBoostCheck.ReferencedMembers(rankRead).Any(m => m.Name == "get_frameCount"),
                    "a tick's ranked dice are read in a buff context under the cast's, once per cast, buff and frame");

                // The production tick reader on the actual Incendiary Runes burning: damage now, and a passed Reflex removes it.
                var read = math.GetMethod("ReadPeriodicTick", Hidden);
                var affliction = math.GetNestedType("Affliction", Hidden);
                var plan = planOf.Invoke(null, new object[] { runesBurn });
                var round = plan.GetType().GetField("Round", Hidden).GetValue(plan);
                var damageReader = (Delegate)typeof(FusionDotCheck).GetMethod(nameof(MeanFire), Hidden)
                    .MakeGenericMethod(affliction).Invoke(null, null);
                var appliedReader = (Delegate)typeof(FusionDotCheck).GetMethod(nameof(Nothing), Hidden)
                    .MakeGenericMethod(affliction).Invoke(null, null);
                foreach (float fail in new[] { 0.25f, 0.6f })
                {
                    var tick = read.Invoke(null, new object[] { round, new Func<ContextActionSavingThrow, float>(_ => fail), damageReader, appliedReader });
                    float hp = (float)affliction.GetField("HitPoints").GetValue(tick.GetType().GetField("Harm", Hidden).GetValue(tick));
                    float stays = (float)tick.GetType().GetField("Survives", Hidden).GetValue(tick);
                    Check(Math.Abs(hp - 3.5f) < 1e-4f && Math.Abs(stays - fail) < 1e-4f,
                        $"actual Incendiary Runes burning: 3.5 now, stays on a failed Reflex ({hp:0.##}, {stays:0.##} at fail {fail})");
                }

                // Molten Orb: its "is the main target" branch is a stable guard, and its fusions reach the joint value.
                string Withheld(AbilityData a) => (string)withheld.Invoke(null, new object[] { a, AbilityProfile.Get(a) });
                bool Qualified(AbilityData a) => (bool)model.Invoke(null, new object[] { a, AbilityProfile.Get(a) });
                string Paths(AbilityData a, BlueprintBuff b)
                {
                    var profile = AbilityProfile.Get(a);
                    return string.Join("+", ((IEnumerable)applications.GetValue(profile)).Cast<object>()
                        .Where(o => o.GetType().GetField("Buff").GetValue(o) == b)
                        .Select(o => buffPath.Invoke(profile, new[] { o.GetType().GetField("Action", Hidden).GetValue(o) }).ToString())
                        .Distinct().OrderBy(s => s, StringComparer.Ordinal));
                }
                bool PeriodicOnly(AbilityData a, BlueprintBuff b) => ((IEnumerable)applications.GetValue(AbilityProfile.Get(a))).Cast<object>()
                    .Any(o => o.GetType().GetField("Buff").GetValue(o) == b && (bool)o.GetType().GetField("PeriodicDamageOnly", Hidden).GetValue(o));
                foreach (var (a, b, save) in new[] {
                    ("Fireball", "MoltenOrb", SavingThrowType.Reflex), ("ExplosiveRunes", "MoltenOrb", SavingThrowType.Reflex),
                    ("MoltenOrb", "ColorSpray", SavingThrowType.Will), ("Fireball", "AcidArrow", SavingThrowType.Reflex),
                    ("ExplosiveRunes", "AcidArrow", SavingThrowType.Reflex), ("IncendiaryRunes", "FrigidTouchCast", SavingThrowType.Fortitude) })
                {
                    var fused = Fuse(a, b, save);
                    var profile = AbilityProfile.Get(fused);
                    string why = Withheld(fused);
                    bool joint = Qualified(fused);
                    Check(why == null && joint && (bool)guards.GetValue(profile),
                        $"actual {a}/{b} ({save}) is offered with the joint value (withheld: {why ?? "no"}, reason: {reason.GetValue(profile) ?? "none"})");
                }
                var molten = Fuse("Fireball", "MoltenOrb", SavingThrowType.Reflex);
                Check(PeriodicOnly(molten, moltenBurn) && Paths(molten, moltenBurn) == "Both",
                    $"Molten Orb's burning is a carrier on both outcomes ({Paths(molten, moltenBurn)})");
                var arrow = Fuse("Fireball", "AcidArrow", SavingThrowType.Reflex);
                Check(PeriodicOnly(arrow, acid) && Paths(arrow, acid) == "Both", $"Acid Arrow's acid is a carrier on both outcomes ({Paths(arrow, acid)})");
                var runes = Fuse("IncendiaryRunes", "FrigidTouchCast", SavingThrowType.Fortitude);
                Check(PeriodicOnly(runes, runesBurn), "arcan's Incendiary Runes/Frigid Touch keeps its burning as a priced carrier");
                var moltenProfile = AbilityProfile.Get(molten);
                Check(moltenProfile.Damage.OfType<ContextActionDealDamage>().Count() >= 3,
                    "Molten Orb's direct hit and splash are both read, each behind its own branch");

                // ★ Fusion coverage C5: ability damage and negative levels join the joint value by their HP equivalent.
                foreach (var (a, b, save) in new[] { ("Fireball", "WrackingRay", SavingThrowType.Reflex),
                                                    ("WrackingRay", "ColorSpray", SavingThrowType.Will) })
                {
                    var fused = Fuse(a, b, save);
                    var profile = AbilityProfile.Get(fused);
                    var kindOf = math.GetMethod("KindOf", Hidden);
                    bool drains = profile.Damage.OfType<ContextActionDealDamage>().Any(d => kindOf.Invoke(null, new object[] { d }).ToString() != "HitPoints");
                    Check(drains && Withheld(fused) == null && Qualified(fused),
                        $"C5: actual {a}/{b} ({save}) with ability damage is valued by the joint model (reason: {reason.GetValue(profile) ?? "none"})");
                }
                var enervation = Fuse("Fireball", "Enervation", SavingThrowType.Reflex);
                Check(!((string)reason.GetValue(AbilityProfile.Get(enervation)) ?? "").Contains("non-HP") && Qualified(enervation) && Withheld(enervation) == null,
                    "C5/C6: Enervation's negative levels are read, and its undead branch's temporary HP is damage the target will not take");
                Check(Calls(Body(engine, "TryFusionValues"), engine, "CarrierBenefit"), "the joint value takes off the temporary HP a cast gives");

                // --- 3. compiled routes ---
                var values = Body(engine, "TryFusionValues");
                Check(Calls(values, engine, "TryPeriodicDamage") && Calls(values, engine, "PeriodicHorizon") &&
                      values.Any(m => m is MethodInfo mi && mi.DeclaringType == typeof(ValueModel) && mi.Name == "FusionValue" && mi.GetParameters().Length == 15),
                    "the joint value takes each outcome's damage over time");
                var periodic = Body(engine, "TryPeriodicDamage");
                Check(Calls(periodic, engine, "TryFusionApplicationRounds") && Calls(periodic, engine, "PeriodicCarrierDamage") &&
                      CallsNamed(periodic, "BuffImmune") && CallsNamed(periodic, "HasFact"),
                    "damage over time reads the native duration, immunity, and gives no refresh credit");
                Check(Calls(Body(engine, "TryFusionControlRounds"), engine, "TryFusionApplicationRounds"),
                    "control and damage over time read one duration rule");
                var carrierDamage = Body(engine, "PeriodicCarrierDamage");
                Check(Calls(carrierDamage, math, "FusedDiceCount") && Calls(carrierDamage, math, "AfterTargetReduction") &&
                      Calls(carrierDamage, typeof(ValueModel), "PeriodicTicks") && Calls(carrierDamage, typeof(ValueModel), "PeriodicWeight") &&
                      Calls(carrierDamage, math, "ReadPeriodicTick") && Calls(carrierDamage, math, "SaveFailChance") &&
                      !Calls(carrierDamage, math, "CreateContext"),
                    "a carrier's ticks: fused dice cap, the recipient's resistance, the native schedule and save, no context");
                Check(Calls(Body(engine, "ScoreAbility"), engine, "PeriodicAllyDamage"),
                    "the friendly-fire cost counts the damage over time the cast leaves on allies");
                var damageTo = Body(typeof(BurstWatch), "DamageTo");
                Check(Calls(damageTo, typeof(AbilityProfile), "AimedAt") && Calls(damageTo, engine, "PeriodicAllyDamage"),
                    "the burst re-check prices a walking-in ally as the ground-aimed cast has it, damage over time included");
                Check(Calls(Body(math, "EvaluateDamageDice"), math, "FusedDiceCount"), "the cast's own damage keeps the same dice cap");
            }
            catch (Exception ex) { Check(false, "exception " + ex.GetType().Name + ": " + ex.Message); }
            failures = bad;
            lines.Insert(0, $"FUSION-DOT {checks} checks, {bad} failures");
            return lines;
        }

        private static Func<ContextActionDealDamage, T> MeanFire<T>() where T : class =>
            deal =>
            {
                var harm = (T)Activator.CreateInstance(typeof(T), true);
                typeof(T).GetField("HitPoints").SetValue(harm,
                    deal.Value.DiceCountValue.Value * (deal.Value.DiceType.Sides() + 1f) / 2f + deal.Value.BonusValue.Value);
                return harm;
            };

        private static Func<ContextActionApplyBuff, T> Nothing<T>() where T : class =>
            _ => (T)Activator.CreateInstance(typeof(T), true);
    }
}
