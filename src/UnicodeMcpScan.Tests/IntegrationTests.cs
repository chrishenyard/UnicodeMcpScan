using System.Text;
using UnicodeMcpScan.Core.Policy;
using UnicodeMcpScan.Core.Scan;

public class IntegrationTests
{
    [Fact]
    public void ScanFile_Finds_ZeroWidth_In_Real_File()
    {
        var cfg = new UnicodePolicyConfig
        {
            StrictWhitelist = true,
            AllowAsciiWhitespace = true,
            AllowedRanges = { "0021-007E" },
            DenyFormatCharacters = true,
            DenyBidiControls = true,
            DenyNonAsciiWhitespace = true,
            RequireNfcNormalization = true,
            DetectMixedScripts = true
        };

        var scanner = new UnicodeScanner(new UnicodePolicy(cfg));

        var file = Path.Combine(Path.GetTempPath(), $"mcp_scan_{Guid.NewGuid():N}.mcp");
        try
        {
            File.WriteAllText(file, "allow: true\nhidden:\u200Bnope\n", new UTF8Encoding(false));
            var issues = scanner.ScanFile(file).ToList();

            Assert.Contains(issues, i => i.CodePoint == 0x200B && i.RuleId == UnicodePolicy.Rule_ZeroWidthOrFormat);
            Assert.All(issues, i => Assert.Equal(file, i.FilePath));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
