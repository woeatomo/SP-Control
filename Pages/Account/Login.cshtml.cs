using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Pages.Account;

public class LoginModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;
    public LoginModel(AppDbContext db, IPasswordHasher<AppUser> hasher) { _db = db; _hasher = hasher; }

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; set; }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? LocalRedirect("/") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var name = (Username ?? "").Trim();
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Username == name);
        var ok = u != null && u.IsActive &&
                 _hasher.VerifyHashedPassword(u, u.PasswordHash, Password ?? "") != PasswordVerificationResult.Failed;
        if (!ok)
        {
            await Task.Delay(700); // memperlambat percobaan tebak password
            Error = "Username atau password salah, atau akun tidak aktif.";
            Password = "";
            return Page();
        }
        u!.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Username),
            new(ClaimTypes.GivenName, u.FullName),
            new(ClaimTypes.Role, u.Role.ToString()),
            new("allsites", u.AllSites ? "1" : "0")
        };
        // Tablet peminta tetap login 30 hari supaya tidak perlu login ulang setiap shift.
        var props = new AuthenticationProperties();
        if (u.Role == UserRole.Requester) { props.IsPersistent = true; props.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30); props.AllowRefresh = true; }
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), props);
        if (u.Role == UserRole.Requester) return LocalRedirect("/Request");
        if (u.Role == UserRole.Technician && !(ReturnUrl ?? "").StartsWith("/Mtc", StringComparison.OrdinalIgnoreCase)) return LocalRedirect("/Mtc");
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage();
    }
}
