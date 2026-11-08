// ★ v0.3.30 test 2 (author request: value Intelligence, Wisdom and Charisma properly): what one point of an ability score's
// modifier adds for a recipient, from the links the game itself gives that score on that unit (TacticalEngine.StatLinks.cs:
// the weapon's attack and damage stat, the saves' and armour class's base stats, the hit points' base stat, facts that
// derive a bonus from the score). Pure, unit-tested.
using System;

namespace CompanionAI_Pathfinder.Tactics
{
    public static partial class ValueModel
    {
        /// <summary>What an ability score feeds on a unit.</summary>
        public enum StatTarget { Attack, Damage, ArmorClass, Save, AllSaves, HitPoints, SpellDc }

        /// <summary>
        /// What one point of an ability score's modifier adds for one unit, in the buff appraisal's units: attack bonus, damage
        /// per hit, armour class, saves (counted as all three: one save is a third), hit points, spell DC.
        /// </summary>
        public struct StatUse
        {
            public float Attack, Damage, Armor, Saves, HitPoints, SpellDc;

            public bool Any => Attack > 0f || Damage > 0f || Armor > 0f || Saves > 0f || HitPoints > 0f || SpellDc > 0f;

            /// <summary>One link of the score: <paramref name="factor"/> of <paramref name="target"/> per point of modifier.
            /// Nothing for a factor that is not a positive number.</summary>
            public void Add(StatTarget target, float factor)
            {
                if (!(factor > 0f) || float.IsInfinity(factor)) return;
                switch (target)
                {
                    case StatTarget.Attack: Attack += factor; break;
                    case StatTarget.Damage: Damage += factor; break;
                    case StatTarget.ArmorClass: Armor += factor; break;
                    case StatTarget.Save: Saves += factor / 3f; break;
                    case StatTarget.AllSaves: Saves += factor; break;
                    case StatTarget.HitPoints: HitPoints += factor; break;
                    case StatTarget.SpellDc: SpellDc += factor; break;
                }
            }
        }

        /// <summary>
        /// The ability modifier points an ability score gains from <paramref name="increment"/> more points (a +2 score is +1
        /// modifier). As the appraisal has always counted it: half the score gained, never below nothing.
        /// </summary>
        public static float ModifierGain(float increment) =>
            float.IsNaN(increment) || float.IsInfinity(increment) ? 0f : Math.Max(0f, increment) / 2f;
    }
}
