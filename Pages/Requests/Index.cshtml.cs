using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Pages.Requests;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "open";
    public List<PartRequest> Items { get; set; } = new();
    public Dictionary<string, int> Counts { get; set; } = new();

    public async Task OnGetAsync()
    {
        var q = _db.PartRequests.AsNoTracking();
        Counts = (await q.GroupBy(r => r.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key.ToString(), x => x.N);
        q = Tab switch
        {
            "done" => q.Where(r => r.Status == RequestStatus.Done),
            "rejected" => q.Where(r => r.Status == RequestStatus.Rejected),
            "all" => q,
            _ => q.Where(r => r.Status == RequestStatus.New || r.Status == RequestStatus.Processing || r.Status == RequestStatus.Ready)
        };
        Items = await q.Include(r => r.Lines).ThenInclude(l => l.Part)
            .OrderBy(r => r.Status == RequestStatus.New ? 0 : r.Status == RequestStatus.Processing ? 1 : r.Status == RequestStatus.Ready ? 2 : 3)
            .ThenByDescending(r => r.Urgency).ThenByDescending(r => r.Id).Take(300).ToListAsync();
    }

    public int C(params RequestStatus[] s) => s.Sum(x => Counts.TryGetValue(x.ToString(), out var n) ? n : 0);
}
