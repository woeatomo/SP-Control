using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Pages.Account;

public class PasswordModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;
    public PasswordModel(AppDbContext db, IPasswordHasher<AppUser> hasher) { _db = db; _hasher = hasher; }

    public string? Error { get; set; }
    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string current, string newPassword, string confirm)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var u = await _db.Users.FirstAsync(x => x.Id == id);
        if (_hasher.VerifyHashedPassword(u, u.PasswordHash, current ?? "") == PasswordVerificationResult.Failed)
            Error = "Password lama salah.";
        else if ((newPassword ?? "").Length < 8) Error = "Password baru minimal 8 karakter.";
        else if (newPassword != confirm) Error = "Konfirmasi password tidak sama.";
        if (Error != null) return Page();
        u.PasswordHash = _hasher.HashPassword(u, newPassword!);
        await _db.SaveChangesAsync();
        TempData["Ok"] = "Password berhasil diganti.";
        return Redirect("/");
    }
}
