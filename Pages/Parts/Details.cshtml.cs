using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Pages.Parts;

public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SparePartControl.Services.ImageStore _images;
    public DetailsModel(AppDbContext db, SparePartControl.Services.ImageStore images) { _db = db; _images = images; }

    public Part Part { get; set; } = null!;
    public List<StockTransaction> History { get; set; } = new();
    public List<Loan> OpenLoans { get; set; } = new();
    public Dictionary<int, MaintenanceActivity> Activities { get; set; } = new();
    /// <summary>Pasangan part: versi original bila ini part 2nd, atau versi -2ND bila ini part original.</summary>
    public Part? Counterpart { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var p = await _db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        Part = p;
        var pairNo = p.HasSecondSuffix ? p.OriginalPartNumber : p.PartNumber + Part.SecondSuffix;
        Counterpart = await _db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.PartNumber == pairNo && x.SiteId == p.SiteId);
        History = await _db.Transactions.AsNoTracking().Where(t => t.PartId == id)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(50).ToListAsync();
        var aids = History.Where(t => t.ActivityId != null).Select(t => t.ActivityId!.Value).Distinct().ToList();
        Activities = await _db.MaintenanceActivities.AsNoTracking().Include(a => a.Machine).Where(a => aids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        OpenLoans = await _db.Loans.AsNoTracking().Where(l => l.PartId == id && l.Status == LoanStatus.Open).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!User.IsInRole("Admin")) return Forbid();
        var p = await _db.Parts.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        if (await _db.Transactions.AnyAsync(t => t.PartId == id) || await _db.StockCheckItems.AnyAsync(i => i.PartId == id))
        {
            TempData["Err"] = "Part sudah punya riwayat transaksi sehingga tidak bisa dihapus. Ubah statusnya menjadi Non-aktif.";
            return RedirectToPage(new { id });
        }
        _db.Parts.Remove(p);
        await _db.SaveChangesAsync();
        _images.Delete(p.ImageFile);
        TempData["Ok"] = $"Part {p.PartNumber} dihapus.";
        return RedirectToPage("Index");
    }
}
