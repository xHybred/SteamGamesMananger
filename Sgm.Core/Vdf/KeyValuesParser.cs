using System.Collections.Generic;
using System.Text;

namespace Sgm.Core.Vdf;

/// <summary>
/// Minimal text-VDF (KeyValues) reader for Steam's <c>libraryfolders.vdf</c> and
/// <c>appmanifest_*.acf</c> files. Handles quoted keys/values and nested braces;
/// values are <see cref="string"/> or nested <c>Dictionary&lt;string, object&gt;</c>.
/// </summary>
public static class KeyValuesParser
{
    public static Dictionary<string, object> Parse(string text)
    {
        var tokens = Tokenize(text);
        var index = 0;
        var result = ReadObject(tokens, ref index);
        return result;
    }

    public static Dictionary<string, object> ParseFile(string path)
        => Parse(File.ReadAllText(path, Encoding.UTF8));

    private static Dictionary<string, object> ReadObject(List<string> tokens, ref int index)
    {
        var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (index < tokens.Count)
        {
            var key = tokens[index++];
            if (key == "}")
                break;
            if (index >= tokens.Count)
                break;

            if (tokens[index] == "{")
            {
                index++; // consume '{'
                obj[key] = ReadObject(tokens, ref index);
            }
            else
            {
                obj[key] = tokens[index++];
            }
        }
        return obj;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                while (i < n && text[i] != '\n')
                    i++;
                continue;
            }
            if (c == '{' || c == '}')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }
            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < n && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < n)
                    {
                        i++;
                        sb.Append(text[i] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            '\\' => '\\',
                            '"' => '"',
                            var other => other,
                        });
                    }
                    else
                    {
                        sb.Append(text[i]);
                    }
                    i++;
                }
                i++; // closing quote
                tokens.Add(sb.ToString());
                continue;
            }

            // Bare token (unquoted value) — read to whitespace.
            var start = i;
            while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}')
                i++;
            tokens.Add(text[start..i]);
        }
        return tokens;
    }
}
