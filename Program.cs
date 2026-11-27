using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using SparePartControl.Data;
using SparePartControl.Models;
using SparePartControl.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(o =>
{
    // Viewer hanya boleh melihat; halaman berikut khusus Admin.
    o.Conventions.AuthorizePage("/Entry", "Admin");
    o.Conventions.AuthorizePage("/Parts/Edit", "Admin");
    o.Conventions.AuthorizePage("/Parts/Import", "Admin");
    // Pengaturan, kurs, site dan user: hanya admin dengan akses semua site.
    o.Conventions.AuthorizeFolder("/Settings", "SuperAdmin");
    o.Conventions.AuthorizeFolder("/Users", "SuperAdmin");
    o.Conventions.AllowAnonymousToPage("/Account/Login");
    o.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=sparepart.db"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SiteContext>();
builder.Services.AddScoped<StockService>();
builder.Services.AddSingleton<AppSettingsService>();
builder.Services.AddScoped<PartCode>();
builder.Services.AddScoped<MasterImport>();
builder.Services.AddSingleton<ImageStore>();
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
// Versi berikutnya: ganti NullRackLocator dengan implementasi lampu/LCD rak.
builder.Services.AddSingleton<IRackLocator, NullRackLocator>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.SlidingExpiration = true;
    o.Cookie.Name = "SparePartControl.Auth";
    o.Events.OnValidatePrincipal = async ctx =>
    {
        // User yang dinonaktifkan / role-nya diubah langsung kehilangan sesi lamanya.
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var id = int.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var n) ? n : 0;
        var role = ctx.Principal?.FindFirstValue(ClaimTypes.Role);
        var all = ctx.Principal?.FindFirstValue("allsites");
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (u == null || !u.IsActive || u.Role.ToString() != role || (u.AllSites ? "1" : "0") != all) ctx.RejectPrincipal();
    };
});
builder.Services.AddAuthorization(o =>
{
    // Semua halaman & API wajib login kecuali yang ditandai AllowAnonymous.
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    o.AddPolicy("Admin", p => p.RequireRole(nameof(UserRole.Admin)));
    o.AddPolicy("SuperAdmin", p => p.RequireRole(nameof(UserRole.Admin)).RequireClaim("allsites", "1"));
});

var app = builder.Build();

// Format angka dibuat tetap (titik = desimal) supaya input form konsisten di semua server.
var culture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en-US")
    .AddSupportedCultures("en-US").AddSupportedUICultures("en-US"));

await SeedData.InitializeAsync(app.Services, app.Configuration.GetValue("SeedSampleData", false));

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

