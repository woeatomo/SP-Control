using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages;

/// <summary>Satu halaman untuk tiga form: Issued, Input (stok masuk) dan Loan.</summary>
[RequireSite("partId")]
public class EntryModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly StockService _stock;
    public EntryModel(AppDbContext db, StockService stock) { _db = db; _stock = stock; }

    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "issue";
    [BindProperty] public EntryHeader Header { get; set; } = new();
    [BindProperty] public List<EntryLine> Lines { get; set; } = new();
    /// <summary>Issued yang dibuat dari permintaan tablet.</summary>
    [BindProperty(SupportsGet = true, Name = "request")] public int? RequestId { get; set; }
    public PartRequest? Request { get; set; }

    public EntryMode EntryMode => Mode switch { "in" => EntryMode.In, "loan" => EntryMode.Loan, _ => EntryMode.Issue };
    public bool IsOut => EntryMode != EntryMode.In;
    public string Title => EntryMode switch
    {
        EntryMode.In => "Form Input (Stok Masuk)",
        EntryMode.Loan => "Form Loan (Peminjaman Part)",
        _ => "Form Issued (Pemakaian Part)"
    };
    public List<string> Pics { get; set; } = new();
    public List<string> Departments { get; set; } = new();
    public string InitialLinesJson { get; set; } = "[]";

    private async Task LoadAsync(IEnumerable<EntryLine> lines)
    {
        Pics = await _db.Transactions.Where(t => t.Pic != null && t.Pic != "").Select(t => t.Pic!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
        Departments = await _db.Transactions.Where(t => t.Department != null && t.Department != "").Select(t => t.Department!).Distinct().OrderBy(x => x).Take(300).ToListAsync();
        var ids = lines.Select(l => l.PartId).ToList();
        var parts = await _db.Parts.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        InitialLinesJson = System.Text.Json.JsonSerializer.Serialize(lines.Where(l => parts.ContainsKey(l.PartId)).Select(l =>
        {
            var p = parts[l.PartId];
            return new { id = p.Id, partNumber = p.PartNumber, name = p.Name, unit = p.Unit, stockQty = p.StockQty,
                location = p.Location, isReusable = p.IsReusable, condition = p.Condition.ToString(), qty = l.Qty, note = l.Note };
        }));
    }

    private async Task LoadRequestAsync()
    {
        if (RequestId.HasValue)
            Request = await _db.PartRequests.AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == RequestId);
    }

    public async Task OnGetAsync(int? partId)
    {
        Header.Date = DateTime.Now;
        var lines = new List<EntryLine>();
        if (partId.HasValue) lines.Add(new EntryLine { PartId = partId.Value, Qty = 1 });
        await LoadRequestAsync();
        if (Request != null && EntryMode == EntryMode.Issue)
        {
            // Isi otomatis dari permintaan: PIC = peminta, line, referensi = no. permintaan.
            // PIC peminta & "digunakan untuk" dari tablet; no. WO dll. dilengkapi PIC spare part.
            Header.Pic = Request.RequesterName; Header.Department = Request.Line;
            Header.Notes = string.Join(" · ", new[] { "Permintaan " + Request.ReqNo, Request.Notes }.Where(x => !string.IsNullOrWhiteSpace(x)));
            lines.AddRange(Request.Lines.Where(l => l.PartId.HasValue).Select(l => new EntryLine { PartId = l.PartId!.Value, Qty = l.Qty }));
        }
        await LoadAsync(lines);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Header.Date == default) Header.Date = DateTime.Now;
        var result = await _stock.PostEntryAsync(EntryMode, Header, Lines);
        if (!result.Ok)
        {
            ModelState.AddModelError("", result.Message);
            await LoadRequestAsync();
            await LoadAsync(Lines);
            return Page();
        }
        if (RequestId.HasValue && EntryMode == EntryMode.Issue)
        {
            await HttpContext.RequestServices.GetRequiredService<RequestService>().MarkIssuedAsync(RequestId.Value, result.Message, User.Identity?.Name);
            TempData["Ok"] = $"Issued tersimpan — No. dokumen {result.Message}. Permintaan ditandai selesai.";
            return Redirect("/Requests");
        }
        var what = EntryMode switch { EntryMode.In => "Input", EntryMode.Loan => "Loan", _ => "Issued" };
        TempData["Ok"] = $"{what} tersimpan — No. dokumen {result.Message} ({Lines.Count(l => l.PartId > 0)} item).";
        if (EntryMode == EntryMode.In)
        {
            // Barang datang → langsung tawarkan cetak label QR untuk ditempel di part / wadah / drawer.
            TempData["OkLink"] = "/Parts/Labels?doc=" + Uri.EscapeDataString(result.Message);
            TempData["OkLinkText"] = "Cetak label QR barang ini";
        }
        return Redirect("/Entry/" + Mode);
    }
}
