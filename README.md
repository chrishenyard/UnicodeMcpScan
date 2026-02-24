UnicodeMcpScan
================

`UnicodeMcpScan` is a command-line tool that scans text files for risky or policy-violating Unicode usage in MCP manifests and related content. It detects things like:

- Non-NFC-normalized text (normalization tricks)
- Mixed-script content (e.g., Latin + Cyrillic)
- Invisible / zero-width characters and format controls
- Non-ASCII whitespace
- Characters outside a configured whitelist

The core scanning logic lives in `src/UnicodeMcpScan.Core`, and the CLI entry point is `src/UnicodeMcpScan.Cli/Program.cs`.

## CLI Usage

```bash
UnicodeMcpScan <path> [options]
```

Where `<path>` can be:

- A single file path, or
- A directory path (optionally scanned recursively)

Examples:

```bash
# Scan a single file
UnicodeMcpScan ./manifest.json

# Scan a directory recursively, with a policy file
UnicodeMcpScan ./mcp --recursive --config=unicode-policy.json
```

## Command-line Options

- `--config=<file>`  
  Path to a JSON policy file (`UnicodePolicyConfig`). Default is `unicode-policy.json` in the current directory.  
  If the file does not exist, the tool exits with code `2`.

- `--recursive`  
  When `<path>` is a directory, scan it recursively (`SearchOption.AllDirectories`).  
  Without this flag, only the top directory is scanned.

- `--include=<glob>`  
  One or more glob patterns to include. If no `--include` is provided, all files are included by default.  
  You can repeat this option:

  ```bash
  UnicodeMcpScan ./mcp --recursive --include=*.json --include=*.md
  ```

- `--exclude=<glob>`  
  One or more glob patterns to exclude. Any file that matches an exclude pattern is skipped:

  ```bash
  UnicodeMcpScan ./mcp --recursive --exclude=*.log --exclude=*temp*
  ```

- `--fail-on=warn|error`  
  Controls what severity level causes a nonzero exit code.

  - `--fail-on=error` (default): exit code `1` if any `Error` severity issues exist.
  - `--fail-on=warn`: exit code `1` if any `Warning` or higher issues exist.

  If no issues are found at all, the exit code is `0` regardless of this setting.

- `--sarif=<file>`  
  Writes a SARIF v2.1.0 report to the specified file using `SarifWriter`.  
  This is useful for CI systems and code analysis tools that understand SARIF.

## Glob Matching Behavior

The `--include` and `--exclude` options use a simple built-in glob matcher:

- Supported wildcards: `*` (any sequence) and `?` (single character)
- Matching is case-insensitive
- Patterns are tested against both:
  - The file name (`Path.GetFileName(filePath)`), and
  - The full path
- `**` is *not* supported; use `--recursive` to traverse subdirectories

Examples:

- `--include=*.mcp` matches any `.mcp` file
- `--exclude=*tests*` skips any path that contains `tests`

## Output Format

For each scanned file, the CLI prints issues to `stdout`.

- Character-level issues (with line information):

  ```text
  <filePath>:<line>:<column> <RuleId> <Severity> <CodePointHex> <Display> - <Message>
  ```

  Example:

  ```text
  ./mcp/manifest.json:12:34 Unicode.Whitelist Warning U+200B <Format U+200B> - Character not in whitelist.
  ```

- File-level issues (line == 0), such as non-NFC text or mixed scripts:

  ```text
  <filePath> <RuleId> <Severity> - <Message>
  ```

At the end of the run:

- If no issues were found:

  ```text
  No issues found.
  ```

- If issues were found:

  ```text
  Total issues: <count> (fail-on: <Warning|Error>)
  ```

Exit codes:

- `0` – success, no issues (or only below configured fail threshold)
- `1` – at least one issue at or above the `--fail-on` severity
- `2` – usage/configuration error (e.g., missing config file, bad arguments)

## JSON Policy Files

The CLI is driven by a JSON configuration file that maps to `UnicodePolicyConfig` in `UnicodeMcpScan.Core.Policy`.

