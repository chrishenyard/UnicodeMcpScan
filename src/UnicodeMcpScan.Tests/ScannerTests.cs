using UnicodeMcpScan.Core.Policy;
using UnicodeMcpScan.Core.Scan;

public class ScannerTests
{
    private static UnicodeScanner CreateDefaultScanner()
    {
        var cfg = new UnicodePolicyConfig
        {
            StrictWhitelist = true,
            AllowAsciiWhitespace = true,
            AllowedRanges = { "0021-007E" }, // printable ASCII (space handled by ascii whitespace)
            DeniedCodePoints = { "007F" },
            DenyFormatCharacters = true,
            DenyBidiControls = true,
            DenyNonAsciiWhitespace = true,
            RequireNfcNormalization = true,
            DetectMixedScripts = true
        };

        return new UnicodeScanner(new UnicodePolicy(cfg));
    }

    [Fact]
    public void Allows_PrintableAscii_And_AsciiWhitespace()
    {
        var scanner = CreateDefaultScanner();
        var text = "Hello, MCP!\nThis is ASCII.\tOK\r\n";
        var issues = scanner.ScanText(text).ToList();
        Assert.Empty(issues);
    }

    [Fact]
    public void Detects_ZeroWidth_Format_Characters()
    {
        var scanner = CreateDefaultScanner();

        var text = "abc\u200Bdef\u200Dghi\uFEFF"; // ZWSP, ZWJ, BOM (Format)
        var issues = scanner.ScanText(text).ToList();

        Assert.Contains(issues, i => i.RuleId == UnicodePolicy.Rule_ZeroWidthOrFormat && i.CodePoint == 0x200B);
        Assert.Contains(issues, i => i.RuleId == UnicodePolicy.Rule_ZeroWidthOrFormat && i.CodePoint == 0x200D);
        Assert.Contains(issues, i => i.RuleId == UnicodePolicy.Rule_ZeroWidthOrFormat && i.CodePoint == 0xFEFF);
        Assert.All(issues.Where(i => i.RuleId == UnicodePolicy.Rule_ZeroWidthOrFormat), i => Assert.Equal(Severity.Error, i.Severity));
    }

    [Fact]
    public void Detects_Bidi_Control_Characters()
    {
        var scanner = CreateDefaultScanner();

        var text = "safe\u202Eevil"; // RLO
        var issues = scanner.ScanText(text).ToList();

        Assert.Contains(issues, i => i.RuleId == UnicodePolicy.Rule_BidiControl && i.CodePoint == 0x202E);
    }

    [Fact]
    public void Detects_NonAscii_Whitespace()
    {
        var scanner = CreateDefaultScanner();

        var text = "hello\u00A0world"; // NBSP
        var issues = scanner.ScanText(text).ToList();

        Assert.Contains(issues, i => i.RuleId == UnicodePolicy.Rule_NonAsciiWhitespace && i.CodePoint == 0x00A0);
    }

    [Fact]
    public void Detects_NonNfc_Normalization()
    {
        var scanner = CreateDefaultScanner();

        var decomposed = "Cafe\u0301"; // e + combining acute (not NFC vs "Café")
        var issues = scanner.ScanText(decomposed).ToList();

        Assert.Contains(issues, i => i.RuleId == UnicodePolicy.Rule_NonNfc);
    }

    [Fact]
    public void Detects_MixedScripts_Heuristic()
    {
        var scanner = CreateDefaultScanner();

        // Latin 'a' replaced with Cyrillic 'а' (U+0430)
        var text = "pаypаl"; // looks like "paypal"
        var issues = scanner.ScanText(text).ToList();

        Assert.Contains(issues, i => i.RuleId == UnicodePolicy.Rule_MixedScripts);
    }

    [Fact]
    public void Whitelist_Violation_Is_Error_In_Strict_Mode()
    {
        var scanner = CreateDefaultScanner();

        var text = "Café"; // 'é' not in ASCII whitelist
        var issues = scanner.ScanText(text).ToList();

        Assert.Contains(issues, i => i.RuleId == UnicodePolicy.Rule_Whitelist && i.Severity == Severity.Error);
    }
}
