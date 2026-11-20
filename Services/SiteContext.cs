using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Services;

/// <summary>
/// Site aktif untuk request ini. Dibaca dari cookie pilihan user, dibatasi oleh akses user.
/// AppDbContext memakai <see cref="FilterIds"/> sebagai global query filter.
/// </summary>
public class SiteContext
{
    public const string CookieName = "sp_site";
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IHttpContextAccessor _http;
    private bool _loaded;
    private List<Site> _allowed = new();
    private Site? _current;
    private bool _unrestricted, _allSites, _isAdmin;
    private int _totalActiveSites;

    public SiteContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor http) { _options = options; _http = http; }

    private void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) { _unrestricted = true; return; }

        using var db = new AppDbContext(_options); // tanpa filter site
        var id = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var n) ? n : 0;
        var u = db.Users.AsNoTracking().FirstOrDefault(x => x.Id == id);
        var active = db.Sites.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Code).ToList();
        _totalActiveSites = active.Count;
        if (u == null) return; // filter kosong: tidak melihat apa pun
        _allSites = u.AllSites;
        _isAdmin = u.Role == UserRole.Admin;
        if (u.AllSites) _allowed = active;
        else
        {
            var ids = db.UserSites.AsNoTracking().Where(x => x.UserId == id).Select(x => x.SiteId).ToHashSet();
            _allowed = active.Where(s => ids.Contains(s.Id)).ToList();
        }

        var pick = _http.HttpContext!.Request.Cookies[CookieName];
        if (pick == "all" && _allowed.Count > 1) _current = null;
        else _current = _allowed.FirstOrDefault(s => s.Id.ToString() == pick) ?? _allowed.FirstOrDefault();
    }

    /// <summary>Tidak ada user login (startup / seed) → tidak ada batasan site.</summary>
    public bool Unrestricted { get { Load(); return _unrestricted; } }
    public IReadOnlyList<Site> Allowed { get { Load(); return _allowed; } }
    /// <summary>Site aktif; null = "Semua site" (mode lihat gabungan).</summary>
    public Site? Current { get { Load(); return _current; } }
    public bool IsAll { get { Load(); return !_unrestricted && _current == null; } }
    public bool HasMultiple { get { Load(); return _allowed.Count > 1; } }
    /// <summary>Lebih dari satu site aktif di sistem (kolom/label site ditampilkan).</summary>
    public bool MultiSiteSystem { get { Load(); return _totalActiveSites > 1; } }
    /// <summary>Admin dengan akses semua site: kelola site, user, kurs dan pengaturan.</summary>
    public bool IsSuperAdmin { get { Load(); return _isAdmin && _allSites; } }

    /// <summary>Kode site untuk ditampilkan di tabel (mode "Semua site").</summary>
    public string CodeOf(int siteId) { Load(); return _allowed.FirstOrDefault(s => s.Id == siteId)?.Code ?? "#" + siteId; }

    public int[]? FilterIds
    {
        get
        {
            Load();
            if (_unrestricted) return null;
            return _current != null ? new[] { _current.Id } : _allowed.Select(s => s.Id).ToArray();
        }
    }

    public static void Remember(HttpResponse resp, string value) =>
        resp.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, Expires = DateTimeOffset.Now.AddYears(1)
        });
}