- Default path: `unicode-policy.json` (configurable via `--config=...`)
- The file is read and deserialized with `System.Text.Json` using `PropertyNameCaseInsensitive = true`, so property names can be camelCase, PascalCase, etc.
- The config determines:
  - Which rules are enabled (e.g., `RequireNfcNormalization`, `DetectMixedScripts`, `DenyBidiControls`, `DenyFormatCharacters`, `DenyNonAsciiWhitespace`)
  - Severity levels for each rule (e.g., `NonNfcSeverity`, `MixedScriptSeverity`)
  - Whitelist mode (`StrictWhitelist` vs more permissive)
  - Whitelisted code points / ranges and allowed categories

Example (simplified) config:

```json
{
  "requireNfcNormalization": true,
  "detectMixedScripts": true,
  "denyBidiControls": true,
  "denyFormatCharacters": true,
  "denyNonAsciiWhitespace": true,
  "strictWhitelist": true,
  "nonNfcSeverity": "Warning",
  "mixedScriptSeverity": "Warning",
  "whitelist": {
    "allowedCodePoints": ["U+0009", "U+000A", "U+0020", "U+0021"],
    "allowedRanges": ["U+0020-U+007E"]
  }
}
```

`UnicodePolicy` consumes this config and exposes rule helpers (e.g., `IsBidiControl`, `IsFormatCharacter`, `IsSuspiciousWhitespace`, `IsInWhitelist`) used by `UnicodeScanner` to enforce the policy during a scan.

## How Scanning Works

The `UnicodeScanner` in `UnicodeMcpScan.Core.Scan` performs the actual analysis:

- Applies file-level rules:
  - NFC normalization check if `RequireNfcNormalization` is enabled
  - Mixed-script detection if `DetectMixedScripts` is enabled
- Iterates text with `System.Text.Rune` to handle full Unicode code points
- For each rune, applies rules according to the policy:
  - BiDi control detection (`DenyBidiControls`)
  - Format / zero-width characters (`DenyFormatCharacters`)
  - Suspicious non-ASCII whitespace (`DenyNonAsciiWhitespace`)
  - Whitelist enforcement (`IsInWhitelist`, driven by `StrictWhitelist` and whitelist data)
- Uses `TextPositionMapper` to translate UTF-16 indices to line and column numbers
- Emits `UnicodeIssue` instances describing every violation, including:
  - File path, line, and column
  - Rule ID and severity
  - Code point (int and `U+XXXX` form)
  - Unicode category and a safe display representation

The CLI aggregates all `UnicodeIssue` results and applies `--fail-on` logic to determine the exit code.

## Tests

The solution includes automated tests under `src/UnicodeMcpScan.Tests`.

Run all tests from the solution root with:

```bash
dotnet test
```

### Scanner Tests

`ScannerTests` focus on the core scanning behavior in `UnicodeMcpScan.Core`:

- Per-character detection:
  - BiDi control characters
  - Format / zero-width characters
  - Suspicious whitespace
  - Characters outside the whitelist
- File-level rules:
  - NFC normalization detection
  - Mixed-script detection
- Line/column mapping:
  - Verifies that `TextPositionMapper` correctly maps UTF-16 indices to human-friendly positions

The tests build policy instances using `UnicodePolicyConfig` and `UnicodePolicy`, then use `UnicodeScanner` to scan synthesized strings or files and assert on the returned `UnicodeIssue` list (rule IDs, severities, positions, and messages).

### Integration Tests

`IntegrationTests` cover end-to-end flows closer to real-world usage:

- Scanning actual sample files and directories
- Verifying that policy configuration is honored (strict vs relaxed whitelist, rule toggles, severity overrides)
- Ensuring the CLI-style behavior works as expected:
  - Correct set of files selected based on include/exclude-like patterns and recursion
  - Proper aggregation of issues and exit-code semantics (`--fail-on` behavior)
  - Valid SARIF output via `SarifWriter.ToSarif`, including correct mapping of `UnicodeIssue` data to SARIF results

These tests act as a safety net when changing the scanner, policy, or CLI argument handling, helping to ensure that behavior remains consistent over time.
