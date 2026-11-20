using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Services;

public class ImportOptions
{
    /// <summary>Hanya cek, tidak menyimpan apa pun.</summary>
    public bool DryRun { get; set; }
    /// <summary>Part yang sudah ada: samakan stok dengan nowStock di file (dicatat sebagai Adjustment).</summary>
    public bool SyncStock { get; set; }
    /// <summary>Ambil kurs dari tabel tblRate (currency, Rate_to_USD) bila ada di file Excel.</summary>
    public bool ImportRates { get; set; } = true;
}

public class ImportResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int StockSynced { get; set; }
    public int Generated { get; set; }
    public int Skipped { get; set; }
    public List<string> RatesUpdated { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<(string Header, string Field)> Mapping { get; set; } = new();
    public List<string> IgnoredHeaders { get; set; } = new();
    public string Source { get; set; } = "";
    public int RowCount { get; set; }
}

/// <summary>
/// Import master data dari Excel MASTER STOCK (goodsCode, goodsName, Model, stockCategory, rackName, makerName,
/// suppName, currency, unit_price, initialStock, minStock, maxStock, nowStock, Remark) atau CSV dengan header serupa.
/// </summary>
public class MasterImport
{
    private readonly AppDbContext _db;
    private readonly StockService _stock;
    private readonly PartCode _codes;
    private readonly AppSettingsService _settings;
    private readonly SiteContext _site;
    public MasterImport(AppDbContext db, StockService stock, PartCode codes, AppSettingsService settings, SiteContext site)
    { _db = db; _stock = stock; _codes = codes; _settings = settings; _site = site; }

    private enum F { Code, Name, Model, StockCat, Status, Rack, Maker, Supplier, Currency, Price, Initial, Min, Max, Now, Remark, Spec, Unit, Machine, Reusable, Condition }

    private static readonly Dictionary<F, string> FieldLabel = new()
    {
        [F.Code] = "Goods Code", [F.Name] = "Goods Name", [F.Model] = "Model", [F.StockCat] = "Stock Category (Active/Inactive)",
        [F.Status] = "Stock Category (Active/Inactive)", [F.Rack] = "Rack", [F.Maker] = "Maker", [F.Supplier] = "Supplier",
        [F.Currency] = "Currency", [F.Price] = "Unit Price", [F.Initial] = "Initial Stock", [F.Min] = "Min Stock", [F.Max] = "Max Stock",
        [F.Now] = "Now Stock", [F.Remark] = "Remark", [F.Spec] = "Spesifikasi", [F.Unit] = "Satuan", [F.Machine] = "Mesin / Line",
        [F.Reusable] = "Reusable", [F.Condition] = "Kondisi (New/2nd)"
    };

    private static F? Classify(string header)
    {
        var n = ExcelMaster.Norm(header);
        if (ExcelMaster.IsCodeHeader(n)) return F.Code;
        return n switch
        {
            "goodsname" or "name" or "partname" or "namapart" or "nama" => F.Name,
            "model" or "category" or "kategori" => F.Model,
            "stockcategory" or "stockcat" or "statusaset" or "assetstatus" => F.StockCat,
            "status" => F.Status,
            "rackname" or "rack" or "location" or "lokasi" or "lokasirak" => F.Rack,
            "makername" or "maker" or "brand" => F.Maker,
            "suppname" or "supplier" or "suppliername" => F.Supplier,
            "currency" or "curr" or "matauang" => F.Currency,
            "unitprice" or "price" or "harga" or "hargasatuan" => F.Price,
            "nowstock" or "stockqty" or "stock" or "stok" or "qty" => F.Now,
            "remark" or "remarks" or "keterangan" => F.Remark,
            "specification" or "spec" or "spesifikasi" => F.Spec,
            "unit" or "uom" or "satuan" when !header.Contains('_') => F.Unit,
            "machine" or "mesin" => F.Machine,
            "reusable" => F.Reusable,
            "condition" or "kondisi" => F.Condition,
            _ when n.StartsWith("initial") || n == "stokawal" => F.Initial,
            _ when n.StartsWith("minstock") || n is "min" or "mins" or "minstok" => F.Min,
            _ when n.StartsWith("maxstock") || n is "max" or "maxs" or "maxstok" => F.Max,
            _ => null
        };
    }

    private static PartStatus? ParseAsset(string v) => v.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace("_", "") switch
    {
        "ACTIVE" or "AKTIF" => PartStatus.Active,
        "INACTIVE" or "NONACTIVE" or "NONAKTIF" or "TIDAKAKTIF" => PartStatus.NonActive,
        _ => null
    };

    private static bool Yes(string s) => s.Trim().ToLowerInvariant() is "yes" or "y" or "ya" or "true" or "1";

