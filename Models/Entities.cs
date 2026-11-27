using System.ComponentModel.DataAnnotations;

namespace SparePartControl.Models;

public enum Currency { USD, SGD, JPY, IDR }
public enum PartStatus { Active, NonActive }
public enum PartCondition { New, Second }
public enum TxType { In, Issue, LoanOut, LoanReturn, Adjustment }
public enum LoanStatus { Open, Closed }
public enum StockCheckStatus { Open, Closed }
/// <summary>Requester = akun tablet peminta (hanya bisa membuat permintaan part).</summary>
public enum UserRole { Admin, Viewer, Requester, Technician }
public enum RequestSource { Tablet, DailyReport }
public enum Shift { A, B }
public enum ActivityType { Breakdown, Corrective, Preventive, Inspection, Improvement, Other }
public enum ActivityStatus { Done, Continue, WaitingPart }
public enum RequestStatus { New, Processing, Ready, Done, Rejected }
public enum RequestUrgency { Normal, Urgent }

/// <summary>
/// Status stok, sama dengan kolom Status di Excel MASTER STOCK:
/// LOW = di bawah min, CRITICAL = sama dengan min, OVERSTOCK = di atas max, selain itu NORMAL.
/// EMPTY = stok 0 (habis).
/// </summary>
public enum StockLevel { Empty, Low, Critical, Normal, Overstock }

