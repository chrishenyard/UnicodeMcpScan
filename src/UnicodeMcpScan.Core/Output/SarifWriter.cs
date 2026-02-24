using System.Text.Json;
using UnicodeMcpScan.Core.Scan;

namespace UnicodeMcpScan.Core.Output;

public static class SarifWriter
{
    public static string ToSarif(IReadOnlyList<UnicodeIssue> issues)
    {
        // Minimal SARIF 2.1.0 log. Enough for GitHub annotations.
        var rules = issues
            .GroupBy(i => i.RuleId)
            .Select(g => new
            {
                id = g.Key,
                name = g.Key,
                shortDescription = new { text = RuleTitle(g.Key) },
            })
            .ToList();

        var results = issues.Select(i => new
        {
            ruleId = i.RuleId,
            level = i.Severity switch
            {
                Severity.Error => "error",
                Severity.Warning => "warning",
                _ => "note"
            },
            message = new { text = $"{i.Message} {i.CodePointHex} {i.Display} [{i.Category}]" },
            locations = new[]
            {
                new
                {
                    physicalLocation = new
                    {
                        artifactLocation = new { uri = i.FilePath },
                        region = new
                        {
                            startLine = Math.Max(i.Line, 1),
                            startColumn = Math.Max(i.Column, 1),
                        }
                    }
                }
            }
        });

        var sarif = new
        {
            version = "2.1.0",
            schema = "https://json.schemastore.org/sarif-2.1.0.json",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "UnicodeMcpScan",
                            informationUri = "https://example.invalid/unicode-mcp-scan",
                            rules
                        }
                    },
                    results
                }
            }
        };

        return JsonSerializer.Serialize(sarif, new JsonSerializerOptions { WriteIndented = true });

        static string RuleTitle(string id) => id switch
        {
            Policy.UnicodePolicy.Rule_Whitelist => "Character not in whitelist",
            Policy.UnicodePolicy.Rule_ZeroWidthOrFormat => "Format/zero-width character",
            Policy.UnicodePolicy.Rule_BidiControl => "BiDi control character",
            Policy.UnicodePolicy.Rule_NonAsciiWhitespace => "Non-ASCII whitespace",
            Policy.UnicodePolicy.Rule_NonNfc => "Text not NFC-normalized",
            Policy.UnicodePolicy.Rule_MixedScripts => "Multiple scripts detected",
            Policy.UnicodePolicy.Rule_InvalidUtf16 => "Invalid UTF encoding",
            _ => "Unicode policy violation"
        };
    }
}
