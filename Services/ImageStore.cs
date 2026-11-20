namespace SparePartControl.Services;

/// <summary>
/// Penyimpanan gambar spare part. File disimpan di luar wwwroot (default: App_Data/part-images)
/// sehingga hanya bisa diakses lewat endpoint yang wajib login.
/// </summary>
public class ImageStore
{
    public const long MaxBytes = 8 * 1024 * 1024;
    private readonly string _dir;

    public ImageStore(IWebHostEnvironment env, IConfiguration cfg)
    {
        var configured = cfg["ImageFolder"];
        _dir = string.IsNullOrWhiteSpace(configured) ? Path.Combine(env.ContentRootPath, "App_Data", "part-images") : configured;
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Kenali jenis gambar dari isi file (bukan dari nama file).</summary>
    private static string? DetectExtension(byte[] h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return ".webp";
        return null;
    }

    public async Task<(string? FileName, string? Error)> SaveAsync(IFormFile file)
    {
        if (file.Length == 0) return (null, "File gambar kosong.");
        if (file.Length > MaxBytes) return (null, "Ukuran gambar maksimal 8 MB.");
        var head = new byte[12];
        await using (var s = file.OpenReadStream())
        {
            var n = await s.ReadAsync(head.AsMemory(0, 12));
            Array.Resize(ref head, n);
        }
        var ext = DetectExtension(head);
        if (ext == null) return (null, "Format gambar harus JPG, PNG atau WebP.");
        var name = Guid.NewGuid().ToString("N") + ext;
        await using var fs = File.Create(Path.Combine(_dir, name));
        await file.CopyToAsync(fs);
        return (name, null);
    }

    public string? PathOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var p = Path.Combine(_dir, Path.GetFileName(fileName)); // GetFileName: cegah path traversal
        return File.Exists(p) ? p : null;
    }

    public void Delete(string? fileName)
    {
        var p = PathOf(fileName);
        if (p != null) try { File.Delete(p); } catch (IOException) { /* file sedang dipakai; biarkan */ }
    }

    public static string ContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg"
    };
}
