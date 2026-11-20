using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SparePartControl.Models;

namespace SparePartControl.Services;

/// <summary>Filter master data: pencarian global + filter per header kolom.</summary>
public class PartFilter
{
    [FromQuery(Name = "q")] public string? Q { get; set; }
    [FromQuery(Name = "fNo")] public string? PartNumber { get; set; }
    [FromQuery(Name = "fName")] public string? Name { get; set; }
    [FromQuery(Name = "fCat")] public string? Category { get; set; }
    [FromQuery(Name = "fBrand")] public string? Brand { get; set; }
    [FromQuery(Name = "fSup")] public string? Supplier { get; set; }
    [FromQuery(Name = "fMc")] public string? Machine { get; set; }
    [FromQuery(Name = "fLoc")] public string? Location { get; set; }
    [FromQuery(Name = "fCur")] public Currency? Currency { get; set; }
    [FromQuery(Name = "fStatus")] public PartStatus? Status { get; set; }
    [FromQuery(Name = "fRem")] public string? Remark { get; set; }
    /// <summary>alert (empty+low+critical) | empty | low | critical | normal | over</summary>
    [FromQuery(Name = "fStock")] public string? Stock { get; set; }
    /// <summary>reusable | second | new</summary>
    [FromQuery(Name = "fFlag")] public string? Flag { get; set; }
    [FromQuery(Name = "sort")] public string? Sort { get; set; }
    [FromQuery(Name = "dir")] public string? Dir { get; set; }

    public bool AnyFilter => !string.IsNullOrWhiteSpace(Q + PartNumber + Name + Category + Brand + Supplier + Machine + Location + Remark + Stock + Flag)
                             || Currency.HasValue || Status.HasValue;

    public IQueryable<Part> Apply(IQueryable<Part> q)
    {
        static string L(string s) => $"%{s.Trim()}%";
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var k = L(Q);
            q = q.Where(p => EF.Functions.Like(p.PartNumber, k) || EF.Functions.Like(p.Name, k)
                || EF.Functions.Like(p.Specification ?? "", k) || EF.Functions.Like(p.Category ?? "", k)
                || EF.Functions.Like(p.Brand ?? "", k) || EF.Functions.Like(p.Supplier ?? "", k) || EF.Functions.Like(p.Machine ?? "", k)
                || EF.Functions.Like(p.Location ?? "", k) || EF.Functions.Like(p.Remarks ?? "", k));
        }
        if (!string.IsNullOrWhiteSpace(PartNumber)) { var k = L(PartNumber); q = q.Where(p => EF.Functions.Like(p.PartNumber, k)); }
        if (!string.IsNullOrWhiteSpace(Name)) { var k = L(Name); q = q.Where(p => EF.Functions.Like(p.Name, k) || EF.Functions.Like(p.Specification ?? "", k)); }
        if (!string.IsNullOrWhiteSpace(Category)) { var k = L(Category); q = q.Where(p => EF.Functions.Like(p.Category ?? "", k)); }
        if (!string.IsNullOrWhiteSpace(Brand)) { var k = L(Brand); q = q.Where(p => EF.Functions.Like(p.Brand ?? "", k)); }
        if (!string.IsNullOrWhiteSpace(Supplier)) { var k = L(Supplier); q = q.Where(p => EF.Functions.Like(p.Supplier ?? "", k)); }
        if (!string.IsNullOrWhiteSpace(Machine)) { var k = L(Machine); q = q.Where(p => EF.Functions.Like(p.Machine ?? "", k)); }
        if (!string.IsNullOrWhiteSpace(Remark)) { var k = L(Remark); q = q.Where(p => EF.Functions.Like(p.Remarks ?? "", k)); }
        if (!string.IsNullOrWhiteSpace(Location)) { var k = L(Location); q = q.Where(p => EF.Functions.Like(p.Location ?? "", k)); }
        if (Currency.HasValue) q = q.Where(p => p.Currency == Currency);
        if (Status.HasValue) q = q.Where(p => p.Status == Status);
        q = Stock switch
        {
            // Aturan sama dengan Part.LevelOf / kolom Status di Excel.
            "alert" => q.Where(p => p.StockQty <= p.MinStock),
            "empty" or "out" => q.Where(p => p.StockQty <= 0),
            "low" => q.Where(p => p.StockQty > 0 && p.StockQty < p.MinStock),
            "critical" => q.Where(p => p.StockQty > 0 && p.StockQty == p.MinStock),
            "normal" => q.Where(p => p.StockQty > 0 && p.StockQty > p.MinStock && (p.MaxStock <= 0 || p.StockQty <= p.MaxStock)),
            "over" => q.Where(p => p.StockQty > 0 && p.MaxStock > 0 && p.StockQty > p.MaxStock && p.StockQty > p.MinStock),
            _ => q
        };
        q = Flag switch
        {
            "reusable" => q.Where(p => p.IsReusable),
            "second" => q.Where(p => p.Condition == PartCondition.Second),
            "new" => q.Where(p => p.Condition == PartCondition.New),
            _ => q
        };
        var desc = Dir == "desc";
        q = Sort switch
        {
            "name" => desc ? q.OrderByDescending(p => p.Name) : q.OrderBy(p => p.Name),
            "cat" => desc ? q.OrderByDescending(p => p.Category) : q.OrderBy(p => p.Category),
            "brand" => desc ? q.OrderByDescending(p => p.Brand) : q.OrderBy(p => p.Brand),
            "sup" => desc ? q.OrderByDescending(p => p.Supplier) : q.OrderBy(p => p.Supplier),
            "mc" => desc ? q.OrderByDescending(p => p.Machine) : q.OrderBy(p => p.Machine),
            "loc" => desc ? q.OrderByDescending(p => p.Location) : q.OrderBy(p => p.Location),
            "stock" => desc ? q.OrderByDescending(p => p.StockQty) : q.OrderBy(p => p.StockQty),
            "max" => desc ? q.OrderByDescending(p => p.MaxStock) : q.OrderBy(p => p.MaxStock),
            "init" => desc ? q.OrderByDescending(p => p.InitialStock) : q.OrderBy(p => p.InitialStock),
            "min" => desc ? q.OrderByDescending(p => p.MinStock) : q.OrderBy(p => p.MinStock),
            "usd" => desc ? q.OrderByDescending(p => p.UnitPriceUsd) : q.OrderBy(p => p.UnitPriceUsd),
            "total" => desc ? q.OrderByDescending(p => p.StockQty * p.UnitPriceUsd) : q.OrderBy(p => p.StockQty * p.UnitPriceUsd),
            _ => desc ? q.OrderByDescending(p => p.PartNumber) : q.OrderBy(p => p.PartNumber)
        };
        return q;
    }
}
