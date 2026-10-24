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
using Kingmaker.UnitLogic;
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
            /// <summary>★ v0.3.20 (test3): what the enemy keeps of extra weapon damage (a Tiny swarm half of piercing and
            /// slashing: WeaponAttackEstimate.FirstPhysicalFactor).</summary>
            public float PhysicalFactor = 1f;
            /// <summary>★ v0.3.21: the weapon it attacks with and the enemy it would attack (weapon-limited parts, invisibility).</summary>
            public ItemEntityWeapon Weapon;
            public UnitEntityData Enemy;
            public int SneakDice;
            /// <summary>★ v0.3.21: the weapon's enhancement now, whether its threat range is doubled already (keen, Improved
            /// Critical), its base threat chance and critical multiplier (RuleCalculateWeaponStats), and whether the enemy
            /// can be critically hit.</summary>
            public int Enhancement;
            public bool DoubleEdge, CanCrit;
            public float BaseThreat, CritMultiplier = 2f;
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

        /// <summary>★ v0.3.20 (test5): the share of the enemies in the fight of this alignment (the game's own test,
        /// Alignment.HasComponent); half when there are none to ask.</summary>
        private static float EnemyAlignmentShare(TacticalContext ctx, Kingmaker.Enums.AlignmentComponent alignment)
        {
            try
            {
                if (ctx.Enemies == null || ctx.Enemies.Count == 0) return 0.5f;
                return ctx.Enemies.Count(e => Kingmaker.Enums.AlignmentExtension.HasComponent(e.Descriptor.Alignment.ValueRaw, alignment)) /
                       (float)ctx.Enemies.Count;
            }
            catch
            {
                return 0.5f;
            }
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
            var abilityCtx = CombatMath.CreateContext(ability, new Kingmaker.Utility.TargetWrapper(ally));
            // ★ v0.3.21: the weapon enchantments it puts on the recipient's weapon (Magic Weapon, Keen Edge, oils, Holy
            // Sword): their parts, for the weapon each one goes on. A buff that only carries one (Keen Edge's is empty) is
            // valued by the enchantment.
            float enchantRounds = EnchantParts(ctx, ability, profile, abilityCtx, buffs.FirstOrDefault(), ally, parts, unread);
            // Mental stats (spell DCs) are not valued here: a buff of only those (or of only a spell prohibition) keeps the
            // old reading.
            // ★ v0.3.21 (gaps): unless it is the stat this recipient's spell DCs come from.
            var dcStat = CastingStat(ally);
            if (!parts.Any(p => (p.Kind != BuffPartKind.MentalStat || (dcStat.HasValue && p.Stat == dcStat.Value)) &&
                                p.Kind != BuffPartKind.ForbidsSpells)) return result;
            result.Modelled = true;

            int casterLevel = 1;
            try { casterLevel = Math.Max(1, abilityCtx?.Params?.CasterLevel ?? 1); } catch { }

            // --- useful rounds ---
            bool oneAttack = profile.BuffsSpentByAttack || profile.BuffsSpentByRoll;
            float duration = buffs.Count > 0 ? buffs.Max(b => EffectiveRounds(ability, profile, b, abilityCtx)) : -1f;
            if (duration <= 0f && enchantRounds > 0f) duration = enchantRounds;
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
            float energyDamage = 0f, energyPool = 0f, diceUp = 0f, critExtra = 0f, damagePercent = 0f, fastHealing = 0f, sight = 0f;
            float spellDc = 0f, spellLevel = 0f, spellPenetration = 0f;
            var onHit = new List<BuffPart>();
            var castingStat = CastingStat(ally);
            bool sizeUp = false, forbidsSpells = false;
            BuffPart invisible = null;
            foreach (var p in parts)
            {
                // ★ v0.3.21: a part for some weapons only counts when the recipient's weapon in hand is one of them.
                if (p.Weapon != null && (off.Weapon?.Blueprint == null || !p.Weapon(off.Weapon.Blueprint))) continue;
                // ★ v0.3.21: a granted feature the recipient has already adds nothing (a Tactician's teamwork feat).
                if (p.Granted != null && ally.Descriptor.HasFact(p.Granted)) continue;
                float amount = PartAmount(p, caster, abilityCtx, ally, casterLevel);
                // ★ v0.3.20 (test5): a bonus against one kind of effect or creature counts for the share of the enemies it
                // works against; any other condition (a weapon kind) at half. Remove Fear's +4 against fear counted as +2
                // to every save for each ally in its burst - up to 8.6 in a fight with no fear (0.3.20-test4 log).
                if (p.Against != Kingmaker.Blueprints.Classes.Spells.SpellDescriptor.None) amount *= EnemyThreat.ShareUsing(p.Against);
                else if (p.AgainstAlignment.HasValue) amount *= EnemyAlignmentShare(ctx, p.AgainstAlignment.Value);
                else if (p.Conditional) amount *= 0.5f;
                // ★ v0.3.21 (gaps): an on-hit effect counts on the share of hits it fires on.
                amount *= p.Chance;
                if (p.CritOnly) amount *= Mathf.Clamp01(off.BaseThreat) * off.HitChance;
                if (p.FirstOnly) amount /= Math.Max(1f, off.HitsPerRound);
                switch (p.Kind)
                {
                    case BuffPartKind.OnHitBuff:
                    case BuffPartKind.OnHitKill:
                        onHit.Add(p);
                        break;
                    case BuffPartKind.Attack:
                        if (p.RangedOnly && !off.Ranged || p.MeleeOnly && off.Ranged) break;
                        attack += Increment(ally, StatType.AdditionalAttackBonus, p, amount);
                        break;
                    case BuffPartKind.Damage:
                        if (p.RangedOnly && !off.Ranged || p.MeleeOnly && off.Ranged) break;
                        if (p.Energy) energyDamage += amount;
                        else damage += Increment(ally, StatType.AdditionalDamage, p, amount);
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
                    case BuffPartKind.EnergyResist:
                        energyResist = Math.Max(energyResist, amount);
                        if (p.Pool != null) energyPool = Math.Max(energyPool, amount);   // ★ v0.3.21: absorbs this much in all
                        break;
                    case BuffPartKind.Invisibility:
                        if (invisible == null || invisible.EndsOnAttack) invisible = p;
                        break;
                    case BuffPartKind.WeaponEnhancement:
                    {
                        // Enhancement bonuses do not stack: only what it adds over the weapon's own (EnhancementValue).
                        float gain = ModifierDescriptorHelper.IsStackable(p.Descriptor) ? amount : Math.Max(0f, amount - off.Enhancement);
                        attack += gain;
                        damage += gain;
                        break;
                    }
                    case BuffPartKind.CritRangeDouble:
                        // The base threat range again (RuleCalculateWeaponStats.CriticalRange), confirmed at about the hit
                        // chance, the multiplier's extra damage: per hit.
                        if (!off.DoubleEdge && off.CanCrit)
                            critExtra = Math.Max(critExtra, off.BaseThreat * off.HitChance * Math.Max(0f, off.CritMultiplier - 1f) * off.PerHit *
                                                            (p.Conditional ? 0.5f : 1f));
                        break;
                    case BuffPartKind.FastHealing: fastHealing = Math.Max(fastHealing, amount); break;
                    case BuffPartKind.Sight: sight = Math.Max(sight, p.Fixed); break;
                    // ★ v0.3.21 (gaps): spellcasting boosts, and the stat its spells' DCs come from (Fox's Cunning on a wizard).
                    case BuffPartKind.SpellDC: spellDc += amount; break;
                    case BuffPartKind.CasterLevel: spellLevel += amount; break;
                    case BuffPartKind.SpellPenetration: spellPenetration += amount; break;
                    case BuffPartKind.MentalStat:
                        if (castingStat.HasValue && p.Stat == castingStat.Value)
                            spellDc += Increment(ally, p.Stat, p, amount) / 2f;   // the ability modifier
                        break;
                    case BuffPartKind.NaturalWeapon:
                        // ★ v0.3.21: claws for empty hands - the dice they add over the empty-hand attack it makes now; nothing
                        // while the hands hold a weapon (the game only arms empty hands with them).
                        if (off.Weapon != null && !off.Weapon.Blueprint.IsUnarmed && !off.Weapon.Blueprint.IsNatural)
                            notes.Add("hands hold a weapon");
                        else if (p.Natural != null)
                        {
                            float claw = DiceAverage(p.Natural.BaseDamage);
                            float now = off.Weapon != null ? DiceAverage(off.Weapon.Blueprint.BaseDamage) : 1.5f;
                            if (claw > now) { damage += claw - now; notes.Add($"{p.Natural.name}"); }
                        }
                        break;
                    case BuffPartKind.DamagePercent:
                        if (p.RangedOnly && !off.Ranged || p.MeleeOnly && off.Ranged) break;
                        damagePercent = Math.Max(damagePercent, amount / 100f);
                        break;
                    case BuffPartKind.WeaponDiceUp:
                        if (off.Weapon != null && amount > 0f) diceUp = Math.Max(diceUp, SizeUpDiceGain(off.Weapon, Mathf.RoundToInt(amount)));
                        break;
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
            damage += diceUp;
            if (critExtra > 0f) { energyDamage += critExtra; notes.Add("keen"); }   // not a damage bonus: no swarm halving twice
            // ★ v0.3.21: invisible (BuffInvisibility; UnitPartConcealment.Calculate, RuleCalculateAttackBonus,
            // RuleCheckTargetFlatFooted): enemies that cannot see invisible miss it half the time, and it attacks them at +2
            // while they lose their Dexterity to AC (sneak attack included). The kind an attack ends counts once: its
            // first attack and one round of cover. Nothing when it is invisible that way already.
            float invisibleShare = 0f, invisibleOnce = 0f;
            if (invisible != null && !AlreadyInvisible(ally, invisible))
            {
                invisibleShare = UnseenShare(ctx);
                if (invisible.Conditional) invisibleShare *= 0.5f;
                if (invisibleShare > 0f)
                {
                    int dex = 0;
                    try { dex = off.Enemy != null ? Mathf.Clamp(off.Enemy.Stats.Dexterity.Bonus, 0, 6) : 0; } catch { }
                    float bonus = 2f + dex;
                    float sneak = off.SneakDice > 0 && off.Enemy != null && !(off.Enemy.CombatState?.IsFlanked ?? false) ? 3.5f * off.SneakDice : 0f;
                    if (invisible.EndsOnAttack)
                        invisibleOnce = invisibleShare * (ValueModel.AttackBonusGain(off.FirstAttack, off.HitChance, bonus) + off.HitChance * sneak);
                    else
                    {
                        attack += bonus * invisibleShare;
                        damage += sneak * invisibleShare;
                    }
                    notes.Add($"unseen by {invisibleShare:P0}{(invisible.EndsOnAttack ? " until it attacks" : "")}");
                }
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
                offencePerRound = ValueModel.AttackBonusGain(d, off.HitChance, attack) + ValueModel.DamageBonusGain(hits, damage * off.PhysicalFactor) +
                                  ValueModel.DamageBonusGain(hits, energyDamage) + (oneAttack ? 0f : extraAttacks * off.FirstAttack) +
                                  d * damagePercent;   // ★ v0.3.21: a share more of what its attacks deal (Dragon Might)
                if (damagePercent > 0f) notes.Add($"damage +{damagePercent:P0}");
                // A recipient that also casts spells uses its weapon only part of the time.
                if (off.Caster) offencePerRound *= 0.5f;
                if (attack != 0f) notes.Add($"attack {attack:+0.#;-0.#}");
                if (damage != 0f || energyDamage != 0f) notes.Add($"damage {damage + energyDamage:+0.#;-0.#} x{hits:0.#} hits");
                if (extraAttacks > 0f && !oneAttack) notes.Add($"+{extraAttacks:0} attack");
                if (offencePerRound != 0f) notes.Add($"({off.DamagePerRound:0.#} dmg/rd at {off.HitChance:P0}{(off.Caster ? ", half: casts" : "")})");
            }
            else if (attack > 0f || damage > 0f || extraAttacks > 0f || energyDamage > 0f || damagePercent > 0f)
                notes.Add("no weapon attack to improve");
            // ★ v0.3.21 (gaps): spellcasting boosts - for a recipient that casts, by how much better its spells land
            // (ValueModel.SpellcastingGain), its casting worth per round by its caster level.
            if ((spellDc > 0f || spellLevel > 0f || spellPenetration > 0f) && castingStat.HasValue)
            {
                int level = 1;
                try { level = Math.Max(1, ally.Descriptor.Spellbooks.Max(b => b.CasterLevel)); } catch { }
                float resistant = 0f;
                try
                {
                    var fighting = ctx.Enemies.Where(e => !CombatMath.IsDisabled(e)).ToList();
                    if (fighting.Count > 0) resistant = fighting.Count(e => (e.Get<Kingmaker.UnitLogic.Parts.UnitPartSpellResistance>()?.GetValue((Kingmaker.UnitLogic.Mechanics.MechanicsContext)null) ?? 0) > 0) / (float)fighting.Count;
                }
                catch { }
                float gain = ValueModel.SpellcastingGain(ValueModel.PerRound(level), spellDc, spellLevel, spellPenetration, resistant);
                if (gain > 0f)
                {
                    offencePerRound += gain;
                    notes.Add($"spells{(spellDc > 0f ? $" DC +{spellDc:0.#}" : "")}{(spellLevel > 0f ? $" CL +{spellLevel:0.#}" : "")}" +
                              $"{(spellPenetration > 0f ? $" pen +{spellPenetration:0.#}" : "")}");
                }
            }
            else if (spellDc > 0f || spellLevel > 0f || spellPenetration > 0f) notes.Add("casts no spells");
            // ★ v0.3.21: See Invisibility / True Seeing - the miss chance it takes away from its attacks on the enemies now:
            // invisible ones (50 %), and Blur / Displacement for True Seeing (UnitPartConcealment.Calculate).
            if (sight > 0f)
            {
                float removable = RemovableMiss(ctx, ally, sight >= 2f);
                if (removable > 0f && off.DamagePerRound > 0f)
                {
                    offencePerRound += (oneAttack ? off.FirstAttack : off.DamagePerRound) * removable * (off.Caster ? 0.5f : 1f);
                    notes.Add($"sees through {removable:P0} miss");
                }
                else notes.Add("nothing hidden to see");
            }
            if (off.Caster) invisibleOnce *= 0.5f;

            // --- defence: what comes at it ---
            float keptPerRound = 0f;
            float total = rounds;
            if (def.Incoming > 0f)
            {
                keptPerRound += ValueModel.ArmorGain(def.Incoming, def.EnemyHitChance, armor);
                // ★ v0.3.21: invisibility's cover - one round for the kind its first attack ends.
                float unseen = invisibleShare * 0.5f * (invisible != null && invisible.EndsOnAttack ? 1f / Math.Max(1f, total) : 1f);
                keptPerRound += def.Incoming * Math.Max(Math.Max(concealment, miss), unseen);
                // ★ v0.3.21: immunities read as large save bonuses; together at most half of what comes at it.
                keptPerRound += def.Incoming * 0.025f * Math.Min(saves, 20f);
                float hitsOnIt = def.PerHit > 0f ? def.Incoming / def.PerHit : 1f;
                // ★ v0.3.21: fast healing gives back what it loses, up to its rate, each round.
                if (fastHealing > 0f) keptPerRound += Math.Min(def.Incoming, fastHealing);
                if (physicalDr > 0f) keptPerRound += Math.Min(def.Incoming * 0.8f, physicalDr * hitsOnIt);
                // ★ v0.3.21: a pool (Protection from Energy) absorbs that much over the whole buff, not per round.
                if (energyResist > 0f)
                    keptPerRound += Math.Min(Math.Min(def.Incoming * 0.15f, energyResist * hitsOnIt),
                                             energyPool > 0f ? energyPool / Math.Max(1f, total) : float.MaxValue);
                float absorbed = ValueModel.TempHpAbsorbed(tempHp + conHp * 0.5f, def.Incoming, total) +
                                 ValueModel.MirrorImagesAbsorbed(images, hitsOnIt, def.PerHit > 0f ? def.PerHit : def.Incoming, total);
                keptPerRound += absorbed / Math.Max(1f, total);
                if (armor != 0f) notes.Add($"AC {armor:+0.#;-0.#}");
                if (concealment > 0f || miss > 0f) notes.Add($"miss {Math.Max(concealment, miss):P0}");
                if (saves != 0f) notes.Add($"saves {saves:+0.#;-0.#}");
                if (tempHp > 0f) notes.Add($"{tempHp:0} temp HP");
                if (images > 0f) notes.Add($"{images:0.#} images");
                if (physicalDr > 0f) notes.Add(physicalDr >= 999f ? "immune to physical damage" : $"DR {physicalDr:0}");
                if (energyResist > 0f) notes.Add(energyPool > 0f ? $"absorbs {energyPool:0} energy" : $"resist {energyResist:0}");
                if (keptPerRound != 0f) notes.Add($"({(def.Expected ? "expects" : "takes")} {def.Incoming:0.#}/rd, hit at {def.EnemyHitChance:P0})");
            }
            else if (armor > 0f || concealment > 0f || tempHp > 0f || images > 0f || physicalDr > 0f || energyResist > 0f)
                notes.Add("nothing attacks it now");

            // ★ v0.3.21 (gaps): what its hits do to the enemy it would attack - a debuff (as the control section values one)
            // for the rounds it lasts, a kill once - by the chance that at least one hit lands each round.
            float onHitOnce = 0f;
            if (onHit.Count > 0 && off.DamagePerRound > 0f && off.Enemy != null)
            {
                float perHitChance = Mathf.Clamp01(off.HitChance);
                int attacks = Math.Max(1, Mathf.RoundToInt(off.HitsPerRound / Math.Max(0.05f, perHitChance)));
                float onHitRound = 0f;
                foreach (var p in onHit)
                {
                    float h = perHitChance * p.Chance * (p.CritOnly ? Mathf.Clamp01(off.BaseThreat) * perHitChance : 1f);
                    float landed = p.FirstOnly ? h : 1f - Mathf.Pow(1f - h, attacks);
                    float fail = p.HitSave != SavingThrowType.Unknown ? 0.5f : 1f;
                    float w = landed * fail * (p.Conditional ? 0.5f : 1f) * (off.Caster ? 0.5f : 1f);
                    if (w <= 0f) continue;
                    if (p.Kind == BuffPartKind.OnHitKill)
                        onHitOnce = Math.Max(onHitOnce, w * CombatMath.TargetWorth(off.Enemy));
                    else if (p.HitBuff != null && AbilityProfile.IsHarmfulBuff(p.HitBuff) && !off.Enemy.Buffs.HasFact(p.HitBuff) &&
                             !CombatMath.BuffImmune(off.Enemy, p.HitBuff, null))
                        onHitRound = Math.Max(onHitRound, w * CombatMath.ControlWorth(off.Enemy) *
                                                          HarmShareIn(ctx, abilityCtx, off.Enemy, new List<BlueprintBuff> { p.HitBuff }));
                }
                if (onHitRound > 0f) { offencePerRound += onHitRound; notes.Add($"hits hinder {off.Enemy.CharacterName}"); }
                if (onHitOnce > 0f) notes.Add($"hits may kill {off.Enemy.CharacterName}");
            }

            float offenceValue = offencePerRound * rounds + invisibleOnce + onHitOnce;
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
                return DurationRounds(apply.DurationValue, ability, abilityCtx);
            }
            catch
            {
                return -1f;
            }
        }

        /// <summary>A ContextDurationValue in rounds with the ability's context, doubled by Extend; -1 when unknown.</summary>
        private static float DurationRounds(ContextDurationValue d, AbilityData ability, MechanicsContext abilityCtx)
        {
            if (d == null) return -1f;
            try
            {
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

        /// <summary>
        /// ★ v0.3.21: adds the parts of the weapon enchantments <paramref name="profile"/> puts on <paramref name="ally"/>'s
        /// weapon (AbilityProfile.WeaponEnchants), each limited to the weapons it goes on (EnhanceWeapon.ApplyMagicFang /
        /// ApplyMagicWeapon, the main hand slot), and returns their longest duration in rounds (-1: none or unknown).
        /// </summary>
        private static float EnchantParts(TacticalContext ctx, AbilityData ability, AbilityProfile profile, MechanicsContext abilityCtx,
                                          BlueprintBuff owner, UnitEntityData ally, List<BuffPart> parts, HashSet<string> unread)
        {
            float rounds = -1f;
            if (profile.WeaponEnchants.Count == 0) return rounds;
            var caster = ability.Caster?.Unit ?? ctx.Unit;
            foreach (var effect in profile.WeaponEnchants)
            {
                if (effect.OnCaster && ally != caster) continue;
                var enchantment = effect.Enchantments[0];
                if (effect.Greater)
                {
                    int level = CombatMath.EvaluateValue(effect.EnchantLevel, abilityCtx, 1);
                    enchantment = effect.Enchantments[Mathf.Clamp(level, 1, effect.Enchantments.Length) - 1];
                }
                var read = BuffPartsReader.ReadEnchantment(enchantment);
                Func<Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon, bool> on =
                    effect.Natural ? (w => w.IsNatural || w.IsUnarmed)
                    : effect.OffHand ? (w => w.IsTwoHanded && !w.IsNatural)
                    : (Func<Kingmaker.Blueprints.Items.Weapons.BlueprintItemWeapon, bool>)(w => !w.IsNatural);
                foreach (var part in read.Parts) parts.Add(part.ForWeapons(on, owner));
                foreach (var u in read.Unread) unread.Add(u);
                float r = effect.Permanent ? 100f : DurationRounds(effect.Duration, ability, abilityCtx);
                rounds = Math.Max(rounds, r);
            }
            return rounds;
        }

        /// <summary>
        /// ★ v0.3.21 (verification audit): the share of <paramref name="enemy"/>'s worth per round that
        /// <paramref name="buffs"/> take away by their penalties (ValueModel.DebuffShare), with the caster's numbers; -1 when
        /// none of their parts can be read (the flat "plain debuff" share applies then). <paramref name="partial"/>: they also
        /// carry effects the reader does not value (★ audit review: the share read is then a floor, not the whole worth).
        /// </summary>
        private static float DebuffShareOf(TacticalContext ctx, AbilityData ability, UnitEntityData enemy, List<BlueprintBuff> buffs,
                                           out bool partial)
        {
            MechanicsContext abilityCtx = null;
            try { abilityCtx = CombatMath.CreateContext(ability, new Kingmaker.Utility.TargetWrapper(enemy)); } catch { }
            return DebuffShareIn(ctx, abilityCtx, enemy, buffs, out partial);
        }

        /// <summary>★ v0.3.21: the same with a given source context (a buff's on-hit debuff: the buff's context).</summary>
        private static float DebuffShareIn(TacticalContext ctx, MechanicsContext abilityCtx, UnitEntityData enemy, List<BlueprintBuff> buffs,
                                           out bool partial)
        {
            partial = false;
            try
            {
                var lists = buffs.Select(BuffPartsReader.Read).ToList();
                partial = lists.Any(l => l.Unread.Count > 0);
                var parts = lists.SelectMany(l => l.Parts).ToList();
                if (parts.Count == 0) return -1f;
                int casterLevel = 1;
                try { casterLevel = Math.Max(1, abilityCtx?.Params?.CasterLevel ?? 1); } catch { }
                float attack = 0f, damage = 0f, armor = 0f, saves = 0f, strength = 0f, dexterity = 0f;
                bool any = false;
                foreach (var p in parts)
                {
                    float amount = PartAmount(p, ctx.Unit, abilityCtx, enemy, casterLevel);
                    if (p.Conditional) amount *= 0.5f;
                    float taken = -amount;   // a penalty is a negative bonus
                    switch (p.Kind)
                    {
                        case BuffPartKind.Attack: attack += taken; any = true; break;
                        case BuffPartKind.Damage: damage += taken; any = true; break;
                        case BuffPartKind.ArmorClass: armor += taken; any = true; break;
                        case BuffPartKind.Saves: saves += taken * p.SaveShare; any = true; break;
                        // ★ audit review: an immunity a debuff carries (a petrified or paralysed body's) is not a penalty and
                        // must not cancel the real ones.
                        case BuffPartKind.ConditionalSaves: if (taken > 0f) { saves += taken * 0.5f; any = true; } break;
                        case BuffPartKind.Strength: strength += taken; any = true; break;
                        case BuffPartKind.Dexterity: dexterity += taken; any = true; break;
                        case BuffPartKind.Constitution: saves += taken * 0.5f; any = true; break;
                        case BuffPartKind.MentalStat: saves += taken * 0.25f; any = true; break;
                    }
                }
                return any ? ValueModel.DebuffShare(attack, damage, armor, saves, strength, dexterity) : -1f;
            }
            catch
            {
                return -1f;
            }
        }

        /// <summary>★ v0.3.21: the stat the recipient's spell DCs come from (its spellbook with the highest caster level), or
        /// null when it casts no spells.</summary>
        private static StatType? CastingStat(UnitEntityData ally)
        {
            try
            {
                var book = ally.Descriptor.Spellbooks.Where(b => b.CasterLevel > 0 && b.MaxSpellLevel >= 1)
                               .OrderByDescending(b => b.CasterLevel).FirstOrDefault();
                return book?.Blueprint != null ? book.Blueprint.CastingAttribute : (StatType?)null;
            }
            catch
            {
                return null;
            }
        }

        private static float DiceAverage(DiceFormula dice) => dice.Rolls * (dice.Dice.Sides() + 1) / 2f;

        private static readonly System.Reflection.FieldInfo ConcealmentEntries =
            HarmonyLib.AccessTools.Field(typeof(Kingmaker.UnitLogic.Parts.UnitPartConcealment), "m_Concealments");

        /// <summary>
        /// ★ v0.3.21: the miss chance <paramref name="ally"/> would stop suffering, on average over the enemies still fighting,
        /// by seeing invisible (an invisible enemy: total concealment, 50 %) or, with <paramref name="trueSeeing"/>, also
        /// through Blur and Displacement - the game's own exceptions (UnitPartConcealment.Calculate). Nothing for what it
        /// sees already, or while blind.
        /// </summary>
        private static float RemovableMiss(TacticalContext ctx, UnitEntityData ally, bool trueSeeing)
        {
            try
            {
                var state = ally.Descriptor.State;
                if (state.HasCondition(UnitCondition.Blindness)) return 0f;
                var fighting = ctx.Enemies.Where(e => !CombatMath.IsDisabled(e)).ToList();
                if (fighting.Count == 0) return 0f;
                bool seesInvisible = ally.Descriptor.IsSeeInvisibility || ally.Get<Kingmaker.UnitLogic.Parts.UnitPartBlindsense>() != null;
                bool seesTrue = state.HasCondition(UnitCondition.TrueSeeing);
                float sum = 0f;
                foreach (var enemy in fighting)
                {
                    float miss = 0f;
                    if (!seesInvisible && enemy.Descriptor.State.HasCondition(UnitCondition.Invisible)) miss = 0.5f;
                    if (trueSeeing && !seesTrue &&
                        ConcealmentEntries?.GetValue(enemy.Get<Kingmaker.UnitLogic.Parts.UnitPartConcealment>()) is
                            List<Kingmaker.UnitLogic.Parts.UnitPartConcealment.ConcealmentEntry> entries)
                        foreach (var entry in entries)
                            if (entry.Descriptor == ConcealmentDescriptor.Blur || entry.Descriptor == ConcealmentDescriptor.Displacement)
                                miss = Math.Max(miss, entry.Concealment == Concealment.Total ? 0.5f : entry.Concealment == Concealment.Partial ? 0.2f : 0f);
                    sum += miss;
                }
                return sum / fighting.Count;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>★ v0.3.21: the share of the enemies still fighting that cannot see an invisible unit (no See Invisibility or
        /// True Seeing - UnitDescriptor.IsSeeInvisibility - and no blindsense, as UnitMemoryController judges it).</summary>
        private static float UnseenShare(TacticalContext ctx)
        {
            try
            {
                var fighting = ctx.Enemies.Where(e => !CombatMath.IsDisabled(e)).ToList();
                if (fighting.Count == 0) return 0f;
                return fighting.Count(e => !e.Descriptor.IsSeeInvisibility && e.Get<Kingmaker.UnitLogic.Parts.UnitPartBlindsense>() == null) /
                       (float)fighting.Count;
            }
            catch
            {
                return 0.5f;
            }
        }

        /// <summary>★ v0.3.21: <paramref name="ally"/> is invisible already in a way at least as good as this part.</summary>
        private static bool AlreadyInvisible(UnitEntityData ally, BuffPart p)
        {
            try
            {
                var state = ally.Descriptor.State;
                return state.HasCondition(Kingmaker.UnitLogic.UnitCondition.GreaterInvisibility) ||
                       p.EndsOnAttack && state.HasCondition(Kingmaker.UnitLogic.UnitCondition.Invisible);
            }
            catch
            {
                return false;
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
            if (p.Formula.Rolls > 0)
            {
                amount += p.Formula.Rolls * (p.Formula.Dice.Sides() + 1) / 2f;
                if (p.Kind == BuffPartKind.TempHp && p.FormulaBonusByCasterLevel && p.Value == null) amount += casterLevel;
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
        private static float SizeUpDiceGain(ItemEntityWeapon weapon, int steps = 1)
        {
            try
            {
                if (weapon == null) return 1f;
                var baseDice = weapon.Blueprint.BaseDamage;
                var now = weapon.Size;
                var bigger = WeaponDamageScaleTable.Scale(baseDice, now.Shift(Math.Max(1, steps)), Size.Medium, weapon.Blueprint);
                var current = WeaponDamageScaleTable.Scale(baseDice, now, Size.Medium, weapon.Blueprint);
                float Avg(DiceFormula f) => f.Rolls * (f.Dice.Sides() + 1) / 2f;
                return Mathf.Clamp(Avg(bigger) - Avg(current), 0f, 6f * Math.Max(1, steps));
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
                        off.PhysicalFactor = est.FirstPhysicalFactor;
                        off.Weapon = est.Weapon;
                        off.Enemy = enemy;
                        off.CanCrit = est.FirstCritChance > 0f;
                        off.CritMultiplier = est.CritMultiplier;
                        if (est.Weapon != null)
                        {
                            var stats = Kingmaker.RuleSystem.Rulebook.Trigger(new Kingmaker.RuleSystem.Rules.RuleCalculateWeaponStats(ally, est.Weapon));
                            off.Enhancement = stats.Enhancement.TotalValue;
                            off.DoubleEdge = stats.DoubleCriticalEdge;
                            off.BaseThreat = Mathf.Clamp01((21 - est.Weapon.Blueprint.CriticalRollEdge) / 20f);
                        }
                    }
                }
                off.StrengthToDamage = CombatMath.StrengthAddsWeaponDamage(ally);
                try { off.SneakDice = Math.Max(0, ally.Stats.SneakAttack.ModifiedValue); } catch { }
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
            var dcStat = CastingStat(ally);
            if (!parts.Any(p => (p.Kind != BuffPartKind.MentalStat || (dcStat.HasValue && p.Stat == dcStat.Value)) &&
                                p.Kind != BuffPartKind.ForbidsSpells)) return result;
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

        /// <summary>The last appraisal of this cast on this recipient, for the decision log; null if none.</summary>
        internal static string LastAppraisal(UnitEntityData caster, AbilityData ability, UnitEntityData recipient) =>
            s_LastAppraisal.TryGetValue(AppraisalKey(caster, ability, recipient), out var s) ? s : null;
    }
}
