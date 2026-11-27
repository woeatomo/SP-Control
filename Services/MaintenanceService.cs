using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Services;

/// <summary>Modul Machine Maintenance: shift, daily report, penautan pergantian part ke transaksi sparepart.</summary>
public class MaintenanceService
{
    /// <summary>Shift A 07:00–19:00, Shift B 19:00–07:00 (shift B setelah tengah malam ikut tanggal mulai shift).</summary>
    public static readonly TimeSpan ShiftAStart = new(7, 0, 0), ShiftBStart = new(19, 0, 0);

    private readonly AppDbContext _db;
    private readonly SiteContext _site;
    private readonly RequestService _requests;
    public MaintenanceService(AppDbContext db, SiteContext site, RequestService requests) { _db = db; _site = site; _requests = requests; }

    public static (DateTime Date, Shift Shift) ShiftOf(DateTime t)
    {
        var time = t.TimeOfDay;
        if (time >= ShiftAStart && time < ShiftBStart) return (t.Date, Shift.A);
        return (time >= ShiftBStart ? t.Date : t.Date.AddDays(-1), Shift.B);
    }

    public static string ShiftLabel(Shift s) => s == Shift.A ? "Shift A (07:00–19:00)" : "Shift B (19:00–07:00)";

    public static string TypeText(ActivityType t) => t switch
    {
        ActivityType.Breakdown => "Breakdown", ActivityType.Corrective => "Corrective", ActivityType.Preventive => "Preventive (PM)",
        ActivityType.Inspection => "Inspeksi", ActivityType.Improvement => "Improvement", _ => "Lainnya"
    };
    public static string TypeCss(ActivityType t) => t switch
    {
        ActivityType.Breakdown => "ty-bd", ActivityType.Corrective => "ty-cm", ActivityType.Preventive => "ty-pm",
        ActivityType.Inspection => "ty-in", ActivityType.Improvement => "ty-im", _ => "ty-ot"
    };
    public static string StatusText(ActivityStatus s) => s switch
    {
        ActivityStatus.Done => "Selesai", ActivityStatus.Continue => "Lanjut shift berikut", _ => "Menunggu part"
    };

    public record Candidate(int Id, string DocNo, DateTime Date, string PartNumber, string Name, decimal Qty, string Unit,
        string? Pic, string? Department, string? Notes, int Score, string Why, bool LinkedHere);

    /// <summary>
    /// Transaksi Issued yang bisa ditautkan ke aktivitas: belum tertaut ke aktivitas lain, tanggal di sekitar aktivitas.
    /// Diurutkan menurut kecocokan (PIC = teknisi, mesin disebut, tanggal sama).
    /// </summary>
    public async Task<List<Candidate>> CandidatesAsync(MaintenanceActivity a)
    {
        var from = a.Date.AddDays(-3); var to = a.Date.AddDays(2);
        var list = await _db.Transactions.AsNoTracking().Include(t => t.Part)
            .Where(t => t.SiteId == a.SiteId && t.Type == TxType.Issue && t.Qty < 0
                        && ((t.ActivityId == null && t.Date >= from && t.Date < to) || (a.Id != 0 && t.ActivityId == a.Id)))
            .OrderByDescending(t => t.Date).Take(200).ToListAsync();
        var techs = (a.Technicians ?? "").Split(',', ';', '/').Select(x => x.Trim()).Where(x => x.Length >= 3).ToList();
        var machine = a.Machine;
        return list.Select(t =>
        {
            int score = 0; var why = new List<string>();
            if (t.ActivityId == a.Id && a.Id != 0) { score += 1000; why.Add("sudah ditautkan"); }
            var hay = $"{t.Department} {t.Notes} {t.Reference}";
            if (machine != null && (hay.Contains(machine.Code, StringComparison.OrdinalIgnoreCase) || hay.Contains(machine.Name, StringComparison.OrdinalIgnoreCase)))
            { score += 50; why.Add("mesin sama"); }
            if (t.Pic != null && techs.Any(n => t.Pic.Contains(n, StringComparison.OrdinalIgnoreCase) || n.Contains(t.Pic, StringComparison.OrdinalIgnoreCase)))
            { score += 40; why.Add("PIC sama"); }
            var days = Math.Abs((t.Date.Date - a.Date).TotalDays);
            if (days == 0) { score += 20; why.Add("hari yang sama"); } else score += Math.Max(0, 10 - (int)days * 3);
            return new Candidate(t.Id, t.DocNo, t.Date, t.Part.PartNumber, t.Part.Name, -t.Qty, t.Part.Unit, t.Pic, t.Department, t.Notes,
                score, string.Join(" · ", why), t.ActivityId == a.Id && a.Id != 0);
        }).OrderByDescending(c => c.Score).ThenByDescending(c => c.Date).ToList();
    }

