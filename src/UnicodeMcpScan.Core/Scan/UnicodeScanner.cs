using System.Globalization;
using System.Text;
using UnicodeMcpScan.Core.Policy;

namespace UnicodeMcpScan.Core.Scan;

public sealed class UnicodeScanner
{
    private readonly UnicodePolicy _policy;

    public UnicodeScanner(UnicodePolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public IReadOnlyList<UnicodeIssue> ScanText(string text, string filePath = "<memory>")
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        var issues = new List<UnicodeIssue>();
        var mapper = new TextPositionMapper(text);

        // Rule: NFC normalization
        if (_policy.Config.RequireNfcNormalization)
        {
            var nfc = text.Normalize(NormalizationForm.FormC);
            if (!string.Equals(text, nfc, StringComparison.Ordinal))
            {
                var sev = _policy.SeverityFor(UnicodePolicy.Rule_NonNfc, _policy.Config.NonNfcSeverity);
                issues.Add(new UnicodeIssue(
                    RuleId: UnicodePolicy.Rule_NonNfc,
                    Severity: sev,
                    FilePath: filePath,
                    Line: 0,
                    Column: 0,
                    CodePoint: 0,
                    CodePointHex: "N/A",
                    Category: "N/A",
                    Display: "<file>",
                    Message: "Text is not NFC-normalized (possible combining-mark/normalization trick)."
                ));
            }
        }

        // Rule: Mixed scripts heuristic (coarse)
        if (_policy.Config.DetectMixedScripts)
        {
            var scripts = DetectScriptsPresent(text);
            if (scripts.Count > 1)
            {
                var sev = _policy.SeverityFor(UnicodePolicy.Rule_MixedScripts, _policy.Config.MixedScriptSeverity);
                issues.Add(new UnicodeIssue(
                    RuleId: UnicodePolicy.Rule_MixedScripts,
                    Severity: sev,
                    FilePath: filePath,
                    Line: 0,
                    Column: 0,
                    CodePoint: 0,
                    CodePointHex: "N/A",
                    Category: "N/A",
                    Display: "<file>",
                    Message: $"Multiple scripts detected ({string.Join(", ", scripts.OrderBy(s => s))}). This can indicate homoglyph/confusable risk."
                ));
            }
        }

        // Per-character rules
        for (int i = 0; i < text.Length;)
        {
            if (!Rune.TryGetRuneAt(text, i, out var rune))
            {
                var (line, col) = mapper.GetLineColumn(i);
                issues.Add(new UnicodeIssue(
                    RuleId: UnicodePolicy.Rule_InvalidUtf16,
                    Severity: _policy.SeverityFor(UnicodePolicy.Rule_InvalidUtf16, Severity.Error),
                    FilePath: filePath,
                    Line: line,
                    Column: col,
                    CodePoint: -1,
                    CodePointHex: "N/A",
                    Category: "N/A",
                    Display: "<invalid utf-16>",
                    Message: "Invalid UTF-16 sequence encountered."
                ));
                i++;
                continue;
            }

            int cp = rune.Value;
            int startIndex = i;
            i += rune.Utf16SequenceLength;

            // Hard deny: BiDi controls
            if (_policy.Config.DenyBidiControls && _policy.IsBidiControl(cp))
            {
                AddIssue(issues, mapper, filePath, startIndex, cp, rune,
                    UnicodePolicy.Rule_BidiControl,
                    _policy.SeverityFor(UnicodePolicy.Rule_BidiControl, Severity.Error),
                    "BiDi control character detected (can reorder/hide text in display).");
                continue;
            }

            // Hard deny: Format (includes most zero-width characters)
            if (_policy.Config.DenyFormatCharacters && _policy.IsFormatCharacter(cp))
            {
                AddIssue(issues, mapper, filePath, startIndex, cp, rune,
                    UnicodePolicy.Rule_ZeroWidthOrFormat,
                    _policy.SeverityFor(UnicodePolicy.Rule_ZeroWidthOrFormat, Severity.Error),
                    "Format/zero-width character detected (invisible or tokenization-affecting).");
                continue;
            }

            // Hard deny: suspicious whitespace
            if (_policy.Config.DenyNonAsciiWhitespace && _policy.IsSuspiciousWhitespace(cp))
            {
                AddIssue(issues, mapper, filePath, startIndex, cp, rune,
                    UnicodePolicy.Rule_NonAsciiWhitespace,
                    _policy.SeverityFor(UnicodePolicy.Rule_NonAsciiWhitespace, Severity.Error),
                    "Non-ASCII whitespace detected (can hide or alter meaning).");
                continue;
            }

            // Whitelist enforcement
            if (!_policy.IsInWhitelist(cp))
            {
                var sev = _policy.Config.StrictWhitelist ? Severity.Error : Severity.Warning;
                sev = _policy.SeverityFor(UnicodePolicy.Rule_Whitelist, sev);

                AddIssue(issues, mapper, filePath, startIndex, cp, rune,
                    UnicodePolicy.Rule_Whitelist,
                    sev,
                    "Character not in whitelist.");
            }
        }

        return issues;
    }

