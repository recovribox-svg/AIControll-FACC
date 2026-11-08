// ★ v0.3.30 test 2 (author request: value Intelligence, Wisdom and Charisma properly, well integrated and performant).
// What one point of an ability score's modifier adds for a unit, read from the links the game itself gives that score
// on that unit, for all six scores alike:
//  - the weapon it fights with: the attack rule's stat (RuleCalculateAttackBonusWithoutTarget.AttackBonusStat, after the
//    unit's own replacements: Weapon Finesse, Zen Archery's Wisdom) and the damage rule's (RuleCalculateWeaponStats
//    .DamageBonusStat and its multiplier: two hands 1.5, a composite bow's Strength);
//  - the saves' base stats (ModifiableValueSavingThrow.BaseStat: Constitution, Dexterity, Wisdom);
//  - the armour class's base attribute (ModifiableValueArmorClass.BaseAttribute, which features can replace), unless the
//    armour already caps it (applied bonus below the stat's);
//  - the hit points' base stat (ModifiableValueHitPoints.BaseStat: Constitution, Charisma for the undead);
//  - the unit's facts that derive a bonus from the score: DerivativeStatBonus (a paladin's Divine Grace: Charisma to all
//    saves), and AddContextStatBonus ranked by the score's bonus (ContextRankConfig StatBonus: a monk's Wisdom to AC) or
//    recalculated when it changes (RecalculateOnStatChange: a Scaled Fist's Charisma to AC).
// The spell DC (casting stat) and a kineticist's main stat stay where they were (TacticalEngine.Buffs.cs). Before, only
// Strength and Dexterity were read, by melee or ranged alone, and Intelligence, Wisdom and Charisma not at all for a
// recipient that did not cast with them: they took the old quality guess (0.7 x level), so Profane Invocation's
// Intelligence looked worth 8.8 on Lann, an archer, and his Wisdom - his Zen Archer bow's attack stat - was not read.
// Facts are read once per blueprint; a unit's links once per round.
using System;
using System.Collections.Generic;
using CompanionAI_Pathfinder.Analysis;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class TacticalEngine
    {
        /// <summary>A fact's bonus that follows an ability score: <see cref="Factor"/> of <see cref="To"/> per modifier point.</summary>
        private struct FactStatLink
        {
            public StatType From;
            public StatType To;
            public float Factor;
        }

        private static readonly Dictionary<BlueprintScriptableObject, FactStatLink[]> s_FactStatLinks =
            new Dictionary<BlueprintScriptableObject, FactStatLink[]>();
        private static readonly FactStatLink[] NoStatLinks = new FactStatLink[0];

        private static readonly System.Reflection.FieldInfo RankStat =
            HarmonyLib.AccessTools.Field(typeof(ContextRankConfig), "m_Stat");

        /// <summary>The bonuses of <paramref name="blueprint"/> that follow an ability score, read once.</summary>
        private static FactStatLink[] FactStatLinksOf(BlueprintScriptableObject blueprint)
        {
            if (blueprint == null) return NoStatLinks;
            if (s_FactStatLinks.TryGetValue(blueprint, out var known)) return known;
            List<FactStatLink> links = null;
            try
            {
                var components = blueprint.ComponentsArray;
                if (components != null)
                {
                    StatType recalculated = StatType.Unknown;
                    foreach (var c in components)
                        if (c is RecalculateOnStatChange r && !r.UseKineticistMainStat && AttributeIndex(r.Stat) >= 0) recalculated = r.Stat;
                    foreach (var c in components)
                    {
                        if (c is DerivativeStatBonus derived)
                        {
                            if (AttributeIndex(derived.BaseStat) >= 0)
                                (links ?? (links = new List<FactStatLink>())).Add(new FactStatLink { From = derived.BaseStat, To = derived.DerivativeStat, Factor = 1f });
                            continue;
                        }
                        if (!(c is AddContextStatBonus bonus) || bonus.Value == null) continue;
                        StatType from = StatType.Unknown;
                        float perPoint = 1f;
                        if (bonus.Value.ValueType == ContextValueType.Rank)
                        {
                            foreach (var rc in components)
                            {
                                if (!(rc is ContextRankConfig config) || config.Type != bonus.Value.ValueRank || !config.IsBasedOnStatBonus) continue;
                                if (RankStat?.GetValue(config) is StatType stat && AttributeIndex(stat) >= 0)
                                {
                                    from = stat;
                                    // A rank of the score itself (BaseStat) moves two points per modifier point.
                                    perPoint = RankBase?.GetValue(config) is ContextRankBaseValueType type && type == ContextRankBaseValueType.BaseStat ? 2f : 1f;
                                }
                                break;
                            }
                        }
                        // A bonus from a custom property that the fact recalculates when the score changes (Scaled Fist).
                        if (from == StatType.Unknown && bonus.Value.ValueType != ContextValueType.Simple) from = recalculated;
                        if (from == StatType.Unknown) continue;
                        (links ?? (links = new List<FactStatLink>())).Add(new FactStatLink
                        {
                            From = from, To = bonus.Stat, Factor = perPoint * Math.Max(1, bonus.Multiplier)
                        });
                    }
                }
            }
            catch { links = null; }
            var result = links?.ToArray() ?? NoStatLinks;
            s_FactStatLinks[blueprint] = result;
            return result;
        }

        private sealed class UnitStatLinks
        {
            public int Fight, Round;
            public readonly ValueModel.StatUse[] ByStat = new ValueModel.StatUse[7];   // StatType.Strength (1) .. Charisma (6)
        }

        private static readonly Dictionary<string, UnitStatLinks> s_UnitStatLinks = new Dictionary<string, UnitStatLinks>();

        private static int AttributeIndex(StatType stat) =>
            stat >= StatType.Strength && stat <= StatType.Charisma ? (int)stat : -1;

        /// <summary>
        /// What one point of <paramref name="stat"/>'s modifier adds for <paramref name="unit"/> beyond its weapon (saves,
        /// armour class, hit points, facts that follow the score), read once a round.
        /// </summary>
        private static ValueModel.StatUse UnitStatUse(UnitEntityData unit, StatType stat)
        {
            int index = AttributeIndex(stat);
            if (unit == null || index < 0) return default;
            string key = unit.UniqueId;
            if (s_UnitStatLinks.TryGetValue(key, out var links) && links.Fight == CombatMemory.Fight && links.Round == CombatMemory.Round)
                return links.ByStat[index];
            if (s_UnitStatLinks.Count > 64) s_UnitStatLinks.Clear();
            links = new UnitStatLinks { Fight = CombatMemory.Fight, Round = CombatMemory.Round };
            try
            {
                var stats = unit.Stats;
                void Save(ModifiableValue save)
                {
                    int i = AttributeIndex((save as ModifiableValueSavingThrow)?.BaseStat?.Type ?? StatType.Unknown);
                    if (i >= 0) links.ByStat[i].Add(ValueModel.StatTarget.Save, 1f);
                }
                Save(stats.SaveFortitude);
                Save(stats.SaveReflex);
                Save(stats.SaveWill);
                if (stats.AC is ModifiableValueArmorClass ac && ac.BaseAttribute != null)
                {
                    int i = AttributeIndex(ac.BaseAttribute.Type);
                    // Armour that caps the bonus already (applied below the stat's) takes nothing more from it.
                    if (i >= 0 && ac.BaseAttributeAppliedBonus >= ac.BaseAttribute.Bonus) links.ByStat[i].Add(ValueModel.StatTarget.ArmorClass, 1f);
                }
                if (stats.HitPoints is ModifiableValueHitPoints hp)
                {
                    int i = hp.BaseStat == HitPointsBaseStat.Constitution ? AttributeIndex(StatType.Constitution)
                          : hp.BaseStat == HitPointsBaseStat.Charisma ? AttributeIndex(StatType.Charisma) : -1;
                    if (i >= 0) links.ByStat[i].Add(ValueModel.StatTarget.HitPoints, Math.Max(1, unit.Descriptor.Progression.CharacterLevel));
                }
                foreach (var fact in unit.Facts.List)
                {
                    if (fact == null || !fact.Active) continue;
                    foreach (var link in FactStatLinksOf(fact.Blueprint as BlueprintScriptableObject))
                    {
                        int i = AttributeIndex(link.From);
                        if (i < 0) continue;
                        switch (link.To)
                        {
                            case StatType.SaveFortitude:
                            case StatType.SaveReflex:
                            case StatType.SaveWill: links.ByStat[i].Add(ValueModel.StatTarget.Save, link.Factor); break;
                            case StatType.AC: links.ByStat[i].Add(ValueModel.StatTarget.ArmorClass, link.Factor); break;
                            case StatType.AdditionalAttackBonus: links.ByStat[i].Add(ValueModel.StatTarget.Attack, link.Factor); break;
                            case StatType.AdditionalDamage: links.ByStat[i].Add(ValueModel.StatTarget.Damage, link.Factor); break;
                            case StatType.HitPoints: links.ByStat[i].Add(ValueModel.StatTarget.HitPoints, link.Factor); break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Buff] stat links of {unit.CharacterName}: {ex.Message}");
            }
            s_UnitStatLinks[key] = links;
            return links.ByStat[index];
        }

        /// <summary>
        /// What one point of <paramref name="stat"/>'s modifier adds for <paramref name="unit"/>: its weapon's attack and
        /// damage stat (<paramref name="off"/>), what else follows the score on it (<see cref="UnitStatUse"/>), and the spell
        /// DC of its casting stat.
        /// </summary>
        private static ValueModel.StatUse StatUseOf(UnitEntityData unit, StatType stat, Offence off, StatType? castingStat)
        {
            var use = UnitStatUse(unit, stat);
            if (off != null)
            {
                if (off.AttackStat == stat) use.Add(ValueModel.StatTarget.Attack, 1f);
                if (off.DamageStat == stat) use.Add(ValueModel.StatTarget.Damage, off.DamageStatMultiplier);
            }
            if (castingStat.HasValue && castingStat.Value == stat) use.Add(ValueModel.StatTarget.SpellDc, 1f);
            return use;
        }

        /// <summary>
        /// The ability score a buff part raises (BuffPartKind Strength, Dexterity, Constitution, MentalStat), or Unknown.
        /// </summary>
        private static StatType AttributeOfPart(BuffPart p)
        {
            switch (p.Kind)
            {
                case BuffPartKind.Strength: return StatType.Strength;
                case BuffPartKind.Dexterity: return StatType.Dexterity;
                case BuffPartKind.Constitution: return StatType.Constitution;
                case BuffPartKind.MentalStat: return AttributeIndex(p.Stat) >= 0 ? p.Stat : StatType.Unknown;
                default: return StatType.Unknown;
            }
        }

        /// <summary>
        /// The weapon's attack and damage stat for <paramref name="unit"/>, by the game's own rules (after its replacements:
        /// Weapon Finesse, Zen Archery, a composite bow's Strength), on <paramref name="off"/>.
        /// </summary>
        private static void ReadWeaponStats(UnitEntityData unit, Kingmaker.Items.ItemEntityWeapon weapon, Offence off,
                                            Kingmaker.RuleSystem.Rules.RuleCalculateWeaponStats triggered = null)
        {
            if (unit == null || weapon == null || off == null) return;
            try
            {
                var stats = triggered ?? Kingmaker.RuleSystem.Rulebook.Trigger(new Kingmaker.RuleSystem.Rules.RuleCalculateWeaponStats(unit, weapon));
                off.DamageStat = stats.DamageBonusStat;
                off.DamageStatMultiplier = Math.Max(0f, stats.DamageBonusStatMultiplier + stats.AdditionalDamageBonusStatMultiplier);
                var attack = Kingmaker.RuleSystem.Rulebook.Trigger(new Kingmaker.RuleSystem.Rules.RuleCalculateAttackBonusWithoutTarget(unit, stats, 0));
                off.AttackStat = attack.AttackBonusStat;
            }
            catch (Exception ex)
            {
                Main.Verbose($"[Buff] weapon stats of {unit.CharacterName}: {ex.Message}");
            }
        }
    }
}
