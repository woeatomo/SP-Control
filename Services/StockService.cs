using Microsoft.EntityFrameworkCore;
using SparePartControl.Data;
using SparePartControl.Models;

namespace SparePartControl.Services;

public enum EntryMode { In, Issue, Loan }

public class EntryHeader
{
    public DateTime Date { get; set; } = DateTime.Now;
    public string? Pic { get; set; }
    public string? Department { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DateTime? DueDate { get; set; }
}

public class EntryLine
{
    public int PartId { get; set; }
    public decimal Qty { get; set; }
    public string? Note { get; set; }
}

public record OpResult(bool Ok, string Message)
{
    public static OpResult Fail(string m) => new(false, m);
    public static OpResult Success(string m) => new(true, m);
}

/// <summary>Semua perubahan stok lewat service ini supaya kartu stok selalu konsisten.</summary>
public class StockService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly SiteContext _site;
    public StockService(AppDbContext db, IHttpContextAccessor http, SiteContext site) { _db = db; _http = http; _site = site; }

    private readonly Dictionary<int, string> _siteCodes = new();
    /// <summary>Kode site (dipakai di nomor dokumen).</summary>
    public async Task<string> SiteCodeAsync(int siteId)
    {
        if (!_siteCodes.TryGetValue(siteId, out var c))
            _siteCodes[siteId] = c = await _db.Sites.Where(s => s.Id == siteId).Select(s => s.Code).FirstOrDefaultAsync() ?? "S" + siteId;
        return c;
    }

    private string? CurrentUser => _http.HttpContext?.User.Identity?.Name;

    /// <summary>
    /// Aturan part 2nd used: part number = part number original + "-2ND".
    /// Kondisi 2nd tanpa akhiran → akhiran ditambahkan; part number berakhiran -2ND → kondisi otomatis 2nd.
    /// </summary>
    public static void NormalizeSecond(Part p)
    {
        p.PartNumber = (p.PartNumber ?? "").Trim();
        if (p.PartNumber.Length == 0) return;
        if (p.HasSecondSuffix)
        {
            p.PartNumber = p.OriginalPartNumber + Part.SecondSuffix; // seragamkan huruf besar akhiran
            p.Condition = PartCondition.Second;
        }
        else if (p.Condition == PartCondition.Second) p.PartNumber += Part.SecondSuffix;
    }

    public static string Fmt(decimal q) => q.ToString("#,0.##");

    public async Task<Dictionary<Currency, decimal>> GetRatesAsync()
    {
        var rates = await _db.ExchangeRates.AsNoTracking().ToDictionaryAsync(r => r.Currency, r => r.UnitsPerUsd);
        foreach (var c in Enum.GetValues<Currency>())
            if (!rates.ContainsKey(c) || rates[c] <= 0) rates[c] = 1;
        rates[Currency.USD] = 1;
        return rates;
    }

    public static decimal ToUsd(decimal price, Currency cur, Dictionary<Currency, decimal> rates)
        => cur == Currency.USD ? price : Math.Round(price / rates[cur], 4);

    /// <summary>Hitung ulang kolom harga USD seluruh master data (dipanggil setelah kurs diubah).</summary>
    public async Task<int> RecalculateUsdAsync()
    {
        var rates = await GetRatesAsync();
        var parts = await _db.Parts.IgnoreQueryFilters().ToListAsync(); // kurs berlaku untuk semua site
        foreach (var p in parts) p.UnitPriceUsd = ToUsd(p.UnitPrice, p.Currency, rates);
        await _db.SaveChangesAsync();
        return parts.Count;
    }

