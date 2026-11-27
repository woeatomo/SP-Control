using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Models;
using SparePartControl.Services;

namespace SparePartControl.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider sp, bool seedSample)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stock = scope.ServiceProvider.GetRequiredService<StockService>();
        await db.Database.MigrateAsync();
        _ = scope.ServiceProvider.GetRequiredService<AppSettingsService>().Code; // muat pengaturan (akhiran part 2nd)

        if (!await db.ExchangeRates.AnyAsync())
        {
            // Kurs awal hanya contoh — perbarui di menu Kurs.
            db.ExchangeRates.AddRange(
                new ExchangeRate { Currency = Currency.USD, UnitsPerUsd = 1 },
                new ExchangeRate { Currency = Currency.SGD, UnitsPerUsd = 1.2768m },
                new ExchangeRate { Currency = Currency.JPY, UnitsPerUsd = 155m },
                new ExchangeRate { Currency = Currency.IDR, UnitsPerUsd = 17120m });
            await db.SaveChangesAsync();
        }

        if (!await db.Users.AnyAsync())
        {
            // Akun awal — WAJIB ganti password setelah login pertama (menu nama user → Ganti Password).
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            var admin = new AppUser { Username = "admin", FullName = "Administrator", Role = UserRole.Admin };
            admin.PasswordHash = hasher.HashPassword(admin, "admin123");
            var viewer = new AppUser { Username = "viewer", FullName = "Viewer", Role = UserRole.Viewer };
            viewer.PasswordHash = hasher.HashPassword(viewer, "viewer123");
            var tablet = new AppUser { Username = "tablet", FullName = "Tablet Produksi", Role = UserRole.Requester };
            tablet.PasswordHash = hasher.HashPassword(tablet, "tablet123");
            var tech = new AppUser { Username = "teknisi", FullName = "Budi", Role = UserRole.Technician };
            tech.PasswordHash = hasher.HashPassword(tech, "teknisi123");
            db.Users.AddRange(admin, viewer, tablet, tech);
            await db.SaveChangesAsync();
        }

        // Data lama (sekali saja): part berkondisi 2nd diberi akhiran 2nd.
        await AppSettingsService.RunOnceAsync(db, "second-suffix", async () =>
        {
            var taken = (await db.Parts.Select(p => p.PartNumber).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var fixedAny = false;
            foreach (var p in await db.Parts.Where(p => p.Condition == PartCondition.Second).ToListAsync())
            {
                if (p.HasSecondSuffix || !taken.Add(p.PartNumber + Part.SecondSuffix)) continue;
                p.PartNumber += Part.SecondSuffix;
                fixedAny = true;
            }
            if (fixedAny) await db.SaveChangesAsync();
        });

        // Database lama (sebelum ada kolom initialStock): isi dari transaksi saldo awal.
        var noInit = await db.Parts.Where(p => p.InitialStock == 0).Select(p => p.Id).ToListAsync();
        if (noInit.Count > 0)
        {
            var saldo = (await db.Transactions.Where(t => t.DocNo == "SALDO-AWAL" && noInit.Contains(t.PartId))
                .Select(t => new { t.PartId, t.Qty }).ToListAsync()).GroupBy(t => t.PartId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            if (saldo.Count > 0)
            {
                foreach (var p in await db.Parts.Where(p => saldo.Keys.Contains(p.Id)).ToListAsync()) p.InitialStock = saldo[p.Id];
                await db.SaveChangesAsync();
            }
        }

        if (!seedSample || await db.Parts.AnyAsync()) return;

        // Contoh diambil dari Excel "00. MASTER STOCK APP" (goodsCode, goodsName, Model, rackName, makerName, suppName,
        // currency, unit_price, initialStock, minStock, maxStock, nowStock).
        var siteId = await db.Sites.OrderBy(s => s.Id).Select(s => s.Id).FirstAsync();
        Part P(string code, string name, string model, string rack, string maker, string supp, Currency cur, decimal price,
            decimal init, decimal min, decimal max, string? remark = null, bool active = false, bool reusable = false) => new()
        {
            SiteId = siteId, PartNumber = code, Name = name, Category = model, Location = rack, Brand = maker, Supplier = supp, Currency = cur,
            UnitPrice = price, InitialStock = init, MinStock = min, MaxStock = max, Remarks = remark, IsReusable = reusable,
            Status = active ? PartStatus.Active : PartStatus.NonActive, Unit = "pcs"
        };

        var samples = new (Part part, decimal now)[]
        {
            (P("MaPC-ABB-0001", "ABB PLUG ACE /T&J SAFETY MARK STD SISIR 13A - B7913", "ELECTRICAL", "Cabinet 4 / Drawer F", "ABB", "NIVEN BINA BENING", Currency.IDR, 40000, 2, 1, 10, active: true), 5),
            (P("MaPC-AMCD-0001", "AMCD PLUG 4662R", "ELECTRICAL", "Small Rack B-8", "AMCD", "PT SINDO SEIKI ADITAMA", Currency.SGD, 130, 4, 2, 5, active: true), 4),
            (P("MaPC-AMCD-0003", "AMCD PLUG 4322R", "ELECTRICAL", "Small Rack B-8", "AMCD", "PT SINDO SEIKI ADITAMA", Currency.IDR, 385700, 2, 1, 10, active: true), 2),
            (P("MaPC-AMPTEC-0001", "TORAY Mixing Nozzle", "INJECTION", "Cabinet 10 / Drawer F", "AMPTEC", "AMPTEC INDUSTRIAL INNOVATION PTE LTD", Currency.IDR, 3750000, 1, 2, 4, active: true), 1),
            (P("MaPC-AND-0001", "AND Thermometer AD-5657-50", "ELECTRICAL", "Cabinet 4 / Drawer E", "AND", "DELCO AUTOPARTS PTE LTD", Currency.SGD, 34.05m, 2, 1, 5), 2),
            (P("MaPC-APEM-0001", "Apem Toggle Switch X12CPN", "ELECTRICAL", "Cabinet 4 / Drawer C", "APEM", "PT SINDO SEIKI ADITAMA", Currency.IDR, 115000, 1, 1, 5, active: true), 1),
            (P("MaPC-AUTONICS-0002", "Autonics Temperature Controller TX4S-24S", "ELECTRICAL", "Cabinet 3 / Drawer E", "AUTONICS", "PT BENWIN INDONESIA", Currency.IDR, 521000, 1, 1, 3, "Part Not Found"), 1),
            (P("MaPC-AUTONICS-0004", "Autonics Control Switch Accey B6MA-4GLN", "ELECTRICAL", "Cabinet 4 / Drawer D", "AUTONICS", "INDOPAC DYNAMIC, PT", Currency.IDR, 650000, 7, 2, 5), 7),
            (P("MaPC-AWG-0001", "AWG CABLE CL2/2464-3599-DS-LF-1PX18AWG", "ELECTRICAL", "Rack 1 / Box 01 / Layer 3", "AWG", "PT. PRIORITAS SUKSES PRATAMA", Currency.IDR, 6411000, 2, 1, 2), 2),
            (P("MaPC-AZBIL-0001", "AZBIL ELECTRICS SENSOR HP7-C31S PHOTO", "BLOW M/C", "Cabinet 2 / Drawer D", "AZBIL", "CV KT INTERNATIONAL", Currency.IDR, 2400000, 2, 1, 20, active: true), 2),
            (P("MaPC-BANDO-0001", "BANDO Belting STS150S8M 1000", "INJECTION", "Cabinet 9 / Drawer B", "BANDO", "PT SINDO SEIKI ADITAMA", Currency.IDR, 195000, 2, 2, 5, active: true), 2),
            (P("MaPC-BANDO-0005", "BANDO Belting S4.5M 450 X 15MM", "INJECTION", "Cabinet 9 / Drawer B", "BANDO", "PT ROYALINDO SEMESTA", Currency.SGD, 35, 6, 2, 5), 6),
            (P("MaPC-BANDO-0022", "BANDO 5V-B-68 offer to BANDO Belting B-68", "CRUSHING", "Rack 2 / Box 01 / Layer 3", "BANDO", "CV KT INTERNATIONAL", Currency.IDR, 100000, 3, 4, 5, active: true), 0),
            (P("MaPC-SSI-0093", "SSI Timing Belt 050-AT5-0700E-L #260506", "ROBOT", "Rack 1 / Box 01 / Layer 2", "SSI", "PT STAR SEIKI INDONESIA", Currency.USD, 313, 4, 2, 5, "Part Not Found", active: true), 4),
            (P("MaPC-SSI-0096", "SSI Timing Belt 025-AT10-0488E-L #260761", "ROBOT", "Rack 4 / Box 02 / Layer 1", "SSI", "PT STAR SEIKI INDONESIA", Currency.USD, 79, 1, 2, 5, active: true), 1),
            (P("MaPC-SSI-0093-2ND", "SSI Timing Belt 050-AT5-0700E-L #260506 (2nd)", "ROBOT", "Rack 1 / Box 01 / Layer 2", "SSI", "PT STAR SEIKI INDONESIA", Currency.USD, 150, 1, 0, 0, active: true, reusable: true), 1),
            (P("MaPC-CARLO-0001", "CARLO Solid State Relay Contactor SSR RM1A48D75", "ELECTRICAL", "Cabinet 4 / Drawer B", "CARLO", "PT. PRIORITAS SUKSES PRATAMA", Currency.IDR, 1600000, 2, 1, 5), 2),
            (P("MaPC-CHIYODA-0001", "CHIYODA FUJI Male Elbow 12R-03ML", "BLOW MACHINE", "Cabinet 8 / Drawer A", "CHIYODA", "CV KT INTERNATIONAL", Currency.IDR, 89500, 26, 3, 10, active: true), 21),
        };
        var rates = await stock.GetRatesAsync();
        foreach (var (part, now) in samples) await stock.CreatePartAsync(part, now, rates);
        await SeedMaintenanceAsync(db, stock, siteId);
    }

    /// <summary>Contoh modul Machine Maintenance (hanya untuk database baru).</summary>
    private static async Task SeedMaintenanceAsync(AppDbContext db, StockService stock, int siteId)
    {
        if (await db.Machines.AnyAsync()) return;
        Machine M(string code, string name, string area, string maker) => new() { SiteId = siteId, Code = code, Name = name, Area = area, Maker = maker };
        var machines = new List<Machine>
        {
            M("INJ-01", "Injection Molding 1", "INJECTION", "Toshiba"), M("INJ-02", "Injection Molding 2", "INJECTION", "Toshiba"),
            M("INJ-03", "Injection Molding 3", "INJECTION", "JSW"), M("INJ-04", "Injection Molding 4", "INJECTION", "JSW"),
            M("INJ-05", "Injection Molding 5", "INJECTION", "Engel"), M("INJ-06", "Injection Molding 6", "INJECTION", "Engel"),
            M("BLW-01", "Blow Molding 1", "BLOW MACHINE", "Aoki"), M("BLW-02", "Blow Molding 2", "BLOW MACHINE", "Aoki"),
            M("BLW-03", "Blow Molding 3", "BLOW MACHINE", "Nissei"), M("CRS-01", "Crusher 1", "CRUSHING", "Matsui"),
            M("CRS-02", "Crusher 2", "CRUSHING", "Matsui"), M("RBT-01", "Robot Take-out 1", "ROBOT", "Yushin"),
            M("RBT-02", "Robot Take-out 2", "ROBOT", "Yushin"), M("CMP-01", "Air Compressor", "UTILITY", "Atlas Copco"),
        };
        machines[^1].PmRequired = true;
        db.Machines.AddRange(machines);
        await db.SaveChangesAsync();
        int Id(string c) => machines.First(m => m.Code == c).Id;

        var today = DateTime.Today; var yday = today.AddDays(-1);
        var first = new DateTime(today.Year, today.Month, 1);
        // PM yang sudah selesai bulan ini (beberapa mesin).
        var pmDone = new[] { "INJ-01", "INJ-03", "BLW-01", "CRS-01", "RBT-01", "CMP-01" };
        var day = first;
        foreach (var c in pmDone)
        {
            if (day >= today) day = first;
            db.MaintenanceActivities.Add(new MaintenanceActivity
            {
                SiteId = siteId, Date = day, Shift = Shift.A, MachineId = Id(c), Type = ActivityType.Preventive, Status = ActivityStatus.Done,
                Action = "PM bulanan: cek & bersihkan, kencangkan baut, cek oli dan tekanan angin", StartTime = new(8, 0, 0), EndTime = new(10, 0, 0),
                MachineStopped = true, Technicians = "Budi, Andi", CreatedBy = "teknisi"
            });
            day = day.AddDays(1);
        }

        // Laporan kemarin: termasuk breakdown dengan pergantian part yang tertaut ke Issued (tanpa transaksi ganda).
        var belt = await db.Parts.FirstAsync(p => p.PartNumber == "MaPC-BANDO-0005");
        var issue = await stock.PostEntryAsync(EntryMode.Issue,
            new EntryHeader { Date = yday.AddHours(9).AddMinutes(20), Pic = "Andi", Department = "INJ-02 Injection Molding 2", Notes = "Belt putus" },
            new List<EntryLine> { new() { PartId = belt.Id, Qty = 1 } });
        var bd = new MaintenanceActivity
        {
            SiteId = siteId, Date = yday, Shift = Shift.A, MachineId = Id("INJ-02"), Type = ActivityType.Breakdown, Status = ActivityStatus.Done,
            Problem = "Conveyor hopper berhenti, belt putus", Cause = "Belt aus (umur pakai)", Action = "Ganti belt baru, setting tension, trial run OK",
            StartTime = new(9, 0, 0), EndTime = new(10, 15, 0), MachineStopped = true, Technicians = "Andi, Rudi", HasPartReplacement = true, CreatedBy = "teknisi"
        };
        db.MaintenanceActivities.AddRange(bd,
            new MaintenanceActivity { SiteId = siteId, Date = yday, Shift = Shift.A, MachineId = Id("INJ-01"), Type = ActivityType.Inspection, Status = ActivityStatus.Done,
                Action = "Inspeksi heater barrel zona 1–4, semua normal", StartTime = new(13, 0, 0), EndTime = new(13, 40, 0), Technicians = "Budi", CreatedBy = "teknisi" },
            new MaintenanceActivity { SiteId = siteId, Date = yday, Shift = Shift.A, MachineId = Id("BLW-02"), Type = ActivityType.Corrective, Status = ActivityStatus.WaitingPart,
                Problem = "Kebocoran angin di solenoid valve", Action = "Ganti seal sementara, menunggu solenoid valve baru", StartTime = new(15, 30, 0), EndTime = new(16, 30, 0),
                Technicians = "Budi, Andi", CreatedBy = "teknisi" },
            new MaintenanceActivity { SiteId = siteId, Date = yday, Shift = Shift.B, MachineId = Id("RBT-02"), Type = ActivityType.Corrective, Status = ActivityStatus.Done,
                Problem = "Robot alarm sensor vacuum", Cause = "Sensor kotor", Action = "Bersihkan sensor & filter vacuum, setting ulang", StartTime = new(21, 10, 0), EndTime = new(21, 50, 0),
                MachineStopped = true, Technicians = "Rudi", CreatedBy = "teknisi" },
            new MaintenanceActivity { SiteId = siteId, Date = yday, Shift = Shift.B, MachineId = Id("CRS-02"), Type = ActivityType.Preventive, Status = ActivityStatus.Done,
                Action = "PM bulanan: asah pisau crusher, cek bearing", StartTime = new(1, 0, 0), EndTime = new(3, 0, 0), MachineStopped = true, Technicians = "Rudi, Sari", CreatedBy = "teknisi" });
        await db.SaveChangesAsync();
        if (issue.Ok)
            foreach (var t in await db.Transactions.Where(t => t.DocNo == issue.Message).ToListAsync()) t.ActivityId = bd.Id;

        // Daily plan hari ini.
        db.MaintenancePlans.AddRange(
            new MaintenancePlan { SiteId = siteId, Date = today, MachineId = Id("INJ-04"), Task = "PM bulanan", Type = ActivityType.Preventive, Shift = Shift.A, Pic = "Budi, Andi" },
            new MaintenancePlan { SiteId = siteId, Date = today, MachineId = Id("INJ-05"), Task = "PM bulanan", Type = ActivityType.Preventive, Shift = Shift.A, Pic = "Budi" },
            new MaintenancePlan { SiteId = siteId, Date = today, MachineId = Id("BLW-02"), Task = "Ganti solenoid valve (lanjutan kemarin)", Type = ActivityType.Corrective, Pic = "Andi" },
            new MaintenancePlan { SiteId = siteId, Date = today, MachineId = Id("RBT-02"), Task = "Cek ulang sensor vacuum", Type = ActivityType.Inspection, Shift = Shift.B, Pic = "Rudi" });
        await db.SaveChangesAsync();
    }
}
