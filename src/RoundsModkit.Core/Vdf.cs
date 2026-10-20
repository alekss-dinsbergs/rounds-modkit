using System.Text;

namespace RoundsModkit.Core;

/// <summary>Minimal reader for Valve's text KeyValues format (appmanifest, libraryfolders, localconfig).</summary>
public sealed class VdfNode
{
    public string? Value { get; init; }
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public VdfNode? this[string key] => Children.TryGetValue(key, out var n) ? n : null;

    public VdfNode? Path(params string[] keys)
    {
        VdfNode? cur = this;
        foreach (var k in keys)
        {
            cur = cur?[k];
            if (cur is null) return null;
        }
        return cur;
    }

    public static VdfNode Parse(string text)
    {
        using var tokens = Tokenize(text).GetEnumerator();
        var root = new VdfNode();
        ParseInto(root, tokens, isRoot: true);
        return root;
    }

    public static VdfNode Load(string path) => Parse(File.ReadAllText(path));

    private readonly record struct Token(string Text, bool Structural);

    private static void ParseInto(VdfNode node, IEnumerator<Token> tokens, bool isRoot)
    {
        while (tokens.MoveNext())
        {
            var key = tokens.Current;
            if (key.Structural)
            {
                if (key.Text == "}" && !isRoot) return;
                continue;
            }
            if (!tokens.MoveNext()) return;
            var next = tokens.Current;
            if (next.Structural && next.Text == "{")
            {
                var child = new VdfNode();
                ParseInto(child, tokens, isRoot: false);
                node.Children[key.Text] = child;
            }
            else
            {
                node.Children[key.Text] = new VdfNode { Value = next.Text };
            }
        }
    }

    // Braces are only structural when unquoted; a quoted "{" is a value.
    private static IEnumerable<Token> Tokenize(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '{' || c == '}')
            {
                i++;
                yield return new Token(c.ToString(), true);
                continue;
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        sb.Append(text[i] switch { 'n' => '\n', 't' => '\t', _ => text[i] });
                    }
                    else sb.Append(text[i]);
                    i++;
                }
                i++;
                yield return new Token(sb.ToString(), false);
                continue;
            }
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}' && text[i] != '"') i++;
            yield return new Token(text[start..i], false);
        }
    }
}
