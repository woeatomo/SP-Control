using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Models;

namespace SparePartControl.Services;

/// <summary>
/// Pencarian spare part: beberapa kata sekaligus (semua harus cocok), tidak peka huruf besar/kecil dan tanda baca
/// ("s8m1440" menemukan "S8M-1440"), di kolom kode, nama, spesifikasi, maker, supplier, model, rack, mesin dan remark.
/// </summary>
public static class PartSearch
{
    public static List<string> Tokens(string? text) =>
        Regex.Split((text ?? "").Trim(), @"\s+").Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();

    /// <summary>Hapus spasi & tanda baca, huruf besar: "S8M-1440 / 60" → "S8M144060".</summary>
    public static string Strip(string? s) => Regex.Replace((s ?? "").ToUpperInvariant(), @"[^\p{L}\p{N}]", "");

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public static IQueryable<Part> Apply(IQueryable<Part> q, string? text)
    {
        foreach (var tok in Tokens(text))
        {
            var raw = "%" + EscapeLike(tok) + "%";
            var stripped = Strip(tok);
            var strippedLike = "%" + stripped + "%";
            var useStripped = stripped.Length >= 2 && stripped != tok.ToUpperInvariant();
            q = q.Where(p =>
                EF.Functions.Like(p.PartNumber + " " + p.Name + " " + (p.Specification ?? "") + " " + (p.Brand ?? "") + " " + (p.Supplier ?? "")
                    + " " + (p.Category ?? "") + " " + (p.Location ?? "") + " " + (p.Machine ?? "") + " " + (p.Remarks ?? ""), raw, "\\")
                // Versi tanpa tanda baca untuk kode/nama/spesifikasi (mis. "s8m1440" ↔ "S8M-1440").
                || (useStripped || stripped.Length >= 3) && EF.Functions.Like(
                    (p.PartNumber + p.Name + (p.Specification ?? "")).ToUpper()
                        .Replace("-", "").Replace(" ", "").Replace("/", "").Replace(".", "").Replace("_", "").Replace("#", "").Replace(",", ""),
                    strippedLike));
        }
        return q;
    }

    /// <summary>Skor relevansi untuk mengurutkan hasil (dihitung di memori).</summary>
    public static int Score(Part p, string? text)
    {
        var tokens = Tokens(text);
        if (tokens.Count == 0) return 0;
        var full = (text ?? "").Trim();
        var code = p.PartNumber ?? "";
        var score = 0;
        if (code.Equals(full, StringComparison.OrdinalIgnoreCase)) score += 1000;
        else if (Strip(code) == Strip(full)) score += 900;
        else if (code.StartsWith(full, StringComparison.OrdinalIgnoreCase)) score += 500;
        else if (code.Contains(full, StringComparison.OrdinalIgnoreCase)) score += 300;
        if ((p.Name ?? "").Contains(full, StringComparison.OrdinalIgnoreCase)) score += 200;
        // Frasa utuh cocok di kolom lain (mis. "cabinet 9" di rack, "sindo seiki" di supplier).
        else if (tokens.Count > 1 && new[] { p.Location, p.Supplier, p.Brand, p.Specification, p.Category, p.Machine, p.Remarks }
                     .Any(x => (x ?? "").Contains(full, StringComparison.OrdinalIgnoreCase))) score += 150;
        string sc = Strip(code), sn = Strip(p.Name), ss = Strip(p.Specification);
        foreach (var t in tokens)
        {
            var st = Strip(t);
            if (st.Length == 0) continue;
            if (sc.Contains(st)) score += 40;
            else if (sn.Contains(st)) score += 25;
            else if (ss.Contains(st)) score += 15;
            else score += 5;
        }
        return score;
    }
}