    public IReadOnlyList<UnicodeIssue> ScanFile(string path, Encoding? encoding = null)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));

        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        string text;
        try
        {
            text = File.ReadAllText(path, encoding);
        }
        catch (DecoderFallbackException ex)
        {
            return new[]
            {
                new UnicodeIssue(
                    RuleId: UnicodePolicy.Rule_InvalidUtf16,
                    Severity: Severity.Error,
                    FilePath: path,
                    Line: 0,
                    Column: 0,
                    CodePoint: -1,
                    CodePointHex: "N/A",
                    Category: "N/A",
                    Display: "<decoding error>",
                    Message: $"Text decoding failed: {ex.Message}"
                )
            };
        }

        return ScanText(text, path);
    }

    private static void AddIssue(
        List<UnicodeIssue> issues,
        TextPositionMapper mapper,
        string filePath,
        int utf16Index,
        int codePoint,
        Rune rune,
        string ruleId,
        Severity severity,
        string message)
    {
        var (line, col) = mapper.GetLineColumn(utf16Index);
        var cat = Rune.GetUnicodeCategory(rune);

        issues.Add(new UnicodeIssue(
            RuleId: ruleId,
            Severity: severity,
            FilePath: filePath,
            Line: line,
            Column: col,
            CodePoint: codePoint,
            CodePointHex: $"U+{codePoint:X4}",
            Category: cat.ToString(),
            Display: SafeDisplay(rune),
            Message: message));
    }

    private static string SafeDisplay(Rune r)
    {
        var cat = Rune.GetUnicodeCategory(r);
        // Make “invisible” things visible in output
        if (cat == UnicodeCategory.Format)
            return $"<{cat} {r}>";
        // Control chars
        if (r.Value <= 0x1F || r.Value == 0x7F)
            return $"<control U+{r.Value:X4}>";

        return $"{r} (U+{r.Value:X4})";
    }

    private static HashSet<string> DetectScriptsPresent(string text)
    {
        // Heuristic: detect scripts by Unicode block-ish ranges (not perfect, but useful).
        // We only track a few “confusable relevant” scripts + “Other”.
        var scripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < text.Length;)
        {
            if (!Rune.TryGetRuneAt(text, i, out var r))
            {
                i++;
                continue;
            }

            i += r.Utf16SequenceLength;

            // Treat ASCII letters as Latin for mixed-script detection.
            // Ignore ASCII digits/whitespace/punctuation to reduce noise.
            if (r.Value <= 0x7F)
            {
                if ((r.Value >= 'A' && r.Value <= 'Z') || (r.Value >= 'a' && r.Value <= 'z'))
                    scripts.Add("Latin");

                continue;
            }

            var s = ScriptFor(r.Value);
            if (s is not null)
                scripts.Add(s);

            // Early exit for speed
            if (scripts.Count > 1)
                return scripts;
        }

        return scripts;

        static string? ScriptFor(int cp)
        {
            // Latin-1 Supplement / Latin Extended
            if ((cp >= 0x00C0 && cp <= 0x024F) || (cp >= 0x1E00 && cp <= 0x1EFF))
                return "Latin";

            // Greek
            if ((cp >= 0x0370 && cp <= 0x03FF) || (cp >= 0x1F00 && cp <= 0x1FFF))
                return "Greek";

            // Cyrillic
            if ((cp >= 0x0400 && cp <= 0x04FF) || (cp >= 0x0500 && cp <= 0x052F))
                return "Cyrillic";

            // Hebrew
            if (cp >= 0x0590 && cp <= 0x05FF)
                return "Hebrew";

            // Arabic
            if ((cp >= 0x0600 && cp <= 0x06FF) || (cp >= 0x0750 && cp <= 0x077F))
                return "Arabic";

            // CJK (very broad)
            if ((cp >= 0x3040 && cp <= 0x30FF) || (cp >= 0x4E00 && cp <= 0x9FFF))
                return "CJK";

            // For MCP files, any other non-ASCII script still counts as “Other”
            return "Other";
        }
    }
}
