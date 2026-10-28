using System.Text.Json;

namespace Madar.Core;

public sealed partial class Store
{
    private object Products(JsonElement input)
    {
        var warehouse = Id(input, false, "warehouse");
        var rows = Query("""
            SELECT p.*,COALESCE(SUM(i.Quantity),0) AS Quantity,COALESCE(SUM(i.Value),0) AS Value,
                (SELECT Price FROM Units WHERE ProductId=p.Id AND Factor=1000 ORDER BY Id LIMIT 1) AS Price
            FROM Products p LEFT JOIN Inventory i ON i.ProductId=p.Id AND ($warehouse=0 OR i.WarehouseId=$warehouse)
            WHERE (p.Name LIKE $search OR p.Barcode LIKE $search)
              AND ($category='' OR p.Category=$category)
            GROUP BY p.Id ORDER BY p.Name LIMIT 2000
            """, ("warehouse", warehouse), ("search", "%" + Str(input, "search") + "%"), ("category", Str(input, "category")));
        if (Bool(input, "low")) rows = rows.Where(r => Convert.ToInt64(r["quantity"]) <= Convert.ToInt64(r["minimum"])).ToList();
        return new
        {
            items = rows,
            units = Query("SELECT * FROM Units ORDER BY Factor,Id"),
            categories = Query("SELECT DISTINCT Category AS Name FROM Products WHERE Category<>'' ORDER BY Category"),
            warehouses = Query("SELECT * FROM Warehouses ORDER BY Id"),
            lots = Query("SELECT l.*,p.Name,p.BaseUnit,w.Name AS Warehouse FROM Lots l JOIN Products p ON p.Id=l.ProductId JOIN Warehouses w ON w.Id=l.WarehouseId WHERE l.Remaining>0 AND ($warehouse=0 OR l.WarehouseId=$warehouse) ORDER BY CASE WHEN l.Expiry='' THEN '9999' ELSE l.Expiry END LIMIT 2000", ("warehouse", warehouse)),
            prices = Query("SELECT h.Id,h.CreatedAt,h.OldPrice,h.NewPrice,u.ProductId,u.Name AS UnitName FROM PriceHistory h JOIN Units u ON u.Id=h.UnitId ORDER BY h.Id DESC LIMIT 300")
        };
    }

    private long SaveProduct(JsonElement input)
    {
        Owner();
        var id = Id(input, false);
        var name = Required(input, "name", "اسم المنتج");
        var unit = Required(input, "baseUnit", "الوحدة الأساسية");
        var barcode = Str(input, "barcode");
        var minimum = Quantity(Decimal(input, "minimum"));
        var price = Money(Decimal(input, "price"));
        if (id == 0)
        {
            Run("INSERT INTO Products(Name,Barcode,Category,BaseUnit,Minimum,Enabled) VALUES($name,$code,$category,$unit,$minimum,$enabled)", ("name", name), ("code", barcode), ("category", Str(input, "category")), ("unit", unit), ("minimum", minimum), ("enabled", Bool(input, "enabled", true)));
            id = LastId();
            Run("INSERT INTO Units(ProductId,Name,Factor,Price) VALUES($id,$name,1000,$price)", ("id", id), ("name", unit), ("price", price));
        }
        else
        {
            if (Long("SELECT COUNT(*) FROM Products WHERE Id=$id", ("id", id)) != 1) throw Error("المنتج غير موجود.");
            var originalUnit = Scalar("SELECT BaseUnit FROM Products WHERE Id=$id", ("id", id));
            if (unit != originalUnit) throw Error("لا يمكن تغيير الوحدة الأساسية؛ أضف وحدة تحويل جديدة.");
            Run("UPDATE Products SET Name=$name,Barcode=$code,Category=$category,Minimum=$minimum,Enabled=$enabled WHERE Id=$id", ("name", name), ("code", barcode), ("category", Str(input, "category")), ("minimum", minimum), ("enabled", Bool(input, "enabled", true)), ("id", id));
            var unitId = Long("SELECT Id FROM Units WHERE ProductId=$id AND Factor=1000 ORDER BY Id LIMIT 1", ("id", id));
            ChangePrice(unitId, price);
        }
        return id;
    }

    private long SaveUnit(JsonElement input)
    {
        Owner();
        var product = Id(input, true, "productId");
        if (Long("SELECT COUNT(*) FROM Products WHERE Id=$id", ("id", product)) != 1) throw Error("المنتج غير موجود.");
        var name = Required(input, "name", "اسم الوحدة");
        var factor = Quantity(Decimal(input, "factor"), false);
        var price = Money(Decimal(input, "price"));
        var id = Id(input, false);
        if (id == 0)
        {
            Run("INSERT INTO Units(ProductId,Name,Factor,Price) VALUES($product,$name,$factor,$price)", ("product", product), ("name", name), ("factor", factor), ("price", price));
            return LastId();
        }
        if (Long("SELECT COUNT(*) FROM Units WHERE Id=$id AND ProductId=$product", ("id", id), ("product", product)) != 1) throw Error("الوحدة غير موجودة.");
        if (Long("SELECT Factor FROM Units WHERE Id=$id", ("id", id)) != factor) throw Error("عامل التحويل ثابت؛ أضف وحدة جديدة بدلاً من تغييره.");
        ChangePrice(id, price);
        return id;
    }

