using SparePartControl.Models;

namespace SparePartControl.Services;

public record RackSignalResult(bool Connected, string Message);

/// <summary>
/// Titik sambung untuk versi berikutnya: saat admin menekan tombol "Location" di popup detail part,
/// lampu di rak berkedip / LCD rak menampilkan posisi part.
/// Buat implementasi baru (mis. HTTP ke ESP32, MQTT, Modbus) lalu daftarkan di Program.cs
/// menggantikan <see cref="NullRackLocator"/>. Halaman dan API tidak perlu diubah.
/// </summary>
public interface IRackLocator
{
    Task<RackSignalResult> SignalAsync(Part part, CancellationToken ct = default);
}

/// <summary>Implementasi sementara: belum ada perangkat rak, hanya mencatat permintaan di log.</summary>
public class NullRackLocator : IRackLocator
{
    private readonly ILogger<NullRackLocator> _log;
    public NullRackLocator(ILogger<NullRackLocator> log) => _log = log;

    public Task<RackSignalResult> SignalAsync(Part part, CancellationToken ct = default)
    {
        _log.LogInformation("Locate diminta untuk {PartNumber} di lokasi {Location}", part.PartNumber, part.Location);
        return Task.FromResult(new RackSignalResult(false, "Perangkat sinyal rak belum terhubung (fitur versi berikutnya)."));
    }
}
