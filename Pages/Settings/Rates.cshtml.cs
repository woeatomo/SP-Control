using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Pages.Settings;

public class RatesModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly StockService _stock;
    public RatesModel(AppDbContext db, StockService stock) { _db = db; _stock = stock; }

    public List<ExchangeRate> Rates { get; set; } = new();

    public async Task OnGetAsync() => Rates = await _db.ExchangeRates.AsNoTracking().ToListAsync();

    public async Task<IActionResult> OnPostAsync(decimal sgd, decimal jpy, decimal idr)
    {
        if (sgd <= 0 || jpy <= 0 || idr <= 0)
        {
            TempData["Err"] = "Kurs harus lebih besar dari 0.";
            return RedirectToPage();
        }
        var map = new Dictionary<Currency, decimal> { [Currency.USD] = 1, [Currency.SGD] = sgd, [Currency.JPY] = jpy, [Currency.IDR] = idr };
        var existing = await _db.ExchangeRates.ToDictionaryAsync(r => r.Currency);
        foreach (var (cur, val) in map)
        {
            if (!existing.TryGetValue(cur, out var r)) { r = new ExchangeRate { Currency = cur }; _db.ExchangeRates.Add(r); }
            if (r.UnitsPerUsd != val) { r.UnitsPerUsd = val; r.UpdatedAt = DateTime.Now; }
        }
        await _db.SaveChangesAsync();
        var n = await _stock.RecalculateUsdAsync();
        TempData["Ok"] = $"Kurs disimpan. Harga USD {n} part di master data dihitung ulang.";
        return RedirectToPage();
    }
}
