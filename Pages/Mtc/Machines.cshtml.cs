using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Mtc;

public class MachinesModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly SiteContext _site;
    public MachinesModel(AppDbContext db, SiteContext site) { _db = db; _site = site; }

    public List<Machine> Items { get; set; } = new();
    [BindProperty] public Machine Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public bool CanEdit => User.IsInRole("Admin") || User.IsInRole("Technician");
    public List<string> Areas { get; set; } = new();

    public async Task OnGetAsync(int? edit)
    {
        await LoadAsync();
        if (edit.HasValue) Input = await _db.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == edit) ?? new Machine();
    }

    private async Task LoadAsync()
    {
        var q = _db.Machines.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Q)) { var k = $"%{Q.Trim()}%"; q = q.Where(m => EF.Functions.Like(m.Code + " " + m.Name + " " + (m.Area ?? "") + " " + (m.Maker ?? ""), k)); }
        Items = await q.OrderBy(m => !m.IsActive).ThenBy(m => m.Area).ThenBy(m => m.Code).ToListAsync();
        Areas = await _db.Machines.Where(m => m.Area != null).Select(m => m.Area!).Distinct().OrderBy(x => x).ToListAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!CanEdit) return Forbid();
        if (_site.Current == null && Input.Id == 0) { TempData["Err"] = "Pilih satu site di menu atas."; return RedirectToPage(); }
        Input.Code = (Input.Code ?? "").Trim().ToUpperInvariant(); Input.Name = (Input.Name ?? "").Trim();
        if (await _db.Machines.AnyAsync(m => m.Code == Input.Code && m.Id != Input.Id && (Input.Id != 0 || m.SiteId == _site.Current!.Id)))
            ModelState.AddModelError("Input.Code", "Kode mesin sudah dipakai.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        if (Input.Id == 0)
        {
            Input.SiteId = _site.Current!.Id;
            _db.Machines.Add(Input);
        }
        else
        {
            var e = await _db.Machines.FirstOrDefaultAsync(m => m.Id == Input.Id);
            if (e == null) return NotFound();
            e.Code = Input.Code; e.Name = Input.Name; e.Area = Input.Area?.Trim(); e.Maker = Input.Maker?.Trim(); e.ModelNo = Input.ModelNo?.Trim();
            e.PmRequired = Input.PmRequired; e.IsActive = Input.IsActive; e.Notes = Input.Notes?.Trim();
        }
        await _db.SaveChangesAsync();
        TempData["Ok"] = $"Mesin {Input.Code} disimpan.";
        return RedirectToPage();
    }

    /// <summary>Import CSV: Code, Name, Area, Maker, Model, PmRequired (Yes/No).</summary>
    public async Task<IActionResult> OnPostImportAsync(IFormFile? file)
    {
        if (!CanEdit) return Forbid();
        if (_site.Current == null) { TempData["Err"] = "Pilih satu site di menu atas."; return RedirectToPage(); }
        if (file == null || file.Length == 0) { TempData["Err"] = "Pilih file CSV."; return RedirectToPage(); }
        using var sr = new StreamReader(file.OpenReadStream());
        var rows = Csv.Parse(await sr.ReadToEndAsync());
        if (rows.Count < 2) { TempData["Err"] = "File kosong."; return RedirectToPage(); }
        var h = rows[0].Select(ExcelMaster.Norm).ToList();
        int C(params string[] n) => h.FindIndex(x => n.Contains(x));
        int ci = C("code", "kode", "machinecode", "kodemesin"), ni = C("name", "nama", "machinename", "namamesin"), ai = C("area", "line", "arealine"),
            mi = C("maker", "brand"), mo = C("model", "modelno", "tipe", "type"), pi = C("pmrequired", "pm", "wajibpm");
        if (ci < 0 || ni < 0) { TempData["Err"] = "Header wajib: Code dan Name."; return RedirectToPage(); }
        var existing = await _db.Machines.Where(m => m.SiteId == _site.Current.Id).ToDictionaryAsync(m => m.Code, StringComparer.OrdinalIgnoreCase);
        int add = 0, upd = 0;
        foreach (var r in rows.Skip(1))
        {
            string G(int i) => i >= 0 && i < r.Length ? r[i].Trim() : "";
            var code = G(ci).ToUpperInvariant(); var name = G(ni);
            if (code.Length == 0 || name.Length == 0 || code.Length > 30) continue;
            if (!existing.TryGetValue(code, out var m)) { m = new Machine { SiteId = _site.Current.Id, Code = code }; _db.Machines.Add(m); existing[code] = m; add++; } else upd++;
            m.Name = name.Length > 120 ? name[..120] : name;
            if (ai >= 0) m.Area = G(ai).Length > 0 ? G(ai) : null;
            if (mi >= 0) m.Maker = G(mi).Length > 0 ? G(mi) : null;
            if (mo >= 0) m.ModelNo = G(mo).Length > 0 ? G(mo) : null;
            if (pi >= 0) m.PmRequired = !(G(pi).ToLowerInvariant() is "no" or "n" or "tidak" or "0" or "false");
        }
        await _db.SaveChangesAsync();
        TempData["Ok"] = $"Import mesin: {add} baru, {upd} diperbarui.";
        return RedirectToPage();
    }
}
