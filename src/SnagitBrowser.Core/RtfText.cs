using System.Text;

namespace SnagitBrowser.Core;

/// <summary>Wandelt einfaches RTF (wie Snagit es speichert) in Klartext um.</summary>
public static class RtfText
{
    static RtfText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly HashSet<string> SkipDestinations =
    [
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "header", "footer",
        "headerl", "headerr", "footerl", "footerr", "listtable", "listoverridetable",
        "rsidtbl", "generator", "xmlnstbl", "themedata", "datastore", "latentstyles",
    ];

    public static string DecodeBase64Rtf(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return Encoding.GetEncoding(1252).GetString(bytes);
    }

    public static string ToPlainText(string rtf)
    {
        if (string.IsNullOrEmpty(rtf)) return "";
        if (!rtf.StartsWith("{\\rtf", StringComparison.Ordinal)) return rtf.Trim();

        var enc = Encoding.GetEncoding(1252);
        var sb = new StringBuilder(rtf.Length);
        var stack = new Stack<(bool Skip, int Uc)>();
        bool skip = false;
        int uc = 1, ucSkip = 0;
        int i = 0, len = rtf.Length;

        void Emit(char ch) { if (!skip) sb.Append(ch); }

        while (i < len)
        {
            char c = rtf[i];
            switch (c)
            {
                case '{':
                    stack.Push((skip, uc)); i++; continue;
                case '}':
                    if (stack.Count > 0) (skip, uc) = stack.Pop();
                    ucSkip = 0; i++; continue;
                case '\r':
                case '\n':
                    i++; continue;
                case '\\':
                    break;
                default:
                    if (ucSkip > 0) ucSkip--; else Emit(c);
                    i++; continue;
            }

            // Steuerzeichen
            i++;
            if (i >= len) break;
            char n = rtf[i];
            if (n is '\\' or '{' or '}') { if (ucSkip > 0) ucSkip--; else Emit(n); i++; continue; }
            if (n is '\r' or '\n') { Emit('\n'); i++; continue; }
            if (n == '~') { Emit(' '); i++; continue; }
            if (n is '-' or '_') { if (n == '_') Emit('-'); i++; continue; }
            if (n == '*') { skip = true; i++; continue; }
            if (n == '\'')
            {
                if (i + 2 < len && byte.TryParse(rtf.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                {
                    if (ucSkip > 0) ucSkip--;
                    else foreach (var ch in enc.GetString([b])) Emit(ch);
                }
                i += 3; continue;
            }
            if (!char.IsAsciiLetter(n)) { i++; continue; }

            int start = i;
            while (i < len && char.IsAsciiLetter(rtf[i])) i++;
            string word = rtf[start..i];
            int? param = null;
            if (i < len && (rtf[i] == '-' || char.IsAsciiDigit(rtf[i])))
            {
                int ps = i++;
                while (i < len && char.IsAsciiDigit(rtf[i])) i++;
                if (int.TryParse(rtf.AsSpan(ps, i - ps), out var p)) param = p;
            }
            if (i < len && rtf[i] == ' ') i++;

            switch (word)
            {
                case "par":
                case "line":
                    Emit('\n'); break;
                case "tab":
                    Emit('\t'); break;
                case "u" when param is not null:
                    int code = param.Value < 0 ? param.Value + 65536 : param.Value;
                    Emit((char)code);
                    ucSkip = uc;
                    break;
                case "uc" when param is not null:
                    uc = param.Value; break;
                case "emdash": Emit('—'); break;
                case "endash": Emit('–'); break;
                case "bullet": Emit('•'); break;
                case "lquote": Emit('‘'); break;
                case "rquote": Emit('’'); break;
                case "ldblquote": Emit('“'); break;
                case "rdblquote": Emit('”'); break;
                default:
                    if (SkipDestinations.Contains(word)) skip = true;
                    break;
            }
        }

        // Zeilen säubern, leere Endzeilen entfernen
        var lines = sb.ToString().Split('\n').Select(l => l.TrimEnd());
        return string.Join("\n", lines).Trim();
    }
}
