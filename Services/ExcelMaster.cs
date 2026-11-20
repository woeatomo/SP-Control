using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using SparePartControl.Models;

namespace SparePartControl.Services;

/// <summary>Tabel mentah hasil baca file (Excel atau CSV) sebelum dipetakan ke Part.</summary>
public class RawTable
{
    public List<string> Headers { get; set; } = new();
    public List<string[]> Rows { get; set; } = new();
    /// <summary>Nomor baris (di file) untuk Rows[0]; dipakai di pesan error.</summary>
    public int FirstRowNumber { get; set; } = 2;
    public string Source { get; set; } = "";
    /// <summary>true bila angka memakai format Indonesia (1.234,56) — CSV dari Excel berbahasa Indonesia.</summary>
    public bool IndonesianNumbers { get; set; }
}

/// <summary>
/// Baca/tulis Excel dengan susunan kolom yang sama seperti file "MASTER STOCK APP" (tabel goodsCode … Remark).
/// </summary>
public static class ExcelMaster
{
    /// <summary>Kolom export, urutan sama dengan Excel MASTER STOCK + kolom tambahan aplikasi di belakang.</summary>
    public static readonly string[] ExportHeaders =
    {
        "goodsCode", "goodsName", "Model", "stockCategory", "rackName", "makerName", "suppName", "currency",
        "unit_price", "unit_USD", "initialStock", "minStock", "maxStock", "nowStock", "Status", "StockValue", "Remark",
        "specification", "unit", "machine", "reusable", "condition"
    };

    /// <summary>Nama header dinormalkan: huruf kecil, hanya huruf/angka ("unit_price" → "unitprice").</summary>
    public static string Norm(string? h) => Regex.Replace((h ?? "").Trim().TrimStart('﻿').ToLowerInvariant(), "[^a-z0-9]", "");

    public static bool IsCodeHeader(string norm) => norm is "goodscode" or "partnumber" or "partcode" or "partno" or "kodepart" or "code";

    public static string LevelText(StockLevel l) => l.ToString().ToUpperInvariant();

