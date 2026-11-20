using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Pages.Users;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;
    public IndexModel(AppDbContext db, IPasswordHasher<AppUser> hasher) { _db = db; _hasher = hasher; }

    public List<AppUser> Users { get; set; } = new();
    public List<Site> Sites { get; set; } = new();
    public Dictionary<int, HashSet<int>> Access { get; set; } = new();
    public int MyId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        Users = await _db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync();
        Sites = await _db.Sites.AsNoTracking().OrderBy(s => s.Code).ToListAsync();
        Access = (await _db.UserSites.AsNoTracking().ToListAsync()).GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.SiteId).ToHashSet());
    }

    private async Task SetSitesAsync(int userId, IEnumerable<int> siteIds)
    {
        _db.UserSites.RemoveRange(await _db.UserSites.Where(x => x.UserId == userId).ToListAsync());
        var valid = await _db.Sites.Select(s => s.Id).ToListAsync();
        foreach (var id in siteIds.Distinct().Where(valid.Contains)) _db.UserSites.Add(new UserSite { UserId = userId, SiteId = id });
    }

    private IActionResult Back(bool ok, string msg) { TempData[ok ? "Ok" : "Err"] = msg; return RedirectToPage(); }

    public async Task<IActionResult> OnPostCreateAsync(string username, string fullName, UserRole role, string password, bool allSites, List<int> sites)
    {
        if (!allSites && sites.Count == 0) return Back(false, "Pilih minimal satu site, atau centang Semua site.");
        username = (username ?? "").Trim();
        if (username.Length < 3 || username.Length > 50 || username.Any(char.IsWhiteSpace)) return Back(false, "Username 3–50 karakter tanpa spasi.");
        if ((password ?? "").Length < 8) return Back(false, "Password minimal 8 karakter.");
        if (await _db.Users.AnyAsync(u => u.Username == username)) return Back(false, $"Username '{username}' sudah dipakai.");
        var u = new AppUser { Username = username, FullName = string.IsNullOrWhiteSpace(fullName) ? username : fullName.Trim(), Role = role, AllSites = allSites };
        u.PasswordHash = _hasher.HashPassword(u, password!);
        _db.Users.Add(u);
        await _db.SaveChangesAsync();
        if (!allSites) { await SetSitesAsync(u.Id, sites); await _db.SaveChangesAsync(); }
        return Back(true, $"User {username} ({role}) dibuat.");
    }

    /// <summary>Jangan sampai tidak ada Admin aktif dengan akses semua site (pengelola sistem) tersisa.</summary>
    private async Task<bool> IsLastAdminAsync(AppUser u) =>
        u.Role == UserRole.Admin && u.AllSites && u.IsActive &&
        await _db.Users.CountAsync(x => x.Role == UserRole.Admin && x.AllSites && x.IsActive) <= 1;

    public async Task<IActionResult> OnPostAccessAsync(int id, bool allSites, List<int> sites)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound();
        if (u.Id == MyId) return Back(false, "Tidak bisa mengubah akses site akun sendiri.");
        if (!allSites && sites.Count == 0) return Back(false, "Pilih minimal satu site, atau centang Semua site.");
        if (!allSites && await IsLastAdminAsync(u)) return Back(false, "Harus ada minimal satu Admin aktif dengan akses semua site.");
        u.AllSites = allSites;
        await SetSitesAsync(u.Id, allSites ? Array.Empty<int>() : sites);
        await _db.SaveChangesAsync();
        return Back(true, $"Akses site {u.Username} disimpan.");
    }

    public async Task<IActionResult> OnPostRoleAsync(int id, UserRole role)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound();
        if (u.Id == MyId) return Back(false, "Tidak bisa mengubah role akun sendiri.");
        if (role != UserRole.Admin && await IsLastAdminAsync(u)) return Back(false, "Harus ada minimal satu Admin aktif dengan akses semua site.");
        u.Role = role;
        await _db.SaveChangesAsync();
        return Back(true, $"Role {u.Username} menjadi {role}.");
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound();
        if (u.Id == MyId) return Back(false, "Tidak bisa menonaktifkan akun sendiri.");
        if (u.IsActive && await IsLastAdminAsync(u)) return Back(false, "Harus ada minimal satu Admin aktif dengan akses semua site.");
        u.IsActive = !u.IsActive;
        await _db.SaveChangesAsync();
        return Back(true, $"User {u.Username} {(u.IsActive ? "diaktifkan" : "dinonaktifkan")}.");
    }

    public async Task<IActionResult> OnPostResetAsync(int id, string password)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound();
        if ((password ?? "").Length < 8) return Back(false, "Password minimal 8 karakter.");
        u.PasswordHash = _hasher.HashPassword(u, password!);
        await _db.SaveChangesAsync();
        return Back(true, $"Password {u.Username} direset.");
    }
}
