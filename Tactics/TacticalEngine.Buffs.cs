// ★ v0.3.19 (test11): helpful buffs valued by what they do for the one who gets them, in damage-equivalent points
// (docs/SHORT_BUFFS_AUDIT.md). The old value was spell level x rounds x a 0..1 "quality" (the best component's class, its
// size ignored): Light of the Angels' 1 temporary hit point scored 10-23 at 96 % HP, Enlarge Person 10.1 in round 1 of
// nearly every fight whoever got it, Guidance's +1 on one roll as much as a +4 for a minute.
//
// Now each part of the buff (Analysis/BuffParts) is evaluated with the caster's real context - the buff's MechanicsContext
// under the ability's execution context, as the game builds it when the spell lands (caster level, ranks) - and valued:
//  - attack, damage, extra attacks (and Strength / Dexterity / size through them): what they add to the recipient's own
//    weapon attacks against the enemy it would attack (CombatMath.EstimateWeaponAttack), half for a recipient that also
//    casts spells;
//  - AC, concealment, miss chance, mirror images, temporary HP, damage reduction, saves: the damage they keep off it, from
//    what comes at it (IncomingDamage: the enemies' threat, or what really hit it lately) and the strongest enemy's hit
//    chance against it, priced as HP kept and a lower chance to drop (ValueModel.PreventionValue);
//  - over the useful rounds: the real duration (DurationValue with the ability's context, Extend included), the rounds the
//    fight is expected to go on, one attack or one roll for buffs spent by it.
// Bonus types follow the game's stacking rule: a non-stacking bonus counts only what it adds over the recipient's best
// bonus of that type. A buff none of whose parts can be read keeps the old formula at 60 % (it is not understood).
using System;
using System.Collections.Generic;
using System.Linq;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using UnityEngine;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>The value of a buff for one recipient, and why.</summary>
        internal sealed class BuffAppraisal
        {
            public float Value;
            /// <summary>At least one part was valued (false: nothing readable, the old formula applies).</summary>
            public bool Modelled;
            public float Rounds;
            public string Detail = "";
        }

        /// <summary>What the recipient's weapon attacks do now (per frame).</summary>
        private sealed class Offence
        {
            public float DamagePerRound, HitChance, PerHit, HitsPerRound, FirstAttack;
            public bool Ranged, TwoHanded, StrengthToDamage, Caster;
            /// <summary>What one size step up adds to a hit: the weapon's dice scaled by the game's table.</summary>
            public float SizeUpDice = 1f;
        }

        /// <summary>What comes at the recipient now (per frame).</summary>
        private sealed class Defence
        {
            public float Incoming, EnemyHitChance = 0.6f, PerHit, Hp, Worth;
            /// <summary>Nothing reaches it yet: <see cref="Incoming"/> is its expected share.</summary>
            public bool Expected;
        }

        private static int s_AppraisalFrame = -1;
        private static readonly Dictionary<string, Offence> s_Offence = new Dictionary<string, Offence>();
        private static readonly Dictionary<string, Defence> s_Defence = new Dictionary<string, Defence>();
        /// <summary>The last appraisal per "caster|ability|recipient", for the decision log.</summary>
        private static readonly Dictionary<string, string> s_LastAppraisal = new Dictionary<string, string>();

        private static void AppraisalFrame()
        {
            int frame = Time.frameCount;
            if (frame == s_AppraisalFrame) return;
            s_AppraisalFrame = frame;
            s_Offence.Clear();
            s_Defence.Clear();
            if (s_LastAppraisal.Count > 512) s_LastAppraisal.Clear();
        }

        private static string AppraisalKey(UnitEntityData caster, AbilityData ability, UnitEntityData recipient) =>
            $"{caster.UniqueId}|{CombatMemory.AbilityKey(ability)}|{recipient?.UniqueId}";

        /// <summary>
        /// The value of <paramref name="buffs"/> (what <paramref name="ability"/> puts on <paramref name="ally"/>) for that
        /// ally, or an appraisal with Modelled = false when none of their parts can be read.
        /// </summary>
        internal static BuffAppraisal AppraiseBuff(TacticalContext ctx, AbilityData ability, AbilityProfile profile,
                                                   List<BlueprintBuff> buffs, UnitEntityData ally)
        {
            var result = new BuffAppraisal();
            AppraisalFrame();
            var parts = new List<BuffPart>();
            var unread = new HashSet<string>();
            foreach (var b in buffs)
            {
                var read = BuffPartsReader.Read(b);
                parts.AddRange(read.Parts);
                foreach (var u in read.Unread) unread.Add(u);
            }
            // Mental stats (spell DCs) are not valued here: a buff of only those keeps the old reading.
            if (!parts.Any(p => p.Kind != BuffPartKind.MentalStat)) return result;
            result.Modelled = true;

            var abilityCtx = CombatMath.CreateContext(ability, new Kingmaker.Utility.TargetWrapper(ally));
            int casterLevel = 1;
            try { casterLevel = Math.Max(1, abilityCtx?.Params?.CasterLevel ?? 1); } catch { }

            // --- useful rounds ---
            bool oneAttack = profile.BuffsSpentByAttack || profile.BuffsSpentByRoll;
            float duration = buffs.Max(b => EffectiveRounds(ability, profile, b, abilityCtx));
            float unknown = profile.HasLongBuff ? ctx.ExpectedRounds : Math.Min(ctx.ExpectedRounds, 3f);
            float rounds = oneAttack || profile.BuffsLastOneRound ? 1f : ValueModel.UsefulRounds(duration, ctx.ExpectedRounds, unknown);
            return AppraiseParts(ctx, ability.Caster?.Unit ?? ctx.Unit, abilityCtx, casterLevel, buffs, parts, unread, ally, rounds,
                                 oneAttack, duration, AppraisalKey(ctx.Unit, ability, ally), false, result);
        }

        /// <summary>
        /// ★ v0.3.19 (class audit B1): what the read parts of <paramref name="buffs"/> are worth to <paramref name="ally"/>
        /// over <paramref name="rounds"/>, with <paramref name="caster"/>'s numbers (<paramref name="sourceCtx"/>: the
        /// ability's context, or a toggle's). Shared by spells (AppraiseBuff) and toggles (AppraiseToggleBuffs).
        /// <paramref name="ignoreForbid"/>: the caller prices a spell prohibition itself (a toggle's own rule).
        /// </summary>
        private static BuffAppraisal AppraiseParts(TacticalContext ctx, UnitEntityData caster, MechanicsContext sourceCtx, int casterLevel,
                                                   List<BlueprintBuff> buffs, List<BuffPart> parts, HashSet<string> unread,
                                                   UnitEntityData ally, float rounds, bool oneAttack, float duration, string key,
                                                   bool ignoreForbid, BuffAppraisal result)
        {
            var abilityCtx = sourceCtx;
            result.Modelled = true;
            result.Rounds = rounds;

            var off = OffenceOf(ctx, ally);
            var def = DefenceOf(ctx, ally);
            var notes = new List<string>();

            // --- what each part adds, with this caster's numbers ---
            float attack = 0f, damage = 0f, extraAttacks = 0f, armor = 0f, concealment = 0f, miss = 0f, saves = 0f;
            float tempHp = 0f, images = 0f, physicalDr = 0f, energyResist = 0f, conHp = 0f;
            bool sizeUp = false, forbidsSpells = false;
            foreach (var p in parts)
            {
                float amount = PartAmount(p, caster, abilityCtx, ally, casterLevel);
                if (p.Conditional) amount *= 0.5f;
                switch (p.Kind)
                {
                    case BuffPartKind.Attack:
                        if (p.RangedOnly && !off.Ranged || p.MeleeOnly && off.Ranged) break;
                        attack += Increment(ally, StatType.AdditionalAttackBonus, p, amount);
                        break;
                    case BuffPartKind.Damage:
                        if (p.RangedOnly && !off.Ranged || p.MeleeOnly && off.Ranged) break;
                        damage += Increment(ally, StatType.AdditionalDamage, p, amount);
                        break;
                    case BuffPartKind.ArmorClass: armor += Increment(ally, StatType.AC, p, amount); break;
                    case BuffPartKind.ConditionalAC: armor += amount; break;
                    case BuffPartKind.Saves:
                        saves += p.SaveShare * (p.SaveShare < 1f ? Increment(ally, p.Stat, p, amount) : Increment(ally, StatType.SaveWill, p, amount));
                        break;
                    case BuffPartKind.ConditionalSaves: saves += amount; break;
                    case BuffPartKind.Strength:
                    {
                        float s = Increment(ally, StatType.Strength, p, amount) / 2f;   // ability modifier
                        if (!off.Ranged) attack += s;
                        if (off.StrengthToDamage) damage += s * (off.TwoHanded ? 1.5f : 1f);
                        break;
                    }
                    case BuffPartKind.Dexterity:
                    {
                        float d = Increment(ally, StatType.Dexterity, p, amount) / 2f;
                        if (off.Ranged) attack += d;
                        armor += d * 0.5f;   // armour may cap it
                        break;
                    }
                    case BuffPartKind.Constitution: conHp += Increment(ally, StatType.Constitution, p, amount) / 2f * Math.Max(1, ally.Descriptor.Progression.CharacterLevel); break;
                    case BuffPartKind.TempHp: tempHp = Math.Max(tempHp, Increment(ally, StatType.TemporaryHitPoints, p, amount)); break;
                    case BuffPartKind.Concealment: concealment = Math.Max(concealment, amount / 100f); break;
                    case BuffPartKind.MissChance: miss = Math.Max(miss, amount / 100f); break;
                    case BuffPartKind.MirrorImages: images = Math.Max(images, amount); break;
                    case BuffPartKind.PhysicalDR: physicalDr = Math.Max(physicalDr, amount); break;
                    case BuffPartKind.EnergyResist: energyResist = Math.Max(energyResist, amount); break;
                    case BuffPartKind.ExtraAttack: extraAttacks = Math.Max(extraAttacks, amount); break;
                    case BuffPartKind.SizeUp: sizeUp = CombatMath.GrownCorpulence(ally, buffs) > 0f; break;
                    case BuffPartKind.ForbidsSpells: forbidsSpells = !ignoreForbid; break;
                }
            }
            if (sizeUp)
            {
                // A size bigger: -1 attack and AC (the game's size modifier); the weapon is the wielder's size
                // (ItemEntityWeapon.Size), so its dice go one step up - bows too (WeaponDamageScaleTable).
                attack -= 1f;
                armor -= 1f;
                damage += off.SizeUpDice;
            }
            if (forbidsSpells && off.Caster)
            {
                result.Value = 0f;
                result.Detail = "stops its spells";
                return result;
            }

            // --- offence: the recipient's own attacks ---
            float offencePerRound = 0f;
            if (off.DamagePerRound > 0f)
            {
                float d = oneAttack ? off.FirstAttack : off.DamagePerRound;
                float hits = oneAttack ? off.HitChance : off.HitsPerRound;
                offencePerRound = ValueModel.AttackBonusGain(d, off.HitChance, attack) + ValueModel.DamageBonusGain(hits, damage) +
                                  (oneAttack ? 0f : extraAttacks * off.FirstAttack);
                // A recipient that also casts spells uses its weapon only part of the time.
                if (off.Caster) offencePerRound *= 0.5f;
                if (attack != 0f) notes.Add($"attack {attack:+0.#;-0.#}");
                if (damage != 0f) notes.Add($"damage {damage:+0.#;-0.#} x{hits:0.#} hits");
                if (extraAttacks > 0f && !oneAttack) notes.Add($"+{extraAttacks:0} attack");
                if (offencePerRound != 0f) notes.Add($"({off.DamagePerRound:0.#} dmg/rd at {off.HitChance:P0}{(off.Caster ? ", half: casts" : "")})");
            }
            else if (attack > 0f || damage > 0f || extraAttacks > 0f)
                notes.Add("no weapon attack to improve");

            // --- defence: what comes at it ---
            float keptPerRound = 0f;
            float total = rounds;
            if (def.Incoming > 0f)
            {
                keptPerRound += ValueModel.ArmorGain(def.Incoming, def.EnemyHitChance, armor);
                keptPerRound += def.Incoming * Math.Max(concealment, miss);
                keptPerRound += def.Incoming * 0.025f * saves;
                float hitsOnIt = def.PerHit > 0f ? def.Incoming / def.PerHit : 1f;
                if (physicalDr > 0f) keptPerRound += Math.Min(def.Incoming * 0.8f, physicalDr * hitsOnIt);
                if (energyResist > 0f) keptPerRound += Math.Min(def.Incoming * 0.15f, energyResist * hitsOnIt);
                float absorbed = ValueModel.TempHpAbsorbed(tempHp + conHp * 0.5f, def.Incoming, total) +
                                 ValueModel.MirrorImagesAbsorbed(images, hitsOnIt, def.PerHit > 0f ? def.PerHit : def.Incoming, total);
                keptPerRound += absorbed / Math.Max(1f, total);
                if (armor != 0f) notes.Add($"AC {armor:+0.#;-0.#}");
                if (concealment > 0f || miss > 0f) notes.Add($"miss {Math.Max(concealment, miss):P0}");
                if (saves != 0f) notes.Add($"saves {saves:+0.#;-0.#}");
                if (tempHp > 0f) notes.Add($"{tempHp:0} temp HP");
                if (images > 0f) notes.Add($"{images:0.#} images");
                if (physicalDr > 0f) notes.Add($"DR {physicalDr:0}");
                if (keptPerRound != 0f) notes.Add($"({(def.Expected ? "expects" : "takes")} {def.Incoming:0.#}/rd, hit at {def.EnemyHitChance:P0})");
            }
            else if (armor > 0f || concealment > 0f || tempHp > 0f || images > 0f || physicalDr > 0f)
                notes.Add("nothing attacks it now");

            float offenceValue = offencePerRound * rounds;
            float defenceValue = ValueModel.PreventionValue(keptPerRound, rounds, def.Hp, def.Incoming, def.Worth);
            // A summon is temporary and replaceable: half (as everywhere, ValueModel.SummonFactor).
            result.Value = (offenceValue + defenceValue) * RoleWeight(ctx, "buff") * (IsSummon(ally) ? ValueModel.SummonFactor : 1f);
            result.Detail = $"{string.Join(", ", notes)} over {rounds:0.#} rd" +
                            (duration > 0f && !oneAttack ? $" (lasts {duration:0.#})" : oneAttack ? " (one attack/roll)" : "") +
                            (unread.Count > 0 ? $"; not read: {string.Join(",", unread.Take(3))}" : "");
            s_LastAppraisal[key] = $"{result.Value:0.#}: {result.Detail}";
            return result;
        }

        /// <summary>
        /// The real duration in rounds the ability gives <paramref name="buff"/>: its DurationValue worked out with the
        /// ability's context (caster level, ranks), doubled by Extend; -1 when it cannot be known (copies a parent).
        /// </summary>
        private static float EffectiveRounds(AbilityData ability, AbilityProfile profile, BlueprintBuff buff,
                                             Kingmaker.UnitLogic.Abilities.AbilityExecutionContext abilityCtx)
        {
            try
            {
                var apply = profile.ApplicationOf(buff);
                if (apply == null) return -1f;
                if (apply.Permanent) return 100f;
                if (apply.SameDuration) return -1f;
                if (apply.UseDurationSeconds) return Math.Max(1f, apply.DurationSeconds / 6f);
                var d = apply.DurationValue;
                if (d == null) return -1f;
                float rate = d.Rate == DurationRate.Rounds ? 1f : d.Rate == DurationRate.Minutes ? 10f :
                             d.Rate == DurationRate.TenMinutes ? 100f : d.Rate == DurationRate.Hours ? 600f : 14400f;
                int sides = d.DiceType.Sides();
                float dice = sides <= 0 ? 0f : CombatMath.EvaluateValue(d.DiceCountValue, abilityCtx, 0) * (sides + 1) / 2f;
                float rounds = (dice + CombatMath.EvaluateValue(d.BonusValue, abilityCtx, 0)) * rate;
                bool extended = false;
                try { extended = d.IsExtendable && ability.HasMetamagic(Kingmaker.UnitLogic.Abilities.Metamagic.Extend); } catch { }
                if (extended) rounds *= 2f;
                return rounds > 0f ? rounds : -1f;
            }
            catch
            {
                return -1f;
            }
        }

        /// <summary>The size of one part with the caster's numbers (the buff's ranks under the ability's context).</summary>
        private static float PartAmount(BuffPart p, UnitEntityData caster, MechanicsContext abilityCtx, UnitEntityData ally, int casterLevel)
        {
            float amount = p.Fixed;
            if (p.ScaleByBaseAttack) amount *= 1 + (ally.Stats.BaseAttackBonus.ModifiedValue / 4);
            MechanicsContext buffCtx = null;
            if (p.Value != null && p.Value.ValueType != ContextValueType.Simple || p.Dice != null || p.Pool != null)
            {
                try
                {
                    buffCtx = new MechanicsContext(caster, ally.Descriptor, p.Owner, abilityCtx);
                    buffCtx.RecalculateRanks();
                }
                catch { buffCtx = abilityCtx; }
            }
            if (p.Value != null)
            {
                int v = p.Value.ValueType == ContextValueType.Simple ? p.Value.Value
                      : p.Value.ValueType == ContextValueType.Shared ? SharedAverage(p.Owner, buffCtx, p.Value.ValueShared, casterLevel)
                      : CombatMath.EvaluateValue(p.Value, buffCtx, 0);
                amount += v * p.Multiplier;
            }
            if (p.Dice != null) amount += CombatMath.EvaluateDice(p.Dice, buffCtx, casterLevel);
            if (p.Kind == BuffPartKind.TempHp && p.Formula.Rolls > 0)
            {
                amount += p.Formula.Rolls * (p.Formula.Dice.Sides() + 1) / 2f;
                if (p.FormulaBonusByCasterLevel && p.Value == null) amount += casterLevel;
            }
            if (p.PerRecipientLevel) amount *= Math.Max(1, ally.Descriptor?.Progression?.CharacterLevel ?? 1);
            if (p.Kind == BuffPartKind.MirrorImages && p.Cap > 0) amount = Math.Min(amount, p.Cap);
            if (p.Pool != null)
            {
                int pool = CombatMath.EvaluateValue(p.Pool, buffCtx, 0);
                if (pool > 0) amount = Math.Min(amount, pool);
            }
            return amount;
        }

        /// <summary>The average of the buff's own shared value (ContextCalculateSharedValue: dice + bonus, x modifier).</summary>
        private static int SharedAverage(BlueprintBuff owner, MechanicsContext ctx, AbilitySharedValue type, int casterLevel)
        {
            try
            {
                foreach (var shared in Kingmaker.Blueprints.BlueprintExtenstions.GetComponents<ContextCalculateSharedValue>(owner))
                    if (shared.ValueType == type)
                        return (int)Math.Round(CombatMath.EvaluateDice(shared.Value, ctx, casterLevel) * shared.Modifier);
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// What <paramref name="amount"/> of a bonus adds on <paramref name="ally"/>: a bonus type that does not stack
        /// (ModifierDescriptorHelper.IsStackable, the game's rule, as patched by mods) counts only above the best bonus of
        /// that type it already has. Penalties count in full.
        /// </summary>
        private static float Increment(UnitEntityData ally, StatType stat, BuffPart p, float amount)
        {
            if (amount <= 0f) return amount;
            try
            {
                if (ModifierDescriptorHelper.IsStackable(p.Descriptor)) return amount;
                var modifiable = ally.Stats.GetStat(stat);
                if (modifiable == null) return amount;
                int existing = 0;
                foreach (var m in modifiable.Modifiers)
                    if (m.ModDescriptor == p.Descriptor) existing = Math.Max(existing, m.ModValue);
                return Math.Max(0f, amount - existing);
            }
            catch
            {
                return amount;
            }
        }

        /// <summary>
        /// The average damage a hit gains when <paramref name="weapon"/>'s wielder grows one size: the game's
        /// <c>WeaponDamageScaleTable</c> from the weapon's current size to the next. 1 when it cannot be read.
        /// </summary>
        private static float SizeUpDiceGain(ItemEntityWeapon weapon)
        {
            try
            {
                if (weapon == null) return 1f;
                var baseDice = weapon.Blueprint.BaseDamage;
                var now = weapon.Size;
                var bigger = WeaponDamageScaleTable.Scale(baseDice, now.Shift(1), Size.Medium, weapon.Blueprint);
                var current = WeaponDamageScaleTable.Scale(baseDice, now, Size.Medium, weapon.Blueprint);
                float Avg(DiceFormula f) => f.Rolls * (f.Dice.Sides() + 1) / 2f;
                return Mathf.Clamp(Avg(bigger) - Avg(current), 0f, 6f);
            }
            catch { return 1f; }
        }

        private static Offence OffenceOf(TacticalContext ctx, UnitEntityData ally)
        {
            if (s_Offence.TryGetValue(ally.UniqueId, out var cached)) return cached;
            var off = new Offence();
            try
            {
                off.Caster = ally.Descriptor.Spellbooks.Any(b => b.CasterLevel > 0 && b.MaxSpellLevel >= 1);
                // The enemy it would attack: the nearest one still fighting.
                var enemy = ctx.Enemies.Where(e => !CombatMath.IsDisabled(e))
                                       .OrderBy(e => Vector3.Distance(e.Position, ally.Position)).FirstOrDefault()
                            ?? ctx.Enemies.FirstOrDefault();
                if (enemy != null)
                {
                    var est = CombatMath.EstimateWeaponAttack(ally, enemy, full: true);
                    if (est != null && est.ExpectedDamage > 0f)
                    {
                        off.DamagePerRound = est.ExpectedDamage;
                        off.HitChance = Mathf.Clamp(est.FirstHitChance, 0.05f, 0.95f);
                        off.PerHit = est.FirstPerHit;
                        off.FirstAttack = est.FirstHitChance * est.FirstPerHit;
                        off.HitsPerRound = est.FirstPerHit > 0f ? est.ExpectedDamage / est.FirstPerHit : est.Attacks * off.HitChance;
                        off.Ranged = est.IsRanged;
                        off.TwoHanded = est.Weapon?.HoldInTwoHands ?? false;
                        off.SizeUpDice = SizeUpDiceGain(est.Weapon);
                    }
                }
                off.StrengthToDamage = CombatMath.StrengthAddsWeaponDamage(ally);
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Buff] offence of {ally.CharacterName}: {ex.Message}");
            }
            s_Offence[ally.UniqueId] = off;
            return off;
        }

        private static Defence DefenceOf(TacticalContext ctx, UnitEntityData ally)
        {
            if (s_Defence.TryGetValue(ally.UniqueId, out var cached)) return cached;
            var def = new Defence();
            try
            {
                def.Incoming = IncomingDamage(ctx, ally, ally.Position);
                // Nothing reaches it this instant (the fight has not closed in yet): its share of what the enemies still
                // fighting can deal, more for one that fights in melee - a buff cast now lasts into the rounds they meet.
                if (def.Incoming <= 0f && ctx.Allies.Count > 0)
                {
                    float threat = ctx.Enemies.Where(e => !CombatMath.IsDisabled(e)).Sum(e => CombatMath.Threat(e)) * ctx.DangerScale;
                    bool melee = !(ally.Body?.PrimaryHand?.MaybeWeapon?.Blueprint?.IsRanged ?? false);
                    def.Incoming = threat / ctx.Allies.Count * (melee ? 0.6f : 0.3f);
                    def.Expected = true;
                }
                def.Hp = Math.Max(1f, CombatMath.HpBeforeDown(ally));
                def.Worth = AllyWorth(ctx, ally);
                // The enemy that threatens it most: its hit chance and damage per hit against this ally.
                var top = IncomingParts(ctx, ally, ally.Position).OrderByDescending(p => p.Value).FirstOrDefault();
                if (top.Key != null)
                {
                    var est = CombatMath.EstimateWeaponAttack(top.Key, ally, full: false);
                    if (est != null && est.FirstHitChance > 0f)
                    {
                        def.EnemyHitChance = Mathf.Clamp(est.FirstHitChance, 0.05f, 0.95f);
                        def.PerHit = est.FirstPerHit;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Buff] defence of {ally.CharacterName}: {ex.Message}");
            }
            s_Defence[ally.UniqueId] = def;
            return def;
        }

        /// <summary>The last appraisal of this cast on this recipient this frame, for the decision log; null if none.</summary>
        /// <summary>
        /// ★ v0.3.19 (class audit B1, docs/CLASS_COMPETENCE_AUDIT.md F2): the same appraisal for what a toggle (a performance,
        /// a rage, a spirit) gives <paramref name="ally"/>, over <paramref name="rounds"/> (its resource and the fight). Its
        /// ranks come from <paramref name="toggleCtx"/> (the performer's context); a spell prohibition is priced by the toggle's
        /// own rule (ForbidSpellCasting with its ignore feature) for the performer; for another recipient it counts.
        /// </summary>
        internal static BuffAppraisal AppraiseToggleBuffs(TacticalContext ctx, MechanicsContext toggleCtx, string toggleKey,
                                                          List<BlueprintBuff> buffs, UnitEntityData ally, float rounds)
        {
            var result = new BuffAppraisal();
            AppraisalFrame();
            var parts = new List<BuffPart>();
            var unread = new HashSet<string>();
            foreach (var b in buffs)
            {
                var read = BuffPartsReader.Read(b);
                parts.AddRange(read.Parts);
                foreach (var u in read.Unread) unread.Add(u);
            }
            if (!parts.Any(p => p.Kind != BuffPartKind.MentalStat && p.Kind != BuffPartKind.ForbidsSpells)) return result;
            int level = Math.Max(1, ctx.Unit.Descriptor?.Progression?.CharacterLevel ?? 1);
            // The performer's own prohibition is priced by the toggle's rule (with its ignore feature); an ally in the area
            // who accepted it (Inspired Rage on a caster) loses its spells: that counts here.
            return AppraiseParts(ctx, ctx.Unit, toggleCtx, level, buffs, parts, unread, ally, rounds, false, -1f,
                                 $"{ctx.Unit.UniqueId}|{toggleKey}|{ally.UniqueId}", ally == ctx.Unit, result);
        }

        /// <summary>The note of a toggle's last valuation (all recipients), for the "[Buff]" line.</summary>
        internal static string LastToggleAppraisal(UnitEntityData caster, string toggleKey) =>
            s_LastAppraisal.TryGetValue($"{caster.UniqueId}|{toggleKey}|", out var s) ? s : null;

        internal static void NoteToggleAppraisal(UnitEntityData caster, string toggleKey, string note)
        {
            AppraisalFrame();
            s_LastAppraisal[$"{caster.UniqueId}|{toggleKey}|"] = note;
        }

        internal static string LastAppraisal(UnitEntityData caster, AbilityData ability, UnitEntityData recipient) =>
            s_LastAppraisal.TryGetValue(AppraisalKey(caster, ability, recipient), out var s) ? s : null;
    }
}