    private void ChangePrice(long unitId, long price)
    {
        var old = Long("SELECT Price FROM Units WHERE Id=$id", ("id", unitId));
        if (old != price)
        {
            Run("INSERT INTO PriceHistory(UnitId,OldPrice,NewPrice,CreatedAt,UserId) VALUES($unit,$old,$new,$date,$user)", ("unit", unitId), ("old", old), ("new", price), ("date", Now()), ("user", session!.Id));
            Run("UPDATE Units SET Price=$price WHERE Id=$id", ("price", price), ("id", unitId));
        }
    }

    private object Parties(JsonElement input) => Query("""
        SELECT p.*,COALESCE((SELECT SUM(CASE WHEN p.Kind='Customer' THEN l.Debit-l.Credit ELSE l.Credit-l.Debit END)
          FROM Ledger l WHERE l.PartyId=p.Id AND l.Account=CASE WHEN p.Kind='Customer' THEN 'Receivable' ELSE 'Payable' END),0) AS Balance,
          (SELECT MIN(d.DueDate) FROM Documents d WHERE d.PartyId=p.Id AND d.Status='Posted' AND d.Kind IN ('Sale','Purchase')
            AND d.Total-d.Paid-COALESCE((SELECT SUM(a.Amount) FROM Allocations a WHERE a.InvoiceId=d.Id),0)>0) AS NextDue
        FROM Parties p WHERE ($kind='' OR p.Kind=$kind) AND p.Name LIKE $search ORDER BY p.Name LIMIT 2000
        """, ("kind", Str(input, "kind")), ("search", "%" + Str(input, "search") + "%"));

    private long SaveParty(JsonElement input)
    {
        var id = Id(input, false);
        var kind = Str(input, "kind", "Customer");
        if (kind is not ("Customer" or "Supplier")) throw Error("نوع الطرف غير صحيح.");
        if (kind == "Supplier") Owner();
        var name = Required(input, "name", "اسم الطرف");
        var days = Math.Clamp(Id(input, false, "dueDays"), 0, 3650);
        if (id == 0)
        {
            Run("INSERT INTO Parties(Name,Kind,Phone,DueDays) VALUES($name,$kind,$phone,$days)", ("name", name), ("kind", kind), ("phone", Str(input, "phone")), ("days", days));
            return LastId();
        }
        if (Scalar("SELECT Kind FROM Parties WHERE Id=$id", ("id", id)) != kind) throw Error("لا يمكن تغيير نوع الطرف.");
        Run("UPDATE Parties SET Name=$name,Phone=$phone,DueDays=$days WHERE Id=$id", ("name", name), ("phone", Str(input, "phone")), ("days", days), ("id", id));
        return id;
    }

    private long SaveWarehouse(JsonElement input)
    {
        Owner();
        Run("INSERT INTO Warehouses(Name) VALUES($name)", ("name", Required(input, "name", "اسم المستودع")));
        return LastId();
    }

    private long CashBalance() => Long("SELECT COALESCE(SUM(Debit-Credit),0) FROM Ledger WHERE Account='Cash'");
    private long PartyBalance(long party, string account) => Long("SELECT COALESCE(SUM(CASE WHEN $account='Receivable' THEN Debit-Credit ELSE Credit-Debit END),0) FROM Ledger WHERE PartyId=$party AND Account=$account", ("party", party), ("account", account));

    private object Documents(JsonElement input) => Query("""
        SELECT d.*,p.Name AS PartyName,COALESCE((SELECT SUM(Amount) FROM Allocations WHERE InvoiceId=d.Id),0) AS Settled,
          (SELECT COUNT(*) FROM Attachments WHERE DocumentId=d.Id) AS Attachments
        FROM Documents d LEFT JOIN Parties p ON p.Id=d.PartyId
        WHERE ($kind='' OR d.Kind=$kind) AND ($status='' OR d.Status=$status)
          AND ($from='' OR substr(d.CreatedAt,1,10)>=$from) AND ($to='' OR substr(d.CreatedAt,1,10)<=$to)
        ORDER BY d.Id DESC LIMIT 500
        """, ("kind", Str(input, "kind")), ("status", Str(input, "status")), ("from", Str(input, "from")), ("to", Str(input, "to")));

    private object Document(long id)
    {
        var rows = Query("SELECT d.*,p.Name AS PartyName,p.Phone,u.Name AS Employee FROM Documents d LEFT JOIN Parties p ON p.Id=d.PartyId JOIN Users u ON u.Id=d.UserId WHERE d.Id=$id", ("id", id));
        if (rows.Count != 1) throw Error("العملية غير موجودة.");
        return new { header = rows[0], lines = Query("SELECT * FROM Lines WHERE DocumentId=$id ORDER BY Id", ("id", id)), attachments = Query("SELECT Id,FileName,CreatedAt FROM Attachments WHERE DocumentId=$id", ("id", id)), settings = Settings() };
    }
}
