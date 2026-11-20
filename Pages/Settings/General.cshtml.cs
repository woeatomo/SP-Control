using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Settings;

public class GeneralModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AppSettingsService _settings;
    private readonly PartCode _codes;
    private readonly SiteContext _site;
    public GeneralModel(AppDbContext db, AppSettingsService settings, PartCode codes, SiteContext site) { _db = db; _settings = settings; _codes = codes; _site = site; }

    [BindProperty] public CompanySettings Company { get; set; } = new();
    [BindProperty] public CodeSettings Code { get; set; } = new();
    /// <summary>Saat akhiran 2nd diganti: ganti juga goods code part 2nd yang sudah ada.</summary>
    [BindProperty] public bool RenameExisting { get; set; } = true;
    public List<string> Errors { get; set; } = new();
    public int SecondCount { get; set; }
    public string OldSuffix => _settings.Code.SecondSuffix;

    private async Task LoadCountsAsync() =>
        SecondCount = await _db.Parts.IgnoreQueryFilters().CountAsync(p => p.Condition == PartCondition.Second);

    public async Task OnGetAsync()
    {
        // Salinan, supaya form tidak mengubah objek yang sedang dipakai aplikasi.
        Company = System.Text.Json.JsonSerializer.Deserialize<CompanySettings>(System.Text.Json.JsonSerializer.Serialize(_settings.Company))!;
        Code = System.Text.Json.JsonSerializer.Deserialize<CodeSettings>(System.Text.Json.JsonSerializer.Serialize(_settings.Code))!;
        await LoadCountsAsync();
    }

    private void Normalize()
    {
        Company.CompanyName = (Company.CompanyName ?? "").Trim();
        Company.AppTitle = string.IsNullOrWhiteSpace(Company.AppTitle) ? "Spare Part Control" : Company.AppTitle.Trim();
        Code.Pattern = (Code.Pattern ?? "").Trim();
        Code.Prefix = (Code.Prefix ?? "").Trim();
        Code.SecondSuffix = (Code.SecondSuffix ?? "").Trim();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Normalize();
        Errors = Code.Validate();
        if (Company.CompanyName.Length > 100 || Company.AppTitle.Length > 60) Errors.Add("Nama perusahaan maks. 100 karakter, judul aplikasi maks. 60.");
        if (Errors.Count > 0) { await LoadCountsAsync(); return Page(); }

        var old = _settings.Code.SecondSuffix;
        var renamed = 0;
        await using var tx = await _db.Database.BeginTransactionAsync();
        if (RenameExisting && !string.Equals(old, Code.SecondSuffix, StringComparison.OrdinalIgnoreCase))
        {
            // Semua site; goods code unik per site.
            var taken = (await _db.Parts.IgnoreQueryFilters().Select(p => new { p.SiteId, p.PartNumber }).ToListAsync())
                .Select(x => x.SiteId + "|" + x.PartNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var p in await _db.Parts.IgnoreQueryFilters().Where(p => p.Condition == PartCondition.Second).ToListAsync())
            {
                var baseCode = p.PartNumber.EndsWith(old, StringComparison.OrdinalIgnoreCase) ? p.PartNumber[..^old.Length] : p.PartNumber;
                var code = baseCode + Code.SecondSuffix;
                if (code.Length > 60 || taken.Contains(p.SiteId + "|" + code)) continue;
                taken.Remove(p.SiteId + "|" + p.PartNumber); taken.Add(p.SiteId + "|" + code);
                p.PartNumber = code; p.UpdatedAt = DateTime.Now;
                renamed++;
            }
        }
        await _settings.SaveAsync(_db, Company, Code);
        await tx.CommitAsync();
        TempData["Ok"] = "Pengaturan disimpan." + (renamed > 0 ? $" {renamed} goods code part 2nd diganti ke akhiran {Code.SecondSuffix} — cetak ulang label QR-nya." : "");
        return RedirectToPage();
    }

    /// <summary>Pratinjau kode berikutnya dengan pengaturan di form (belum disimpan) dan data part sebenarnya.</summary>
    public async Task<IActionResult> OnPostPreviewAsync(string? maker, string? model, string? supp)
    {
        Normalize();
        var errors = Code.Validate();
        if (errors.Count > 0) return new JsonResult(new { error = string.Join(" ", errors) });
        var (code, error) = await _codes.NextAsync(new CodeInput(maker, model, supp), Code, _site.Current ?? _site.Allowed.FirstOrDefault());
        return new JsonResult(new { code, second = code == null ? null : code + Code.SecondSuffix, error });
    }
}