// ---- API kecil untuk scan QR / pencarian part / popup detail ----
// Pindah site aktif (pilihan disimpan di cookie; akses tetap dicek SiteContext).
app.MapGet("/site/switch", (string? id, string? returnUrl, HttpContext http, SiteContext site) =>
{
    if (id == "all" ? site.HasMultiple : site.Allowed.Any(s => s.Id.ToString() == id))
        SiteContext.Remember(http.Response, id!);
    return Results.LocalRedirect(!string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/");
});

static object PartDto(Part p) => new
{
    p.Id, p.SiteId, SiteCode = p.Site?.Code, p.PartNumber, p.Name, p.Specification, p.Category, p.Brand, p.Supplier, p.Machine, p.Unit,
    p.StockQty, p.MinStock, p.MaxStock, p.InitialStock, p.Location, p.IsReusable, p.Remarks,
    Level = p.Level.ToString().ToUpperInvariant(), p.SuggestedOrder,
    Condition = p.Condition.ToString(), Status = p.Status.ToString(),
    Currency = p.Currency.ToString(), p.UnitPrice, p.UnitPriceUsd, p.TotalValueUsd, p.ImageUrl
};

app.MapGet("/api/parts/lookup", async (string? code, AppDbContext db) =>
{
    code = (code ?? "").Trim();
    if (code.Length == 0) return Results.NotFound();
    var p = await db.Parts.AsNoTracking().Include(x => x.Site).FirstOrDefaultAsync(x => x.PartNumber == code);
    return p == null ? Results.NotFound() : Results.Json(PartDto(p));
});

app.MapGet("/api/parts/{id:int}", async (int id, AppDbContext db) =>
{
    var p = await db.Parts.AsNoTracking().Include(x => x.Site).FirstOrDefaultAsync(x => x.Id == id);
    return p == null ? Results.NotFound() : Results.Json(PartDto(p));
});

app.MapGet("/api/parts/search", async (string? q, AppDbContext db) =>
{
    q = (q ?? "").Trim();
    if (q.Length == 0) return Results.Json(Array.Empty<object>());
    var like = $"%{q}%";
    var list = await db.Parts.AsNoTracking().Include(x => x.Site)
        .Where(p => EF.Functions.Like(p.PartNumber, like) || EF.Functions.Like(p.Name, like)
                    || EF.Functions.Like(p.Specification ?? "", like))
        .OrderBy(p => p.PartNumber).Take(10).ToListAsync();
    return Results.Json(list.Select(PartDto));
});

// Goods code berikutnya untuk maker tertentu (tombol "Generate" di form part).
app.MapGet("/api/parts/nextcode", async (string? maker, string? model, string? supp, PartCode codes) =>
{
    var (code, error) = await codes.NextAsync(new CodeInput(maker, model, supp));
    return code == null ? Results.BadRequest(new { error }) : Results.Json(new { code });
}).RequireAuthorization("Admin");

// Gambar spare part (wajib login, karena file disimpan di luar wwwroot).
app.MapGet("/parts/{id:int}/image", async (int id, HttpContext http, AppDbContext db, ImageStore images) =>
{
    var file = await db.Parts.AsNoTracking().Where(x => x.Id == id).Select(x => x.ImageFile).FirstOrDefaultAsync();
    var path = images.PathOf(file);
    if (path == null) return Results.NotFound();
    http.Response.Headers.CacheControl = "private, max-age=604800";
    return Results.File(path, ImageStore.ContentType(path));
});

// Pencarian pintar untuk halaman Cari Part & kotak cari di menu atas.
// f: stock (ada stok) | restock (EMPTY/LOW/CRITICAL) | reusable | second. Hasil diurutkan menurut relevansi.
app.MapGet("/api/parts/find", async (string? q, string? f, string? model, int? take, AppDbContext db) =>
{
    var query = db.Parts.AsNoTracking().Include(x => x.Site).AsQueryable();
    query = PartSearch.Apply(query, q);
    query = f switch
    {
        "stock" => query.Where(p => p.StockQty > 0),
        "restock" => query.Where(p => p.StockQty <= p.MinStock),
        "reusable" => query.Where(p => p.IsReusable),
        "second" => query.Where(p => p.Condition == PartCondition.Second),
        _ => query
    };
    if (!string.IsNullOrWhiteSpace(model)) query = query.Where(p => p.Category == model);
    if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(f) && string.IsNullOrWhiteSpace(model))
        return Results.Json(new { total = 0, items = Array.Empty<object>() });
    var total = await query.CountAsync();
    // Ambil kandidat secukupnya lalu urutkan menurut skor relevansi.
    var candidates = await query.OrderBy(p => p.PartNumber).Take(400).ToListAsync();
    var n = Math.Clamp(take ?? 40, 1, 100);
    var items = candidates
        .Select(p => new { p, s = PartSearch.Score(p, q) })
        .OrderByDescending(x => x.s).ThenByDescending(x => x.p.StockQty > 0).ThenBy(x => x.p.PartNumber)
        .Take(n).Select(x => PartDto(x.p)).ToList();
    return Results.Json(new { total, items });
});

// Tombol "Location": minta rak memberi sinyal posisi part (lampu berkedip / LCD). Khusus Admin.
app.MapPost("/api/parts/{id:int}/locate", async (int id, HttpRequest req, AppDbContext db, IRackLocator rack, CancellationToken ct) =>
{
    // Header khusus ini tidak bisa dikirim form lintas-situs → mencegah CSRF.
    if (req.Headers["X-Requested-With"] != "XMLHttpRequest") return Results.BadRequest();
    var p = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    if (p == null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(p.Location))
        return Results.Json(new { location = "", connected = false, message = "Lokasi part ini belum diisi di master data." });
    var r = await rack.SignalAsync(p, ct);
    return Results.Json(new { location = p.Location, connected = r.Connected, message = r.Message });
}).RequireAuthorization("Admin");

// Gambar QR (PNG). Isi QR = part number, sehingga bisa dibaca scanner USB maupun kamera.
app.MapGet("/qr", (string? c, int? s) =>
{
    if (string.IsNullOrWhiteSpace(c) || c.Length > 200) return Results.BadRequest();
    using var gen = new QRCodeGenerator();
    using var data = gen.CreateQrCode(c.Trim(), QRCodeGenerator.ECCLevel.M);
    var png = new PngByteQRCode(data).GetGraphic(Math.Clamp(s ?? 6, 2, 20));
    return Results.File(png, "image/png");
});

app.Run();