    /// <summary>Nomor dokumen per site: {PREFIX}-{SITE}-{yyMMdd}-{001}, mis. ISS-PC1-261120-001.</summary>
    public async Task<string> NextDocNoAsync(string prefix, DateTime date, int siteId)
    {
        var head = $"{prefix}-{await SiteCodeAsync(siteId)}-{date:yyMMdd}-";
        var existing = await _db.Transactions.IgnoreQueryFilters().Where(t => t.SiteId == siteId && t.DocNo.StartsWith(head))
            .Select(t => t.DocNo).Distinct().ToListAsync();
        var max = existing.Select(d => int.TryParse(d[head.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return head + (max + 1).ToString("000");
    }

    private StockTransaction Move(Part part, TxType type, decimal signedQty, DateTime date, string docNo,
        string? pic, string? dept, string? reference, string? notes, int? loanId = null)
    {
        part.StockQty += signedQty;
        part.UpdatedAt = DateTime.Now;
        var tx = new StockTransaction
        {
            Date = date, Type = type, DocNo = docNo, Part = part, SiteId = part.SiteId, Qty = signedQty,
            BalanceAfter = part.StockQty, UnitPriceUsd = part.UnitPriceUsd,
            Pic = pic?.Trim(), Department = dept?.Trim(), Reference = reference?.Trim(), Notes = notes?.Trim(),
            LoanId = loanId, CreatedBy = CurrentUser
        };
        _db.Transactions.Add(tx);
        return tx;
    }

    /// <summary>Posting form Input (stok masuk), Issued (dipakai maintenance) atau Loan (dipinjam).</summary>
    public async Task<OpResult> PostEntryAsync(EntryMode mode, EntryHeader h, List<EntryLine> lines)
    {
        lines = lines.Where(l => l.PartId > 0).ToList();
        if (lines.Count == 0) return OpResult.Fail("Belum ada part yang ditambahkan.");
        if (lines.Any(l => l.Qty <= 0)) return OpResult.Fail("Qty harus lebih dari 0.");
        if (mode != EntryMode.In && string.IsNullOrWhiteSpace(h.Pic))
            return OpResult.Fail("Nama PIC wajib diisi.");

        await using var dbTx = await _db.Database.BeginTransactionAsync();
        var ids = lines.Select(l => l.PartId).Distinct().ToList();
        var parts = await _db.Parts.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        if (parts.Count != ids.Count) return OpResult.Fail("Ada part yang tidak ditemukan di master data.");

        if (mode != EntryMode.In)
        {
            foreach (var g in lines.GroupBy(l => l.PartId))
            {
                var p = parts[g.Key];
                var need = g.Sum(x => x.Qty);
                if (need > p.StockQty)
                    return OpResult.Fail($"Stok {p.PartNumber} tidak cukup (stok {Fmt(p.StockQty)} {p.Unit}, diminta {Fmt(need)}).");
            }
        }

        var (prefix, type) = mode switch
        {
            EntryMode.In => ("IN", TxType.In),
            EntryMode.Issue => ("ISS", TxType.Issue),
            _ => ("LOAN", TxType.LoanOut)
        };
        // Satu dokumen = satu site.
        var siteId = parts.Values.First().SiteId;
        if (parts.Values.Any(p => p.SiteId != siteId)) return OpResult.Fail("Semua part dalam satu dokumen harus dari site yang sama.");
        if (_site.Current != null && _site.Current.Id != siteId) return OpResult.Fail("Part bukan milik site aktif.");
        var docNo = await NextDocNoAsync(prefix, h.Date, siteId);

        foreach (var l in lines)
        {
            var p = parts[l.PartId];
            var note = string.Join(" · ", new[] { h.Notes, l.Note }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var signed = mode == EntryMode.In ? l.Qty : -l.Qty;
            var tx = Move(p, type, signed, h.Date, docNo, h.Pic, h.Department, h.Reference, note);
            if (mode == EntryMode.Loan)
            {
                var loan = new Loan
                {
                    DocNo = docNo, Part = p, SiteId = p.SiteId, Qty = l.Qty, Borrower = h.Pic!.Trim(), Department = h.Department?.Trim(),
                    LoanDate = h.Date, DueDate = h.DueDate, Notes = note
                };
                _db.Loans.Add(loan);
                await _db.SaveChangesAsync();
                tx.LoanId = loan.Id;
            }
        }
        await _db.SaveChangesAsync();
        await dbTx.CommitAsync();
        return OpResult.Success(docNo);
    }

    /// <summary>Pengembalian loan. usedQty = bagian yang ternyata terpakai → dicatat sebagai Issued.</summary>
    public async Task<OpResult> ReturnLoanAsync(int loanId, decimal returnQty, decimal usedQty, DateTime date, string? notes)
    {
        if (returnQty < 0 || usedQty < 0 || returnQty + usedQty <= 0)
            return OpResult.Fail("Isi qty kembali dan/atau qty terpakai.");
        await using var dbTx = await _db.Database.BeginTransactionAsync();
        var loan = await _db.Loans.Include(l => l.Part).FirstOrDefaultAsync(l => l.Id == loanId);
        if (loan == null) return OpResult.Fail("Data loan tidak ditemukan.");
        if (loan.Status == LoanStatus.Closed) return OpResult.Fail("Loan ini sudah selesai.");
        if (returnQty + usedQty > loan.Outstanding)
            return OpResult.Fail($"Melebihi sisa pinjaman ({Fmt(loan.Outstanding)} {loan.Part.Unit}).");

        // Semua qty loan kembali ke stok dulu; bagian yang terpakai lalu dikeluarkan lagi sebagai Issued
        // supaya laporan pemakaian tetap akurat.
        var total = returnQty + usedQty;
        Move(loan.Part, TxType.LoanReturn, total, date, loan.DocNo, loan.Borrower, loan.Department, null, notes, loan.Id);
        if (usedQty > 0)
        {
            var docNo = await NextDocNoAsync("ISS", date, loan.SiteId);
            Move(loan.Part, TxType.Issue, -usedQty, date, docNo, loan.Borrower, loan.Department, loan.DocNo,
                "Dari loan (terpakai)" + (string.IsNullOrWhiteSpace(notes) ? "" : " · " + notes), loan.Id);
        }
        loan.ReturnedQty += returnQty;
        loan.UsedQty += usedQty;
        if (loan.Outstanding <= 0)
        {
            loan.Status = LoanStatus.Closed;
            loan.ClosedDate = date;
        }
        await _db.SaveChangesAsync();
        await dbTx.CommitAsync();
        return OpResult.Success($"Loan {loan.DocNo} diperbarui.");
    }

    /// <summary>Buat part baru; stok awal dicatat sebagai transaksi masuk.</summary>
    public async Task<Part> CreatePartAsync(Part part, decimal initialStock, Dictionary<Currency, decimal>? rates = null)
    {
        rates ??= await GetRatesAsync();
        if (part.SiteId == 0)
            part.SiteId = _site.Current?.Id ?? throw new InvalidOperationException("Pilih site terlebih dahulu.");
        NormalizeSecond(part);
        if (part.InitialStock == 0) part.InitialStock = initialStock;
        part.UnitPriceUsd = ToUsd(part.UnitPrice, part.Currency, rates);
        part.StockQty = 0;
        part.CreatedAt = part.UpdatedAt = DateTime.Now;
        _db.Parts.Add(part);
        if (initialStock > 0)
            Move(part, TxType.In, initialStock, DateTime.Now, "SALDO-AWAL", null, null, null, "Saldo awal");
        await _db.SaveChangesAsync();
        return part;
    }

    /// <summary>Samakan stok part dengan qty target; selisihnya dicatat sebagai Adjustment (belum SaveChanges).</summary>
    public bool SetStock(Part part, decimal target, string docNo, string note)
    {
        var diff = target - part.StockQty;
        if (diff == 0) return false;
        Move(part, TxType.Adjustment, diff, DateTime.Now, docNo, null, null, null, note);
        return true;
    }

    /// <summary>Tutup sesi stock checking; opsional posting selisih sebagai adjustment stok.</summary>
    public async Task<OpResult> CloseStockCheckAsync(int sessionId, bool postAdjustment)
    {
        await using var dbTx = await _db.Database.BeginTransactionAsync();
        var s = await _db.StockCheckSessions.Include(x => x.Items).ThenInclude(i => i.Part)
            .FirstOrDefaultAsync(x => x.Id == sessionId);
        if (s == null) return OpResult.Fail("Sesi tidak ditemukan.");
        if (s.Status == StockCheckStatus.Closed) return OpResult.Fail("Sesi sudah ditutup.");
        var n = 0;
        if (postAdjustment)
        {
            var docNo = $"SC-{await SiteCodeAsync(s.SiteId)}-{s.Period}";
            foreach (var i in s.Items.Where(i => i.Variance is not null and not 0))
            {
                Move(i.Part, TxType.Adjustment, i.Variance!.Value, DateTime.Now, docNo, s.CheckedBy, null, null,
                    $"Selisih stock checking {s.Period}" + (string.IsNullOrWhiteSpace(i.Note) ? "" : " · " + i.Note));
                n++;
            }
        }
        s.Status = StockCheckStatus.Closed;
        s.ClosedAt = DateTime.Now;
        s.AdjustmentPosted = postAdjustment;
        await _db.SaveChangesAsync();
        await dbTx.CommitAsync();
        return OpResult.Success(postAdjustment ? $"Sesi ditutup, {n} adjustment diposting." : "Sesi ditutup tanpa adjustment.");
    }
}
