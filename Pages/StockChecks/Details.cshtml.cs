using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.StockChecks;

public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly StockService _stock;
    public DetailsModel(AppDbContext db, StockService stock) { _db = db; _stock = stock; }

    public StockCheckSession Session { get; set; } = null!;
    public List<StockCheckItem> Items { get; set; } = new();
    /// <summary>Hasil hitung hanya bisa diisi Admin selama sesi masih berjalan.</summary>
    public bool IsOpen => Session.Status == StockCheckStatus.Open && User.IsInRole("Admin");

    private async Task<bool> LoadAsync(int id)
    {
        var s = await _db.StockCheckSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return false;
        Session = s;
        Items = await _db.StockCheckItems.AsNoTracking().Include(i => i.Part).Where(i => i.SessionId == id)
            .OrderBy(i => i.Part.Location).ThenBy(i => i.Part.PartNumber).ToListAsync();
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    /// <summary>Simpan hasil hitung satu part (dipanggil via AJAX).</summary>
    public async Task<IActionResult> OnPostCountAsync(int id, int partId, decimal? counted, string? note)
    {
        if (!User.IsInRole("Admin")) return Forbid();
        var s = await _db.StockCheckSessions.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        if (s.Status == StockCheckStatus.Closed) return BadRequest(new { error = "Sesi sudah ditutup." });
        if (counted is < 0) return BadRequest(new { error = "Qty tidak boleh negatif." });
        var part = await _db.Parts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == partId);
        if (part == null) return BadRequest(new { error = "Part tidak ditemukan." });

        var item = await _db.StockCheckItems.FirstOrDefaultAsync(i => i.SessionId == id && i.PartId == partId);
        var isNew = item == null;
        if (item == null)
        {
            // Part yang dibuat setelah sesi dimulai tetap bisa dihitung.
            item = new StockCheckItem { SessionId = id, PartId = partId };
            _db.StockCheckItems.Add(item);
        }
        // Stok sistem diambil pada saat dihitung, supaya transaksi yang terjadi selama periode opname tidak jadi selisih palsu.
        item.SystemQty = part.StockQty;
        item.UnitPriceUsd = part.UnitPriceUsd;
        item.CountedQty = counted;
        item.CheckedAt = counted.HasValue ? DateTime.Now : null;
        item.Note = note?.Trim();
        await _db.SaveChangesAsync();
        return new JsonResult(new { ok = true, isNew, systemQty = item.SystemQty, variance = item.Variance });
    }

    public async Task<IActionResult> OnPostCloseAsync(int id, bool postAdjustment)
    {
        if (!User.IsInRole("Admin")) return Forbid();
        var r = await _stock.CloseStockCheckAsync(id, postAdjustment);
        TempData[r.Ok ? "Ok" : "Err"] = r.Message;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetExportAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        var bytes = Csv.Build(
            new[] { "Period", "PartNumber", "Name", "Location", "Unit", "SystemQty", "CountedQty", "Variance", "UnitPriceUSD", "VarianceUSD", "CheckedAt", "Note" },
            Items.Select(i => new[] { Session.Period, i.Part.PartNumber, i.Part.Name, i.Part.Location, i.Part.Unit,
                i.SystemQty.ToString("0.##"), i.CountedQty?.ToString("0.##"), i.Variance?.ToString("0.##"), i.UnitPriceUsd.ToString("0.####"),
                i.Variance.HasValue ? (i.Variance.Value * i.UnitPriceUsd).ToString("0.00") : "", i.CheckedAt?.ToString("yyyy-MM-dd HH:mm"), i.Note }));
        return File(bytes, "text/csv", $"stock-checking-{Session.Period}.csv");
    }
}
