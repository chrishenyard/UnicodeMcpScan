using UnicodeMcpScan.Core.Scan;

namespace UnicodeMcpScan.Core.Policy;

public sealed class UnicodePolicyConfig
{
    /// <summary>
    /// If true: any character not allowed by whitelist is an Error.
    /// If false: non-whitelisted is Warning (you can tune rule severities too).
    /// </summary>
    public bool StrictWhitelist { get; init; } = true;

    /// <summary>Allow standard ASCII whitespace: space, tab, LF, CR (recommended).</summary>
    public bool AllowAsciiWhitespace { get; init; } = true;

    /// <summary>
    /// Allowlist for scalar code points.
    /// Example: [ "0021", "2019" ] or [ "0x2019" ] etc.
    /// </summary>
    public List<string> AllowedCodePoints { get; init; } = new();

    /// <summary>
    /// Allowlist ranges for scalar code points (inclusive), hex strings like "0021-007E".
    /// </summary>
    public List<string> AllowedRanges { get; init; } = new();

    /// <summary>
    /// Allowlist Unicode categories, e.g. "UppercaseLetter", "LowercaseLetter", "DecimalDigitNumber"
    /// Use cautiously for MCP/instruction files.
    /// </summary>
    public List<string> AllowedCategories { get; init; } = new();

    /// <summary>
    /// Hard deny: any UnicodeCategory.Format is Error (recommended true for MCP).
    /// </summary>
    public bool DenyFormatCharacters { get; init; } = true;

    /// <summary>
    /// Hard deny: any BiDi controls is Error (recommended true for MCP).
    /// </summary>
    public bool DenyBidiControls { get; init; } = true;

    /// <summary>
    /// Hard deny: non-ASCII whitespace (NBSP, thin space, etc.) is Error (recommended true).
    /// </summary>
    public bool DenyNonAsciiWhitespace { get; init; } = true;

    /// <summary>
    /// If true: require NFC normalization. If text != Normalize(FormC) => Warning or Error depending on setting below.
    /// </summary>
    public bool RequireNfcNormalization { get; init; } = true;

    public Severity NonNfcSeverity { get; init; } = Severity.Warning;

    /// <summary>
    /// If true: warn/error when file contains multiple scripts (Latin + Cyrillic/Greek/etc.).
    /// Helps catch homoglyph tricks.
    /// </summary>
    public bool DetectMixedScripts { get; init; } = true;

    public Severity MixedScriptSeverity { get; init; } = Severity.Warning;

    /// <summary>
    /// Optional explicit deny list for scalar code points (hex strings).
    /// Even if whitelist allows them.
    /// </summary>
    public List<string> DeniedCodePoints { get; init; } = new();

    /// <summary>
    /// Optional override of rule severities by RuleId.
    /// </summary>
    public Dictionary<string, Severity> RuleSeverities { get; init; } = new();
}
