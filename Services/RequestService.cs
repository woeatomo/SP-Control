using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Services;

public class NewRequestLine
{
    public int? PartId { get; set; }
    public string? Text { get; set; }
    public decimal Qty { get; set; } = 1;
}

public class NewRequest
{
    public string? Name { get; set; }
    public string? Line { get; set; }
    public bool Urgent { get; set; }
    public string? Notes { get; set; }
    public List<NewRequestLine> Items { get; set; } = new();
}

/// <summary>Permintaan part dari tablet peminta dan penanganannya di ruang sparepart.</summary>
public class RequestService
{
    private readonly AppDbContext _db;
    private readonly SiteContext _site;
    private readonly StockService _stock;
    public RequestService(AppDbContext db, SiteContext site, StockService stock) { _db = db; _site = site; _stock = stock; }

    public static string StatusText(RequestStatus s) => s switch
    {
        RequestStatus.New => "Menunggu",
        RequestStatus.Processing => "Sedang disiapkan",
        RequestStatus.Ready => "Siap diambil",
        RequestStatus.Done => "Selesai",
        _ => "Ditolak"
    };

    public async Task<(PartRequest? Request, string? Error)> CreateAsync(NewRequest r, string? createdBy)
    {
        var site = _site.Current;
        if (site == null) return (null, "Tablet belum terhubung ke satu site. Hubungi admin.");
        var name = (r.Name ?? "").Trim();
        if (name.Length == 0) return (null, "Nama peminta wajib diisi.");
        var items = r.Items.Where(i => i.Qty > 0 && (i.PartId.HasValue || !string.IsNullOrWhiteSpace(i.Text))).Take(30).ToList();
        if (items.Count == 0) return (null, "Tambahkan minimal satu part.");
        if (items.Any(i => i.Qty > 9999)) return (null, "Qty terlalu besar.");

        // Part harus ada di site ini (filter site berlaku).
        var ids = items.Where(i => i.PartId.HasValue).Select(i => i.PartId!.Value).Distinct().ToList();
        var valid = await _db.Parts.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync();
        if (valid.Count != ids.Count) return (null, "Ada part yang tidak ditemukan. Muat ulang halaman.");

        var head = $"REQ-{site.Code}-{DateTime.Now:yyMMdd}-";
        var existing = await _db.PartRequests.IgnoreQueryFilters().Where(x => x.SiteId == site.Id && x.ReqNo.StartsWith(head)).Select(x => x.ReqNo).ToListAsync();
        var n = existing.Select(x => int.TryParse(x[head.Length..], out var k) ? k : 0).DefaultIfEmpty(0).Max() + 1;

        var req = new PartRequest
        {
            SiteId = site.Id, ReqNo = head + n.ToString("000"), RequesterName = Trunc(name, 100), Line = Trunc(r.Line, 120),
            Urgency = r.Urgent ? RequestUrgency.Urgent : RequestUrgency.Normal, Notes = Trunc(r.Notes, 500), CreatedBy = createdBy,
            Lines = items.Select(i => new PartRequestLine
            {
                PartId = i.PartId, FreeText = i.PartId.HasValue ? null : Trunc(i.Text, 200), Qty = Math.Round(i.Qty, 2)
            }).ToList()
        };
        _db.PartRequests.Add(req);
        await _db.SaveChangesAsync();
        return (req, null);
    }

    private static string? Trunc(string? s, int max)
    {
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s.Length > max ? s[..max] : s;
    }

    public async Task<string?> SetStatusAsync(int id, RequestStatus status, string? note, string? by)
    {
        var r = await _db.PartRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return "Permintaan tidak ditemukan.";
        if (r.Status is RequestStatus.Done or RequestStatus.Rejected && status != r.Status) return "Permintaan sudah ditutup.";
        if (status == RequestStatus.Rejected && string.IsNullOrWhiteSpace(note)) return "Isi alasan penolakan.";
        r.Status = status; r.HandledBy = by; r.UpdatedAt = DateTime.Now;
        if (note != null) r.ResponseNote = Trunc(note, 300);
        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>Hubungkan baris teks bebas ke part di katalog (dipilih admin dari hasil pencarian).</summary>
    public async Task<string?> LinkLineAsync(int requestId, int lineId, int? partId)
    {
        var line = await _db.PartRequestLines.Include(l => l.Request).FirstOrDefaultAsync(l => l.Id == lineId && l.RequestId == requestId);
        if (line == null) return "Baris tidak ditemukan.";
        if (partId.HasValue && !await _db.Parts.AnyAsync(p => p.Id == partId && p.SiteId == line.Request.SiteId)) return "Part tidak ditemukan di site ini.";
        line.PartId = partId;
        await _db.SaveChangesAsync();
        return null;
    }

    /// <summary>Dipanggil setelah Issued tersimpan dari permintaan.</summary>
    public async Task MarkIssuedAsync(int requestId, string docNo, string? by)
    {
        var r = await _db.PartRequests.FirstOrDefaultAsync(x => x.Id == requestId);
        if (r == null) return;
        r.Status = RequestStatus.Done; r.IssueDocNo = docNo; r.HandledBy = by; r.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public static object PartCard(Part p) => new
    {
        p.Id, p.PartNumber, p.Name, p.Specification, p.Unit, p.StockQty, p.MinStock, p.Location, p.ImageUrl,
        Level = p.Level.ToString().ToUpperInvariant(), p.IsReusable, Condition = p.Condition.ToString()
    };

    /// <summary>Detail untuk popup admin: part katalog, atau kandidat hasil pencarian untuk teks bebas.</summary>
    public async Task<object?> DetailAsync(int id)
    {
        var r = await _db.PartRequests.AsNoTracking().Include(x => x.Lines).ThenInclude(l => l.Part).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return null;
        var lines = new List<object>();
        foreach (var l in r.Lines.OrderBy(l => l.Id))
        {
            List<object> matches = new();
            if (l.Part == null && !string.IsNullOrWhiteSpace(l.FreeText))
            {
                matches = (await PartSearch.FuzzyAsync(_db.Parts.AsNoTracking(), l.FreeText, 8)).Select(PartCard).ToList();
            }
            lines.Add(new { l.Id, l.Qty, l.FreeText, Part = l.Part == null ? null : PartCard(l.Part), Matches = matches });
        }
        return new
        {
            r.Id, r.ReqNo, r.RequesterName, r.Line, Urgent = r.Urgency == RequestUrgency.Urgent, r.Notes,
            Status = r.Status.ToString(), StatusText = StatusText(r.Status), r.CreatedAt, r.HandledBy, r.ResponseNote, r.IssueDocNo,
            Lines = lines
        };
    }
}
