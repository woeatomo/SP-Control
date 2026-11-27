using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SparePartControl.Services;

namespace SparePartControl.Pages.Request;

/// <summary>Halaman tablet peminta di ruang produksi / maintenance.</summary>
[Authorize(Policy = "Kiosk")]
public class IndexModel : PageModel
{
    public SiteContext Site { get; }
    public AppSettingsService Settings { get; }
    public IndexModel(SiteContext site, AppSettingsService settings) { Site = site; Settings = settings; }
    public void OnGet() { }
}
