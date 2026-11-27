using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Mtc;

/// <summary>Dashboard Machine Maintenance: daily plan hari ini, progress PM bulan ini, pekerjaan terbuka.</summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly MaintenanceService _mtc;
    public IndexModel(AppDbContext db, MaintenanceService mtc) { _db = db; _mtc = mtc; }

    public DateTime Today => DateTime.Today;
    public (DateTime Date, Shift Shift) Now { get; set; }
    public List<MaintenancePlan> Plans { get; set; } = new();
    public List<MaintenanceService.PmMachine> Pm { get; set; } = new();
    public List<MaintenanceActivity> OpenWork { get; set; } = new();
    public List<MaintenanceActivity> Recent { get; set; } = new();
    public int TodayCount { get; set; }
    public int TodayBreakdown { get; set; }
    public double TodayDowntime { get; set; }
    public int PartsThisMonth { get; set; }
    public int UnlinkedReports { get; set; }
    public bool CanEdit => User.IsInRole("Admin") || User.IsInRole("Technician");

    public static string PlanState(MaintenancePlan p) =>
        p.Activities.Any(a => a.Status == ActivityStatus.Done) ? "done" : p.Activities.Count > 0 ? "progress" : "open";

    public async Task OnGetAsync()
    {
        Now = MaintenanceService.ShiftOf(DateTime.Now);
        Plans = await _db.MaintenancePlans.AsNoTracking().Include(p => p.Machine).Include(p => p.Activities)
            .Where(p => p.Date == Today && !p.Cancelled).ToListAsync();
        Plans = Plans.OrderBy(p => PlanState(p) == "done").ThenBy(p => p.Shift).ThenBy(p => p.Machine?.Code).ToList();
        Pm = await _mtc.PmProgressAsync(Today);

        var today = await _db.MaintenanceActivities.AsNoTracking().Where(a => a.Date == Today).ToListAsync();
        TodayCount = today.Count; TodayBreakdown = today.Count(a => a.Type == ActivityType.Breakdown);
        TodayDowntime = today.Where(a => a.MachineStopped && a.Duration.HasValue).Sum(a => a.Duration!.Value.TotalMinutes);

        var weekAgo = Today.AddDays(-7);
        OpenWork = await _db.MaintenanceActivities.AsNoTracking().Include(a => a.Machine)
            .Where(a => a.Date >= weekAgo && a.Status != ActivityStatus.Done).OrderByDescending(a => a.Date).ThenByDescending(a => a.Id).Take(8).ToListAsync();
        Recent = await _db.MaintenanceActivities.AsNoTracking().Include(a => a.Machine)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.Shift).ThenByDescending(a => a.Id).Take(6).ToListAsync();

        var first = new DateTime(Today.Year, Today.Month, 1);
        PartsThisMonth = await _db.Transactions.CountAsync(t => t.ActivityId != null && t.Date >= first);
        var withParts = await _db.MaintenanceActivities.AsNoTracking().Where(a => a.HasPartReplacement && a.Date >= first).Select(a => a.Id).ToListAsync();
        var linked = await _db.Transactions.Where(t => t.ActivityId != null && withParts.Contains(t.ActivityId.Value)).Select(t => t.ActivityId!.Value).Distinct().ToListAsync();
        var pending = await _db.PartRequests.Where(r => r.ActivityId != null && withParts.Contains(r.ActivityId.Value) && r.Status != RequestStatus.Rejected).Select(r => r.ActivityId!.Value).Distinct().ToListAsync();
        UnlinkedReports = withParts.Count(id => !linked.Contains(id) && !pending.Contains(id));
    }
}
