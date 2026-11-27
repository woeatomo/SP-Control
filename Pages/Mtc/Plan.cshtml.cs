using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Mtc;

public class PlanModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SiteContext _site;
    private readonly MaintenanceService _mtc;
    public PlanModel(AppDbContext db, SiteContext site, MaintenanceService mtc) { _db = db; _site = site; _mtc = mtc; }

    [BindProperty(SupportsGet = true)] public DateTime? Date { get; set; }
    public DateTime Day => (Date ?? DateTime.Today).Date;
    public List<MaintenancePlan> Items { get; set; } = new();
    public List<Machine> Machines { get; set; } = new();
    /// <summary>Mesin wajib PM yang belum PM dan belum ada plan PM bulan ini.</summary>
    public List<Machine> PmMissing { get; set; } = new();
    public bool CanEdit => User.IsInRole("Admin") || User.IsInRole("Technician");

    public async Task OnGetAsync()
    {
        Items = await _db.MaintenancePlans.AsNoTracking().Include(p => p.Machine).Include(p => p.Activities)
            .Where(p => p.Date == Day).OrderBy(p => p.Cancelled).ThenBy(p => p.Shift).ThenBy(p => p.Machine!.Code).ToListAsync();
        Machines = await _db.Machines.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Area).ThenBy(m => m.Code).ToListAsync();
        PmMissing = (await _mtc.PmProgressAsync(Day)).Where(x => !x.DoneDate.HasValue && !x.Planned).Select(x => x.Machine).ToList();
    }

    private IActionResult Back(string? ok = null, string? err = null)
    {
        if (ok != null) TempData["Ok"] = ok; if (err != null) TempData["Err"] = err;
        return Redirect($"/Mtc/Plan?date={Day:yyyy-MM-dd}");
    }

    public async Task<IActionResult> OnPostAddAsync(List<int> machineIds, string? task, ActivityType type, Shift? shift, string? pic)
    {
        if (!CanEdit) return Forbid();
        if (_site.Current == null) return Back(err: "Pilih satu site di menu atas.");
        task = task?.Trim();
        if (string.IsNullOrWhiteSpace(task)) return Back(err: "Isi pekerjaan yang direncanakan.");
        if (Day < DateTime.Today.AddDays(-1)) return Back(err: "Tidak bisa menambah plan untuk tanggal yang sudah lewat.");
        var ids = machineIds.Count == 0 ? new List<int?> { null } : machineIds.Distinct().Select(x => (int?)x).ToList();
        foreach (var id in ids)
            _db.MaintenancePlans.Add(new MaintenancePlan
            {
                SiteId = _site.Current.Id, Date = Day, MachineId = id, Task = task.Length > 300 ? task[..300] : task, Type = type, Shift = shift,
                Pic = string.IsNullOrWhiteSpace(pic) ? null : pic.Trim(), CreatedBy = User.Identity?.Name
            });
        await _db.SaveChangesAsync();
        return Back(ok: $"{ids.Count} plan ditambahkan.");
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        if (!CanEdit) return Forbid();
        var p = await _db.MaintenancePlans.Include(x => x.Activities).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        if (p.Activities.Count == 0) { _db.MaintenancePlans.Remove(p); await _db.SaveChangesAsync(); return Back(ok: "Plan dihapus."); }
        p.Cancelled = !p.Cancelled; await _db.SaveChangesAsync();
        return Back(ok: p.Cancelled ? "Plan dibatalkan." : "Plan diaktifkan lagi.");
    }

    /// <summary>Pindahkan plan yang belum selesai dari tanggal ini ke hari berikutnya.</summary>
    public async Task<IActionResult> OnPostCarryAsync()
    {
        if (!CanEdit) return Forbid();
        var open = await _db.MaintenancePlans.Include(p => p.Activities)
            .Where(p => p.Date == Day && !p.Cancelled).ToListAsync();
        var n = 0;
        foreach (var p in open.Where(p => !p.Activities.Any(a => a.Status == ActivityStatus.Done)))
        {
            _db.MaintenancePlans.Add(new MaintenancePlan { SiteId = p.SiteId, Date = Day.AddDays(1), MachineId = p.MachineId, Task = p.Task, Type = p.Type, Shift = p.Shift, Pic = p.Pic, CreatedBy = User.Identity?.Name });
            n++;
        }
        await _db.SaveChangesAsync();
        TempData["Ok"] = $"{n} plan yang belum selesai disalin ke {Day.AddDays(1):dd MMM}.";
        return Redirect($"/Mtc/Plan?date={Day.AddDays(1):yyyy-MM-dd}");
    }
}
