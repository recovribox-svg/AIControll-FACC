// ★ v0.3.50 review2: native quote scalar boundaries and the production held/incoming defensive valuation routes.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CompanionAI_Pathfinder.Tactics;

namespace ProfileAudit
{
    internal static class BuffDefenceCheck
    {
        internal static List<string> Run(out int failures)
        {
            int checks = 0, bad = 0;
            var lines = new List<string>();
            void Check(bool ok, string why) { checks++; if (!ok) { bad++; lines.Add("BUFF-DEFENCE FAILED: " + why); } }
            try
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var engine = typeof(TacticalEngine);
                var type = engine.GetNestedType("Defence", all);
                var defence = Activator.CreateInstance(type, true);
                var quote = new WeaponAttackEstimate();
                type.GetField("AttackQuote", all).SetValue(defence, quote);
                type.GetField("AttackIncoming", all).SetValue(defence, 10f);
                var method = engine.GetMethod("TryArmorProtectionFromQuote", all);
                object[] a;
                bool Price(int ab, int ac, float armor, bool held)
                {
                    quote.FirstAttackBonus = ab; quote.FirstArmorClass = ac;
                    a = new object[] { defence, armor, held, 0f };
                    return (bool)method.Invoke(null, a);
                }
                a = null;
                Check(Price(10, 30, 1f, true) && Near((float)a[3], 10f), "held AC at the native 5% boundary can prevent a full current damage stream");
                Check(Price(10, 30, 2f, true) && Near((float)a[3], 20f), "held loss may exceed current incoming; no false clamp");
                Check(Price(0, 50, 1f, true) && Near((float)a[3], 0f), "AC remaining beyond the native floor has no invented loss");
                Check(Price(40, 20, 1f, false) && Near((float)a[3], 0f), "incoming AC remaining beyond 95% adds nothing");
                Check(Price(10, 21, 1f, false) && Near((float)a[3], 1f), "ordinary incoming AC uses native uncapped endpoints");
                float incomingGain = (float)a[3];
                type.GetField("AttackIncoming", all).SetValue(defence, 9f);
                Check(Price(10, 22, 1f, true) && Near((float)a[3], incomingGain), "add/remove endpoints agree after normalizing by the current raw roll");
                type.GetField("AttackIncoming", all).SetValue(defence, 10f);
                Check(Price(10, 21, -1f, true) && Near((float)a[3], -1f), "removing an AC penalty has a negative retention value");
                Check(!Price(10, 21, 0.5f, false), "fractional weighted AC keeps the prior approximation without rounding");
                Check(!Price(10, int.MinValue, 1f, true), "AC arithmetic overflow remains outside the native scalar route");
                Check(!Price(10, 21, float.NaN, true), "unusable AC remains outside the native scalar route");
                type.GetField("AttackQuote", all).SetValue(defence, null);
                Check(!Price(10, 21, 1f, true), "missing native quote is not fabricated");
                type.GetField("AttackQuote", all).SetValue(defence, quote);
                type.GetField("AttackIncoming", all).SetValue(defence, 0f);
                Check(!Price(10, 21, 1f, true), "automatic-only/no ordinary incoming buys no armour credit");

                float gain = ValueModel.PreventionValue(1f, 3f, 10f, 10f, 20f);
                Check(Near(gain, ValueModel.HeldPreventionLoss(1f, 3f, 10f, 9f, 20f)),
                    "held urgency compares removal from current danger, matching identical add/remove endpoints");
                Check(ValueModel.HeldPreventionLoss(10f, 1f, 10f, 0f, 20f) > 3f,
                    "removing real protection can create a drop risk from zero current danger");
                var calls = SaveBoostCheck.ReferencedMembers(method).ToArray();
                Check(calls.Any(m => m.Name == "HitProbability") && calls.Any(m => m.Name == "ArmorProtectionFromRolls"),
                    "production armour route uses native-derived raw-roll endpoints and tested relative scaling");
                Check(!calls.Any(m => m.Name == "EstimateWeaponAttack" || m.Name == "Trigger" || m.Name == "CreateContext" || m.Name == "IncomingParts"),
                    "armour comparison performs no additional actor, native rule, context or enemy query");
                var appraisal = SaveBoostCheck.ReferencedMembers(engine.GetMethod("AppraiseParts", all)).ToArray();
                Check(appraisal.Any(m => m.Name == "TryArmorProtectionFromQuote") && appraisal.Any(m => m.Name == "HeldPreventionLoss"),
                    "ordinary and held appraisals take the production corrected routes");
                Check(appraisal.Any(m => m.Name == "Incoming") && appraisal.Any(m => m.Name == "NextRisk"),
                    "total and attached danger remain in the urgency baseline");
                var producer = SaveBoostCheck.ReferencedMembers(engine.GetMethod("DefenceOf", all)).ToArray();
                Check(producer.Any(m => m.Name == "AttackQuote") && producer.Any(m => m.Name == "AttackIncoming"),
                    "defence retains the already-read ordinary source quote and split damage stream");
            }
            catch (Exception ex) { bad++; lines.Add("BUFF-DEFENCE FAILED: " + ex.GetBaseException()); }
            failures = bad;
            lines.Insert(0, $"BUFF-DEFENCE {checks} checks, failures {bad}; native quote scalars and compiled routes; live actor, buff and Unity execution NOT TESTED");
            return lines;
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
    }
}