    public static byte[] Export(IEnumerable<Part> parts, Dictionary<Currency, decimal> unitsPerUsd)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("MASTER STOCK");
        for (var c = 0; c < ExportHeaders.Length; c++) ws.Cell(1, c + 1).Value = ExportHeaders[c];
        var r = 2;
        foreach (var p in parts)
        {
            object?[] v =
            {
                p.PartNumber, p.Name, p.Category, p.Status == PartStatus.Active ? "ACTIVE" : "INACTIVE", p.Location, p.Brand, p.Supplier,
                p.Currency.ToString(), p.UnitPrice, p.UnitPriceUsd, p.InitialStock, p.MinStock, p.MaxStock, p.StockQty,
                LevelText(p.Level), Math.Round(p.TotalValueUsd, 2), p.Remarks,
                p.Specification, p.Unit, p.Machine, p.IsReusable ? "Yes" : "No", p.Condition == PartCondition.Second ? "2nd" : "New"
            };
            for (var c = 0; c < v.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (v[c])
                {
                    case decimal d: cell.Value = d; break;
                    case string s: cell.Value = s; break;
                }
            }
            r++;
        }
        var table = ws.Range(1, 1, Math.Max(2, r - 1), ExportHeaders.Length).CreateTable("tblMaster");
        table.Theme = XLTableTheme.TableStyleMedium2;
        foreach (var col in new[] { 9, 10, 16 }) ws.Column(col).Style.NumberFormat.Format = "#,##0.00";
        foreach (var col in new[] { 11, 12, 13, 14 }) ws.Column(col).Style.NumberFormat.Format = "#,##0.##";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 300), 8, 60);

        // Kurs, susunan sama dengan tblRate di Excel (Rate_to_USD = pengali ke USD).
        var rs = wb.AddWorksheet("RATE");
        rs.Cell(1, 1).Value = "currency"; rs.Cell(1, 2).Value = "Rate_to_USD"; rs.Cell(1, 3).Value = "Units_per_USD";
        var i = 2;
        foreach (var (cur, u) in unitsPerUsd.OrderBy(k => k.Key))
        {
            rs.Cell(i, 1).Value = cur.ToString();
            rs.Cell(i, 2).Value = u == 0 ? 0 : 1 / u;
            rs.Cell(i, 3).Value = u;
            i++;
        }
        rs.Range(1, 1, i - 1, 3).CreateTable("tblRate");
        rs.Column(2).Style.NumberFormat.Format = "0.000000000";
        rs.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string CellText(IXLCell cell)
    {
        // Sel berisi rumus (mis. XLOOKUP) dibaca dari nilai tersimpan terakhir, tidak dihitung ulang.
        var v = cell.HasFormula ? cell.CachedValue : cell.Value;
        return v.Type switch
        {
            XLDataType.Number => v.GetNumber().ToString("0.############", CultureInfo.InvariantCulture),
            XLDataType.Text => v.GetText().Trim(),
            XLDataType.Boolean => v.GetBoolean() ? "Yes" : "No",
            XLDataType.DateTime => v.GetDateTime().ToString("yyyy-MM-dd"),
            _ => ""
        };
    }

    /// <summary>Cari tabel di workbook yang header-nya memenuhi syarat (mis. ada kolom goodsCode).</summary>
    private static RawTable? FindTable(XLWorkbook wb, Func<List<string>, bool> isHeader)
    {
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed();
            if (used == null) continue;
            var lastRow = used.LastRow().RowNumber();
            var lastCol = used.LastColumn().ColumnNumber();
            var firstCol = used.FirstColumn().ColumnNumber();
            for (var hr = used.FirstRow().RowNumber(); hr <= Math.Min(lastRow, used.FirstRow().RowNumber() + 30); hr++)
            {
                var headers = Enumerable.Range(firstCol, lastCol - firstCol + 1).Select(c => CellText(ws.Cell(hr, c))).ToList();
                if (!isHeader(headers.Select(Norm).ToList())) continue;
                var t = new RawTable { Headers = headers, FirstRowNumber = hr + 1, Source = ws.Name };
                for (var r = hr + 1; r <= lastRow; r++)
                {
                    var row = Enumerable.Range(firstCol, lastCol - firstCol + 1).Select(c => CellText(ws.Cell(r, c))).ToArray();
                    t.Rows.Add(row); // baris kosong tetap dimasukkan supaya nomor baris di pesan error tepat
                }
                while (t.Rows.Count > 0 && t.Rows[^1].All(x => x.Length == 0)) t.Rows.RemoveAt(t.Rows.Count - 1);
                return t;
            }
        }
        return null;
    }

    public static (RawTable? Parts, RawTable? Rates) ReadWorkbook(Stream s)
    {
        using var wb = new XLWorkbook(s);
        var parts = FindTable(wb, h => h.Any(IsCodeHeader) && h.Any(x => x is "goodsname" or "name" or "partname" or "namapart"));
        var rates = FindTable(wb, h => h.Contains("currency") && h.Any(x => x.StartsWith("ratetousd") || x == "rate") && !h.Any(IsCodeHeader));
        return (parts, rates);
    }

    public static RawTable ReadCsv(string text)
    {
        var rows = Csv.Parse(text);
        var firstLine = text.Split('\n')[0];
        var t = new RawTable
        {
            Source = "CSV",
            IndonesianNumbers = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ','),
            Headers = rows.Count > 0 ? rows[0].ToList() : new(),
            Rows = rows.Skip(1).ToList()
        };
        return t;
    }

    /// <summary>
    /// Baca angka dari teks, mendukung "18.5", "85000", "40.000,00" (format Indonesia) dan "40,000.00".
    /// </summary>
    public static bool TryNumber(string? raw, bool indonesian, out decimal value)
    {
        value = 0;
        var s = (raw ?? "").Trim().Replace(" ", "").Replace(" ", "");
        if (s.Length == 0) return true;
        int lastDot = s.LastIndexOf('.'), lastComma = s.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
            s = lastComma > lastDot ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (lastComma >= 0)
            s = !indonesian && Regex.IsMatch(s, @"^-?\d{1,3}(,\d{3})+$") ? s.Replace(",", "") : s.Replace(',', '.');
        else if (lastDot >= 0 && indonesian && Regex.IsMatch(s, @"^-?\d{1,3}(\.\d{3})+$"))
            s = s.Replace(".", "");
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
