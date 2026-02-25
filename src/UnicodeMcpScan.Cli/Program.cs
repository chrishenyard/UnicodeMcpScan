using System.Text.Json;
using System.Text.Json.Serialization;
using UnicodeMcpScan.Core.Output;
using UnicodeMcpScan.Core.Policy;
using UnicodeMcpScan.Core.Scan;

namespace UnicodeMcpScan.Cli;

public class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("UnicodeMcpScan usage:");
            Console.WriteLine("  UnicodeMcpScan <path> [--config=unicode-policy.json] [--recursive] [--include=glob] [--exclude=glob]");
            Console.WriteLine("                [--fail-on=warn|error] [--sarif=out.sarif]");
            return 2;
        }

        var path = args[0];

        string configPath = GetArgValue(args, "--config") ?? "unicode-policy.json";
        bool recursive = args.Any(a => a.Equals("--recursive", StringComparison.OrdinalIgnoreCase));
        string failOn = GetArgValue(args, "--fail-on") ?? "error";
        string? sarifOut = GetArgValue(args, "--sarif");

        var includes = GetMultiArgValues(args, "--include");
        var excludes = GetMultiArgValues(args, "--exclude");

        var policy = UnicodePolicyConfig.CreateDefaultPolicy();

        if (File.Exists(configPath))
        {
            var cfg = JsonSerializer.Deserialize<UnicodePolicyConfig>(
                File.ReadAllText(configPath),
                JsonOptions)
                ?? throw new InvalidOperationException("Failed to parse config.");

            policy = new UnicodePolicy(cfg);
        }

        var scanner = new UnicodeScanner(policy);

        var files = ResolveFiles(path, recursive, includes, excludes).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine("No matching files.");
            return 0;
        }

        var allIssues = new List<UnicodeIssue>();

        foreach (var file in files)
        {
            var issues = scanner.ScanFile(file);
            allIssues.AddRange(issues);

            foreach (var i in issues.Where(x => x.Line > 0))
            {
                Console.WriteLine($"{i.FilePath}:{i.Line}:{i.Column} {i.RuleId} {i.Severity} {i.CodePointHex} {i.Display} - {i.Message}");
            }

            foreach (var i in issues.Where(x => x.Line == 0))
            {
                // file-level issues like NFC/mixed-script
                Console.WriteLine($"{i.FilePath} {i.RuleId} {i.Severity} - {i.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(sarifOut))
        {
            File.WriteAllText(sarifOut, SarifWriter.ToSarif(allIssues));
            Console.WriteLine($"SARIF written: {sarifOut}");
        }

        var threshold = failOn.Equals("warn", StringComparison.OrdinalIgnoreCase) ? Severity.Warning : Severity.Error;
        var hasFail = allIssues.Any(i => i.Severity >= threshold);

        if (!allIssues.Any())
        {
            Console.WriteLine("No issues found.");
            return 0;
        }

        Console.WriteLine($"Total issues: {allIssues.Count} (fail-on: {threshold})");
        return hasFail ? 1 : 0;
    }

    static string? GetArgValue(string[] args, string key)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
        return arg is null ? null : arg.Split('=', 2)[1];
    }

    static List<string> GetMultiArgValues(string[] args, string key)
    {
        // Accept repeated: --include=*.mcp --include=*.md
        return args
            .Where(a => a.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Split('=', 2)[1])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    static IEnumerable<string> ResolveFiles(string path, bool recursive, List<string> includes, List<string> excludes)
    {
        if (File.Exists(path))
        {
            if (Matches(path, includes, excludes)) yield return Path.GetFullPath(path);
            yield break;
        }

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);

        var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var file in Directory.EnumerateFiles(path, "*", opt))
        {
            if (Matches(file, includes, excludes))
                yield return Path.GetFullPath(file);
        }
    }

    static bool Matches(string filePath, List<string> includes, List<string> excludes)
    {
        // If no includes, include all.
        bool included = includes.Count == 0 || includes.Any(p => GlobMatch(filePath, p));
        if (!included) return false;

        bool excluded = excludes.Any(p => GlobMatch(filePath, p));
        return !excluded;
    }

    static bool GlobMatch(string filePath, string pattern)
    {
        // Minimal glob support: * and ? against filename or full path.
        // Common usage: *.mcp, *.md, ** not supported (use --recursive).
        var name = Path.GetFileName(filePath);

        return SimpleMatch(name, pattern) || SimpleMatch(filePath, pattern);

        static bool SimpleMatch(string text, string pat)
        {
            int ti = 0, pi = 0;
            int starText = -1, starPat = -1;

            while (ti < text.Length)
            {
                if (pi < pat.Length && (pat[pi] == '?' || char.ToLowerInvariant(pat[pi]) == char.ToLowerInvariant(text[ti])))
                {
                    pi++; ti++;
                    continue;
                }

                if (pi < pat.Length && pat[pi] == '*')
                {
                    starPat = pi++;
                    starText = ti;
                    continue;
                }

                if (starPat != -1)
                {
                    pi = starPat + 1;
                    ti = ++starText;
                    continue;
                }

                return false;
            }

            while (pi < pat.Length && pat[pi] == '*') pi++;
            return pi == pat.Length;
        }
    }
}