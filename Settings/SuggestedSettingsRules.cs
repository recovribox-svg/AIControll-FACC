namespace CompanionAI_Pathfinder.Settings
{
    /// <summary>
    /// ★ v0.3.70 features review (issue #115): what a character's build says its role and range should be. Every character
    /// started as DPS / Mixed, so a player following the companions' automatic level-up builds had to set each one by hand.
    /// The suggestion is read from the character as it is (its weapon, shield, spellbook and weapon feats), so it matches the
    /// default build when the game levels the companion and still fits a custom build. The default builds (Frostbird's
    /// "Companion Auto Level Builds" guide) give, for example: Seelah Paladin with longsword and shield, Lann Zen Archer and
    /// Wenduag Fighter with longbows, Ember Witch and Nenio Scroll Savant casting, Daeran Oracle (Life) and Sosiel Cleric
    /// healing, Camellia Spirit Hunter with a finesse rapier, Woljif Eldritch Scoundrel with two daggers. Pure rules only.
    /// </summary>
    public static class SuggestedSettingsRules
    {
        public struct BuildFacts
        {
            /// <summary>The weapon in hand shoots (bow, crossbow).</summary>
            public bool RangedWeapon;
            /// <summary>A shield in the off hand.</summary>
            public bool Shield;
            /// <summary>A spellbook that reaches 9th-level spells, the character's main progression.</summary>
            public bool FullCaster;
            /// <summary>That spellbook is divine (cleric, oracle, shaman, druid).</summary>
            public bool Divine;
            /// <summary>Feats invested in the weapon in hand (Weapon Focus on its type, Weapon Finesse).</summary>
            public bool WeaponFeats;
            /// <summary>A kineticist without a kinetic blade in hand: its attacks are blasts.</summary>
            public bool BlastKineticist;
        }

        public struct Suggestion
        {
            public AIRole Role;
            public RangePreference Range;
            public string Why;
        }

        public static Suggestion Suggest(BuildFacts f)
        {
            var s = new Suggestion { Role = AIRole.DPS, Range = RangePreference.Mixed };
            if (f.RangedWeapon) { s.Range = RangePreference.Ranged; s.Why = "ranged weapon"; }
            else if (f.BlastKineticist) { s.Range = RangePreference.Ranged; s.Why = "kinetic blasts"; }
            else if (f.FullCaster)
            {
                s.Range = f.WeaponFeats ? RangePreference.Mixed : RangePreference.Ranged;
                s.Why = f.WeaponFeats ? "full caster with weapon feats" : "full caster";
            }
            else { s.Range = RangePreference.Melee; s.Why = "melee weapon"; }

            if (f.FullCaster && f.Divine) { s.Role = AIRole.Support; s.Why += ", divine full caster"; }
            else if (f.Shield && s.Range == RangePreference.Melee) { s.Role = AIRole.Tank; s.Why += " and shield"; }
            return s;
        }
    }
}
