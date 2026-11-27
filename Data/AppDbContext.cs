using Microsoft.EntityFrameworkCore;
using SparePartControl.Models;

namespace SparePartControl.Data;

public class AppDbContext : DbContext
{
    private readonly SparePartControl.Services.SiteContext? _site;
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public AppDbContext(DbContextOptions<AppDbContext> options, SparePartControl.Services.SiteContext site) : base(options) { _site = site; }

    /// <summary>
    /// Site yang boleh dilihat request ini (null = tanpa batasan, mis. saat seed/startup).
    /// Dipakai oleh global query filter sehingga semua query otomatis terbatas ke site aktif.
    /// </summary>
    public int[]? SiteIds => _site?.FilterIds;

    public DbSet<Site> Sites => Set<Site>();
    public DbSet<PartRequest> PartRequests => Set<PartRequest>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<MaintenanceActivity> MaintenanceActivities => Set<MaintenanceActivity>();
    public DbSet<PartRequestLine> PartRequestLines => Set<PartRequestLine>();
    public DbSet<UserSite> UserSites => Set<UserSite>();

    public DbSet<Part> Parts => Set<Part>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<StockTransaction> Transactions => Set<StockTransaction>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<StockCheckSession> StockCheckSessions => Set<StockCheckSession>();
    public DbSet<StockCheckItem> StockCheckItems => Set<StockCheckItem>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite tidak punya tipe decimal native; simpan sebagai REAL supaya SUM/ORDER BY jalan di database.
        // Jika pindah ke SQL Server, hapus baris ini dan pakai HavePrecision(18, 4).
        builder.Properties<decimal>().HaveConversion<double>();
        builder.Properties<Currency>().HaveConversion<string>().HaveMaxLength(3);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Site>().HasIndex(s => s.Code).IsUnique();
        b.Entity<Site>().Property(s => s.Code).UseCollation("NOCASE");
        b.Entity<UserSite>().HasKey(x => new { x.UserId, x.SiteId });
        b.Entity<UserSite>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserSite>().HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Cascade);

        // Goods code unik per site (site lain boleh memakai kode yang sama).
        b.Entity<Part>().HasIndex(p => new { p.SiteId, p.PartNumber }).IsUnique();
        b.Entity<Part>().HasOne(p => p.Site).WithMany().HasForeignKey(p => p.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockTransaction>().HasOne<Site>().WithMany().HasForeignKey(t => t.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Loan>().HasOne<Site>().WithMany().HasForeignKey(t => t.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockCheckSession>().HasOne(s => s.Site).WithMany().HasForeignKey(t => t.SiteId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<PartRequest>().HasIndex(r => new { r.SiteId, r.Status });
        b.Entity<PartRequest>().HasOne<Site>().WithMany().HasForeignKey(r => r.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PartRequestLine>().HasOne(l => l.Request).WithMany(r => r.Lines).HasForeignKey(l => l.RequestId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PartRequestLine>().HasOne(l => l.Part).WithMany().HasForeignKey(l => l.PartId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<PartRequest>().HasQueryFilter(r => SiteIds == null || SiteIds.Contains(r.SiteId));
        b.Entity<PartRequestLine>().HasQueryFilter(l => SiteIds == null || SiteIds.Contains(l.Request.SiteId));

        // ---- Machine Maintenance ----
        b.Entity<Machine>().HasIndex(m => new { m.SiteId, m.Code }).IsUnique();
        b.Entity<Machine>().Property(m => m.Code).UseCollation("NOCASE");
        b.Entity<Machine>().HasOne<Site>().WithMany().HasForeignKey(m => m.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MaintenancePlan>().HasIndex(p => new { p.SiteId, p.Date });
        b.Entity<MaintenancePlan>().HasOne(p => p.Machine).WithMany().HasForeignKey(p => p.MachineId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<MaintenanceActivity>().HasIndex(a => new { a.SiteId, a.Date, a.Shift });
        b.Entity<MaintenanceActivity>().HasIndex(a => new { a.MachineId, a.Date });
        b.Entity<MaintenanceActivity>().HasOne(a => a.Machine).WithMany().HasForeignKey(a => a.MachineId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<MaintenanceActivity>().HasOne(a => a.Plan).WithMany(p => p.Activities).HasForeignKey(a => a.PlanId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<StockTransaction>().HasIndex(t => t.ActivityId);
        b.Entity<StockTransaction>().HasOne<MaintenanceActivity>().WithMany().HasForeignKey(t => t.ActivityId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<PartRequest>().HasOne<MaintenanceActivity>().WithMany().HasForeignKey(r => r.ActivityId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Machine>().HasQueryFilter(m => SiteIds == null || SiteIds.Contains(m.SiteId));
        b.Entity<MaintenancePlan>().HasQueryFilter(m => SiteIds == null || SiteIds.Contains(m.SiteId));
        b.Entity<MaintenanceActivity>().HasQueryFilter(m => SiteIds == null || SiteIds.Contains(m.SiteId));

        b.Entity<Part>().HasQueryFilter(p => SiteIds == null || SiteIds.Contains(p.SiteId));
        b.Entity<StockTransaction>().HasQueryFilter(t => SiteIds == null || SiteIds.Contains(t.SiteId));
        b.Entity<Loan>().HasQueryFilter(l => SiteIds == null || SiteIds.Contains(l.SiteId));
        b.Entity<StockCheckSession>().HasQueryFilter(s => SiteIds == null || SiteIds.Contains(s.SiteId));
        b.Entity<StockCheckItem>().HasQueryFilter(i => SiteIds == null || SiteIds.Contains(i.Session.SiteId));
        b.Entity<Part>().Property(p => p.PartNumber).UseCollation("NOCASE");
        b.Entity<AppUser>().HasIndex(u => u.Username).IsUnique();
        b.Entity<AppUser>().Property(u => u.Username).UseCollation("NOCASE");
        b.Entity<StockTransaction>().HasIndex(t => t.Date);
        b.Entity<StockTransaction>().HasIndex(t => new { t.PartId, t.Date });
        b.Entity<StockTransaction>().HasOne(t => t.Part).WithMany().HasForeignKey(t => t.PartId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Loan>().HasOne(l => l.Part).WithMany().HasForeignKey(l => l.PartId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockCheckSession>().HasIndex(s => new { s.SiteId, s.Year, s.Semester }).IsUnique();
        b.Entity<StockTransaction>().HasIndex(t => new { t.SiteId, t.DocNo });
        b.Entity<StockCheckItem>().HasIndex(i => new { i.SessionId, i.PartId }).IsUnique();
        b.Entity<StockCheckItem>().HasOne(i => i.Part).WithMany().HasForeignKey(i => i.PartId).OnDelete(DeleteBehavior.Restrict);
    }
}
