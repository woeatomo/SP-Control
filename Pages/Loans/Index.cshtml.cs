using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Loans;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly StockService _stock;
    public IndexModel(AppDbContext db, StockService stock) { _db = db; _stock = stock; }

    [BindProperty(SupportsGet = true)] public string Show { get; set; } = "open";
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public List<Loan> Items { get; set; } = new();

    public async Task OnGetAsync()
    {
        var q = _db.Loans.AsNoTracking().Include(l => l.Part).AsQueryable();
        if (Show != "all") q = q.Where(l => l.Status == LoanStatus.Open);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var k = $"%{Q.Trim()}%";
            q = q.Where(l => EF.Functions.Like(l.Borrower, k) || EF.Functions.Like(l.Part.PartNumber, k)
                             || EF.Functions.Like(l.Part.Name, k) || EF.Functions.Like(l.DocNo, k));
        }
        Items = await q.OrderBy(l => l.Status).ThenByDescending(l => l.LoanDate).Take(500).ToListAsync();
    }

    public async Task<IActionResult> OnPostReturnAsync(int id, decimal returnQty, decimal usedQty, string? notes)
    {
        if (!User.IsInRole("Admin")) return Forbid();
        var r = await _stock.ReturnLoanAsync(id, returnQty, usedQty, DateTime.Now, notes);
        TempData[r.Ok ? "Ok" : "Err"] = r.Message;
        return RedirectToPage(new { Show, Q });
    }
}
