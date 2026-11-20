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
            db.Users.AddRange(admin, viewer);
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
    }
}
