using System.Globalization;
using System.Text;
using UnicodeMcpScan.Core.Scan;

namespace UnicodeMcpScan.Core.Policy;

public sealed class UnicodePolicy
{
    // Rule IDs (stable strings for CI/SARIF)
    public const string Rule_Whitelist = "MCP0001";
    public const string Rule_ZeroWidthOrFormat = "MCP0002";
    public const string Rule_BidiControl = "MCP0003";
    public const string Rule_NonAsciiWhitespace = "MCP0004";
    public const string Rule_NonNfc = "MCP0005";
    public const string Rule_MixedScripts = "MCP0006";
    public const string Rule_InvalidUtf16 = "MCP0007";

    private readonly UnicodePolicyConfig _cfg;

    private readonly HashSet<int> _allowed = new();
    private readonly List<(int Start, int End)> _allowedRanges = new();
    private readonly HashSet<UnicodeCategory> _allowedCategories = new();
    private readonly HashSet<int> _denied = new();

    // BiDi controls (common set; intentionally conservative)
    private static readonly HashSet<int> BidiControls = new()
    {
        0x061C, // ARABIC LETTER MARK
        0x200E, // LRM
        0x200F, // RLM
        0x202A, // LRE
        0x202B, // RLE
        0x202C, // PDF
        0x202D, // LRO
        0x202E, // RLO
        0x2066, // LRI
        0x2067, // RLI
        0x2068, // FSI
        0x2069, // PDI
    };

    // Non-ASCII whitespace that commonly hides content or changes tokenization/rendering
    private static readonly HashSet<int> SuspiciousWhitespace = new()
    {
        0x00A0, // NBSP
        0x1680, // OGHAM SPACE MARK
        0x180E, // MONGOLIAN VOWEL SEPARATOR (historic)
        0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A, // en/em/thin spaces
        0x2028, // LINE SEPARATOR
        0x2029, // PARAGRAPH SEPARATOR
        0x202F, // NARROW NBSP
        0x205F, // MEDIUM MATHEMATICAL SPACE
        0x3000, // IDEOGRAPHIC SPACE
    };

    public UnicodePolicy(UnicodePolicyConfig cfg)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));

        foreach (var s in _cfg.AllowedCodePoints)
            _allowed.Add(ParseHexCodePoint(s));

        foreach (var s in _cfg.AllowedRanges)
        {
            var parts = s.Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) throw new ArgumentException($"Invalid range '{s}'");
            var start = ParseHexCodePoint(parts[0]);
            var end = ParseHexCodePoint(parts[1]);
            if (start > end) throw new ArgumentException($"Range start > end '{s}'");
            _allowedRanges.Add((start, end));
        }

        foreach (var cat in _cfg.AllowedCategories)
        {
            if (!Enum.TryParse<UnicodeCategory>(cat, ignoreCase: true, out var parsed))
                throw new ArgumentException($"Invalid UnicodeCategory '{cat}'");
            _allowedCategories.Add(parsed);
        }

        foreach (var s in _cfg.DeniedCodePoints)
            _denied.Add(ParseHexCodePoint(s));
    }

    public UnicodePolicyConfig Config => _cfg;

    public bool IsAsciiWhitespaceAllowed(int cp)
    {
        if (!_cfg.AllowAsciiWhitespace) return false;
        return cp is 0x20 or 0x09 or 0x0A or 0x0D;
    }

    public bool IsInWhitelist(int cp)
    {
        if (!Rune.IsValid(cp)) return false;
        if (_denied.Contains(cp)) return false;
        if (IsAsciiWhitespaceAllowed(cp)) return true;

        if (_allowed.Contains(cp)) return true;
        foreach (var (s, e) in _allowedRanges)
            if (cp >= s && cp <= e) return true;

        var cat = Rune.GetUnicodeCategory(new Rune(cp));
        if (_allowedCategories.Contains(cat)) return true;

        return false;
    }

    public bool IsBidiControl(int cp) => BidiControls.Contains(cp);

    public bool IsSuspiciousWhitespace(int cp)
    {
        // ASCII whitespace is not "suspicious"
        if (cp is 0x20 or 0x09 or 0x0A or 0x0D) return false;

        // Any other whitespace-like chars we hard-deny (in MCP mode)
        // Include explicit set plus general check for separators.
        if (SuspiciousWhitespace.Contains(cp)) return true;

        if (Rune.IsValid(cp))
        {
            var r = new Rune(cp);
            var cat = Rune.GetUnicodeCategory(r);
            if (cat is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                return true;
        }

        return false;
    }

    public bool IsFormatCharacter(int cp)
    {
        if (!Rune.IsValid(cp)) return false;
        return Rune.GetUnicodeCategory(new Rune(cp)) == UnicodeCategory.Format;
    }

    public Severity SeverityFor(string ruleId, Severity defaultSeverity)
    {
        if (_cfg.RuleSeverities.TryGetValue(ruleId, out var s)) return s;
        return defaultSeverity;
    }

    private static int ParseHexCodePoint(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Empty code point");

        var t = s.Trim();
        if (t.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            t = t[2..];
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            t = t[2..];

        if (!int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp))
            throw new ArgumentException($"Invalid hex code point '{s}'");

        if (!Rune.IsValid(cp))
            throw new ArgumentException($"Not a valid Unicode scalar value: '{s}'");

        return cp;
    }
}
