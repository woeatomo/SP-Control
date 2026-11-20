using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Parts;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public PartFilter F { get; set; } = new();
    [BindProperty(SupportsGet = true, Name = "page")] public int PageNo { get; set; } = 1;
    [BindProperty(SupportsGet = true, Name = "size")] public int PageSize { get; set; } = 25;

    public List<Part> Items { get; set; } = new();
    public int Total { get; set; }
    public decimal TotalValueUsd { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    public async Task OnGetAsync()
    {
        PageSize = Math.Clamp(PageSize, 10, 500);
        var q = F.Apply(_db.Parts.AsNoTracking());
        Total = await q.CountAsync();
        TotalValueUsd = await q.SumAsync(p => p.StockQty * p.UnitPriceUsd);
        PageNo = Math.Clamp(PageNo, 1, TotalPages);
        Items = await q.Skip((PageNo - 1) * PageSize).Take(PageSize).ToListAsync();
        Models = await _db.Parts.Where(p => p.Category != null && p.Category != "").Select(p => p.Category!).Distinct().OrderBy(x => x).ToListAsync();
    }

    public List<string> Models { get; set; } = new();

    public async Task<IActionResult> OnGetExportAsync()
    {
        var list = await F.Apply(_db.Parts.AsNoTracking()).ToListAsync();
        var bytes = Csv.Build(ExcelMaster.ExportHeaders, list.Select(p => new[]
        {
            p.PartNumber, p.Name, p.Category, p.Status == PartStatus.Active ? "ACTIVE" : "INACTIVE", p.Location, p.Brand, p.Supplier,
            p.Currency.ToString(), p.UnitPrice.ToString("0.####"), p.UnitPriceUsd.ToString("0.####"), p.InitialStock.ToString("0.##"),
            p.MinStock.ToString("0.##"), p.MaxStock.ToString("0.##"), p.StockQty.ToString("0.##"), ExcelMaster.LevelText(p.Level),
            p.TotalValueUsd.ToString("0.00"), p.Remarks, p.Specification, p.Unit, p.Machine, p.IsReusable ? "Yes" : "No",
            p.Condition == PartCondition.Second ? "2nd" : "New"
        }));
        return File(bytes, "text/csv", $"master-stock-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    /// <summary>Export ke .xlsx dengan susunan kolom sama seperti Excel MASTER STOCK (+ sheet RATE).</summary>
    public async Task<IActionResult> OnGetExportExcelAsync([FromServices] StockService stock)
    {
        var list = await F.Apply(_db.Parts.AsNoTracking()).ToListAsync();
        var bytes = ExcelMaster.Export(list, await stock.GetRatesAsync());
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"MASTER STOCK {DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    /// <summary>Bangun URL dengan mempertahankan filter yang aktif.</summary>
    public string Url(params (string key, string? val)[] changes)
    {
        var d = Request.Query.Where(kv => kv.Key != "handler").ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        foreach (var (k, v) in changes) { if (string.IsNullOrEmpty(v)) d.Remove(k); else d[k] = v; }
        return "/Parts" + Microsoft.AspNetCore.Http.QueryString.Create(d.Where(kv => kv.Value != "")!);
    }

    public string SortUrl(string key) =>
        Url(("sort", key), ("dir", (F.Sort ?? "no") == key && F.Dir != "desc" ? "desc" : null), ("page", null));

    public string SortMark(string key) => (F.Sort ?? "no") == key ? (F.Dir == "desc" ? " ▼" : " ▲") : "";
}
