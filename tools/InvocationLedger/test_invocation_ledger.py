"""Tests for invocation_ledger.py: python3 -I tools/InvocationLedger/test_invocation_ledger.py

Synthetic rows in the exact templates the mod writes (TacticalEngine UniqueKeptNote / the [Buff] decision row,
CommandTrace ISSUED/ENDED, the [Tactics] decision row). No played log content.
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import invocation_ledger as ledger  # noqa: E402

GUID = {v: k for k, v in ledger.ABILITY_TIER.items()}
P = '[CompanionAI_Pathfinder] '


def fight():
    return [P + '★★★ Party combat started ★★★']


def decide(unit, rnd, enemies=5, hp=100):
    return [P + f'[Tactics] {unit} {rnd} HP{hp}% [SMWF-] enemies={enemies} rounds~3.0 diff=0.50 :: Attack->Goblin=4.0 => x']


def kept(stat, tier, holder):
    return [P + f'[Buff] Lann: Profane Invocation — {stat} [{GUID[(stat, tier)]}] stays on {holder} '
                f'(unique buff: Seelah 1.0 is not clearly better than 5.0 there)']


def cast(stat, tier, recipient, reasons='buff', net=2.0, gross=2.0, mode='TB', result='Success', acted=True, ended=True):
    label = f'{stat}->{recipient}({reasons})'
    rows = [P + f'[Buff] Lann: Profane Invocation — {label} = {net} (Free) - {gross}: AC +1 over 3 rd (lasts 100); '
                f'ability={GUID[(stat, tier)]}; best other: Attack->Goblin = 1',
            P + f"[Cmd] Lann: ISSUED UnitUseAbility 'Profane Invocation — {label}' "
                f"mode={'TB/Preparing' if mode == 'TB' else 'RT'} forcedPath=none approachR=13.2"]
    if ended:
        rows.append(P + f"[Cmd] Lann: ENDED UnitUseAbility 'Profane Invocation — {label}' result={result} "
                        f"acted={acted} moved=0.0m after 0.0s")
    return rows


def kinds(result):
    return [r['kind'] for r in result['rows']]


class LedgerTests(unittest.TestCase):
    def test_first_placement_sibling_and_settled_move(self):
        log = (fight() + kept('Dexterity', 1, 'Aeiluvetar') + decide('Lann', 'R1') +
               cast('Charisma', 1, 'Camellia') +
               cast('Strength', 1, 'Camellia', 'replaces ProfaneAscensionCharismaBuff1 -0.9+buff', 3.1, 4.0) +
               cast('Dexterity', 1, 'Seelah', 'from Aeiluvetar -2.5+buff', 4.0, 6.5))
        result = ledger.build(log)
        self.assertEqual(kinds(result), ['first-placement', 'sibling-replacement [sibling]', 'move-settled'])
        self.assertEqual(result['rows'][2]['engine_holder'], 'Aeiluvetar')
        self.assertEqual(result['rows'][2]['holder_loss'], 2.5)

    def test_override_and_under_settle_use_the_placement_round(self):
        log = (fight() + decide('Lann', 'R1') + cast('Dexterity', 1, 'Seelah') +
               decide('Lann', 'R2') + cast('Dexterity', 1, 'Tuvel', 'from Seelah -3.0+buff', 9.0, 12.0) +
               decide('Lann', 'R3') + cast('Dexterity', 1, 'Lann', 'from Tuvel -2.0+buff', 1.5, 3.5) +
               decide('Lann', 'R6') + cast('Dexterity', 1, 'Seelah', 'from Lann -1.0+buff', 2.0, 3.0))
        result = ledger.build(log)
        self.assertEqual(kinds(result)[1:], ['move-override', 'move-under-settle', 'move-settled'])
        self.assertEqual(result['rows'][1]['rounds_since_placed'], 1)
        self.assertEqual(result['rows'][3]['rounds_since_placed'], 3)

    def test_interrupted_cast_moves_nothing(self):
        log = (fight() + kept('Dexterity', 1, 'Aeiluvetar') + decide('Lann', 'RT1') +
               cast('Dexterity', 1, 'Seelah', 'from Aeiluvetar -2.5+buff', mode='RT', result='Interrupt', acted=False) +
               cast('Dexterity', 1, 'Camellia', 'from Aeiluvetar -2.5+buff', mode='RT'))
        result = ledger.build(log)
        self.assertEqual(kinds(result), ['not-applied', 'move-settled'])
        self.assertEqual(result['acted'], 1)

    def test_holder_unseen_names_the_ledger_holder_and_evidence(self):
        log = (fight() + decide('Seelah', 'R1', hp=40) + decide('Lann', 'R1') + cast('Dexterity', 1, 'Seelah') +
               [P + '[Tactics] Tuvel: revive Seelah with Cure Light Wounds = -5.0 [needs 8 HP]'] +
               decide('Lann', 'R3') + cast('Dexterity', 1, 'Camellia'))
        row = ledger.build(log)['rows'][1]
        self.assertEqual(row['kind'], 'holder-unseen')
        self.assertIn('ledger holder Seelah', row['evidence'])
        self.assertIn('last decision R1 at 40% HP', row['evidence'])
        self.assertIn('revive considered', row['evidence'])

    def test_cross_tier_is_redundant_only_without_another_tier_b_buff_on_the_recipient(self):
        def run(tuvel_holds_charisma_2):
            log = fight() + kept('Dexterity', 1, 'Lann') + kept('Dexterity', 2, 'Orvath')
            if tuvel_holds_charisma_2:
                log += kept('Charisma', 2, 'Tuvel')
            log += (decide('Lann', 'RT2') + cast('Dexterity', 1, 'Tuvel', 'from Lann -7.9+buff', 9.6, 17.6, mode='RT') +
                    cast('Dexterity', 2, 'Lann', 'replaces ProfaneAscensionConstitutionBuff2 -1.2+from Orvath -6.1+buff',
                         22.5, 29.8, mode='RT'))
            return [s for s in ledger.build(log)['sequences'] if s['kind'] == 'cross-tier']
        held = run(True)
        self.assertEqual(len(held), 1)
        self.assertTrue(held[0]['verdict'].startswith('not redundant: Tuvel held Charisma tier 2'))
        free = run(False)
        self.assertTrue(free[0]['verdict'].startswith('redundant: tier-2 Dexterity straight onto Tuvel'))
        self.assertIn("keeps Lann's Constitution tier 2", free[0]['verdict'])

    def test_same_stat_flag_when_the_other_tier_is_already_there(self):
        log = (fight() + kept('Dexterity', 2, 'Seelah') + decide('Lann', 'R1') + cast('Dexterity', 1, 'Seelah'))
        self.assertIn('same-stat', ledger.build(log)['rows'][0]['kind'])

    def test_chain_and_replacement_in_the_next_fight(self):
        log = (fight() + kept('Strength', 1, 'Valmot') + kept('Dexterity', 1, 'Tuvel') + decide('Lann', 'R4') +
               cast('Strength', 1, 'Abrikandilu', 'from Valmot -1.6+buff', 1.2, 2.8) + decide('Lann', 'R5') +
               cast('Strength', 1, 'Bloodragers', 'from Abrikandilu -1.7+buff', 7.2, 9.8) + decide('Lann', 'R9', enemies=1) +
               cast('Strength', 1, 'Tuvel', 'replaces ProfaneAscensionDexterityBuff1 -0.0+from Bloodragers -0.4+buff', 1.2, 1.6) +
               fight() + decide('Lann', 'R1') + cast('Dexterity', 1, 'Seelah', net=11.4, gross=11.4))
        result = ledger.build(log)
        by_kind = {s['kind']: s for s in result['sequences']}
        self.assertIn('Valmot -> Abrikandilu -> Bloodragers within one round', by_kind['chain']['verdict'])
        self.assertIn('move-settled, then move-override', by_kind['chain']['verdict'])
        self.assertIn('cast again in fight 2 on Seelah', by_kind['re-placed']['verdict'])
        self.assertIn('last-enemy', result['rows'][2]['kind'])
        self.assertEqual(result['rows'][3]['kind'], 'first-placement')
        self.assertNotIn('return', by_kind)

    def test_return_to_an_earlier_holder(self):
        log = (fight() + kept('Strength', 1, 'Valmot') + decide('Lann', 'R1') +
               cast('Strength', 1, 'Tuvel', 'from Valmot -1.6+buff', 3.0, 4.6) + decide('Lann', 'R5') +
               cast('Strength', 1, 'Valmot', 'from Tuvel -1.0+buff', 2.0, 3.0))
        result = ledger.build(log)
        returns = [s for s in result['sequences'] if s['kind'] == 'return']
        self.assertEqual(len(returns), 1)
        self.assertIn('Strength/1 back on Valmot', returns[0]['verdict'])
        self.assertFalse([s for s in result['sequences'] if s['kind'] == 'chain'])

    def test_names_ended_matching_and_missing_outcome(self):
        log = (fight() + decide('Lann', 'RT1') +
               cast('Dexterity', 1, '[unknown key: d2885ae4-c793-459e-9dcc-554576dc074b]', mode='RT', ended=False) +
               cast('Wisdom', 1, '', mode='RT', ended=False) +
               [P + "[Cmd] Lann: ENDED UnitUseAbility 'Profane Invocation — Wisdom->(buff)' result=Success acted=True "
                    'moved=0.0m after 2.5s'])
        rows = ledger.build(log)['rows']
        self.assertEqual(rows[0]['recipient'], 'main character (name key missing)')
        self.assertEqual(rows[0]['kind'], 'no-outcome')
        self.assertEqual(rows[1]['recipient'], '(unnamed ally)')
        self.assertEqual((rows[1]['kind'], rows[1]['seconds']), ('first-placement', 2.5))

    def test_cast_without_its_decision_row_is_unread_and_moves_nothing(self):
        rows = cast('Dexterity', 1, 'Seelah', mode='RT')[1:]          # ISSUED and ENDED only
        log = fight() + kept('Dexterity', 1, 'Aeiluvetar') + decide('Lann', 'RT1') + rows + \
            cast('Dexterity', 1, 'Camellia', 'from Aeiluvetar -2.0+buff', mode='RT')
        result = ledger.build(log)
        self.assertEqual(kinds(result), ['unread', 'move-settled'])

    def test_verbose_prefix_and_markdown(self):
        log = fight() + [P + '[V] ' + decide('Lann', 'RT1')[0][len(P):]] + cast('Charisma', 2, 'Tuvel', mode='RT')
        result = ledger.build(log)
        self.assertEqual(result['rows'][0]['round'], 'RT1')
        text = ledger.markdown(result)
        self.assertIn('issued 1, acted 1', text)
        self.assertIn('| Charisma 2 | Tuvel |', text)


if __name__ == '__main__':
    unittest.main()