/// <summary>Site / section spare part control. Master part, stok, transaksi, loan dan stock checking dipisah per site.</summary>
public class Site
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Kode site wajib diisi"), StringLength(10), Display(Name = "Kode Site")]
    public string Code { get; set; } = "";
    [Required(ErrorMessage = "Nama site wajib diisi"), StringLength(100), Display(Name = "Nama Site / Section")]
    public string Name { get; set; } = "";
    /// <summary>Isi {PREFIX} khusus site ini (kosong = pakai prefix umum di Pengaturan).</summary>
    [StringLength(20), Display(Name = "Prefix Goods Code")]
    public string? CodePrefix { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Akses user ke site tertentu (dipakai bila user tidak punya akses semua site).</summary>
public class UserSite
{
    public int UserId { get; set; }
    public int SiteId { get; set; }
}

/// <summary>Master data spare part.</summary>
public class Part
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }

    [Required(ErrorMessage = "Goods code wajib diisi"), StringLength(60)]
    [Display(Name = "Goods Code"), DisplayFormat(ConvertEmptyStringToNull = false)]
    public string PartNumber { get; set; } = "";

    [Required(ErrorMessage = "Goods name wajib diisi"), StringLength(200)]
    [Display(Name = "Goods Name")]
    public string Name { get; set; } = "";

    [StringLength(300), Display(Name = "Spesifikasi")]
    public string? Specification { get; set; }

    /// <summary>Kolom "Model" di Excel (ELECTRICAL, INJECTION, ROBOT, BLOW M/C, …).</summary>
    [StringLength(80), Display(Name = "Model")]
    public string? Category { get; set; }

    [StringLength(80), Display(Name = "Maker")]
    public string? Brand { get; set; }

    [StringLength(120), Display(Name = "Supplier")]
    public string? Supplier { get; set; }

    [StringLength(120), Display(Name = "Mesin / Line")]
    public string? Machine { get; set; }

    [StringLength(100), Display(Name = "Rack")]
    public string? Location { get; set; }

    [Required, StringLength(20), Display(Name = "Satuan")]
    public string Unit { get; set; } = "pcs";

    [Display(Name = "Mata Uang")]
    public Currency Currency { get; set; } = Currency.IDR;

    [Range(0, 1e12, ErrorMessage = "Harga tidak boleh negatif"), Display(Name = "Harga Satuan")]
    public decimal UnitPrice { get; set; }

    /// <summary>Harga satuan setelah dikonversi ke USD (dihitung otomatis dari kurs).</summary>
    [Display(Name = "Harga Satuan (USD)")]
    public decimal UnitPriceUsd { get; set; }

    [Display(Name = "Stok")]
    public decimal StockQty { get; set; }

    [Range(0, 1e9, ErrorMessage = "Min stock tidak boleh negatif"), Display(Name = "Min Stock")]
    public decimal MinStock { get; set; }

    /// <summary>Batas atas; stok di atasnya = OVERSTOCK. 0 = tidak dipakai.</summary>
    [Range(0, 1e9, ErrorMessage = "Max stock tidak boleh negatif"), Display(Name = "Max Stock")]
    public decimal MaxStock { get; set; }

    /// <summary>Qty awal saat part didaftarkan (informasi, sama dengan kolom initialStock di Excel).</summary>
    [Display(Name = "Initial Stock")]
    public decimal InitialStock { get; set; }

    [Display(Name = "Reusable")]
    public bool IsReusable { get; set; }

    [Display(Name = "Kondisi")]
    public PartCondition Condition { get; set; } = PartCondition.New;

    [Display(Name = "Stock Category")]
    public PartStatus Status { get; set; } = PartStatus.Active;

    [StringLength(500), Display(Name = "Remark")]
    public string? Remarks { get; set; }

    /// <summary>Nama file gambar part (opsional), disimpan oleh ImageStore.</summary>
    [StringLength(60)] public string? ImageFile { get; set; }
    public bool HasImage => !string.IsNullOrEmpty(ImageFile);
    /// <summary>URL gambar; nama file ikut di query supaya cache browser berganti saat gambar diganti.</summary>
    public string? ImageUrl => HasImage ? $"/parts/{Id}/image?v={ImageFile![..8]}" : null;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public decimal TotalValueUsd => StockQty * UnitPriceUsd;
    public StockLevel Level => LevelOf(StockQty, MinStock, MaxStock);
    /// <summary>Perlu perhatian (muncul di notifikasi dashboard): EMPTY, LOW, CRITICAL.</summary>
    public bool NeedsRestock => Level is StockLevel.Empty or StockLevel.Low or StockLevel.Critical;
    /// <summary>Saran qty order: sampai Max (atau Min bila Max kosong).</summary>
    public decimal SuggestedOrder => Math.Max(0, (MaxStock > 0 ? MaxStock : MinStock) - StockQty);

    public static StockLevel LevelOf(decimal now, decimal min, decimal max) =>
        now <= 0 ? StockLevel.Empty
        : now < min ? StockLevel.Low
        : now == min ? StockLevel.Critical
        : max > 0 && now > max ? StockLevel.Overstock
        : StockLevel.Normal;

    /// <summary>
    /// Akhiran wajib untuk part bekas (2nd used): part number original + akhiran (default "-2ND").
    /// Diatur di menu Pengaturan; nilainya diisi oleh AppSettingsService.
    /// </summary>
    public static string SecondSuffix { get; set; } = "-2ND";
    public bool HasSecondSuffix => (PartNumber ?? "").EndsWith(SecondSuffix, StringComparison.OrdinalIgnoreCase);
    /// <summary>Part number original (tanpa akhiran -2ND).</summary>
    public string OriginalPartNumber => HasSecondSuffix ? PartNumber[..^SecondSuffix.Length] : PartNumber ?? "";
}

