using System.Text.Json;

namespace Madar.Core;

public sealed partial class Store
{
    private sealed record InvoiceLine(long Product, long Unit, string Name, string UnitName, long Quantity, long BaseQuantity, long Price, long Total, string Batch, string Expiry);

    private long InvoiceTotal(JsonElement input)
    {
        if (!input.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() is < 1 or > 200) throw Error("أضف الأصناف أولاً.");
        var total = checked(lines.EnumerateArray().Sum(line => RoundedMoney((decimal)Quantity(Decimal(line, "quantity"), false) * Money(Decimal(line, "price")) / 1000)));
        if (total <= 0 || total > 10_000_000_000) throw Error("إجمالي الفاتورة غير مسموح.");
        return total;
    }

    private long SaveInvoice(JsonElement input, bool post)
    {
        var kind = Str(input, "kind");
        if (kind is not ("Sale" or "Purchase")) throw Error("نوع الفاتورة غير صحيح.");
        if (kind == "Purchase") Owner();
        var request = Required(input, "requestId", "معرّف الفاتورة");
        if (!Guid.TryParse(request, out _)) throw Error("معرّف الفاتورة غير صحيح.");
        var old = Query("SELECT Id,Status,UserId FROM Documents WHERE RequestId=$request", ("request", request));
        if (old.Count > 0 && (string)old[0]["status"]! == "Posted") return Convert.ToInt64(old[0]["id"]);
        if (old.Count > 0 && Convert.ToInt64(old[0]["userId"]) != session!.Id && session.Role != "Owner") throw Error("المسودة تخص موظفاً آخر.");
        var warehouse = Id(input, true, "warehouseId");
        if (Long("SELECT COUNT(*) FROM Warehouses WHERE Id=$id", ("id", warehouse)) != 1) throw Error("المستودع غير موجود.");
        var party = Id(input, false, "partyId");
        if (party > 0 && Scalar("SELECT Kind FROM Parties WHERE Id=$id", ("id", party)) != (kind == "Sale" ? "Customer" : "Supplier")) throw Error("اختر الطرف المناسب لنوع الفاتورة.");
        if (!input.TryGetProperty("lines", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is < 1 or > 200) throw Error("أضف من صنف واحد إلى 200 صنف.");
        var lines = new List<InvoiceLine>();
        foreach (var item in items.EnumerateArray())
        {
            var unit = Id(item, true, "unitId");
            var found = Query("SELECT u.*,p.Name AS ProductName,p.Enabled FROM Units u JOIN Products p ON p.Id=u.ProductId WHERE u.Id=$id", ("id", unit));
            if (found.Count != 1 || Convert.ToInt64(found[0]["enabled"]) != 1) throw Error("أحد الأصناف غير موجود أو معطل.");
            var row = found[0];
            var quantity = Quantity(Decimal(item, "quantity"), false);
            var rawBase = (decimal)quantity * Convert.ToInt64(row["factor"]) / 1000;
            if (rawBase != decimal.Truncate(rawBase) || rawBase > 100_000_000_000) throw Error("الكمية بعد تحويل الوحدة تتجاوز الدقة أو الحجم المسموح.");
            var price = Money(Decimal(item, "price"));
            var total = RoundedMoney((decimal)quantity * price / 1000);
            var expiry = Str(item, "expiry");
            if (expiry != "" && !DateOnly.TryParseExact(expiry, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) throw Error("تاريخ الصلاحية غير صحيح.");
            lines.Add(new(Convert.ToInt64(row["productId"]), unit, (string)row["productName"]!, (string)row["name"]!, quantity, checked((long)rawBase), price, total, Str(item, "batch"), expiry));
        }
        var totalAmount = checked(lines.Sum(l => l.Total));
        if (totalAmount <= 0 || totalAmount > 10_000_000_000) throw Error("إجمالي الفاتورة يجب أن يكون موجباً وضمن الحد المسموح.");
        var paid = Bool(input, "full", true) ? totalAmount : Money(Decimal(input, "paid"));
        if (paid > totalAmount) throw Error("المدفوع أكبر من الإجمالي.");
        if (paid < totalAmount && party == 0) throw Error("الفاتورة الآجلة تحتاج عميلاً أو مورداً.");
        var due = Str(input, "dueDate");
        if (paid < totalAmount && due == "") due = DateOnly.FromDateTime(DateTime.Today).AddDays((int)Long("SELECT DueDays FROM Parties WHERE Id=$id", ("id", party))).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (due != "" && !DateOnly.TryParseExact(due, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) throw Error("تاريخ الاستحقاق غير صحيح.");
        var shift = post ? ActiveShift() : (long?)null;
        var id = old.Count == 0 ? 0 : Convert.ToInt64(old[0]["id"]);
        if (id == 0)
        {
            Run("INSERT INTO Documents(RequestId,Kind,Status,PartyId,WarehouseId,ShiftId,UserId,CreatedAt,DueDate,Total,Paid,Note) VALUES($request,$kind,'Draft',$party,$warehouse,$shift,$user,$date,$due,$total,$paid,$note)", ("request", request), ("kind", kind), ("party", party == 0 ? null : party), ("warehouse", warehouse), ("shift", shift), ("user", session!.Id), ("date", Now()), ("due", due), ("total", totalAmount), ("paid", paid), ("note", Str(input, "note")));
            id = LastId();
        }
        else
        {
            Run("DELETE FROM Lines WHERE DocumentId=$id", ("id", id));
            Run("UPDATE Documents SET Kind=$kind,PartyId=$party,WarehouseId=$warehouse,ShiftId=$shift,DueDate=$due,Total=$total,Paid=$paid,Note=$note WHERE Id=$id", ("kind", kind), ("party", party == 0 ? null : party), ("warehouse", warehouse), ("shift", shift), ("due", due), ("total", totalAmount), ("paid", paid), ("note", Str(input, "note")), ("id", id));
        }
        long cost = 0;
        foreach (var line in lines)
        {
            long lineCost = 0;
            if (post)
            {
                if (kind == "Purchase")
                {
                    IncreaseStock(line.Product, warehouse, line.BaseQuantity, line.Total, id, line.Batch, line.Expiry);
                    lineCost = line.Total;
                }
                else lineCost = ConsumeStock(line.Product, warehouse, line.BaseQuantity, false);
                cost = checked(cost + lineCost);
            }
            Run("INSERT INTO Lines(DocumentId,ProductId,UnitId,Name,UnitName,Quantity,BaseQuantity,Price,Total,Cost,Batch,Expiry) VALUES($document,$product,$unit,$name,$unitName,$quantity,$base,$price,$total,$cost,$batch,$expiry)", ("document", id), ("product", line.Product), ("unit", line.Unit), ("name", line.Name), ("unitName", line.UnitName), ("quantity", line.Quantity), ("base", line.BaseQuantity), ("price", line.Price), ("total", line.Total), ("cost", lineCost), ("batch", line.Batch), ("expiry", line.Expiry));
        }
        if (post)
        {
            if (kind == "Sale")
            {
                Entry(id, "Cash", paid, 0); Entry(id, "Receivable", totalAmount - paid, 0, party); Entry(id, "Sales", 0, totalAmount);
                Entry(id, "CostOfSales", cost, 0); Entry(id, "Inventory", 0, cost);
            }
            else
            {
                if (CashBalance() < paid) throw Error("رصيد الصندوق غير كافٍ للدفع.");
                Entry(id, "Inventory", totalAmount, 0); Entry(id, "Cash", 0, paid); Entry(id, "Payable", 0, totalAmount - paid, party);
            }
            Run("UPDATE Documents SET Status='Posted',Cost=$cost,CreatedAt=$date WHERE Id=$id", ("cost", cost), ("date", Now()), ("id", id));
            AssertBalanced(id);
        }
        return id;
    }

    private void IncreaseStock(long product, long warehouse, long quantity, long value, long document, string batch, string expiry)
    {
        if (expiry != "" && !DateOnly.TryParseExact(expiry, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) throw Error("تاريخ الصلاحية غير صحيح.");
        Run("INSERT INTO Inventory(ProductId,WarehouseId,Quantity,Value) VALUES($product,$warehouse,$quantity,$value) ON CONFLICT(ProductId,WarehouseId) DO UPDATE SET Quantity=Quantity+excluded.Quantity,Value=Value+excluded.Value", ("product", product), ("warehouse", warehouse), ("quantity", quantity), ("value", value));
        Run("INSERT INTO Lots(ProductId,WarehouseId,DocumentId,Batch,Expiry,Remaining) VALUES($product,$warehouse,$document,$batch,$expiry,$quantity)", ("product", product), ("warehouse", warehouse), ("document", document), ("batch", batch), ("expiry", expiry), ("quantity", quantity));
    }

    private long ConsumeStock(long product, long warehouse, long quantity, bool includeExpired)
    {
        var stock = Long("SELECT Quantity FROM Inventory WHERE ProductId=$p AND WarehouseId=$w", ("p", product), ("w", warehouse));
        var value = Long("SELECT Value FROM Inventory WHERE ProductId=$p AND WarehouseId=$w", ("p", product), ("w", warehouse));
        if (quantity > stock) throw Error("المخزون غير كافٍ لأحد الأصناف.");
        var lots = Query("SELECT Id,Remaining FROM Lots WHERE ProductId=$p AND WarehouseId=$w AND Remaining>0 AND ($all=1 OR Expiry='' OR Expiry>=$today) ORDER BY CASE WHEN Expiry='' THEN '9999' ELSE Expiry END,Id", ("p", product), ("w", warehouse), ("all", includeExpired), ("today", DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        if (lots.Sum(l => Convert.ToInt64(l["remaining"])) < quantity) throw Error("الكمية المتاحة تشمل دفعات منتهية؛ عالج الصلاحية قبل البيع.");
        var remaining = quantity;
        foreach (var lot in lots)
        {
            var take = Math.Min(remaining, Convert.ToInt64(lot["remaining"]));
            Run("UPDATE Lots SET Remaining=Remaining-$take WHERE Id=$id", ("take", take), ("id", lot["id"]));
            remaining -= take; if (remaining == 0) break;
        }
        var cost = quantity == stock ? value : RoundedMoney((decimal)value * quantity / stock);
        Run("UPDATE Inventory SET Quantity=Quantity-$q,Value=Value-$cost WHERE ProductId=$p AND WarehouseId=$w", ("q", quantity), ("cost", cost), ("p", product), ("w", warehouse));
        return cost;
    }

    private long PostCash(JsonElement input)
    {
        var kind = Str(input, "kind");
        if (kind is not ("Expense" or "Capital" or "Receipt" or "Payment")) throw Error("نوع الحركة غير صحيح.");
        if (kind != "Receipt") Owner();
        var request = Required(input, "requestId", "معرّف العملية");
        if (!Guid.TryParse(request, out _)) throw Error("معرّف العملية غير صحيح.");
        var existing = Long("SELECT Id FROM Documents WHERE RequestId=$request AND Status='Posted'", ("request", request));
        if (existing > 0) return existing;
        var amount = Money(Decimal(input, "amount"));
        if (amount <= 0) throw Error("أدخل مبلغاً موجباً.");
        var party = Id(input, false, "partyId");
        var note = Required(input, "note", "وصف الحركة");
        var shift = ActiveShift();
        if (kind is "Receipt" or "Payment")
        {
            if (Scalar("SELECT Kind FROM Parties WHERE Id=$id", ("id", party)) != (kind == "Receipt" ? "Customer" : "Supplier")) throw Error("اختر الطرف الصحيح.");
            if (amount > PartyBalance(party, kind == "Receipt" ? "Receivable" : "Payable")) throw Error("المبلغ يتجاوز المستحق.");
        }
        else party = 0;
        if (kind is "Payment" or "Expense" && CashBalance() < amount) throw Error("رصيد الصندوق غير كافٍ.");
        var id = CashDocument(kind, amount, party, note, shift, request);
        if (kind is "Receipt" or "Payment") Allocate(id, party, amount);
        return id;
    }

    private long CashDocument(string kind, long amount, long party, string note, long shift, string request)
    {
        Run("INSERT INTO Documents(RequestId,Kind,Status,PartyId,WarehouseId,ShiftId,UserId,CreatedAt,Total,Paid,Note) VALUES($request,$kind,'Posted',$party,1,$shift,$user,$date,$amount,$amount,$note)", ("request", request), ("kind", kind), ("party", party == 0 ? null : party), ("shift", shift), ("user", session!.Id), ("date", Now()), ("amount", amount), ("note", note));
        var id = LastId();
        if (kind is "Capital" or "Receipt" or "CashSurplus")
        { Entry(id, "Cash", amount, 0); Entry(id, kind == "Capital" ? "Capital" : kind == "CashSurplus" ? "CashSurplus" : "Receivable", 0, amount, party); }
        else { Entry(id, kind == "Expense" ? "Expense" : "Payable", amount, 0, party); Entry(id, "Cash", 0, amount); }
        AssertBalanced(id); return id;
    }

    private void Allocate(long payment, long party, long amount)
    {
        var rows = Query("SELECT d.Id,d.Total-d.Paid-COALESCE((SELECT SUM(a.Amount) FROM Allocations a WHERE a.InvoiceId=d.Id),0) AS Remaining FROM Documents d WHERE d.PartyId=$party AND d.Status='Posted' AND d.Kind IN ('Sale','Purchase') ORDER BY CASE WHEN d.DueDate='' THEN d.CreatedAt ELSE d.DueDate END,d.Id", ("party", party));
        foreach (var row in rows)
        {
            var take = Math.Min(amount, Convert.ToInt64(row["remaining"]));
            if (take <= 0) continue;
            Run("INSERT INTO Allocations(PaymentId,InvoiceId,Amount) VALUES($payment,$invoice,$amount)", ("payment", payment), ("invoice", row["id"]), ("amount", take));
            amount -= take; if (amount == 0) break;
        }
        if (amount != 0) throw Error("تعذر توزيع التسوية على الفواتير؛ لم تُحفظ العملية.");
    }

    private object? CurrentShift() => Query("SELECT s.*,u.Name AS Employee FROM Shifts s JOIN Users u ON u.Id=s.UserId WHERE s.Status IN ('Open','Review') ORDER BY s.Id DESC LIMIT 1").FirstOrDefault();
    private long ActiveShift()
    {
        var shifts = Query("SELECT Id,UserId,Status FROM Shifts WHERE Status IN ('Open','Review') LIMIT 1");
        if (shifts.Count != 1 || (string)shifts[0]["status"]! != "Open") throw Error("افتح وردية الصندوق أولاً، أو اعتمد الوردية المعلقة.");
        if (Convert.ToInt64(shifts[0]["userId"]) != session!.Id && session.Role != "Owner") throw Error("الوردية مفتوحة لموظف آخر.");
        return Convert.ToInt64(shifts[0]["id"]);
    }
    private long OpenShift()
    {
        if (CurrentShift() is not null) throw Error("توجد وردية مفتوحة أو قيد المراجعة.");
        Run("INSERT INTO Shifts(UserId,OpenedAt,OpeningCash,Status) VALUES($user,$date,$cash,'Open')", ("user", session!.Id), ("date", Now()), ("cash", CashBalance()));
        return LastId();
    }
    private object CloseShift(JsonElement input)
    {
        var shift = ActiveShift();
        var counted = Money(Decimal(input, "counted"));
        var expected = CashBalance();
        var reason = Str(input, "reason");
        if (counted != expected && reason.Length == 0) throw Error("اكتب سبب فرق الصندوق للمراجعة.");
        Run("UPDATE Shifts SET CountedCash=$counted,ExpectedCash=$expected,ClosedAt=$date,Reason=$reason,Status=$status WHERE Id=$id", ("counted", counted), ("expected", expected), ("date", Now()), ("reason", reason), ("status", counted == expected ? "Closed" : "Review"), ("id", shift));
        return new { id = shift, difference = counted - expected, needsApproval = counted != expected };
    }
    private long ApproveShift(JsonElement input)
    {
        Owner(); var id = Id(input);
        var row = Query("SELECT * FROM Shifts WHERE Id=$id AND Status='Review'", ("id", id)).SingleOrDefault() ?? throw Error("الوردية غير موجودة أو لا تحتاج مراجعة.");
        var reason = Required(input, "reason", "سبب اعتماد الفرق");
        var difference = Convert.ToInt64(row["countedCash"]) - Convert.ToInt64(row["expectedCash"]);
        if (CashBalance() != Convert.ToInt64(row["expectedCash"])) throw Error("تغير رصيد الصندوق بعد الإغلاق؛ راجع السجل.");
        if (difference != 0) CashDocument(difference > 0 ? "CashSurplus" : "Expense", Math.Abs(difference), 0, "تسوية فرق وردية: " + reason, id, Guid.NewGuid().ToString());
        Run("UPDATE Shifts SET Status='Closed',ApprovedBy=$user,Reason=Reason || ' / اعتماد: ' || $reason WHERE Id=$id", ("user", session!.Id), ("reason", reason), ("id", id));
        return id;
    }

    private long AdjustStock(JsonElement input)
    {
        Owner();
        var product = Id(input, true, "productId"); var warehouse = Id(input, true, "warehouseId");
        var counted = Quantity(Decimal(input, "quantity")); var reason = Required(input, "reason", "سبب التسوية");
        var current = Long("SELECT Quantity FROM Inventory WHERE ProductId=$p AND WarehouseId=$w", ("p", product), ("w", warehouse));
        var delta = counted - current;
        if (delta == 0) throw Error("الكمية الفعلية تطابق المخزون الحالي.");
        var shift = ActiveShift();
        Run("INSERT INTO Documents(RequestId,Kind,Status,WarehouseId,ShiftId,UserId,CreatedAt,Note) VALUES($request,'Adjustment','Posted',$warehouse,$shift,$user,$date,$note)", ("request", Guid.NewGuid().ToString()), ("warehouse", warehouse), ("shift", shift), ("user", session!.Id), ("date", Now()), ("note", reason));
        var id = LastId();
        long value;
        if (delta < 0) { value = ConsumeStock(product, warehouse, -delta, true); Entry(id, "Expense", value, 0); Entry(id, "Inventory", 0, value); }
        else
        {
            value = RoundedMoney((decimal)delta * Money(Decimal(input, "unitCost")) / 1000);
            IncreaseStock(product, warehouse, delta, value, id, Str(input, "batch"), Str(input, "expiry"));
            Entry(id, "Inventory", value, 0); Entry(id, "InventorySurplus", 0, value);
        }
        var productRow = Query("SELECT Name,BaseUnit FROM Products WHERE Id=$id", ("id", product)).Single();
        var unit = Long("SELECT Id FROM Units WHERE ProductId=$id AND Factor=1000 ORDER BY Id LIMIT 1", ("id", product));
        Run("INSERT INTO Lines(DocumentId,ProductId,UnitId,Name,UnitName,Quantity,BaseQuantity,Price,Total,Cost) VALUES($document,$product,$unit,$name,$unitName,$quantity,$quantity,0,0,$cost)", ("document", id), ("product", product), ("unit", unit), ("name", productRow["name"]), ("unitName", productRow["baseUnit"]), ("quantity", Math.Abs(delta)), ("cost", value));
        Run("UPDATE Documents SET Cost=$value WHERE Id=$id", ("value", value), ("id", id));
        AssertBalanced(id); return id;
    }

    private void Entry(long document, string account, long debit, long credit, long party = 0)
    {
        if (debit == 0 && credit == 0) return;
        Run("INSERT INTO Ledger(DocumentId,Account,PartyId,Debit,Credit) VALUES($document,$account,$party,$debit,$credit)", ("document", document), ("account", account), ("party", party == 0 ? null : party), ("debit", debit), ("credit", credit));
    }
    private void AssertBalanced(long id)
    {
        if (Long("SELECT COALESCE(SUM(Debit-Credit),0) FROM Ledger WHERE DocumentId=$id", ("id", id)) != 0 || CashBalance() < 0) throw Error("القيد غير متوازن أو رصيد الصندوق سالب؛ لم تُحفظ العملية.");
    }
}
