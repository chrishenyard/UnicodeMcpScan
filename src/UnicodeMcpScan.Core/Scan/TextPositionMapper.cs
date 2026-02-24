namespace UnicodeMcpScan.Core.Scan;

/// <summary>
/// Converts a UTF-16 index into line/column (1-based).
/// Efficient enough for typical instruction files.
/// </summary>
public sealed class TextPositionMapper
{
    private readonly string _text;

    public TextPositionMapper(string text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public (int Line, int Column) GetLineColumn(int utf16Index)
    {
        int line = 1;
        int col = 1;

        var max = Math.Min(utf16Index, _text.Length);
        for (int i = 0; i < max; i++)
        {
            var ch = _text[i];
            if (ch == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }

        return (line, col);
    }
}
