using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Services;

/// <summary>Pengaturan umum perusahaan.</summary>
public class CompanySettings
{
    public string CompanyName { get; set; } = "";
    public string AppTitle { get; set; } = "Spare Part Control";
}

/// <summary>
/// Format goods code. Pola memakai token:
/// {PREFIX} {SITE} {MAKER} {MODEL} {SUPP} {YYYY} {YY} {MM} {SEQ}. {PREFIX} bisa dibedakan per site. Contoh: "{PREFIX}-{MAKER}-{SEQ}" → MaPC-BANDO-0001.
/// </summary>
public class CodeSettings
{
    public string Pattern { get; set; } = "{PREFIX}-{MAKER}-{SEQ}";
    public string Prefix { get; set; } = "MaPC";
    public int SeqDigits { get; set; } = 4;
    /// <summary>true = nomor urut dihitung terpisah per kombinasi (mis. per maker); false = satu nomor urut untuk semua part.</summary>
    public bool SeqPerGroup { get; set; } = true;
    /// <summary>Panjang maksimum segmen MAKER/MODEL/SUPP (0 = tidak dibatasi).</summary>
    public int SegmentMaxLength { get; set; }
    public bool Uppercase { get; set; } = true;
    /// <summary>Hapus spasi & simbol dari segmen (mis. "Sindo Seiki" → "SINDOSEIKI").</summary>
    public bool StripSymbols { get; set; } = true;
    /// <summary>Akhiran part 2nd used.</summary>
    public string SecondSuffix { get; set; } = "-2ND";
    /// <summary>Goods code kosong saat simpan / import → dibuat otomatis.</summary>
    public bool AutoGenerateWhenEmpty { get; set; } = true;

    public static readonly string[] Tokens = { "{PREFIX}", "{SITE}", "{MAKER}", "{MODEL}", "{SUPP}", "{YYYY}", "{YY}", "{MM}", "{SEQ}" };

    public List<string> Validate()
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(Pattern) || Pattern.Length > 80) e.Add("Pola wajib diisi (maks. 80 karakter).");
        else
        {
            if (Pattern.Split("{SEQ}").Length != 2) e.Add("Pola harus berisi tepat satu token {SEQ}.");
            var unknown = System.Text.RegularExpressions.Regex.Matches(Pattern, @"\{[^}]*\}").Select(m => m.Value).Where(t => !Tokens.Contains(t)).Distinct().ToList();
            if (unknown.Count > 0) e.Add("Token tidak dikenal: " + string.Join(", ", unknown));
        }
        if (SeqDigits is < 1 or > 9) e.Add("Digit nomor urut 1–9.");
        if (SegmentMaxLength is < 0 or > 40) e.Add("Panjang segmen 0–40.");
        if ((Prefix ?? "").Length > 20) e.Add("Prefix maksimal 20 karakter.");
        if (string.IsNullOrWhiteSpace(SecondSuffix) || SecondSuffix.Length > 10 || SecondSuffix.Any(char.IsWhiteSpace))
            e.Add("Akhiran part 2nd wajib diisi, maks. 10 karakter, tanpa spasi.");
        return e;
    }
}

/// <summary>Pengaturan yang disimpan di tabel AppSettings (JSON per bagian), dicache di memori.</summary>
public class AppSettingsService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _cfg;
    private CompanySettings? _company;
    private CodeSettings? _code;
    private readonly object _lock = new();

    public AppSettingsService(IServiceScopeFactory scopes, IConfiguration cfg) { _scopes = scopes; _cfg = cfg; }

    public CompanySettings Company { get { EnsureLoaded(); return _company!; } }
    public CodeSettings Code { get { EnsureLoaded(); return _code!; } }

    private void EnsureLoaded()
    {
        if (_code != null) return;
        lock (_lock)
        {
            if (_code != null) return;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = db.AppSettings.AsNoTracking().ToDictionary(s => s.Key, s => s.Value);
            _company = rows.TryGetValue("company", out var c) ? JsonSerializer.Deserialize<CompanySettings>(c) ?? new() : new();
            if (rows.TryGetValue("code", out var k)) _code = JsonSerializer.Deserialize<CodeSettings>(k) ?? new();
            else
            {
                // Belum pernah disimpan: pakai PartCodePrefix lama dari appsettings.json bila ada.
                _code = new CodeSettings();
                if (!string.IsNullOrWhiteSpace(_cfg["PartCodePrefix"])) _code.Prefix = _cfg["PartCodePrefix"]!.Trim();
            }
            Part.SecondSuffix = _code.SecondSuffix;
        }
    }

    private static async Task UpsertAsync(AppDbContext db, string key, object value)
    {
        var json = JsonSerializer.Serialize(value);
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null) db.AppSettings.Add(new AppSetting { Key = key, Value = json });
        else row.Value = json;
    }

    public async Task SaveAsync(AppDbContext db, CompanySettings company, CodeSettings code)
    {
        await UpsertAsync(db, "company", company);
        await UpsertAsync(db, "code", code);
        await db.SaveChangesAsync();
        lock (_lock)
        {
            _company = company;
            _code = code;
            Part.SecondSuffix = code.SecondSuffix;
        }
    }

    /// <summary>Penanda migrasi data satu kali.</summary>
    public static async Task<bool> RunOnceAsync(AppDbContext db, string key, Func<Task> action)
    {
        key = "once." + key;
        if (await db.AppSettings.AnyAsync(s => s.Key == key)) return false;
        await action();
        db.AppSettings.Add(new AppSetting { Key = key, Value = DateTime.Now.ToString("O") });
        await db.SaveChangesAsync();
        return true;
    }
}
