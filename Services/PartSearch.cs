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

    /// <summary>Padanan istilah umum (Indonesia → Inggris) untuk permintaan yang ditulis bebas.</summary>
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["oli"] = new[] { "oil" }, ["pelumas"] = new[] { "oil", "lubricant", "grease" }, ["gemuk"] = new[] { "grease" },
        ["hidrolik"] = new[] { "hydraulic" }, ["bantalan"] = new[] { "bearing" }, ["laher"] = new[] { "bearing" },
        ["sabuk"] = new[] { "belt" }, ["vanbelt"] = new[] { "belt" }, ["fanbelt"] = new[] { "belt" },
        ["selang"] = new[] { "hose" }, ["baut"] = new[] { "bolt" }, ["mur"] = new[] { "nut" }, ["ring"] = new[] { "washer", "ring" },
        ["kabel"] = new[] { "cable", "wire" }, ["saklar"] = new[] { "switch" }, ["sakelar"] = new[] { "switch" },
        ["pompa"] = new[] { "pump" }, ["katup"] = new[] { "valve" }, ["rantai"] = new[] { "chain" }, ["gir"] = new[] { "gear", "sprocket" },
        ["gearbox"] = new[] { "gearbox", "reducer" }, ["silinder"] = new[] { "cylinder" }, ["saringan"] = new[] { "filter" },
        ["sekring"] = new[] { "fuse" }, ["lampu"] = new[] { "lamp" }, ["kontaktor"] = new[] { "contactor" }, ["relai"] = new[] { "relay" },
        ["termometer"] = new[] { "thermometer" }, ["pengukur"] = new[] { "gauge" }, ["tekanan"] = new[] { "pressure" },
        ["nosel"] = new[] { "nozzle" }, ["pegas"] = new[] { "spring" }, ["per"] = new[] { "spring" }, ["sambungan"] = new[] { "fitting", "connector", "elbow" },
        ["siku"] = new[] { "elbow" }, ["timer"] = new[] { "timer" }, ["motor"] = new[] { "motor" }, ["kipas"] = new[] { "fan" }
    };

    private static IEnumerable<string> Expand(string token)
    {
        yield return token;
        if (Synonyms.TryGetValue(token, out var syn)) foreach (var s in syn) yield return s;
    }

    private static string Haystack(Part p) => string.Join(" ", p.PartNumber, p.Name, p.Specification, p.Brand, p.Category, p.Location, p.Machine, p.Remarks);

    /// <summary>
    /// Pencarian untuk teks bebas: coba cocok semua kata dulu; bila kosong, cari per kata (dan padanannya)
    /// yang cocok di awal kata, urutkan menurut jumlah kata yang cocok. Kata pendek (&lt; 3 huruf) diabaikan di tahap ini.
    /// </summary>
    public static async Task<List<Part>> FuzzyAsync(IQueryable<Part> parts, string? text, int take)
    {
        var strict = await Apply(parts, text).Take(300).ToListAsync();
        if (strict.Count > 0)
            return strict.OrderByDescending(p => Score(p, text)).ThenByDescending(p => p.StockQty > 0).Take(take).ToList();

        var groups = Tokens(text).Where(t => t.Length >= 3 && !t.All(char.IsDigit)).Select(t => Expand(t).Distinct(StringComparer.OrdinalIgnoreCase).ToList()).ToList();
        if (groups.Count == 0) return new();
        var cand = new Dictionary<int, Part>();
        foreach (var w in groups.SelectMany(g => g).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var p in await parts.Where(p => EF.Functions.Like(p.PartNumber + " " + p.Name + " " + (p.Specification ?? "") + " " + (p.Brand ?? "")
                         + " " + (p.Category ?? "") + " " + (p.Location ?? "") + " " + (p.Machine ?? "") + " " + (p.Remarks ?? ""), "%" + EscapeLike(w) + "%", "\\"))
                         .Take(100).ToListAsync())
                cand[p.Id] = p;

        // Hitung kelompok kata yang cocok di AWAL kata (supaya "oli" tidak cocok dengan "Solid").
        int Hits(Part p)
        {
            var hay = Haystack(p);
            return groups.Count(g => g.Any(w => Regex.IsMatch(hay, @"(?<![\p{L}\p{N}])" + Regex.Escape(w), RegexOptions.IgnoreCase)));
        }
        return cand.Values.Select(p => (p, h: Hits(p))).Where(x => x.h > 0)
            .OrderByDescending(x => x.h).ThenByDescending(x => x.p.StockQty > 0).ThenBy(x => x.p.PartNumber)
            .Take(take).Select(x => x.p).ToList();
    }
}
