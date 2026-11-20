using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Pages.StockChecks;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SparePartControl.Services.SiteContext _site;
    public IndexModel(AppDbContext db, SparePartControl.Services.SiteContext site) { _db = db; _site = site; }

    public record Row(StockCheckSession S, int Total, int Counted, int Variance, decimal VarianceUsd);
    public List<Row> Rows { get; set; } = new();
    public int CurYear => DateTime.Today.Year;
    public int CurSemester => DateTime.Today.Month <= 6 ? 1 : 2;

    public async Task OnGetAsync()
    {
        var sessions = await _db.StockCheckSessions.AsNoTracking().OrderByDescending(s => s.Year).ThenByDescending(s => s.Semester).ToListAsync();
        foreach (var s in sessions)
        {
            var items = await _db.StockCheckItems.AsNoTracking().Where(i => i.SessionId == s.Id)
                .Select(i => new { i.SystemQty, i.CountedQty, i.UnitPriceUsd }).ToListAsync();
            var counted = items.Where(i => i.CountedQty.HasValue).ToList();
            Rows.Add(new Row(s, items.Count, counted.Count, counted.Count(i => i.CountedQty != i.SystemQty),
                counted.Sum(i => (i.CountedQty!.Value - i.SystemQty) * i.UnitPriceUsd)));
        }
    }

    public async Task<IActionResult> OnPostCreateAsync(int year, int semester, string? checkedBy, string? notes)
    {
        if (!User.IsInRole("Admin")) return Forbid();
        if (_site.Current == null) { TempData["Err"] = "Pilih satu site di menu atas untuk memulai stock checking."; return RedirectToPage(); }
        if (year < 2000 || year > 2100 || semester is < 1 or > 2 || string.IsNullOrWhiteSpace(checkedBy))
        {
            TempData["Err"] = "Isi tahun, semester dan nama petugas.";
            return RedirectToPage();
        }
        if (await _db.StockCheckSessions.AnyAsync(s => s.Year == year && s.Semester == semester))
        {
            TempData["Err"] = $"Sesi stock checking {year}-S{semester} untuk site {_site.Current.Code} sudah ada.";
            return RedirectToPage();
        }
        var s = new StockCheckSession { SiteId = _site.Current.Id, Year = year, Semester = semester, CheckedBy = checkedBy.Trim(), Notes = notes };
        // Snapshot: semua part aktif + part non-aktif yang masih punya stok (masih bernilai aset).
        var parts = await _db.Parts.AsNoTracking().Where(p => p.Status == PartStatus.Active || p.StockQty > 0).ToListAsync();
        s.Items = parts.Select(p => new StockCheckItem { PartId = p.Id, SystemQty = p.StockQty, UnitPriceUsd = p.UnitPriceUsd }).ToList();
        _db.StockCheckSessions.Add(s);
        await _db.SaveChangesAsync();
        return RedirectToPage("Details", new { id = s.Id });
    }
}
