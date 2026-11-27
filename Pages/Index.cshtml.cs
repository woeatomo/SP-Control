using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SparePartControl.Services.SiteContext _site;
    public IndexModel(AppDbContext db, SparePartControl.Services.SiteContext site) { _db = db; _site = site; }

    /// <summary>Ringkasan per site (mode "Semua site").</summary>
    public record SiteRow(Site Site, int Parts, decimal ActiveValue, decimal InactiveValue, int Restock, int Overstock, StockCheckSession? Check);
    public List<SiteRow> SiteRows { get; set; } = new();

    public int TotalParts { get; set; }
    public int ActiveParts { get; set; }
    public int NonActiveParts { get; set; }
    public decimal ActiveValue { get; set; }
    public decimal NonActiveValue { get; set; }
    public List<Part> Alerts { get; set; } = new();
    public int OverstockCount { get; set; }
    public int Count(StockLevel l) => Alerts.Count(p => p.Level == l);
    public List<Loan> OpenLoans { get; set; } = new();
    public List<StockTransaction> Recent { get; set; } = new();
    public List<(string Label, decimal Value)> Monthly { get; set; } = new();
    public decimal IssuedThisMonth { get; set; }
    public decimal IssuedLastMonth { get; set; }
    public List<int> MonthlyCount { get; set; } = new();
    /// <summary>Jumlah part per status stok (EMPTY … OVERSTOCK).</summary>
    public Dictionary<StockLevel, int> LevelCounts { get; set; } = new();
    /// <summary>Estimasi biaya order semua part yang perlu restock (saran order × unit_USD).</summary>
    public decimal RestockCost { get; set; }
    public int OverdueLoans => OpenLoans.Count(l => l.IsOverdue);
    public record TopRow(int PartId, string Code, string Name, string Unit, decimal Qty, decimal Value, int SiteId);
    public List<TopRow> TopUsed { get; set; } = new();
    public decimal TotalValue => ActiveValue + NonActiveValue;

    /// <summary>"5 menit lalu", "kemarin", …</summary>
    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "baru saja";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} menit lalu";
        if (d.TotalHours < 24 && t.Date == DateTime.Today) return $"{(int)d.TotalHours} jam lalu";
        if (t.Date == DateTime.Today.AddDays(-1)) return "kemarin " + t.ToString("HH:mm");
        return t.ToString(t.Year == DateTime.Today.Year ? "dd MMM HH:mm" : "dd MMM yyyy");
    }

    public int CurYear => DateTime.Today.Year;
    public int CurSemester => DateTime.Today.Month <= 6 ? 1 : 2;
    public StockCheckSession? CurrentCheck { get; set; }
    public (int Counted, int Total) CheckProgress { get; set; }
    public StockCheckSession? LastClosedCheck { get; set; }

    public async Task OnGetAsync()
    {
        var parts = _db.Parts.AsNoTracking();
        ActiveParts = await parts.CountAsync(p => p.Status == PartStatus.Active);
        NonActiveParts = await parts.CountAsync(p => p.Status == PartStatus.NonActive);
        TotalParts = ActiveParts + NonActiveParts;
        ActiveValue = await parts.Where(p => p.Status == PartStatus.Active).SumAsync(p => p.StockQty * p.UnitPriceUsd);
        NonActiveValue = await parts.Where(p => p.Status == PartStatus.NonActive).SumAsync(p => p.StockQty * p.UnitPriceUsd);

        // Notifikasi untuk SEMUA part (aktif maupun non-aktif), karena status aktif/non-aktif masih diisi manual.
        // Status mengikuti kolom Status di Excel: EMPTY (0), LOW (< min), CRITICAL (= min).
        Alerts = await parts.Where(p => p.StockQty <= p.MinStock).OrderBy(p => p.StockQty).ThenBy(p => p.PartNumber).ToListAsync();
        Alerts = Alerts.OrderBy(p => p.Level).ThenBy(p => p.PartNumber).ToList();
        OverstockCount = await parts.CountAsync(p => p.StockQty > 0 && p.MaxStock > 0 && p.StockQty > p.MaxStock && p.StockQty > p.MinStock);

        OpenLoans = await _db.Loans.AsNoTracking().Include(l => l.Part).Where(l => l.Status == LoanStatus.Open).OrderBy(l => l.DueDate).ToListAsync();
        Recent = await _db.Transactions.AsNoTracking().Include(t => t.Part).OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(7).ToListAsync();

        // Komposisi status stok (aturan sama dengan kolom Status Excel).
        var levels = await parts.Select(p => new { p.StockQty, p.MinStock, p.MaxStock }).ToListAsync();
        LevelCounts = Enum.GetValues<StockLevel>().ToDictionary(l => l, l => levels.Count(x => Part.LevelOf(x.StockQty, x.MinStock, x.MaxStock) == l));
        RestockCost = Alerts.Sum(p => p.SuggestedOrder * p.UnitPriceUsd);

        // Part paling banyak dipakai 30 hari terakhir (berdasarkan nilai).
        var since = DateTime.Today.AddDays(-30);
        var used = await _db.Transactions.AsNoTracking().Where(t => t.Type == TxType.Issue && t.Date >= since)
            .Select(t => new { t.PartId, t.SiteId, t.Part.PartNumber, t.Part.Name, t.Part.Unit, t.Qty, t.UnitPriceUsd }).ToListAsync();
        TopUsed = used.GroupBy(t => t.PartId)
            .Select(g => new TopRow(g.Key, g.First().PartNumber, g.First().Name, g.First().Unit, g.Sum(x => -x.Qty), g.Sum(x => -x.Qty * x.UnitPriceUsd), g.First().SiteId))
            .OrderByDescending(r => r.Value).ThenByDescending(r => r.Qty).Take(5).ToList();

        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-5);
        var issues = await _db.Transactions.AsNoTracking().Where(t => t.Type == TxType.Issue && t.Date >= first)
            .Select(t => new { t.Date, t.Qty, t.UnitPriceUsd }).ToListAsync();
        for (var i = 0; i < 6; i++)
        {
            var m = first.AddMonths(i);
            var inMonth = issues.Where(x => x.Date.Year == m.Year && x.Date.Month == m.Month).ToList();
            Monthly.Add((m.ToString("MMM yy"), inMonth.Sum(x => -x.Qty * x.UnitPriceUsd)));
            MonthlyCount.Add(inMonth.Count);
        }
        IssuedThisMonth = Monthly[^1].Value;
        IssuedLastMonth = Monthly[^2].Value;

        if (_site.IsAll)
        {
            var stats = await _db.Parts.AsNoTracking().GroupBy(p => p.SiteId).Select(g => new
            {
                g.Key, N = g.Count(),
                A = g.Where(p => p.Status == PartStatus.Active).Sum(p => p.StockQty * p.UnitPriceUsd),
                I = g.Where(p => p.Status == PartStatus.NonActive).Sum(p => p.StockQty * p.UnitPriceUsd),
                R = g.Count(p => p.StockQty <= p.MinStock),
                O = g.Count(p => p.StockQty > 0 && p.MaxStock > 0 && p.StockQty > p.MaxStock && p.StockQty > p.MinStock)
            }).ToListAsync();
            var checks = await _db.StockCheckSessions.AsNoTracking().Where(s => s.Year == CurYear && s.Semester == CurSemester).ToListAsync();
            SiteRows = _site.Allowed.Select(s =>
            {
                var x = stats.FirstOrDefault(v => v.Key == s.Id);
                return new SiteRow(s, x?.N ?? 0, x?.A ?? 0, x?.I ?? 0, x?.R ?? 0, x?.O ?? 0, checks.FirstOrDefault(c => c.SiteId == s.Id));
            }).ToList();
        }
        CurrentCheck = _site.IsAll ? null : await _db.StockCheckSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Year == CurYear && s.Semester == CurSemester);
        if (CurrentCheck != null)
            CheckProgress = (await _db.StockCheckItems.CountAsync(i => i.SessionId == CurrentCheck.Id && i.CountedQty != null),
                await _db.StockCheckItems.CountAsync(i => i.SessionId == CurrentCheck.Id));
        LastClosedCheck = await _db.StockCheckSessions.AsNoTracking().Where(s => s.Status == StockCheckStatus.Closed)
            .OrderByDescending(s => s.ClosedAt).FirstOrDefaultAsync();
    }
}
