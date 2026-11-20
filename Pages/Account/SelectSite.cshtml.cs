using Microsoft.AspNetCore.Mvc.RazorPages;
using SparePartControl.Services;

namespace SparePartControl.Pages.Account;

public class SelectSiteModel : PageModel
{
    public SiteContext SiteCtx { get; }
    public SelectSiteModel(SiteContext site) => SiteCtx = site;
    public string ReturnUrl { get; set; } = "/";
    public void OnGet(string? returnUrl) =>
        ReturnUrl = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/";
}
