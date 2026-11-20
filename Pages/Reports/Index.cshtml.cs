using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Reports;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SiteContext _site;
    public IndexModel(AppDbContext db, SiteContext site) { _db = db; _site = site; }

    public static readonly (string Key, string Title, bool UsesDates)[] Types =
    {
        ("asset", "Stock Value per Model (ACTIVE vs INACTIVE)", false),
        ("asset-site", "Stock Value per Site / Section", false),
        ("asset-maker", "Stock Value per Maker", false),
        ("asset-supp", "Stock Value per Supplier", false),
        ("asset-cur", "Nilai Aset per Mata Uang", false),
        ("usage-part", "Pemakaian (Issued) per Part", true),
        ("usage-pic", "Pemakaian (Issued) per PIC Maintenance", true),
        ("usage-mc", "Pemakaian (Issued) per Mesin / Departemen", true),
        ("lowstock", "Perlu Restock (EMPTY / LOW / CRITICAL) + Saran Order", false),
        ("overstock", "OVERSTOCK (di atas Max)", false),
        ("loans", "Loan Belum Kembali", false),
        ("reusable", "Part Reusable & 2nd", false),
    };

    [BindProperty(SupportsGet = true)] public string Type { get; set; } = "asset";
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public string Title { get; set; } = "";
    public bool UsesDates { get; set; }
    public List<(string Name, bool Num)> Cols { get; set; } = new();
    public List<string[]> Rows { get; set; } = new();
    public string[]? Footer { get; set; }

    private static string Q(decimal d) => d.ToString("#,0.##");
    private static string M(decimal d) => d.ToString("#,0.00");

    private async Task BuildAsync()
    {
        var t = Types.FirstOrDefault(x => x.Key == Type);
        if (t.Key == null) { t = Types[0]; Type = t.Key; }
        Title = t.Title; UsesDates = t.UsesDates;
        From ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        To ??= DateTime.Today;
        var end = To.Value.Date.AddDays(1);

        switch (Type)
        {
            case "asset":
            {
                var parts = await _db.Parts.AsNoTracking().Select(p => new { p.Category, p.Status, p.StockQty, p.UnitPriceUsd }).ToListAsync();
                Cols = new() { ("Model", false), ("Item ACTIVE", true), ("Value ACTIVE (USD)", true), ("Item INACTIVE", true), ("Value INACTIVE (USD)", true), ("Total (USD)", true) };
                foreach (var g in parts.GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "(tanpa model)" : p.Category).OrderBy(g => g.Key))
                {
                    var a = g.Where(p => p.Status == PartStatus.Active).ToList();
                    var n = g.Where(p => p.Status == PartStatus.NonActive).ToList();
                    Rows.Add(new[] { g.Key!, a.Count.ToString(), M(a.Sum(p => p.StockQty * p.UnitPriceUsd)), n.Count.ToString(),
                        M(n.Sum(p => p.StockQty * p.UnitPriceUsd)), M(g.Sum(p => p.StockQty * p.UnitPriceUsd)) });
                }
                var act = parts.Where(p => p.Status == PartStatus.Active).ToList();
                var non = parts.Where(p => p.Status == PartStatus.NonActive).ToList();
                Footer = new[] { "TOTAL", act.Count.ToString(), M(act.Sum(p => p.StockQty * p.UnitPriceUsd)), non.Count.ToString(),
                    M(non.Sum(p => p.StockQty * p.UnitPriceUsd)), M(parts.Sum(p => p.StockQty * p.UnitPriceUsd)) };
                break;
            }
            case "asset-maker":
            case "asset-supp":
            case "asset-site":
            {
                var byMaker = Type == "asset-maker"; var bySite = Type == "asset-site";
                var raw = await _db.Parts.AsNoTracking().Select(p => new { p.SiteId, p.Brand, p.Supplier, p.Status, p.StockQty, p.UnitPriceUsd }).ToListAsync();
                var parts = raw.Select(p => new
                {
                    Key = bySite ? _site.CodeOf(p.SiteId) + " — " + (_site.Allowed.FirstOrDefault(s => s.Id == p.SiteId)?.Name ?? "") : byMaker ? p.Brand : p.Supplier,
                    p.Status, p.StockQty, p.UnitPriceUsd
                }).ToList();
                Cols = new() { (bySite ? "Site" : byMaker ? "Maker" : "Supplier", false), ("Jumlah Item", true), ("Value ACTIVE (USD)", true), ("Value INACTIVE (USD)", true), ("Total (USD)", true) };
                foreach (var g in parts.GroupBy(p => string.IsNullOrWhiteSpace(p.Key) ? "(kosong)" : bySite ? p.Key! : p.Key!.Trim().ToUpperInvariant())
                             .OrderByDescending(g => g.Sum(p => p.StockQty * p.UnitPriceUsd)))
                    Rows.Add(new[] { g.Key, g.Count().ToString(),
                        M(g.Where(p => p.Status == PartStatus.Active).Sum(p => p.StockQty * p.UnitPriceUsd)),
                        M(g.Where(p => p.Status == PartStatus.NonActive).Sum(p => p.StockQty * p.UnitPriceUsd)),
                        M(g.Sum(p => p.StockQty * p.UnitPriceUsd)) });
                Footer = new[] { "TOTAL", parts.Count.ToString(),
                    M(parts.Where(p => p.Status == PartStatus.Active).Sum(p => p.StockQty * p.UnitPriceUsd)),
                    M(parts.Where(p => p.Status == PartStatus.NonActive).Sum(p => p.StockQty * p.UnitPriceUsd)),
                    M(parts.Sum(p => p.StockQty * p.UnitPriceUsd)) };
                break;
            }
            case "asset-cur":
            {
                var parts = await _db.Parts.AsNoTracking().Select(p => new { p.Currency, p.Status, p.StockQty, p.UnitPrice, p.UnitPriceUsd }).ToListAsync();
                Cols = new() { ("Mata Uang", false), ("Jumlah Item", true), ("Nilai (mata uang asli)", true), ("Nilai Aktif (USD)", true), ("Nilai Non-aktif (USD)", true), ("Total (USD)", true) };
                foreach (var g in parts.GroupBy(p => p.Currency).OrderBy(g => g.Key))
                    Rows.Add(new[] { g.Key.ToString(), g.Count().ToString(), M(g.Sum(p => p.StockQty * p.UnitPrice)),
                        M(g.Where(p => p.Status == PartStatus.Active).Sum(p => p.StockQty * p.UnitPriceUsd)),
                        M(g.Where(p => p.Status == PartStatus.NonActive).Sum(p => p.StockQty * p.UnitPriceUsd)),
                        M(g.Sum(p => p.StockQty * p.UnitPriceUsd)) });
                Footer = new[] { "TOTAL", parts.Count.ToString(), "",
                    M(parts.Where(p => p.Status == PartStatus.Active).Sum(p => p.StockQty * p.UnitPriceUsd)),
                    M(parts.Where(p => p.Status == PartStatus.NonActive).Sum(p => p.StockQty * p.UnitPriceUsd)),
                    M(parts.Sum(p => p.StockQty * p.UnitPriceUsd)) };
                break;
            }
            case "usage-part":
            case "usage-pic":
            case "usage-mc":
            {
                var tx = await _db.Transactions.AsNoTracking().Where(x => x.Type == TxType.Issue && x.Date >= From.Value.Date && x.Date < end)
                    .Select(x => new { x.Part.PartNumber, x.Part.Name, x.Part.Unit, x.Qty, x.UnitPriceUsd, x.Pic, x.Department, x.DocNo }).ToListAsync();
                decimal Val(IEnumerable<dynamic> xs) => xs.Sum(x => (decimal)(-x.Qty * x.UnitPriceUsd));
                if (Type == "usage-part")
                {
                    Cols = new() { ("Part Number", false), ("Nama Part", false), ("Jumlah Transaksi", true), ("Qty Dipakai", true), ("Satuan", false), ("Nilai (USD)", true) };
                    foreach (var g in tx.GroupBy(x => new { x.PartNumber, x.Name, x.Unit }).OrderByDescending(g => g.Sum(x => -x.Qty * x.UnitPriceUsd)))
                        Rows.Add(new[] { g.Key.PartNumber, g.Key.Name, g.Count().ToString(), Q(g.Sum(x => -x.Qty)), g.Key.Unit, M(g.Sum(x => -x.Qty * x.UnitPriceUsd)) });
                    Footer = new[] { "TOTAL", "", tx.Count.ToString(), "", "", M(tx.Sum(x => -x.Qty * x.UnitPriceUsd)) };
                }
                else
                {
                    var byPic = Type == "usage-pic";
                    Cols = new() { (byPic ? "PIC Maintenance" : "Mesin / Departemen", false), ("Jumlah Dokumen", true), ("Jumlah Item", true), ("Nilai (USD)", true) };
                    foreach (var g in tx.GroupBy(x => string.IsNullOrWhiteSpace(byPic ? x.Pic : x.Department) ? "(kosong)" : (byPic ? x.Pic : x.Department))
                                 .OrderByDescending(g => g.Sum(x => -x.Qty * x.UnitPriceUsd)))
                        Rows.Add(new[] { g.Key!, g.Select(x => x.DocNo).Distinct().Count().ToString(), g.Count().ToString(), M(g.Sum(x => -x.Qty * x.UnitPriceUsd)) });
                    Footer = new[] { "TOTAL", tx.Select(x => x.DocNo).Distinct().Count().ToString(), tx.Count.ToString(), M(tx.Sum(x => -x.Qty * x.UnitPriceUsd)) };
                }
                break;
            }
            case "lowstock":
            case "overstock":
            {
                var over = Type == "overstock";
                var parts = over
                    ? await _db.Parts.AsNoTracking().Where(p => p.StockQty > 0 && p.MaxStock > 0 && p.StockQty > p.MaxStock && p.StockQty > p.MinStock).ToListAsync()
                    : await _db.Parts.AsNoTracking().Where(p => p.StockQty <= p.MinStock).ToListAsync();
                parts = parts.OrderBy(p => p.SiteId).ThenBy(p => p.Level).ThenBy(p => p.Supplier).ThenBy(p => p.PartNumber).ToList();
                var withSite = _site.IsAll;
                Cols = new() { ("goodsCode", false), ("goodsName", false), ("Model", false), ("rackName", false), ("suppName", false), ("Curr", false),
                    ("unit_price", true), ("min", true), ("max", true), ("nowStock", true), (over ? "Kelebihan" : "Saran Order", true),
                    (over ? "Nilai Kelebihan (USD)" : "Estimasi Order (USD)", true), ("Status", false), ("stockCategory", false) };
                decimal total = 0;
                foreach (var p in parts)
                {
                    var qty = over ? p.StockQty - p.MaxStock : p.SuggestedOrder;
                    total += qty * p.UnitPriceUsd;
                    var row = new[] { p.PartNumber, p.Name, p.Category ?? "", p.Location ?? "", p.Supplier ?? "", p.Currency.ToString(),
                        p.UnitPrice.ToString("#,0.##"), Q(p.MinStock), Q(p.MaxStock), Q(p.StockQty), Q(qty), M(qty * p.UnitPriceUsd),
                        ExcelMaster.LevelText(p.Level), p.Status == PartStatus.Active ? "ACTIVE" : "INACTIVE" };
                    Rows.Add(withSite ? row.Prepend(_site.CodeOf(p.SiteId)).ToArray() : row);
                }
                Footer = new[] { "TOTAL", $"{parts.Count} item", "", "", "", "", "", "", "", "", "", M(total), "", "" };
                if (withSite) { Cols.Insert(0, ("Site", false)); Footer = Footer.Prepend("").ToArray(); Footer[0] = "TOTAL"; Footer[1] = ""; }
                break;
            }
            case "loans":
            {
                var loans = await _db.Loans.AsNoTracking().Include(l => l.Part).Where(l => l.Status == LoanStatus.Open).OrderBy(l => l.LoanDate).ToListAsync();
                Cols = new() { ("No. Dokumen", false), ("Tgl Pinjam", false), ("Peminjam", false), ("Part Number", false), ("Nama Part", false), ("Qty Sisa", true), ("Nilai (USD)", true), ("Rencana Kembali", false), ("Status", false) };
                foreach (var l in loans)
                    Rows.Add(new[] { l.DocNo, l.LoanDate.ToString("yyyy-MM-dd"), l.Borrower, l.Part.PartNumber, l.Part.Name, Q(l.Outstanding),
                        M(l.Outstanding * l.Part.UnitPriceUsd), l.DueDate?.ToString("yyyy-MM-dd") ?? "", l.IsOverdue ? "TERLAMBAT" : "Dipinjam" });
                Footer = new[] { "TOTAL", "", "", "", "", "", M(loans.Sum(l => l.Outstanding * l.Part.UnitPriceUsd)), "", "" };
                break;
            }
            case "reusable":
            {
                var parts = await _db.Parts.AsNoTracking().Where(p => p.IsReusable || p.Condition == PartCondition.Second).OrderBy(p => p.PartNumber).ToListAsync();
                Cols = new() { ("goodsCode", false), ("goodsName", false), ("rackName", false), ("Reusable", false), ("Kondisi", false), ("stockCategory", false), ("Stok", true), ("Satuan", false), ("Nilai (USD)", true) };
                foreach (var p in parts)
                    Rows.Add(new[] { p.PartNumber, p.Name, p.Location ?? "", p.IsReusable ? "Ya" : "Tidak", p.Condition == PartCondition.Second ? "2nd" : "Baru",
                        p.Status == PartStatus.Active ? "ACTIVE" : "INACTIVE", Q(p.StockQty), p.Unit, M(p.TotalValueUsd) });
                Footer = new[] { "TOTAL", "", "", "", "", "", "", "", M(parts.Sum(p => p.TotalValueUsd)) };
                break;
            }
        }
    }

    public async Task OnGetAsync() => await BuildAsync();

    public async Task<IActionResult> OnGetExportAsync()
    {
        await BuildAsync();
        var rows = Rows.Select(r => r.Select((c, i) => Cols[i].Num ? c.Replace(",", "") : c));
        if (Footer != null) rows = rows.Append(Footer.Select((c, i) => Cols[i].Num ? c.Replace(",", "") : c));
        return File(Csv.Build(Cols.Select(c => c.Name), rows!), "text/csv", $"report-{Type}-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }
}