/// <summary>Pengaturan aplikasi (key → JSON).</summary>
public class AppSetting
{
    [Key, StringLength(50)] public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Pengguna aplikasi. Admin = boleh transaksi & ubah data; Viewer = hanya melihat.</summary>
public class AppUser
{
    public int Id { get; set; }
    [StringLength(50)] public string Username { get; set; } = "";
    [StringLength(100)] public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Viewer;
    /// <summary>true = akses semua site (Admin + AllSites = super admin: kelola site, user, kurs, pengaturan).</summary>
    public bool AllSites { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>Kurs: berapa unit mata uang untuk 1 USD (mis. IDR 16.500 = 1 USD).</summary>
public class ExchangeRate
{
    [Key]
    public Currency Currency { get; set; }
    public decimal UnitsPerUsd { get; set; } = 1;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>Kartu stok: setiap pergerakan stok tercatat di sini.</summary>
public class StockTransaction
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    /// <summary>
    /// Aktivitas daily report maintenance yang memakai part ini. Satu baris Issued hanya bisa
    /// tertaut ke SATU aktivitas → pergantian part tidak bisa tercatat ganda.
    /// </summary>
    public int? ActivityId { get; set; }
    public DateTime Date { get; set; }
    public TxType Type { get; set; }
    [StringLength(30)] public string DocNo { get; set; } = "";
    public int PartId { get; set; }
    public Part Part { get; set; } = null!;
    /// <summary>Bertanda: positif = stok masuk, negatif = stok keluar.</summary>
    public decimal Qty { get; set; }
    public decimal BalanceAfter { get; set; }
    public decimal UnitPriceUsd { get; set; }
    [StringLength(100)] public string? Pic { get; set; }
    [StringLength(120)] public string? Department { get; set; }
    [StringLength(100)] public string? Reference { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public int? LoanId { get; set; }
    /// <summary>Username yang menginput transaksi.</summary>
    [StringLength(50)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public decimal ValueUsd => Math.Abs(Qty) * UnitPriceUsd;
}

public class Loan
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    [StringLength(30)] public string DocNo { get; set; } = "";
    public int PartId { get; set; }
    public Part Part { get; set; } = null!;
    public decimal Qty { get; set; }
    public decimal ReturnedQty { get; set; }
    /// <summary>Qty yang tidak dikembalikan karena terpakai (dikonversi menjadi Issued).</summary>
    public decimal UsedQty { get; set; }
    [StringLength(100)] public string Borrower { get; set; } = "";
    [StringLength(120)] public string? Department { get; set; }
    public DateTime LoanDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? ClosedDate { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Open;
    [StringLength(500)] public string? Notes { get; set; }

    public decimal Outstanding => Qty - ReturnedQty - UsedQty;
    public bool IsOverdue => Status == LoanStatus.Open && DueDate.HasValue && DueDate.Value.Date < DateTime.Today;
}

/// <summary>Sesi stock checking (stock opname) per semester.</summary>
public class StockCheckSession
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }
    public int Year { get; set; }
    /// <summary>1 = Jan–Jun, 2 = Jul–Des.</summary>
    public int Semester { get; set; }
    [StringLength(100)] public string CheckedBy { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? ClosedAt { get; set; }
    public StockCheckStatus Status { get; set; } = StockCheckStatus.Open;
    public bool AdjustmentPosted { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public List<StockCheckItem> Items { get; set; } = new();

    public string Period => $"{Year}-S{Semester}";
    public string PeriodLabel => Semester == 1 ? $"Jan–Jun {Year}" : $"Jul–Des {Year}";
}

public class StockCheckItem
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public StockCheckSession Session { get; set; } = null!;
    public int PartId { get; set; }
    public Part Part { get; set; } = null!;
    /// <summary>Stok sistem pada saat dihitung (snapshot).</summary>
    public decimal SystemQty { get; set; }
    public decimal? CountedQty { get; set; }
    public decimal UnitPriceUsd { get; set; }
    public DateTime? CheckedAt { get; set; }
    [StringLength(300)] public string? Note { get; set; }

    public decimal? Variance => CountedQty.HasValue ? CountedQty.Value - SystemQty : null;
}

/// <summary>Permintaan part dari tablet di ruang produksi / maintenance.</summary>
public class PartRequest
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    [StringLength(30)] public string ReqNo { get; set; } = "";
    /// <summary>Tablet = permintaan biasa; DailyReport = konfirmasi part yang sudah dipakai tapi belum di-issue.</summary>
    public RequestSource Source { get; set; }
    /// <summary>Aktivitas daily report yang terkait; Issued dari permintaan ini otomatis tertaut ke aktivitas tersebut.</summary>
    public int? ActivityId { get; set; }
    [StringLength(100)] public string RequesterName { get; set; } = "";
    /// <summary>Line / mesin / departemen peminta.</summary>
    [StringLength(120)] public string? Line { get; set; }
    public RequestUrgency Urgency { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.New;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>Akun tablet yang mengirim.</summary>
    [StringLength(50)] public string? CreatedBy { get; set; }
    [StringLength(50)] public string? HandledBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    /// <summary>Alasan ditolak / pesan untuk peminta.</summary>
    [StringLength(300)] public string? ResponseNote { get; set; }
    /// <summary>No. dokumen Issued yang memenuhi permintaan ini.</summary>
    [StringLength(30)] public string? IssueDocNo { get; set; }
    public List<PartRequestLine> Lines { get; set; } = new();
}

public class PartRequestLine
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public PartRequest Request { get; set; } = null!;
    /// <summary>Part dari katalog; null bila peminta hanya menulis teks.</summary>
    public int? PartId { get; set; }
    public Part? Part { get; set; }
    /// <summary>Teks yang ditulis peminta (untuk part yang tidak ditemukan di katalog).</summary>
    [StringLength(200)] public string? FreeText { get; set; }
    public decimal Qty { get; set; } = 1;
}

/// <summary>Master mesin untuk modul Machine Maintenance.</summary>
public class Machine
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    [Required(ErrorMessage = "Kode mesin wajib diisi"), StringLength(30), Display(Name = "Kode Mesin")]
    public string Code { get; set; } = "";
    [Required(ErrorMessage = "Nama mesin wajib diisi"), StringLength(120), Display(Name = "Nama Mesin")]
    public string Name { get; set; } = "";
    [StringLength(80), Display(Name = "Area / Line")] public string? Area { get; set; }
    [StringLength(80), Display(Name = "Maker")] public string? Maker { get; set; }
    [StringLength(80), Display(Name = "Model / Tipe")] public string? ModelNo { get; set; }
    /// <summary>Wajib PM setiap bulan (dihitung di progress PM dashboard).</summary>
    [Display(Name = "Wajib PM bulanan")] public bool PmRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
    [StringLength(300)] public string? Notes { get; set; }
    public string Label => Code + " — " + Name;
}

/// <summary>Rencana pekerjaan maintenance (daily plan).</summary>
public class MaintenancePlan
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public DateTime Date { get; set; }
    /// <summary>null = sepanjang hari / shift mana saja.</summary>
    public Shift? Shift { get; set; }
    public int? MachineId { get; set; }
    public Machine? Machine { get; set; }
    [StringLength(300)] public string Task { get; set; } = "";
    public ActivityType Type { get; set; } = ActivityType.Preventive;
    [StringLength(120)] public string? Pic { get; set; }
    [StringLength(50)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool Cancelled { get; set; }
    public List<MaintenanceActivity> Activities { get; set; } = new();
}

/// <summary>Aktivitas harian tim maintenance (daily report).</summary>
public class MaintenanceActivity
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public DateTime Date { get; set; }
    public Shift Shift { get; set; }
    public int? MachineId { get; set; }
    public Machine? Machine { get; set; }
    /// <summary>Lokasi / mesin di luar master (mis. utilitas).</summary>
    [StringLength(120)] public string? MachineText { get; set; }
    public ActivityType Type { get; set; }
    [StringLength(500)] public string? Problem { get; set; }
    [StringLength(500)] public string? Cause { get; set; }
    [StringLength(1000)] public string? Action { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    /// <summary>Mesin berhenti (downtime) selama pekerjaan.</summary>
    public bool MachineStopped { get; set; }
    public ActivityStatus Status { get; set; } = ActivityStatus.Done;
    /// <summary>Nama teknisi yang mengerjakan (dipisah koma).</summary>
    [StringLength(200)] public string? Technicians { get; set; }
    public bool HasPartReplacement { get; set; }
    [StringLength(500)] public string? Remarks { get; set; }
    public int? PlanId { get; set; }
    public MaintenancePlan? Plan { get; set; }
    [StringLength(50)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public string MachineLabel => Machine != null ? Machine.Label : MachineText ?? "-";
    /// <summary>Durasi pekerjaan (shift malam bisa lewat tengah malam).</summary>
    public TimeSpan? Duration => StartTime.HasValue && EndTime.HasValue
        ? (EndTime.Value >= StartTime.Value ? EndTime.Value - StartTime.Value : EndTime.Value + TimeSpan.FromDays(1) - StartTime.Value) : null;
}
