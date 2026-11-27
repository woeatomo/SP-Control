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
    o.Conventions.AuthorizeFolder("/Requests", "Admin");
    o.Conventions.AuthorizePage("/Mtc/Activity", "Mtc");
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
builder.Services.AddScoped<RequestService>();
builder.Services.AddScoped<MaintenanceService>();
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
    o.AddPolicy("Kiosk", p => p.RequireRole(nameof(UserRole.Requester), nameof(UserRole.Admin), nameof(UserRole.Technician)));
    // Isi daily report / plan / master mesin: teknisi maintenance & admin.
    o.AddPolicy("Mtc", p => p.RequireRole(nameof(UserRole.Technician), nameof(UserRole.Admin)));
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
// Akun tablet peminta (Requester) hanya boleh membuka halaman permintaan.
app.Use(async (ctx, next) =>
{
    if (ctx.User.IsInRole(nameof(UserRole.Requester)))
    {
        var path = ctx.Request.Path.Value ?? "/";
        string[] allowed = { "/Request", "/api/request/", "/Account/Login", "/Account/AccessDenied", "/qr", "/lib/", "/css/", "/js/", "/favicon" };
        var isImage = System.Text.RegularExpressions.Regex.IsMatch(path, @"^/parts/\d+/image$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!isImage && !allowed.Any(a => path.Equals(a, StringComparison.OrdinalIgnoreCase) || path.StartsWith(a.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)
                                          || (a.EndsWith('/') && path.StartsWith(a, StringComparison.OrdinalIgnoreCase))))
        {
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) { ctx.Response.StatusCode = 403; return; }
            ctx.Response.Redirect("/Request");
            return;
        }
    }
    // Teknisi maintenance: modul maintenance + cari part + minta part (tanpa transaksi / harga / pengaturan).
    if (ctx.User.IsInRole(nameof(UserRole.Technician)))
    {
        var path = ctx.Request.Path.Value ?? "/";
        string[] allowed = { "/Mtc", "/api/mtc/", "/Search", "/Request", "/api/request/", "/api/parts/find", "/api/parts/lookup", "/api/parts/search",
                             "/Account/", "/qr", "/lib/", "/css/", "/js/", "/favicon", "/site/switch" };
        var ok = path == "/" || allowed.Any(a => path.Equals(a, StringComparison.OrdinalIgnoreCase)
                     || path.StartsWith(a.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) || (a.EndsWith('/') && path.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
                 || System.Text.RegularExpressions.Regex.IsMatch(path, @"^/(api/)?parts/\d+(/image)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (path == "/") { ctx.Response.Redirect("/Mtc"); return; }
        if (!ok)
        {
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) { ctx.Response.StatusCode = 403; return; }
            ctx.Response.Redirect("/Mtc");
            return;
        }
    }
    await next();
});
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

// ================= Permintaan part (tablet peminta) =================
static bool IsAjax(HttpRequest r) => r.Headers["X-Requested-With"] == "XMLHttpRequest"; // cegah CSRF lintas situs

