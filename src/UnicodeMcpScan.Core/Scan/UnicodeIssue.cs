namespace UnicodeMcpScan.Core.Scan;

public enum Severity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed record UnicodeIssue(
    string RuleId,
    Severity Severity,
    string FilePath,
    int Line,
    int Column,
    int CodePoint,
    string CodePointHex,
    string Category,
    string Display,
    string Message
);
