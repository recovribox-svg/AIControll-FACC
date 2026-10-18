using System.Linq;
using CompanionAI_Pathfinder.Tactics;
using Xunit;

// Test IDs refer to docs/PROGRESSIVE_SPELLS.md §3.2.
public class SpellTierPolicyTests
{
    private static TierOption Spell(string key, int level, float value, float chance = 0.7f, string book = "Wizard", bool bypass = false) =>
        new TierOption { Key = key, Label = key, Book = book, Level = level, InScope = true, Value = value, Threshold = 0.5f, SuccessChance = chance, Bypass = bypass };

    private static TierOption Other(string key, int level, float value) =>
        new TierOption { Key = key, Label = key, Book = "Wizard", Level = level, InScope = false, Value = value, Threshold = 0.5f };

    [Fact] // T1
    public void LowestViableTierHoldsBackHigherTiers()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("mm", 1, 8), Spell("scorch", 2, 20), Spell("fireball", 3, 40), Other("weapon", 0, 10) });
        Assert.Equal(new[] { "fireball", "scorch" }, d.HeldBack.OrderBy(k => k));
        Assert.DoesNotContain("mm", d.HeldBack);
        Assert.DoesNotContain("weapon", d.HeldBack);
    }

    [Fact] // T2
    public void NoLowerTierAvailableUsesNextTier()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("scorch", 2, 20), Spell("fireball", 3, 40) });
        Assert.Equal(new[] { "fireball" }, d.HeldBack);
    }

    [Fact] // T3
    public void LowTierUnlikelyToWorkIsSkipped()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("daze", 1, 6, chance: 0.12f), Spell("hold", 3, 40, chance: 0.7f) });
        Assert.Empty(d.HeldBack);
        Assert.Contains("lower not viable", d.Notes.Single());
    }

    [Fact] // T3b
    public void WeakButWorkingLowTierStillBlocksStrongerHighTier()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("mm", 1, 3, chance: 0.95f), Spell("fireball", 3, 60) });
        Assert.Equal(new[] { "fireball" }, d.HeldBack);
    }

    [Fact] // T3c: below the engine's own value threshold
    public void LowTierBelowEngineThresholdIsSkipped()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("mm", 1, 0.3f), Spell("fireball", 3, 40) });
        Assert.Empty(d.HeldBack);
    }

    [Fact] // T4
    public void CantripsAreNeverHeldAndNeverAFloor()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("ray", 0, 5), Spell("mm", 1, 8), Spell("fireball", 3, 40) });
        Assert.Equal(new[] { "fireball" }, d.HeldBack);
        Assert.DoesNotContain("ray", d.HeldBack);
    }

    [Fact] // T5: a Completely Normal level-1 spell arrives with effective level 0
    public void EffectiveLevelZeroIsExempt()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("cn-mm", 0, 8), Spell("scorch", 2, 20), Spell("fireball", 3, 40) });
        Assert.Equal(new[] { "fireball" }, d.HeldBack);   // floor is L2: the free spell does not form a floor
    }

    [Fact] // T6
    public void EachSpellbookHasItsOwnLadder()
    {
        var d = SpellTierPolicy.Gate(new[]
        {
            Spell("w1", 1, 8, book: "Wizard"), Spell("w3", 3, 40, book: "Wizard"),
            Spell("m3", 3, 30, book: "Mythic"), Spell("m5", 5, 50, book: "Mythic"),
        });
        Assert.Equal(new[] { "m5", "w3" }, d.HeldBack.OrderBy(k => k));
    }

    [Fact] // T7
    public void OutOfScopeOptionsAreNeverHeld()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("mm", 1, 8), Other("haste", 3, 30), Other("cure", 4, 25) });
        Assert.Empty(d.HeldBack);
    }

    [Fact] // T8
    public void BypassedCastCompetesNormally()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("mm", 1, 8), Spell("fireball-boss", 3, 40, bypass: true), Spell("fireball-mooks", 3, 35) });
        Assert.Equal(new[] { "fireball-mooks" }, d.HeldBack);
        Assert.Contains("fireball-boss", d.BypassedLabels);
    }

    [Fact]
    public void NothingViableLeavesEverythingToNormalScoring()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("a", 1, 5, chance: 0.1f), Spell("b", 3, 9, chance: 0.1f) });
        Assert.Empty(d.HeldBack);
        Assert.Empty(d.Notes);
    }

    [Fact]
    public void NegativeLevelIsNeverGatedOrAFloor()
    {
        // The game side maps unknown levels (variants: GetSpellLevel = -1) to the parent's level; anything still <= 0 is free.
        var d = SpellTierPolicy.Gate(new[] { Spell("odd", -1, 50), Spell("w2", 2, 20), Spell("w4", 4, 40) });
        Assert.Equal(new[] { "w4" }, d.HeldBack);
        Assert.DoesNotContain("odd", d.HeldBack);
    }

    [Fact]
    public void ValueExactlyAtThresholdIsViable()
    {
        var low = Spell("w1", 1, 0.5f);
        low.Threshold = 0.5f;
        var d = SpellTierPolicy.Gate(new[] { low, Spell("w3", 3, 30) });
        Assert.Equal(new[] { "w3" }, d.HeldBack);
    }

    [Fact]
    public void ChanceExactlyAtFloorIsViable()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("w1", 1, 8, chance: SpellTierPolicy.MinSuccessChance), Spell("w3", 3, 30) });
        Assert.Equal(new[] { "w3" }, d.HeldBack);
    }

    [Fact]
    public void NotesNameEveryBookAndTheSkippedLowerTiers()
    {
        var d = SpellTierPolicy.Gate(new[]
        {
            Spell("w1", 1, 8, chance: 0.1f, book: "Wizard"), Spell("w2", 2, 20, book: "Wizard"),
            Spell("m3", 3, 30, book: "Mythic"),
        });
        Assert.Equal(2, d.Notes.Count);
        Assert.Contains(d.Notes, n => n.StartsWith("Wizard: tier L2") && n.Contains("lower not viable") && n.Contains("w1 L1"));
        Assert.Contains(d.Notes, n => n == "Mythic: tier L3");
    }
    [Fact] // ★ v0.3.12: lower tiers that are not viable are never removed, only higher ones
    public void LowerNonViableTiersAreNeverHeld()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("daze", 1, 6, chance: 0.1f), Spell("scorch", 2, 20), Spell("fireball", 3, 40) });
        Assert.Equal(new[] { "fireball" }, d.HeldBack);
        Assert.Contains("still selectable", d.Notes.Single());
    }

    [Fact] // ★ v0.3.12: the ladder key is unique per book; the name is only a label
    public void BooksWithTheSameNameButDifferentKeysAreSeparateLadders()
    {
        TierOption Book(string key, int level, string id) => new TierOption
        {
            Key = key, Label = key, Book = id, BookName = "Spellbook", Level = level, InScope = true, Value = 10, Threshold = 0.5f, SuccessChance = 0.7f
        };
        var d = SpellTierPolicy.Gate(new[] { Book("a1", 1, "guid-a"), Book("b3", 3, "guid-b") });
        Assert.Empty(d.HeldBack);
        Assert.Equal(2, d.Notes.Count);
        Assert.All(d.Notes, n => Assert.StartsWith("Spellbook: tier", n));
    }

    [Fact] // ★ v0.3.12: an option without a book key is its own ladder, never merged into a named book
    public void MissingBookKeyIsItsOwnLadder()
    {
        var d = SpellTierPolicy.Gate(new[] { Spell("w1", 1, 8, book: "Wizard"), Spell("x3", 3, 30, book: null) });
        Assert.Empty(d.HeldBack);
    }
}
