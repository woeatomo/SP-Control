using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Settings;

/// <summary>Kelola site / section spare part control.</summary>
public class SitesModel : PageModel
{
    private readonly AppDbContext _db;
    public SitesModel(AppDbContext db) => _db = db;

    public record Row(Site Site, int Parts, decimal Value, int Users);
    public List<Row> Rows { get; set; } = new();
    [BindProperty] public Site Input { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public async Task OnGetAsync(int? edit)
    {
        await LoadAsync();
        if (edit.HasValue) Input = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == edit) ?? new Site();
    }

    private async Task LoadAsync()
    {
        var sites = await _db.Sites.AsNoTracking().OrderBy(s => s.Code).ToListAsync();
        // Statistik semua site (abaikan site aktif di menu atas).
        var stats = await _db.Parts.IgnoreQueryFilters().GroupBy(p => p.SiteId)
            .Select(g => new { g.Key, N = g.Count(), V = g.Sum(p => p.StockQty * p.UnitPriceUsd) }).ToListAsync();
        var users = await _db.UserSites.GroupBy(u => u.SiteId).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var allUsers = await _db.Users.CountAsync(u => u.AllSites && u.IsActive);
        Rows = sites.Select(s => new Row(s, stats.FirstOrDefault(x => x.Key == s.Id)?.N ?? 0, stats.FirstOrDefault(x => x.Key == s.Id)?.V ?? 0,
            (users.FirstOrDefault(x => x.Key == s.Id)?.N ?? 0) + allUsers)).ToList();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        Input.Code = (Input.Code ?? "").Trim().ToUpperInvariant();
        Input.Name = (Input.Name ?? "").Trim();
        Input.CodePrefix = string.IsNullOrWhiteSpace(Input.CodePrefix) ? null : Input.CodePrefix.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(Input.Code, "^[A-Z0-9_-]{1,10}$"))
            Errors.Add("Kode site 1–10 karakter: huruf, angka, - atau _ (tanpa spasi). Kode dipakai di nomor dokumen, mis. ISS-PC1-261120-001.");
        if (Input.Name.Length == 0) Errors.Add("Nama site wajib diisi.");
        if (await _db.Sites.AnyAsync(s => s.Code == Input.Code && s.Id != Input.Id)) Errors.Add($"Kode site {Input.Code} sudah dipakai.");
        if (Errors.Count > 0) { await LoadAsync(); return Page(); }

        if (Input.Id == 0)
        {
            _db.Sites.Add(new Site { Code = Input.Code, Name = Input.Name, CodePrefix = Input.CodePrefix, IsActive = true });
            TempData["Ok"] = $"Site {Input.Code} — {Input.Name} dibuat. Atur akses user di menu User, lalu pilih site di menu atas untuk mulai mengisi master data.";
        }
        else
        {
            var s = await _db.Sites.FirstOrDefaultAsync(x => x.Id == Input.Id);
            if (s == null) return NotFound();
            var codeChanged = s.Code != Input.Code;
            s.Code = Input.Code; s.Name = Input.Name; s.CodePrefix = Input.CodePrefix;
            TempData["Ok"] = $"Site {s.Code} disimpan." + (codeChanged ? " Nomor dokumen lama tetap memakai kode site sebelumnya." : "");
        }
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var s = await _db.Sites.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        if (s.IsActive && await _db.Sites.CountAsync(x => x.IsActive) <= 1)
        {
            TempData["Err"] = "Harus ada minimal satu site aktif.";
            return RedirectToPage();
        }
        s.IsActive = !s.IsActive;
        await _db.SaveChangesAsync();
        TempData["Ok"] = s.IsActive ? $"Site {s.Code} diaktifkan." : $"Site {s.Code} dinonaktifkan — datanya disimpan tetapi site tidak bisa dipilih.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var s = await _db.Sites.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        if (await _db.Parts.IgnoreQueryFilters().AnyAsync(p => p.SiteId == id) || await _db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.SiteId == id)
            || await _db.StockCheckSessions.IgnoreQueryFilters().AnyAsync(t => t.SiteId == id))
        {
            TempData["Err"] = $"Site {s.Code} sudah berisi data sehingga tidak bisa dihapus. Nonaktifkan saja.";
            return RedirectToPage();
        }
        if (await _db.Sites.CountAsync() <= 1) { TempData["Err"] = "Harus ada minimal satu site."; return RedirectToPage(); }
        _db.Sites.Remove(s);
        await _db.SaveChangesAsync();
        TempData["Ok"] = $"Site {s.Code} dihapus.";
        return RedirectToPage();
    }
}
