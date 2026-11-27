using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Mtc;

[RequireSite]
public class ActivityModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly MaintenanceService _mtc;
    private readonly SiteContext _site;
    public ActivityModel(AppDbContext db, MaintenanceService mtc, SiteContext site) { _db = db; _mtc = mtc; _site = site; }

    [BindProperty] public MaintenanceActivity A { get; set; } = new();
    /// <summary>Id transaksi Issued yang dicentang sebagai pergantian part.</summary>
    [BindProperty] public List<int> LinkTx { get; set; } = new();
    /// <summary>JSON [{partId, qty}] part yang dipakai tapi belum ada transaksinya.</summary>
    [BindProperty] public string? Unrecorded { get; set; }

    public List<Machine> Machines { get; set; } = new();
    public List<string> TechNames { get; set; } = new();
    public List<MaintenanceService.PendingPart> Pending { get; set; } = new();
    public string CandidatesJson { get; set; } = "[]";
    public MaintenancePlan? Plan { get; set; }
    public bool IsNew => A.Id == 0;

    private async Task LoadAsync()
    {
        Machines = await _db.Machines.AsNoTracking().Where(m => m.IsActive || m.Id == A.MachineId).OrderBy(m => m.Area).ThenBy(m => m.Code).ToListAsync();
        TechNames = (await _db.MaintenanceActivities.AsNoTracking().Where(a => a.Technicians != null).OrderByDescending(a => a.Id).Take(300).Select(a => a.Technicians!).ToListAsync())
            .SelectMany(x => x.Split(',')).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        if (A.MachineId.HasValue) A.Machine = Machines.FirstOrDefault(m => m.Id == A.MachineId);
        A.SiteId = A.SiteId == 0 ? _site.Current!.Id : A.SiteId;
        CandidatesJson = JsonSerializer.Serialize(await _mtc.CandidatesAsync(A), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (!IsNew) Pending = (await _mtc.PendingPartsAsync(new[] { A.Id })).GetValueOrDefault(A.Id) ?? new();
        if (A.PlanId.HasValue) Plan = await _db.MaintenancePlans.AsNoTracking().Include(p => p.Machine).FirstOrDefaultAsync(p => p.Id == A.PlanId);
    }

    public async Task<IActionResult> OnGetAsync(int? id, int? plan)
    {
        if (id.HasValue)
        {
            var a = await _db.MaintenanceActivities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound();
            A = a;
        }
        else
        {
            var (date, shift) = MaintenanceService.ShiftOf(DateTime.Now);
            A = new MaintenanceActivity { Date = date, Shift = shift, Type = ActivityType.Corrective, Technicians = User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value };
            if (plan.HasValue && await _db.MaintenancePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == plan) is { } pl)
            {
                // Kerjakan dari daily plan: mesin, jenis dan PIC ikut terisi.
                A.PlanId = pl.Id; A.MachineId = pl.MachineId; A.Type = pl.Type; A.Action = pl.Task;
                if (!string.IsNullOrWhiteSpace(pl.Pic)) A.Technicians = pl.Pic;
                if (pl.Date.Date == date) A.Shift = pl.Shift ?? shift;
            }
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Remove("A.Machine"); ModelState.Remove("A.Plan");
        if (!A.MachineId.HasValue && string.IsNullOrWhiteSpace(A.MachineText)) ModelState.AddModelError("A.MachineId", "Pilih mesin, atau isi lokasi lain.");
        if (string.IsNullOrWhiteSpace(A.Action)) ModelState.AddModelError("A.Action", "Tindakan / pekerjaan yang dilakukan wajib diisi.");
        if (string.IsNullOrWhiteSpace(A.Technicians)) ModelState.AddModelError("A.Technicians", "Isi nama teknisi.");
        if (A.Date > DateTime.Today.AddDays(1)) ModelState.AddModelError("A.Date", "Tanggal tidak boleh di masa depan.");
        var unrecorded = new List<NewRequestLine>();
        try { unrecorded = JsonSerializer.Deserialize<List<NewRequestLine>>(Unrecorded ?? "[]", new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new(); } catch (JsonException) { }
        unrecorded = unrecorded.Where(u => u.PartId.HasValue && u.Qty > 0).ToList();
        if (A.HasPartReplacement && LinkTx.Count == 0 && unrecorded.Count == 0 && (IsNew || !await _db.PartRequests.AnyAsync(r => r.ActivityId == A.Id && r.Status != RequestStatus.Rejected)))
            ModelState.AddModelError("Parts", "Ada pergantian part: centang part dari daftar pengambilan, atau tambahkan part yang belum tercatat.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        MaintenanceActivity e;
        if (IsNew)
        {
            e = new MaintenanceActivity { SiteId = _site.Current!.Id, CreatedBy = User.Identity?.Name };
            _db.MaintenanceActivities.Add(e);
        }
        else
        {
            e = await _db.MaintenanceActivities.FirstOrDefaultAsync(x => x.Id == A.Id) ?? throw new InvalidOperationException();
            e.UpdatedAt = DateTime.Now;
        }
        e.Date = A.Date.Date; e.Shift = A.Shift; e.MachineId = A.MachineId; e.MachineText = A.MachineId.HasValue ? null : A.MachineText?.Trim();
        e.Type = A.Type; e.Problem = A.Problem?.Trim(); e.Cause = A.Cause?.Trim(); e.Action = A.Action?.Trim();
        e.StartTime = A.StartTime; e.EndTime = A.EndTime; e.MachineStopped = A.MachineStopped; e.Status = A.Status;
        e.Technicians = string.Join(", ", (A.Technicians ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
        e.HasPartReplacement = A.HasPartReplacement; e.Remarks = A.Remarks?.Trim(); e.PlanId = A.PlanId;
        await _db.SaveChangesAsync();
        e.Machine = e.MachineId.HasValue ? await _db.Machines.FirstOrDefaultAsync(m => m.Id == e.MachineId) : null;

        var msg = $"Aktivitas {e.MachineLabel} tersimpan.";
        var (linked, rejected) = await _mtc.SetLinksAsync(e, e.HasPartReplacement ? LinkTx : new List<int>());
        if (linked > 0) msg += $" {linked} part tertaut ke transaksi sparepart.";
        if (rejected > 0) msg += $" {rejected} part tidak ditautkan karena sudah dipakai di laporan lain.";
        if (e.HasPartReplacement && unrecorded.Count > 0)
        {
            var (req, err) = await _mtc.RequestUsageConfirmationAsync(e, unrecorded, User.Identity?.Name);
            msg += req != null ? $" Konfirmasi pemakaian {req.ReqNo} dikirim ke tim sparepart." : " Konfirmasi pemakaian gagal: " + err;
        }
        TempData["Ok"] = msg;
        return Redirect($"/Mtc/Report?date={e.Date:yyyy-MM-dd}#act{e.Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var e = await _db.MaintenanceActivities.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        // Lepas tautan part (transaksi sparepart tetap ada, bisa ditautkan ke laporan lain).
        foreach (var t in await _db.Transactions.Where(t => t.ActivityId == id).ToListAsync()) t.ActivityId = null;
        _db.MaintenanceActivities.Remove(e);
        await _db.SaveChangesAsync();
        TempData["Ok"] = "Aktivitas dihapus. Transaksi part yang tertaut dilepas dan bisa ditautkan ke laporan lain.";
        return Redirect($"/Mtc/Report?date={e.Date:yyyy-MM-dd}");
    }
}
