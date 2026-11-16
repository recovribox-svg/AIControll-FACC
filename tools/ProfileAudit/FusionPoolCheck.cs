// ★ Fusion F7 (docs/FUSION_SLEEP_POOL.md): a fused spell that spends a shared Hit Dice pool (Sleep).
// Actual holder/source/delivery graphs, the pool plan's metadata and the compiled routes.
// Actor-free: no Game/Player, context, selection, Rulebook, cast, command or Unity timing.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CompanionAI_Pathfinder.Tactics;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ProfileAudit
{
    internal static class FusionPoolCheck
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static List<string> Run(IEnumerable<SimpleBlueprint> loaded, out int failures)
        {
            int checks = 0, bad = 0; var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("FUSION-POOL FAILED: " + why); } }
            try
            {
                var all = loaded.OfType<BlueprintAbility>().ToList();
                BlueprintAbility Spell(string name) => all.First(b => b.name == name);
                var holder = all.Single(b => b.AssetGuid.ToString() == "4fcc9f0db7f6495497e5163b71aa7bcc");
                AbilityData Fuse(string a, string b, string delivery, SavingThrowType save)
                {
                    var data = CasterPolicyCheck.Data(holder);
                    data.MagicHackData = new MagicHackData { Spell1 = Spell(a), Spell2 = Spell(b), DeliverBlueprint = Spell(delivery),
                        SavingThrowType = save, SpellSchool = SpellSchool.Enchantment, SpellLevel = 2 };
                    return data;
                }
                var engine = typeof(TacticalEngine);
                var control = engine.Assembly.GetType("CompanionAI_Pathfinder.Tactics.SharedHdControl", true);
                var refusal = control.GetMethod("FusedRefusal", Hidden);
                var withheld = engine.GetMethod("FusionWithheld", Hidden);
                var detail = engine.GetMethod("FusionWithheldDetail", Hidden);
                var model = engine.GetMethod("FusionOutcomeModel", Hidden);
                var unsupported = typeof(AbilityProfile).GetProperty("FusionUnsupported", Hidden);
                var pooledFlag = typeof(AbilityProfile).GetProperty("FusionSharedHd", Hidden);
                var planField = typeof(AbilityProfile).GetField("FusionSharedHdPlan", Hidden);
                var applications = typeof(AbilityProfile).GetProperty("BuffApplications", Hidden);
                var before = typeof(AbilityProfile).GetMethod("FusionDamageBefore", Hidden);
                var reason = typeof(AbilityProfile).GetField("FusionOutcomeMetadataReason", Hidden);
                Check(refusal != null && withheld != null && detail != null && model != null && unsupported != null &&
                      pooledFlag != null && planField != null && applications != null && before != null,
                    "production pool plan, admission and ordering contracts exist");
                string Refusal(AbilityData a) => (string)refusal.Invoke(null, new object[] { a, AbilityProfile.Get(a) });
                string Withheld(AbilityData a) => (string)withheld.Invoke(null, new object[] { a, AbilityProfile.Get(a) });
                string Detail(AbilityData a) => (string)detail.Invoke(null, new object[] { a, AbilityProfile.Get(a) });
                object Plan(AbilityData a) { Refusal(a); return planField.GetValue(AbilityProfile.Get(a)); }
                T Of<T>(object plan, string field) => (T)plan.GetType().GetField(field, Hidden).GetValue(plan);

                // 1. The native difference: the fused cast's context is the holder's, and so is its pool.
                var own = Spell("Sleep").GetComponents<ContextCalculateSharedValue>().Where(c => c.ValueType == AbilitySharedValue.Damage).ToArray();
                Check(own.Length == 1 && own[0].Value.DiceType == Kingmaker.RuleSystem.DiceType.Zero && own[0].Value.BonusValue.Value == 4,
                    "actual Sleep carries its own fixed pool of 4 Hit Dice");
                var held = holder.GetComponents<ContextCalculateSharedValue>().Where(c => c.ValueType == AbilitySharedValue.Damage).ToArray();
                Check(held.Length == 1 && held[0].Value.DiceType == Kingmaker.RuleSystem.DiceType.D6 && held[0].Value.DiceCountValue.Value == 2 &&
                      held[0].Value.BonusValue.Value == 0 && held[0].Modifier == 1.0,
                    "actual fusion holder slot carries one 2d6 source for the same shared value");
                string PoolOf(BlueprintAbility b) => string.Join("+", b.GetComponents<ContextCalculateSharedValue>()
                    .Where(c => c.ValueType == AbilitySharedValue.Damage)
                    .Select(c => $"{c.Value.DiceCountValue.Value}{c.Value.DiceType}+{c.Value.BonusValue.Value}"));
                var slots = all.Where(b => b.name.StartsWith("MagicHackDefaultSlot", StringComparison.Ordinal)).ToList();
                var touchSlots = all.Where(b => b.name.StartsWith("MagicHackTouchSlot", StringComparison.Ordinal)).ToList();
                var slotPools = slots.Select(PoolOf).Distinct().ToList();
                lines.Add($"FUSION-POOL holder slots: {slots.Count} ordinary with pool source {string.Join(" | ", slotPools)}; " +
                          $"{touchSlots.Count} touch with {touchSlots.Count(b => PoolOf(b).Length > 0)} pool sources");
                Check(slots.Count >= 2 && slotPools.Count == 1 && slotPools[0] == "2D6+0",
                    "every loaded ordinary holder slot carries the same 2d6 pool source (the engine reads the live one)");
                Check(touchSlots.All(b => PoolOf(b).Length == 0),
                    "touch holder slots carry no pool source; a touch-delivered pool stays outside the forecast");
                var context = typeof(MechanicsContext).GetMethod("RecalculateSharedValues", Hidden);
                Check(context != null && SaveBoostCheck.ReferencedMembers(context).Any(m => m.Name == "AssociatedBlueprint"),
                    "native shared values are calculated from the context's own blueprint");

                // 2. The played pair and its sibling: plan metadata.
                var runes = Fuse("ExplosiveRunes", "Sleep", "ExplosiveRunes", SavingThrowType.Will);
                var plan = Plan(runes);
                Check(Refusal(runes) == null && plan != null && Of<object>(plan, "Delivery").ToString() == "Around" &&
                      Of<AbilityTargetsAround>(plan, "Selector") == Spell("ExplosiveRunes").GetComponent<AbilityTargetsAround>() &&
                      Of<AbilitySharedValue>(plan, "SharedValue") == AbilitySharedValue.Damage,
                    "Explosive Runes/Sleep: recipients by the deliver blueprint's own around-selector (" + (Refusal(runes) ?? "ok") + ")");
                Check(Spell("Sleep").GetComponent<AbilityTargetsAround>().Targets == TargetType.Enemy &&
                      Spell("ExplosiveRunes").GetComponent<AbilityTargetsAround>().Targets == TargetType.Any,
                    "ordinary Sleep is enemy-only; Explosive Runes has a broader damage-area selector");
                var enemyDelivery = Fuse("ExplosiveRunes", "Sleep", "Sleep", SavingThrowType.Will);
                Check(Refusal(enemyDelivery) == null && Of<AbilityTargetsAround>(Plan(enemyDelivery), "Selector").Targets == TargetType.Enemy &&
                      AbilityProfile.Get(enemyDelivery).AoETargets == TargetType.Enemy,
                    "a fused cast delivered through Sleep retains its enemy-only selector; no ally restriction is added");
                // outcome2: pooled control cannot disable the other payload's existing walking-in damage safety.
                // Invoke the production policy on the actual profiles; no actor walk or future pool is simulated.
                var walkingMethod = engine.GetMethod("ForecastWalkingAreaAllies", Hidden);
                Check(walkingMethod != null, "production walking-in area policy is independently callable");
                var walking = (Func<AbilityProfile, bool, bool>)Delegate.CreateDelegate(
                    typeof(Func<AbilityProfile, bool, bool>), walkingMethod);
                Check(walking(AbilityProfile.Get(runes), true),
                    "supported Explosive Runes/Sleep keeps walking-in ally safety for its independent damage");
                Check(!walking(AbilityProfile.Get(CasterPolicyCheck.Data(Spell("Sleep"))), true),
                    "ordinary Sleep keeps its current-position enemy pool selection");
                Check(!walking(AbilityProfile.Get(enemyDelivery), true),
                    "a Sleep-delivered enemy-only fusion adds no walking-in allies");
                Check(walking(AbilityProfile.Get(runes), false),
                    "an unsupported fused pool cannot suppress ordinary area ally safety");
                Check(walking(AbilityProfile.Get(CasterPolicyCheck.Data(Spell("ExplosiveRunes"))), false),
                    "ordinary Explosive Runes keeps its existing walking-in ally safety");
                Check(walking(AbilityProfile.Get(Fuse("IncendiaryRunes", "FrigidTouchCast", "IncendiaryRunes", SavingThrowType.Reflex)), false),
                    "a saved Frigid fusion without a shared pool keeps the same walking-in policy");
                var budgets = plan == null ? null : Of<int[]>(plan, "Budgets"); var chances = plan == null ? null : Of<float[]>(plan, "Chances");
                Check(budgets != null && budgets.SequenceEqual(Enumerable.Range(2, 11)) && Math.Abs(chances.Sum() - 1f) < 1e-5f &&
                      Math.Abs(chances[0] - 1f / 36f) < 1e-6f && Math.Abs(chances[5] - 6f / 36f) < 1e-6f && Math.Abs(chances[10] - 1f / 36f) < 1e-6f &&
                      Math.Abs(budgets.Zip(chances, (b, c) => b * c).Sum() - 7f) < 1e-4f,
                    "the pool is the exact 2d6 distribution: 2 to 12, mean 7");
                Check(plan != null && Of<bool>(plan, "Rolled") && Of<int>(plan, "MaximizedBudget") == 12, "a maximized cast reads the full 12, as the game does");
                var empoweredBudgets = Of<int[]>(plan, "EmpoweredBudgets"); var empoweredChances = Of<float[]>(plan, "EmpoweredChances");
                Check(empoweredBudgets.SequenceEqual(new[] { 3, 4, 6, 7, 9, 10, 12, 13, 15, 16, 18 }) &&
                      empoweredChances.SequenceEqual(chances) && Of<int>(plan, "EmpoweredMaximizedBudget") == 18,
                    "Empower uses the native integer-half distribution and Maximize plus Empower yields 18");
                // Actual native calculator, maximized to avoid RNG/Rulebook/actors; params can contain automatic flags.
                int Native(Metamagic meta, int count, int bonus)
                {
                    var contextFixture = (MechanicsContext)RuntimeHelpers.GetUninitializedObject(typeof(MechanicsContext));
                    var parameters = (AbilityParams)RuntimeHelpers.GetUninitializedObject(typeof(AbilityParams));
                    typeof(AbilityParams).GetField("<Metamagic>k__BackingField", Hidden).SetValue(parameters, meta);
                    typeof(MechanicsContext).GetField("m_Params", Hidden).SetValue(contextFixture, parameters);
                    return ContextValueHelper.CalculateDiceValue(Kingmaker.RuleSystem.DiceType.D6,
                        new ContextValue { ValueType = ContextValueType.Simple, Value = count },
                        new ContextValue { ValueType = ContextValueType.Simple, Value = bonus }, contextFixture);
                }
                Check(Native(Metamagic.Maximize, 2, 0) == 12 && Native(Metamagic.Maximize | Metamagic.Empower, 2, 0) == 18,
                    "real native calculator confirms Maximize/Empower from effective context params without a caster or random roll");
                Check(Native(Metamagic.Maximize | Metamagic.Empower, 0, 3) == 4,
                    "real native d6 context empowers its bonus even when the dice count is zero");
                foreach (var save in new[] { SavingThrowType.Will, SavingThrowType.Fortitude })
                {
                    var snow = Fuse("Snowball", "Sleep", "Snowball", save);
                    Check(Refusal(snow) == null && Of<object>(Plan(snow), "Delivery").ToString() == "Single",
                        $"Snowball/Sleep ({save}): one clicked recipient through one simple projectile (" + (Refusal(snow) ?? "ok") + ")");
                }
                var area = Fuse("Snowball", "Sleep", "Sleep", SavingThrowType.Will);
                Check(Refusal(area) == null && Of<object>(Plan(area), "Delivery").ToString() == "Around" &&
                      Of<AbilityTargetsAround>(Plan(area), "Selector") == Spell("Sleep").GetComponent<AbilityTargetsAround>(),
                    "Snowball/Sleep delivered as Sleep's burst: Sleep's own selector (" + (Refusal(area) ?? "ok") + ")");

                // 3. Admission: pool plan plus joint outcome value.
                foreach (var a in new[] { runes, Fuse("Snowball", "Sleep", "Snowball", SavingThrowType.Fortitude), area })
                {
                    var profile = AbilityProfile.Get(a); var hack = a.MagicHackData;
                    Check((bool)pooledFlag.GetValue(profile) && unsupported.GetValue(profile) == null && Withheld(a) == null &&
                          (bool)model.Invoke(null, new object[] { a, profile }),
                        $"{hack.Spell1.name}/{hack.Spell2.name} via {hack.DeliverBlueprint.name} ({hack.SavingThrowType}) is offered " +
                        $"(withheld: {Withheld(a) ?? "no"}, detail: {Detail(a) ?? reason.GetValue(profile) ?? "none"})");
                }

                // 4. Still withheld, each for its own reason.
                var noSave = Fuse("ExplosiveRunes", "Sleep", "ExplosiveRunes", SavingThrowType.Unknown);
                Check((string)unsupported.GetValue(AbilityProfile.Get(noSave)) == "independent nested saving throw",
                    "without a root save Sleep's own save is a second roll: the existing refusal stands");
                void Gate(AbilityData a, string expected, string label)
                {
                    string why = Withheld(a), because = Detail(a);
                    Check(why == "shared Hit Dice selection" && (because ?? "").Contains(expected),
                        $"{label} stays withheld for '{expected}' (withheld: {why ?? "no"}, detail: {because ?? "none"})");
                }
                Gate(Fuse("BurningHands", "Sleep", "BurningHands", SavingThrowType.Reflex), "cone, line, chain or repeated delivery", "a cone-delivered pool");
                Gate(Fuse("Sleep", "DeepSlumber", "Sleep", SavingThrowType.Will), "exactly one source spell", "two pool spells in one cast");

                // 5. Order inside the cast: damage dealt before the sleep exists does not wake it.
                bool Before(AbilityData a)
                {
                    var profile = AbilityProfile.Get(a);
                    var pooledBuffs = ((IEnumerable)typeof(AbilityProfile).GetProperty("SharedHdBuffs", Hidden).GetValue(profile)).Cast<object>().ToList();
                    object sleep = ((IEnumerable)applications.GetValue(profile)).Cast<object>()
                        .First(o => pooledBuffs.Contains(o.GetType().GetField("Buff").GetValue(o)));
                    return (bool)before.Invoke(profile, new object[] { sleep });
                }
                Check(Before(runes), "Explosive Runes then Sleep: the damage precedes the sleep");
                Check(!Before(Fuse("Sleep", "ExplosiveRunes", "ExplosiveRunes", SavingThrowType.Will)), "Sleep then Explosive Runes: the damage would wake the sleeper, no control is credited");

                // 6. Compiled routes.
                bool Calls(MethodInfo m, string name) => m != null && SaveBoostCheck.ReferencedMembers(m).Any(x => x.Name == name);
                Check(engine.GetMethods(Hidden).Any(m => m.Name == "ScoreAbility" &&
                          Calls(m, "ForecastWalkingAreaAllies") && Calls(m, "AlliesWalkingInto")),
                    "shared TB/RT scoring uses the production gate and existing walking-in reader");
                Check(Calls(control.GetMethod("TryForAim", Hidden), "TryForFusedAim"), "a fused aim is forecast by the fused pool reader, not refused");
                var fused = control.GetMethod("TryForFusedAim", Hidden);
                Check(SaveBoostCheck.ReferencedMembers(fused).Count(m => m.DeclaringType == typeof(MechanicsContext) && m.Name == "HasMetamagic") == 2 &&
                      !SaveBoostCheck.ReferencedMembers(fused).Any(m => m.DeclaringType == typeof(AbilityData) && m.Name == "HasMetamagic"),
                    "fused pool uses effective context Empower/Maximize rather than raw ability flags");
                Check(Calls(fused, "TryForecast") && Calls(fused, "CreateContext") && Calls(fused, "Select") && !Calls(fused, "Trigger") && !Calls(fused, "Run"),
                    "the fused forecast reads one context and the native selector; it runs no action or rule");
                Check(Calls(withheld, "FusedRefusal") && Calls(withheld, "FusionOutcomeModel"), "admission needs the pool plan and the joint value");
                var rounds = engine.GetMethod("TryFusionControlRounds", Hidden);
                Check(Calls(rounds, "Eligibility") && Calls(rounds, "PooledRounds") && Calls(rounds, "FusionDamageBefore"),
                    "the control reader weighs the pooled sleep by the forecast and by the cast's own damage order");
                var share = (Func<float, float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float, float>),
                    typeof(ValueModel).GetMethod("PooledRounds", Hidden));
                Check(Math.Abs(share(1f, 3f, 0.4f) - 1.8f) < 1e-5f && share(1f, 3f, 0f) == 1f && share(1f, 3f, 1f) == 3f,
                    "a pooled control joins the envelope with its chance");
            }
            catch (Exception ex) { bad++; lines.Add("FUSION-POOL FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"FUSION-POOL {checks} checks, failures {bad}; graphs, metadata, compiled routes and native Max/Empower on inert params-only contexts; no actor, selection, cast or Unity timing");
            return lines;
        }
    }
}
