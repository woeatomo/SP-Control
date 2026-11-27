using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;

namespace SparePartControl.Pages;

/// <summary>Halaman Cari Part: pencarian cepat untuk teknisi (hasil berupa kartu, langsung saat mengetik).</summary>
public class SearchModel : PageModel
{
    private readonly AppDbContext _db;
    public SearchModel(AppDbContext db) => _db = db;

    public List<string> Models { get; set; } = new();
    public int TotalParts { get; set; }

    public async Task OnGetAsync()
    {
        Models = await _db.Parts.Where(p => p.Category != null && p.Category != "").Select(p => p.Category!).Distinct().OrderBy(x => x).ToListAsync();
        TotalParts = await _db.Parts.CountAsync();
    }
}
