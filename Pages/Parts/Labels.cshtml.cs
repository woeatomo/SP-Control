using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Parts;

/// <summary>
/// Pembuat label QR untuk dicetak dan ditempel di part, wadah, atau drawer.
/// Sumber part: ?ids= (pilihan), ?doc= (dokumen Input / barang datang), atau ?all=1 + filter master data.
/// </summary>
public class LabelsModel : PageModel
{
    private readonly AppDbContext _db;
    public LabelsModel(AppDbContext db) => _db = db;

    public const int MaxLabels = 3000;

    [BindProperty(SupportsGet = true)] public PartFilter F { get; set; } = new();
    [BindProperty(SupportsGet = true, Name = "ids")] public List<int> Ids { get; set; } = new();
    [BindProperty(SupportsGet = true, Name = "doc")] public string? Doc { get; set; }
    [BindProperty(SupportsGet = true, Name = "all")] public bool All { get; set; }
    /// <summary>s = kecil (part), m = sedang (wadah), l = besar (drawer / rak).</summary>
    [BindProperty(SupportsGet = true, Name = "size")] public string Size { get; set; } = "m";
    [BindProperty(SupportsGet = true, Name = "copies")] public int Copies { get; set; } = 1;
    /// <summary>Hanya untuk ?doc=: satu label per qty yang diterima.</summary>
    [BindProperty(SupportsGet = true, Name = "perQty")] public bool PerQty { get; set; }

    public List<(Part Part, int Copies)> Items { get; set; } = new();
    public int TotalLabels => Items.Sum(i => i.Copies);
    public bool Truncated { get; set; }
    public bool HasSource => Ids.Count > 0 || !string.IsNullOrWhiteSpace(Doc) || All;

    public async Task OnGetAsync()
    {
        if (Size is not ("s" or "m" or "l")) Size = "m";
        Copies = Math.Clamp(Copies, 1, 100);
        var received = new Dictionary<int, decimal>();
        List<Part> parts;

        if (!string.IsNullOrWhiteSpace(Doc))
        {
            received = (await _db.Transactions.AsNoTracking().Where(t => t.DocNo == Doc && t.Qty > 0)
                    .Select(t => new { t.PartId, t.Qty }).ToListAsync())
                .GroupBy(t => t.PartId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            var pid = received.Keys.ToList();
            parts = await _db.Parts.AsNoTracking().Where(p => pid.Contains(p.Id)).OrderBy(p => p.PartNumber).ToListAsync();
        }
        else if (Ids.Count > 0)
            parts = await _db.Parts.AsNoTracking().Where(p => Ids.Contains(p.Id)).OrderBy(p => p.PartNumber).ToListAsync();
        else if (All)
            parts = await F.Apply(_db.Parts.AsNoTracking()).ToListAsync();
        else
            parts = new();

        var total = 0;
        foreach (var p in parts)
        {
            var n = PerQty && received.TryGetValue(p.Id, out var q) ? Math.Clamp((int)Math.Ceiling(q), 1, 500) : Copies;
            if (total + n > MaxLabels) { Truncated = true; break; }
            Items.Add((p, n));
            total += n;
        }
    }
}
