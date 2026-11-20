using System.Text;

namespace SparePartControl.Services;

public static class Csv
{
    public static string Escape(string? s)
    {
        s ??= "";
        // Cegah formula injection saat dibuka di Excel.
        if (s.Length > 0 && "=+@\t\r".Contains(s[0])) s = "'" + s;
        return s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static byte[] Build(IEnumerable<string> headers, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(Escape)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>Parser CSV sederhana (mendukung tanda kutip dan pemisah , atau ;).</summary>
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var firstLine = text.Split('\n')[0];
        var sep = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
        var cur = new List<string>();
        var sb = new StringBuilder();
        var inQ = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQ)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQ = false;
                else sb.Append(c);
            }
            else if (c == '"') inQ = true;
            else if (c == sep) { cur.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                cur.Add(sb.ToString()); sb.Clear();
                if (cur.Any(x => x.Trim().Length > 0)) rows.Add(cur.ToArray());
                cur = new List<string>();
            }
            else sb.Append(c);
        }
        cur.Add(sb.ToString());
        if (cur.Any(x => x.Trim().Length > 0)) rows.Add(cur.ToArray());
        return rows;
    }
}
