using ProfileAudit;
using Xunit;

namespace CompanionAI.Tests
{
    public class FusionAuditOptionsTests
    {
        [Fact]
        public void CoverageReportRemainsSeparateFromInputsAndOrdinaryReport()
        {
            Assert.True(AuditOptions.TryParse(new[] { "--fusion-coverage", "fusion.json", "spells", "classes", "audit.txt" }, out var options, out _));
            Assert.Equal("fusion.json", options.FusionCoverageOutput);
            Assert.Equal(new[] { "spells", "classes" }, options.Roots);
            Assert.Equal("audit.txt", options.Output);
        }

        [Theory]
        [InlineData("--fusion-coverage", "audit.txt")]
        [InlineData("--fusion-coverage", "--blocklist-checks", "spells", "audit.txt")]
        [InlineData("--fusion-coverage", "same.txt", "spells", "./same.txt")]
        [InlineData("--fusion-coverage", "one.json", "--fusion-coverage", "two.json", "spells", "audit.txt")]
        [InlineData("--optional-provider", "provider.dll", "--fusion-coverage", "fusion.json", "audit.txt")]
        public void InvalidRequestsCannotOverwriteOrPretendToAuditMissingGraphs(params string[] args)
        {
            Assert.False(AuditOptions.TryParse(args, out _, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }
    }
}
