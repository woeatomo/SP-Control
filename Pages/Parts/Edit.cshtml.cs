using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Parts;

[RequireSite("id", "from")]
public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly StockService _stock;
    private readonly ImageStore _images;
    public EditModel(AppDbContext db, StockService stock, ImageStore images) { _db = db; _stock = stock; _images = images; }

    [BindProperty] public IFormFile? Image { get; set; }
    [BindProperty] public bool RemoveImage { get; set; }

    [BindProperty] public Part Part { get; set; } = new();
    [BindProperty] public decimal InitialStock { get; set; }
    public bool IsNew => Part.Id == 0;
    public Dictionary<Currency, decimal> Rates { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<string> Units { get; set; } = new();

    private async Task LoadListsAsync()
    {
        Rates = await _stock.GetRatesAsync();
        Categories = await _db.Parts.Where(p => p.Category != null && p.Category != "").Select(p => p.Category!).Distinct().OrderBy(x => x).ToListAsync();
        Code = HttpContext.RequestServices.GetRequiredService<AppSettingsService>().Code;
        CodeExample = PartCode.Render(Code, new CodeInput("Bando", "Injection", "Supplier"), 1, DateTime.Now);
        Makers = await _db.Parts.Where(p => p.Brand != null && p.Brand != "").Select(p => p.Brand!).Distinct().OrderBy(x => x).ToListAsync();
        Racks = await _db.Parts.Where(p => p.Location != null && p.Location != "").Select(p => p.Location!).Distinct().OrderBy(x => x).Take(500).ToListAsync();
        Suppliers = await _db.Parts.Where(p => p.Supplier != null && p.Supplier != "").Select(p => p.Supplier!).Distinct().OrderBy(x => x).ToListAsync();
        Units = await _db.Parts.Select(p => p.Unit).Distinct().OrderBy(x => x).ToListAsync();
    }

    public List<string> Suppliers { get; set; } = new();
    public List<string> Makers { get; set; } = new();
    public List<string> Racks { get; set; } = new();
    public CodeSettings Code { get; set; } = new();
    public string CodeExample { get; set; } = "";
    /// <summary>Part original yang disalin saat membuat versi 2nd.</summary>
    public string? CopiedFrom { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id, int? from)
    {
        if (id.HasValue)
        {
            var p = await _db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p == null) return NotFound();
            Part = p;
        }
        else if (from.HasValue)
        {
            // "Buat part 2nd": salin data part original, part number + "-2ND".
            var o = await _db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == from);
            if (o == null) return NotFound();
            CopiedFrom = o.PartNumber;
            Part = new Part
            {
                PartNumber = o.OriginalPartNumber + Part.SecondSuffix, Name = o.Name, Specification = o.Specification,
                Category = o.Category, Brand = o.Brand, Supplier = o.Supplier, Machine = o.Machine, Location = o.Location,
                Unit = o.Unit, Currency = o.Currency, UnitPrice = o.UnitPrice, Condition = PartCondition.Second,
                IsReusable = true, Status = o.Status
            };
        }
        await LoadListsAsync();
        return Page();
    }

    /// <summary>Form yang gagal validasi tetap menampilkan gambar yang sudah tersimpan.</summary>
    private async Task ReloadImageAsync()
    {
        if (Part.Id != 0)
            Part.ImageFile = await _db.Parts.Where(p => p.Id == Part.Id).Select(p => p.ImageFile).FirstOrDefaultAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Goods code dikosongkan → dibuat otomatis (bila diaktifkan di Pengaturan).
        var settings = HttpContext.RequestServices.GetRequiredService<AppSettingsService>().Code;
        if (Part.Id == 0 && string.IsNullOrWhiteSpace(Part.PartNumber) && settings.AutoGenerateWhenEmpty)
        {
            var (code, error) = await HttpContext.RequestServices.GetRequiredService<PartCode>()
                .NextAsync(new CodeInput(Part.Brand, Part.Category, Part.Supplier));
            ModelState.Remove("Part.PartNumber");
            if (code != null) Part.PartNumber = code;
            else ModelState.AddModelError("Part.PartNumber", "Goods code otomatis gagal: " + error);
        }
        StockService.NormalizeSecond(Part);
        if (await _db.Parts.AnyAsync(p => p.PartNumber == Part.PartNumber && p.Id != Part.Id))
            ModelState.AddModelError("Part.PartNumber", "Part number sudah dipakai.");
        if (InitialStock < 0) ModelState.AddModelError(nameof(InitialStock), "Stok awal tidak boleh negatif.");
        if (Part.MaxStock > 0 && Part.MaxStock < Part.MinStock)
            ModelState.AddModelError("Part.MaxStock", "Max stock harus ≥ min stock (atau 0 bila tidak dipakai).");
        if (!ModelState.IsValid) { await ReloadImageAsync(); await LoadListsAsync(); return Page(); }

        // Gambar tidak wajib, tapi kalau diupload harus valid.
        string? newImage = null;
        if (Image != null && Image.Length > 0)
        {
            var (file, error) = await _images.SaveAsync(Image);
            if (error != null)
            {
                ModelState.AddModelError(nameof(Image), error);
                await ReloadImageAsync(); await LoadListsAsync();
                return Page();
            }
            newImage = file;
        }

        if (Part.Id == 0)
        {
            Part.ImageFile = newImage;
            Part.InitialStock = InitialStock;
            var created = await _stock.CreatePartAsync(Part, InitialStock);
            TempData["Ok"] = $"Part {created.PartNumber} ditambahkan."
                + (newImage == null ? " Gambar belum diupload — disarankan menambahkannya lewat Edit supaya part mudah dikenali." : "");
            TempData["OkLink"] = $"/Parts/Labels?ids={created.Id}";
            TempData["OkLinkText"] = "Cetak label QR";
            return RedirectToPage("Details", new { id = created.Id });
        }

        var e = await _db.Parts.FirstOrDefaultAsync(p => p.Id == Part.Id);
        if (e == null) { _images.Delete(newImage); return NotFound(); }
        if (newImage != null) { _images.Delete(e.ImageFile); e.ImageFile = newImage; }
        else if (RemoveImage) { _images.Delete(e.ImageFile); e.ImageFile = null; }
        var rates = await _stock.GetRatesAsync();
        e.PartNumber = Part.PartNumber; e.Name = Part.Name.Trim(); e.Specification = Part.Specification;
        e.Category = Part.Category?.Trim(); e.Brand = Part.Brand?.Trim(); e.Supplier = Part.Supplier?.Trim(); e.Machine = Part.Machine?.Trim();
        e.Location = Part.Location?.Trim(); e.Unit = Part.Unit.Trim(); e.Currency = Part.Currency;
        e.UnitPrice = Part.UnitPrice; e.UnitPriceUsd = StockService.ToUsd(Part.UnitPrice, Part.Currency, rates);
        e.MinStock = Part.MinStock; e.MaxStock = Part.MaxStock; e.IsReusable = Part.IsReusable; e.Condition = Part.Condition;
        e.Status = Part.Status; e.Remarks = Part.Remarks; e.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        TempData["Ok"] = $"Part {e.PartNumber} disimpan.";
        return RedirectToPage("Details", new { id = e.Id });
    }
}
