using System.Text;

namespace Keryx.Analyzers;

/// <summary>
///     Writes generated source line by line, indenting four spaces per open block, with <c>\n</c> line endings on every platform so the
///     output is deterministic.
/// </summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private int _indent;

    public void Line(string text = "")
    {
        if (text.Length > 0)
        {
            _builder.Append(' ', _indent * 4).Append(text);
        }

        _builder.Append('\n');
    }

    public void Open()
    {
        Line("{");
        _indent++;
    }

    public void Close(string suffix = "")
    {
        _indent--;
        Line("}" + suffix);
    }

    public override string ToString() => _builder.ToString();
}
