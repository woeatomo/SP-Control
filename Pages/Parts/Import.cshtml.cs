using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SparePartControl.Services;

namespace SparePartControl.Pages.Parts;

/// <summary>
/// Import master data dari file Excel MASTER STOCK (.xlsx / .xlsm) atau CSV.
/// Langkah 1: upload → dicek tanpa menyimpan (preview). Langkah 2: tombol "Simpan import".
/// </summary>
[RequireSite]
public class ImportModel : PageModel
{
    private readonly MasterImport _import;
    private readonly string _dir;
    public ImportModel(MasterImport import, IWebHostEnvironment env)
    {
        _import = import;
        _dir = Path.Combine(env.ContentRootPath, "App_Data", "imports");
        Directory.CreateDirectory(_dir);
    }

    [BindProperty] public bool SyncStock { get; set; }
    [BindProperty] public bool ImportRates { get; set; } = true;
    [BindProperty] public string? Token { get; set; }
    public string? FileName { get; set; }
    public ImportResult? Result { get; set; }
    public bool Committed { get; set; }
    public string? Error { get; set; }

    public void OnGet() { }

    public IActionResult OnGetTemplate()
    {
        var bytes = Csv.Build(ExcelMaster.ExportHeaders.Take(17).Where(h => h is not ("unit_USD" or "Status" or "StockValue")), new[]
        {
            new[] { "MaPC-BANDO-0001", "BANDO Belting STS150S8M 1000", "INJECTION", "ACTIVE", "Cabinet 9 / Drawer B", "BANDO",
                    "PT SINDO SEIKI ADITAMA", "IDR", "195000", "2", "2", "5", "2", "" }
        });
        return File(bytes, "text/csv", "template-master-stock.csv");
    }

    private static readonly string[] Allowed = { ".xlsx", ".xlsm", ".csv" };

    private async Task<(RawTable? parts, RawTable? rates, string? error)> ReadAsync(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await System.IO.File.ReadAllBytesAsync(path);
                // CSV dari Excel bisa UTF-8 atau ANSI (Windows-1252); coba UTF-8 dulu.
                string text;
                try { text = new UTF8Encoding(false, true).GetString(bytes); }
                catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
                return (ExcelMaster.ReadCsv(text), null, null);
            }
            await using var fs = System.IO.File.OpenRead(path);
            var (p, r) = ExcelMaster.ReadWorkbook(fs);
            return (p, r, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, null, "File tidak bisa dibaca sebagai Excel/CSV. Tutup file di Excel lalu coba lagi, atau simpan ulang sebagai .xlsx. (" + ex.GetType().Name + ")");
        }
    }

    /// <summary>Langkah 1: upload dan cek.</summary>
    public async Task<IActionResult> OnPostCheckAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0) { Error = "Pilih file terlebih dahulu."; return Page(); }
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!Allowed.Contains(ext)) { Error = "Format file harus .xlsx, .xlsm atau .csv (file .xls lama: simpan ulang sebagai .xlsx)."; return Page(); }
        if (file.Length > 30 * 1024 * 1024) { Error = "Ukuran file maksimal 30 MB."; return Page(); }

        foreach (var old in new DirectoryInfo(_dir).GetFiles().Where(f => f.CreationTime < DateTime.Now.AddDays(-1))) old.Delete();
        Token = Guid.NewGuid().ToString("N") + ext;
        await using (var fs = System.IO.File.Create(Path.Combine(_dir, Token))) await file.CopyToAsync(fs);
        FileName = Path.GetFileName(file.FileName);

        var (parts, rates, err) = await ReadAsync(Path.Combine(_dir, Token));
        if (err != null) { Error = err; Token = null; return Page(); }
        Result = await _import.RunAsync(parts, rates, new ImportOptions { DryRun = true, SyncStock = SyncStock, ImportRates = ImportRates });
        return Page();
    }

    /// <summary>Langkah 2: simpan.</summary>
    public async Task<IActionResult> OnPostCommitAsync(string? fileName)
    {
        if (Token == null || !Regex.IsMatch(Token, @"^[0-9a-f]{32}\.(xlsx|xlsm|csv)$")) return BadRequest();
        var path = Path.Combine(_dir, Token);
        if (!System.IO.File.Exists(path)) { Error = "File sementara sudah tidak ada. Upload ulang."; Token = null; return Page(); }
        FileName = fileName;
        var (parts, rates, err) = await ReadAsync(path);
        if (err != null) { Error = err; return Page(); }
        Result = await _import.RunAsync(parts, rates, new ImportOptions { DryRun = false, SyncStock = SyncStock, ImportRates = ImportRates });
        System.IO.File.Delete(path);
        Committed = true;
        Token = null;
        return Page();
    }
}
