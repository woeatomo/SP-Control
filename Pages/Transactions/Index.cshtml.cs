using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Transactions;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SiteContext _site;
    public IndexModel(AppDbContext db, SiteContext site) { _db = db; _site = site; }

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public TxType? Type { get; set; }
    [BindProperty(SupportsGet = true)] public string? Part { get; set; }
    [BindProperty(SupportsGet = true)] public string? Pic { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNo { get; set; } = 1;
    /// <summary>Hanya Issued yang belum tertaut ke daily report maintenance (rekonsiliasi).</summary>
    [BindProperty(SupportsGet = true)] public bool Unreported { get; set; }
    /// <summary>Aktivitas daily report untuk transaksi yang tertaut (id → label).</summary>
    public Dictionary<int, MaintenanceActivity> Activities { get; set; } = new();
    public const int PageSize = 50;

    public List<StockTransaction> Items { get; set; } = new();
    public int Total { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    private IQueryable<StockTransaction> Query()
    {
        var q = _db.Transactions.AsNoTracking().Include(t => t.Part).AsQueryable();
        if (From.HasValue) q = q.Where(t => t.Date >= From.Value.Date);
        if (To.HasValue) { var end = To.Value.Date.AddDays(1); q = q.Where(t => t.Date < end); }
        if (Type.HasValue) q = q.Where(t => t.Type == Type);
        if (Unreported) q = q.Where(t => t.Type == TxType.Issue && t.ActivityId == null);
        if (!string.IsNullOrWhiteSpace(Part))
        {
            var k = $"%{Part.Trim()}%";
            q = q.Where(t => EF.Functions.Like(t.Part.PartNumber, k) || EF.Functions.Like(t.Part.Name, k) || EF.Functions.Like(t.DocNo, k));
        }
        if (!string.IsNullOrWhiteSpace(Pic))
        {
            var k = $"%{Pic.Trim()}%";
            q = q.Where(t => EF.Functions.Like(t.Pic ?? "", k) || EF.Functions.Like(t.Department ?? "", k));
        }
        return q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id);
    }

    public async Task OnGetAsync()
    {
        var q = Query();
        Total = await q.CountAsync();
        PageNo = Math.Clamp(PageNo, 1, TotalPages);
        Items = await q.Skip((PageNo - 1) * PageSize).Take(PageSize).ToListAsync();
        var aids = Items.Where(t => t.ActivityId != null).Select(t => t.ActivityId!.Value).Distinct().ToList();
        Activities = await _db.MaintenanceActivities.AsNoTracking().Include(a => a.Machine).Where(a => aids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var list = await Query().ToListAsync();
        var bytes = Csv.Build(
            new[] { "Site", "Date", "Type", "DocNo", "PartNumber", "Name", "Qty", "Unit", "BalanceAfter", "UnitPriceUSD", "ValueUSD", "PIC", "Machine/Dept", "Reference", "Notes", "InputBy", "DailyReportActivityId" },
            list.Select(t => new[] { _site.CodeOf(t.SiteId), t.Date.ToString("yyyy-MM-dd HH:mm"), t.Type.ToString(), t.DocNo, t.Part.PartNumber, t.Part.Name,
                t.Qty.ToString("0.##"), t.Part.Unit, t.BalanceAfter.ToString("0.##"), t.UnitPriceUsd.ToString("0.####"),
                t.ValueUsd.ToString("0.00"), t.Pic, t.Department, t.Reference, t.Notes, t.CreatedBy, t.ActivityId?.ToString() }));
        return File(bytes, "text/csv", $"transaksi-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    public string Url(params (string key, string? val)[] changes)
    {
        var d = Request.Query.Where(kv => kv.Key != "handler").ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        foreach (var (k, v) in changes) { if (string.IsNullOrEmpty(v)) d.Remove(k); else d[k] = v; }
        return "/Transactions" + QueryString.Create(d.Where(kv => kv.Value != "")!);
    }
}
