using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Services;

public record CodeInput(string? Maker, string? Model, string? Supplier);

/// <summary>Pembuat goods code berdasarkan pola di menu Pengaturan.</summary>
public class PartCode
{
    private readonly AppDbContext _db;
    private readonly AppSettingsService _settings;
    private readonly SiteContext _site;
    public PartCode(AppDbContext db, AppSettingsService settings, SiteContext site) { _db = db; _settings = settings; _site = site; }

    public static string PrefixFor(CodeSettings s, Site? site) => string.IsNullOrWhiteSpace(site?.CodePrefix) ? s.Prefix ?? "" : site!.CodePrefix!.Trim();

    public static string Segment(string? value, CodeSettings s)
    {
        var v = (value ?? "").Trim();
        if (s.Uppercase) v = v.ToUpperInvariant();
        v = s.StripSymbols ? Regex.Replace(v, @"[^\p{L}\p{N}]", "") : Regex.Replace(v, @"\s+", "-");
        if (s.SegmentMaxLength > 0 && v.Length > s.SegmentMaxLength) v = v[..s.SegmentMaxLength];
        return v;
    }

    /// <summary>Token yang dipakai pola tapi datanya kosong (mis. Maker belum diisi).</summary>
    public static List<string> MissingInputs(CodeSettings s, CodeInput input)
    {
        var miss = new List<string>();
        if (s.Pattern.Contains("{MAKER}") && Segment(input.Maker, s).Length == 0) miss.Add("Maker");
        if (s.Pattern.Contains("{MODEL}") && Segment(input.Model, s).Length == 0) miss.Add("Model");
        if (s.Pattern.Contains("{SUPP}") && Segment(input.Supplier, s).Length == 0) miss.Add("Supplier");
        return miss;
    }

    /// <summary>
    /// Ubah pola menjadi regex. Mode per-kombinasi: token diganti nilai sebenarnya; mode global: token apa saja.
    /// Nomor urut ditangkap di grup "seq".
    /// </summary>
    private static (Regex rx, string literalHead) BuildRegex(CodeSettings s, CodeInput input, DateTime now, bool exact, Site? site)
    {
        var values = new Dictionary<string, string>
        {
            ["{PREFIX}"] = PrefixFor(s, site), ["{SITE}"] = site?.Code.ToUpperInvariant() ?? "", ["{MAKER}"] = Segment(input.Maker, s), ["{MODEL}"] = Segment(input.Model, s),
            ["{SUPP}"] = Segment(input.Supplier, s), ["{YYYY}"] = now.ToString("yyyy"), ["{YY}"] = now.ToString("yy"), ["{MM}"] = now.ToString("MM")
        };
        var sb = new StringBuilder("^");
        var head = new StringBuilder();
        var beforeSeq = true;
        foreach (var part in Regex.Split(s.Pattern, @"(\{[A-Z]+\})"))
        {
            if (part.Length == 0) continue;
            if (part == "{SEQ}") { sb.Append(@"(?<seq>\d+)"); beforeSeq = false; continue; }
            string lit;
            if (values.TryGetValue(part, out var v))
            {
                // {PREFIX}/{SITE} selalu tetap; token lain tetap hanya di mode per-kombinasi.
                if (!exact && part is not ("{PREFIX}" or "{SITE}")) { sb.Append(".*?"); beforeSeq = false; continue; }
                lit = v;
            }
            else lit = part;
            sb.Append(Regex.Escape(lit));
            if (beforeSeq) head.Append(lit);
        }
        sb.Append('$');
        return (new Regex(sb.ToString(), RegexOptions.IgnoreCase), head.ToString());
    }

    public static string Render(CodeSettings s, CodeInput input, int seq, DateTime now, Site? site = null) =>
        s.Pattern.Replace("{PREFIX}", PrefixFor(s, site)).Replace("{SITE}", site?.Code.ToUpperInvariant() ?? "SITE").Replace("{MAKER}", Segment(input.Maker, s)).Replace("{MODEL}", Segment(input.Model, s))
            .Replace("{SUPP}", Segment(input.Supplier, s)).Replace("{YYYY}", now.ToString("yyyy")).Replace("{YY}", now.ToString("yy"))
            .Replace("{MM}", now.ToString("MM")).Replace("{SEQ}", seq.ToString(new string('0', s.SeqDigits)));

    /// <summary>Kode berikutnya. settingsOverride dipakai untuk pratinjau di halaman Pengaturan sebelum disimpan.</summary>
    public async Task<(string? Code, string? Error)> NextAsync(CodeInput input, CodeSettings? settingsOverride = null, Site? siteOverride = null)
    {
        var s = settingsOverride ?? _settings.Code;
        var miss = MissingInputs(s, input);
        if (miss.Count > 0) return (null, $"Isi {string.Join(" dan ", miss)} terlebih dahulu.");
        var site = siteOverride ?? _site.Current;
        if (site == null && !_site.Unrestricted) return (null, "Pilih satu site terlebih dahulu (bukan \"Semua site\").");
        var now = DateTime.Now;
        var (rx, head) = BuildRegex(s, input, now, s.SeqPerGroup, site);
        // Goods code unik per site → nomor urut dihitung dari part di site ini saja.
        var q = _db.Parts.IgnoreQueryFilters().AsNoTracking().Where(p => site == null || p.SiteId == site.Id).Select(p => p.PartNumber);
        if (head.Length > 0) q = q.Where(c => c.StartsWith(head));
        var codes = await q.ToListAsync();
        var max = codes.Select(c => rx.Match(c)).Where(m => m.Success)
            .Select(m => int.TryParse(m.Groups["seq"].Value, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        // Lewati kode yang kebetulan sudah dipakai (mis. kode manual).
        var taken = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var seq = max + 1; seq < max + 10000; seq++)
        {
            var code = Render(s, input, seq, now, site);
            if (!taken.Contains(code) && !await _db.Parts.IgnoreQueryFilters().AnyAsync(p => p.PartNumber == code && (site == null || p.SiteId == site.Id)))
                return (code, null);
        }
        return (null, "Tidak menemukan nomor urut kosong.");
    }
}