// Katalog untuk tablet: tanpa harga/nilai. q kosong → part yang paling sering diminta / dipakai.
app.MapGet("/api/request/catalog", async (string? q, AppDbContext db) =>
{
    List<Part> parts;
    if (string.IsNullOrWhiteSpace(q))
    {
        var since = DateTime.Today.AddDays(-90);
        var popular = await db.Transactions.AsNoTracking().Where(t => t.Type == TxType.Issue && t.Date >= since)
            .GroupBy(t => t.PartId).Select(g => new { g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(12).Select(x => x.Key).ToListAsync();
        parts = await db.Parts.AsNoTracking().Where(p => popular.Contains(p.Id)).ToListAsync();
        parts = parts.OrderBy(p => popular.IndexOf(p.Id)).ToList();
        if (parts.Count < 12)
            parts.AddRange(await db.Parts.AsNoTracking().Where(p => !popular.Contains(p.Id) && p.Status == PartStatus.Active && p.StockQty > 0)
                .OrderBy(p => p.Name).Take(12 - parts.Count).ToListAsync());
    }
    else
    {
        parts = await PartSearch.FuzzyAsync(db.Parts.AsNoTracking(), q, 24);
    }
    return Results.Json(parts.Select(p => new
    {
        p.Id, p.PartNumber, p.Name, p.Specification, p.Unit, p.ImageUrl, p.Category,
        Available = p.StockQty > 0, Low = p.StockQty > 0 && p.StockQty <= p.MinStock
    }));
}).RequireAuthorization("Kiosk");

app.MapPost("/api/request/submit", async (HttpRequest http, NewRequest body, RequestService svc) =>
{
    if (!IsAjax(http)) return Results.BadRequest();
    var (req, error) = await svc.CreateAsync(body, http.HttpContext.User.Identity?.Name);
    return req == null ? Results.BadRequest(new { error }) : Results.Json(new { req.Id, req.ReqNo });
}).RequireAuthorization("Kiosk");

// Papan status di tablet: permintaan 24 jam terakhir di site ini.
app.MapGet("/api/request/board", async (AppDbContext db) =>
{
    var since = DateTime.Now.AddHours(-24);
    var list = await db.PartRequests.AsNoTracking().Where(r => r.CreatedAt >= since || r.Status == RequestStatus.New || r.Status == RequestStatus.Processing || r.Status == RequestStatus.Ready)
        .OrderByDescending(r => r.Id).Take(30)
        .Select(r => new { r.Id, r.ReqNo, r.RequesterName, r.Line, r.Status, r.CreatedAt, r.UpdatedAt, r.ResponseNote, r.Urgency, Items = r.Lines.Count }).ToListAsync();
    return Results.Json(list.Select(r => new
    {
        r.Id, r.ReqNo, r.RequesterName, r.Line, Status = r.Status.ToString(), StatusText = RequestService.StatusText(r.Status),
        r.CreatedAt, r.UpdatedAt, r.ResponseNote, Urgent = r.Urgency == RequestUrgency.Urgent, r.Items
    }));
}).RequireAuthorization("Kiosk");

// Notifikasi untuk PC ruang sparepart: permintaan yang belum selesai.
app.MapGet("/api/requests/poll", async (AppDbContext db, SiteContext site) =>
{
    var open = await db.PartRequests.AsNoTracking()
        .Where(r => r.Status == RequestStatus.New || r.Status == RequestStatus.Processing || r.Status == RequestStatus.Ready)
        .OrderByDescending(r => r.Id).Take(50)
        .Select(r => new { r.Id, r.SiteId, r.ReqNo, r.RequesterName, r.Line, r.Status, r.Urgency, r.CreatedAt, r.Source, Items = r.Lines.Count }).ToListAsync();
    return Results.Json(new
    {
        newCount = open.Count(r => r.Status == RequestStatus.New),
        openCount = open.Count,
        items = open.Select(r => new
        {
            r.Id, r.ReqNo, r.RequesterName, r.Line, Status = r.Status.ToString(), StatusText = RequestService.StatusText(r.Status),
            Urgent = r.Urgency == RequestUrgency.Urgent, r.CreatedAt, r.Items, Site = site.MultiSiteSystem ? site.CodeOf(r.SiteId) : null,
            Usage = r.Source == RequestSource.DailyReport
        })
    });
}).RequireAuthorization("Admin");

app.MapGet("/api/requests/{id:int}", async (int id, RequestService svc) =>
    await svc.DetailAsync(id) is { } d ? Results.Json(d) : Results.NotFound()).RequireAuthorization("Admin");

app.MapPost("/api/requests/{id:int}/status", async (int id, HttpRequest http, StatusBody body, RequestService svc) =>
{
    if (!IsAjax(http)) return Results.BadRequest();
    if (!Enum.TryParse<RequestStatus>(body.Status, true, out var st) || st == RequestStatus.Done) return Results.BadRequest(new { error = "Status tidak valid." });
    var err = await svc.SetStatusAsync(id, st, body.Note, http.HttpContext.User.Identity?.Name);
    return err == null ? Results.Ok() : Results.BadRequest(new { error = err });
}).RequireAuthorization("Admin");

app.MapPost("/api/requests/{id:int}/lines/{lineId:int}/link", async (int id, int lineId, HttpRequest http, LinkBody body, RequestService svc) =>
{
    if (!IsAjax(http)) return Results.BadRequest();
    var err = await svc.LinkLineAsync(id, lineId, body.PartId);
    return err == null ? Results.Ok() : Results.BadRequest(new { error = err });
}).RequireAuthorization("Admin");

// Kandidat transaksi Issued untuk ditautkan ke aktivitas daily report (dihitung ulang saat form berubah).
app.MapGet("/api/mtc/candidates", async (DateTime date, int? machineId, string? techs, int? activityId, AppDbContext db, MaintenanceService mtc, SiteContext site) =>
{
    if (site.Current == null) return Results.BadRequest();
    var a = new MaintenanceActivity
    {
        Id = activityId ?? 0, SiteId = site.Current.Id, Date = date.Date, Technicians = techs, MachineId = machineId,
        Machine = machineId.HasValue ? await db.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == machineId) : null
    };
    return Results.Json(await mtc.CandidatesAsync(a));
}).RequireAuthorization("Mtc");

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

record StatusBody(string? Status, string? Note);
record LinkBody(int? PartId);
