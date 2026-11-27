using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Mtc;

/// <summary>Laporan harian maintenance per shift (default: hari kemarin).</summary>
public class ReportModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly MaintenanceService _mtc;
    public ReportModel(AppDbContext db, MaintenanceService mtc) { _db = db; _mtc = mtc; }

    [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }
    /// <summary>"" = semua, "A", "B"</summary>
    [BindProperty(SupportsGet = true)] public string? Shift { get; set; }
    /// <summary>time | machine | type | status | duration</summary>
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "time";
    [BindProperty(SupportsGet = true)] public ActivityType? Type { get; set; }
    [BindProperty(SupportsGet = true)] public ActivityStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool OnlyParts { get; set; }

    public DateTime Day => (Date ?? DateTime.Today.AddDays(-1)).Date;
    public List<MaintenanceActivity> Items { get; set; } = new();
    public Dictionary<int, List<MaintenanceService.LinkedPart>> Linked { get; set; } = new();
    public Dictionary<int, List<MaintenanceService.PendingPart>> Pending { get; set; } = new();
    public bool CanEdit => User.IsInRole("Admin") || User.IsInRole("Technician");

    private async Task LoadAsync()
    {
        var q = _db.MaintenanceActivities.AsNoTracking().Include(a => a.Machine).Where(a => a.Date == Day);
        if (Shift is "A" or "B") { var s = Enum.Parse<Models.Shift>(Shift); q = q.Where(a => a.Shift == s); }
        if (Type.HasValue) q = q.Where(a => a.Type == Type);
        if (Status.HasValue) q = q.Where(a => a.Status == Status);
        if (OnlyParts) q = q.Where(a => a.HasPartReplacement);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var k = $"%{Q.Trim()}%";
            q = q.Where(a => EF.Functions.Like((a.Machine != null ? a.Machine.Code + " " + a.Machine.Name + " " + (a.Machine.Area ?? "") : a.MachineText ?? "")
                + " " + (a.Problem ?? "") + " " + (a.Cause ?? "") + " " + (a.Action ?? "") + " " + (a.Technicians ?? ""), k));
        }
        var list = await q.ToListAsync();
        // Shift B: jam 00:00–07:00 termasuk akhir shift → urutkan setelah 19:00–23:59.
        double Key(MaintenanceActivity a) => a.StartTime is { } t ? (a.Shift == Models.Shift.B && t < MaintenanceService.ShiftAStart ? t.TotalMinutes + 1440 : t.TotalMinutes) : 99999;
        Items = (Sort switch
        {
            "machine" => list.OrderBy(a => a.MachineLabel).ThenBy(Key),
            "type" => list.OrderBy(a => a.Type).ThenBy(Key),
            "status" => list.OrderByDescending(a => a.Status).ThenBy(Key),
            "duration" => list.OrderByDescending(a => a.Duration ?? TimeSpan.Zero),
            _ => list.OrderBy(Key).ThenBy(a => a.Id)
        }).ToList();
        Linked = await _mtc.LinkedPartsAsync(Items.Select(a => a.Id));
        Pending = await _mtc.PendingPartsAsync(Items.Select(a => a.Id));
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnGetExportAsync()
    {
        await LoadAsync();
        var rows = Items.Select(a => new[]
        {
            a.Date.ToString("yyyy-MM-dd"), a.Shift.ToString(), a.StartTime?.ToString(@"hh\:mm"), a.EndTime?.ToString(@"hh\:mm"),
            a.Duration.HasValue ? ((int)a.Duration.Value.TotalMinutes).ToString() : "", a.Machine?.Code ?? "", a.Machine?.Name ?? a.MachineText,
            a.Machine?.Area, MaintenanceService.TypeText(a.Type), a.Problem, a.Cause, a.Action, a.MachineStopped ? "Ya" : "Tidak",
            MaintenanceService.StatusText(a.Status), a.Technicians,
            string.Join("; ", (Linked.GetValueOrDefault(a.Id) ?? new()).Select(p => $"{p.PartNumber} x{StockService.Fmt(p.Qty)} ({p.DocNo})")),
            string.Join("; ", (Pending.GetValueOrDefault(a.Id) ?? new()).Select(p => $"{p.Label} x{StockService.Fmt(p.Qty)} ({p.ReqNo}, menunggu)")),
            a.Remarks
        });
        var bytes = Csv.Build(new[] { "Date", "Shift", "Start", "End", "Minutes", "MachineCode", "Machine", "Area", "Type", "Problem", "Cause", "Action",
            "MachineStopped", "Status", "Technicians", "PartsReplaced", "PartsPendingConfirmation", "Remarks" }, rows);
        return File(bytes, "text/csv", $"daily-report-{Day:yyyyMMdd}{(Shift is "A" or "B" ? "-shift" + Shift : "")}.csv");
    }

    public string Url(params (string k, string? v)[] changes)
    {
        var d = Request.Query.Where(kv => kv.Key != "handler").ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in changes) { if (string.IsNullOrEmpty(v)) d.Remove(k); else d[k] = v; }
        return "/Mtc/Report" + QueryString.Create(d!);
    }
}