    public async Task<ImportResult> RunAsync(RawTable? parts, RawTable? rates, ImportOptions opt)
    {
        var res = new ImportResult();
        if (parts == null)
        {
            res.Errors.Add("Tidak menemukan tabel master di file. Pastikan ada baris header dengan kolom goodsCode dan goodsName.");
            return res;
        }
        var siteId = _site.Current?.Id ?? 0;
        if (siteId == 0) { res.Errors.Add("Pilih satu site terlebih dahulu (bukan \"Semua site\")."); return res; }
        res.Source = parts.Source;
        res.RowCount = parts.Rows.Count(r => r.Any(c => c.Length > 0));

        // Pemetaan kolom.
        var col = new Dictionary<F, int>();
        for (var i = 0; i < parts.Headers.Count; i++)
        {
            var h = parts.Headers[i];
            if (string.IsNullOrWhiteSpace(h)) continue;
            var f = Classify(h);
            if (f == null || col.ContainsKey(f.Value)) { res.IgnoredHeaders.Add(h); continue; }
            col[f.Value] = i;
            res.Mapping.Add((h, FieldLabel[f.Value]));
        }
        // Kolom "Status" di Excel MASTER STOCK berisi NORMAL/LOW/… (rumus) — abaikan bila stockCategory ada.
        if (col.ContainsKey(F.StockCat) && col.Remove(F.Status, out var si))
        {
            res.Mapping.RemoveAll(m => m.Header == parts.Headers[si]);
            res.IgnoredHeaders.Add(parts.Headers[si]);
        }
        if (!col.ContainsKey(F.Code) || !col.ContainsKey(F.Name))
        {
            res.Errors.Add("Kolom goodsCode dan goodsName wajib ada di baris header.");
            return res;
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        // 1. Kurs dari tblRate (Rate_to_USD = pengali ke USD, seperti rumus StockValue di Excel).
        var ratesChanged = false;
        if (opt.ImportRates && rates != null)
        {
            var hn = rates.Headers.Select(ExcelMaster.Norm).ToList();
            int ci = hn.IndexOf("currency"), ri = hn.FindIndex(x => x.StartsWith("ratetousd") || x == "rate"), ui = hn.IndexOf("unitsperusd");
            var existing = await _db.ExchangeRates.ToDictionaryAsync(r => r.Currency);
            foreach (var row in rates.Rows)
            {
                string Cell(int i) => i >= 0 && i < row.Length ? row[i] : "";
                if (!Enum.TryParse<Currency>(Cell(ci).Trim(), true, out var cur) || cur == Currency.USD) continue;
                decimal units = 0;
                if (ExcelMaster.TryNumber(Cell(ui), rates.IndonesianNumbers, out var u) && u > 0) units = u;
                else if (ExcelMaster.TryNumber(Cell(ri), rates.IndonesianNumbers, out var rate) && rate > 0) units = 1 / rate;
                if (units <= 0) continue;
                if (!existing.TryGetValue(cur, out var er)) { er = new ExchangeRate { Currency = cur }; _db.ExchangeRates.Add(er); existing[cur] = er; }
                if (Math.Abs(er.UnitsPerUsd - units) > 0.0000001m)
                {
                    er.UnitsPerUsd = units; er.UpdatedAt = DateTime.Now; ratesChanged = true;
                    res.RatesUpdated.Add($"{cur}: 1 USD = {units:#,0.####}");
                }
            }
            await _db.SaveChangesAsync();
        }
        var rateMap = await _stock.GetRatesAsync();

        // 2. Part.
        var existingParts = await _db.Parts.ToDictionaryAsync(p => p.PartNumber, StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var syncDoc = $"IMPORT-{await _stock.SiteCodeAsync(siteId)}-{DateTime.Now:yyMMdd-HHmm}";

        for (var r = 0; r < parts.Rows.Count; r++)
        {
            var row = parts.Rows[r];
            var line = parts.FirstRowNumber + r;
            if (row.All(c => c.Trim().Length == 0)) continue;
            bool Has(F f) => col.ContainsKey(f);
            string G(F f) => col.TryGetValue(f, out var i) && i < row.Length ? row[i].Trim() : "";
            string? S(F f) { var v = G(f); return v.Length == 0 ? null : v; }
            bool Num(F f, out decimal d) => ExcelMaster.TryNumber(G(f), parts.IndonesianNumbers, out d);

            var code = G(F.Code); var name = G(F.Name);
            if (code.Length == 0 && name.Length == 0) continue;
            if (code.Length == 0 && name.Length > 0 && _settings.Code.AutoGenerateWhenEmpty)
            {
                // Goods code kosong → dibuat otomatis sesuai format di Pengaturan.
                var (gen, genErr) = await _codes.NextAsync(new CodeInput(G(F.Maker), G(F.Model), G(F.Supplier)));
                if (gen == null) { res.Errors.Add($"Baris {line} ({name}): goods code kosong dan tidak bisa dibuat otomatis — {genErr}"); res.Skipped++; continue; }
                code = gen;
                res.Generated++;
            }
            if (code.Length == 0 || name.Length == 0) { res.Errors.Add($"Baris {line}: goodsCode/goodsName kosong — dilewati."); res.Skipped++; continue; }
            if (!Num(F.Price, out var price) || !Num(F.Initial, out var initial) || !Num(F.Min, out var min) || !Num(F.Max, out var max) || !Num(F.Now, out var now)
                || price < 0 || initial < 0 || min < 0 || max < 0 || now < 0)
            { res.Errors.Add($"Baris {line} ({code}): ada angka yang tidak valid / negatif — dilewati."); res.Skipped++; continue; }
            Currency cur = Currency.IDR;
            if (G(F.Currency).Length > 0 && !Enum.TryParse(G(F.Currency), true, out cur))
            { res.Errors.Add($"Baris {line} ({code}): currency '{G(F.Currency)}' tidak dikenal (USD/SGD/JPY/IDR) — dilewati."); res.Skipped++; continue; }

            PartStatus? asset = Has(F.StockCat) ? ParseAsset(G(F.StockCat)) : Has(F.Status) ? ParseAsset(G(F.Status)) : null;
            var cond = G(F.Condition).ToLowerInvariant() is "2nd" or "second" or "bekas" or "used" ? PartCondition.Second : PartCondition.New;
            if (code.EndsWith(Part.SecondSuffix, StringComparison.OrdinalIgnoreCase)) cond = PartCondition.Second;
            else if (cond == PartCondition.Second) code += Part.SecondSuffix;

            if (seen.TryGetValue(code, out var firstLine))
            { res.Errors.Add($"Baris {line}: goodsCode {code} dobel (sudah ada di baris {firstLine}) — dilewati."); res.Skipped++; continue; }
            seen[code] = line;

            if (existingParts.TryGetValue(code, out var e))
            {
                e.Name = name;
                if (Has(F.Model)) e.Category = S(F.Model);
                if (asset.HasValue) e.Status = asset.Value;
                if (Has(F.Rack)) e.Location = S(F.Rack);
                if (Has(F.Maker)) e.Brand = S(F.Maker);
                if (Has(F.Supplier)) e.Supplier = S(F.Supplier);
                if (G(F.Currency).Length > 0) e.Currency = cur;
                if (G(F.Price).Length > 0) e.UnitPrice = price;
                if (G(F.Initial).Length > 0) e.InitialStock = initial;
                if (G(F.Min).Length > 0) e.MinStock = min;
                if (G(F.Max).Length > 0) e.MaxStock = max;
                if (Has(F.Remark)) e.Remarks = S(F.Remark);
                if (Has(F.Spec)) e.Specification = S(F.Spec);
                if (G(F.Unit).Length > 0) e.Unit = G(F.Unit);
                if (Has(F.Machine)) e.Machine = S(F.Machine);
                if (Has(F.Reusable)) e.IsReusable = Yes(G(F.Reusable));
                e.Condition = e.HasSecondSuffix ? PartCondition.Second : PartCondition.New;
                e.UnitPriceUsd = StockService.ToUsd(e.UnitPrice, e.Currency, rateMap);
                e.UpdatedAt = DateTime.Now;
                res.Updated++;
                if (opt.SyncStock && G(F.Now).Length > 0 && _stock.SetStock(e, now, syncDoc, "Disamakan dengan nowStock dari file import"))
                    res.StockSynced++;
            }
            else
            {
                var p = new Part
                {
                    PartNumber = code, Name = name, Category = S(F.Model), Status = asset ?? PartStatus.Active, Location = S(F.Rack),
                    Brand = S(F.Maker), Supplier = S(F.Supplier), Currency = cur, UnitPrice = price,
                    InitialStock = G(F.Initial).Length > 0 ? initial : now, MinStock = min, MaxStock = max,
                    Remarks = S(F.Remark), Specification = S(F.Spec), Unit = G(F.Unit).Length > 0 ? G(F.Unit) : "pcs",
                    Machine = S(F.Machine), IsReusable = Yes(G(F.Reusable)), Condition = cond
                };
                await _stock.CreatePartAsync(p, now, rateMap);
                existingParts[p.PartNumber] = p;
                res.Added++;
            }
        }
        await _db.SaveChangesAsync();
        if (ratesChanged) await _stock.RecalculateUsdAsync();

        if (opt.DryRun) await tx.RollbackAsync();
        else await tx.CommitAsync();
        return res;
    }
}
