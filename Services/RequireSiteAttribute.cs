using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;

namespace SparePartControl.Services;

/// <summary>
/// Halaman yang mengubah data (transaksi, part baru, import) harus berjalan di satu site, bukan "Semua site".
/// Bila halaman dibuka untuk part tertentu (mis. ?partId=5), site otomatis dipindah ke site part tersebut.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RequireSiteAttribute : Attribute, IAsyncPageFilter
{
    private readonly string[] _partParams;
    public RequireSiteAttribute(params string[] partIdParams) => _partParams = partIdParams;

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext ctx, PageHandlerExecutionDelegate next)
    {
        var http = ctx.HttpContext;
        var site = http.RequestServices.GetRequiredService<SiteContext>();
        if (!site.IsAll) { await next(); return; }

        var url = http.Request.Path + http.Request.QueryString;
        foreach (var p in _partParams)
        {
            var raw = http.Request.Query[p].FirstOrDefault() ?? ctx.RouteData.Values[p]?.ToString();
            if (!int.TryParse(raw, out var partId)) continue;
            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            var siteId = await db.Parts.Where(x => x.Id == partId).Select(x => (int?)x.SiteId).FirstOrDefaultAsync();
            if (siteId.HasValue)
            {
                SiteContext.Remember(http.Response, siteId.Value.ToString());
                ctx.Result = new LocalRedirectResult(url);
                return;
            }
        }
        ctx.Result = new RedirectToPageResult("/Account/SelectSite", new { returnUrl = url });
    }
}