    /// <summary>
    /// Tautkan baris Issued ke aktivitas. Hanya baris yang belum tertaut ke aktivitas LAIN yang bisa dipilih,
    /// jadi satu pergantian part tidak pernah tercatat dua kali.
    /// </summary>
    public async Task<(int Linked, int Rejected)> SetLinksAsync(MaintenanceActivity a, IEnumerable<int> txIds)
    {
        var wanted = txIds.Distinct().ToHashSet();
        var current = await _db.Transactions.Where(t => t.ActivityId == a.Id).ToListAsync();
        foreach (var t in current.Where(t => !wanted.Contains(t.Id))) t.ActivityId = null;
        var add = await _db.Transactions.Where(t => wanted.Contains(t.Id) && t.SiteId == a.SiteId && t.Type == TxType.Issue).ToListAsync();
        int linked = 0, rejected = 0;
        foreach (var t in add)
        {
            if (t.ActivityId == null || t.ActivityId == a.Id) { t.ActivityId = a.Id; linked++; }
            else rejected++; // sudah dipakai aktivitas lain (mis. diisi teknisi lain bersamaan)
        }
        await _db.SaveChangesAsync();
        return (linked, rejected + wanted.Count - add.Count);
    }

    /// <summary>
    /// Part sudah dipakai tapi belum ada transaksinya → kirim "konfirmasi pemakaian" ke PC sparepart
    /// (lewat jalur notifikasi permintaan). Issued yang dibuat dari sana otomatis tertaut ke aktivitas ini.
    /// </summary>
    public async Task<(PartRequest? Request, string? Error)> RequestUsageConfirmationAsync(MaintenanceActivity a, List<NewRequestLine> items, string? by)
    {
        var who = string.IsNullOrWhiteSpace(a.Technicians) ? by ?? "Teknisi" : a.Technicians!;
        var nr = new NewRequest
        {
            Name = who, Line = a.Machine?.Label ?? a.MachineText,
            Notes = $"Konfirmasi pemakaian dari daily report {a.Date:dd MMM yyyy} shift {a.Shift}" + (string.IsNullOrWhiteSpace(a.Action) ? "" : " — " + a.Action),
            Items = items
        };
        return await _requests.CreateAsync(nr, by, RequestSource.DailyReport, a.Id);
    }

    public record LinkedPart(int TxId, string DocNo, string PartNumber, string Name, decimal Qty, string Unit, int PartId);
    public record PendingPart(string ReqNo, string Label, decimal Qty, RequestStatus Status);

    public async Task<Dictionary<int, List<LinkedPart>>> LinkedPartsAsync(IEnumerable<int> activityIds)
    {
        var ids = activityIds.ToList();
        return (await _db.Transactions.AsNoTracking().Include(t => t.Part).Where(t => t.ActivityId != null && ids.Contains(t.ActivityId.Value)).ToListAsync())
            .GroupBy(t => t.ActivityId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(t => new LinkedPart(t.Id, t.DocNo, t.Part.PartNumber, t.Part.Name, -t.Qty, t.Part.Unit, t.PartId)).ToList());
    }

    public async Task<Dictionary<int, List<PendingPart>>> PendingPartsAsync(IEnumerable<int> activityIds)
    {
        var ids = activityIds.ToList();
        var reqs = await _db.PartRequests.AsNoTracking().Include(r => r.Lines).ThenInclude(l => l.Part)
            .Where(r => r.ActivityId != null && ids.Contains(r.ActivityId.Value) && r.Status != RequestStatus.Done && r.Status != RequestStatus.Rejected).ToListAsync();
        return reqs.GroupBy(r => r.ActivityId!.Value).ToDictionary(g => g.Key, g => g.SelectMany(r => r.Lines.Select(l =>
            new PendingPart(r.ReqNo, l.Part != null ? l.Part.PartNumber + " — " + l.Part.Name : l.FreeText ?? "-", l.Qty, r.Status))).ToList());
    }

    public record PmMachine(Machine Machine, DateTime? DoneDate, bool Planned);
    /// <summary>Progress PM bulan berjalan: mesin wajib PM yang sudah punya aktivitas Preventive selesai.</summary>
    public async Task<List<PmMachine>> PmProgressAsync(DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1); var next = first.AddMonths(1);
        var machines = await _db.Machines.AsNoTracking().Where(m => m.IsActive && m.PmRequired).OrderBy(m => m.Area).ThenBy(m => m.Code).ToListAsync();
        var done = (await _db.MaintenanceActivities.AsNoTracking()
                .Where(a => a.Type == ActivityType.Preventive && a.Status == ActivityStatus.Done && a.MachineId != null && a.Date >= first && a.Date < next)
                .Select(a => new { a.MachineId, a.Date }).ToListAsync())
            .GroupBy(a => a.MachineId!.Value).ToDictionary(g => g.Key, g => g.Max(x => x.Date));
        var planned = (await _db.MaintenancePlans.AsNoTracking()
                .Where(p => !p.Cancelled && p.Type == ActivityType.Preventive && p.MachineId != null && p.Date >= first && p.Date < next)
                .Select(p => p.MachineId!.Value).ToListAsync()).ToHashSet();
        return machines.Select(m => new PmMachine(m, done.TryGetValue(m.Id, out var d) ? d : null, planned.Contains(m.Id))).ToList();
    }
}
