using System.Text.Json;

namespace Madar.Core;

public sealed partial class Store
{
    private object Dashboard()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var start = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return new
        {
            sales = Long("SELECT COALESCE(SUM(Total),0) FROM Documents WHERE Status='Posted' AND Kind='Sale' AND substr(CreatedAt,1,10)=$day", ("day", today)),
            purchases = Long("SELECT COALESCE(SUM(Total),0) FROM Documents WHERE Status='Posted' AND Kind='Purchase' AND substr(CreatedAt,1,10)=$day", ("day", today)),
            invoices = Long("SELECT COUNT(*) FROM Documents WHERE Status='Posted' AND Kind IN ('Sale','Purchase') AND substr(CreatedAt,1,10)=$day", ("day", today)),
            cash = CashBalance(),
            receivable = Long("SELECT COALESCE(SUM(Debit-Credit),0) FROM Ledger WHERE Account='Receivable'"),
            payable = Long("SELECT COALESCE(SUM(Credit-Debit),0) FROM Ledger WHERE Account='Payable'"),
            lowStock = Long("SELECT COUNT(*) FROM (SELECT p.Id FROM Products p LEFT JOIN Inventory i ON i.ProductId=p.Id WHERE p.Enabled=1 GROUP BY p.Id HAVING COALESCE(SUM(i.Quantity),0)<=p.Minimum)"),
            expiring = Long("SELECT COUNT(*) FROM Lots WHERE Remaining>0 AND Expiry<>'' AND Expiry<=$until", ("until", DateTime.Today.AddDays(30).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))),
            drafts = Long("SELECT COUNT(*) FROM Documents WHERE Status='Draft'"),
            weekly = Query("SELECT substr(CreatedAt,1,10) AS Day,SUM(Total) AS Total FROM Documents WHERE Status='Posted' AND Kind='Sale' AND substr(CreatedAt,1,10)>=$start GROUP BY substr(CreatedAt,1,10) ORDER BY Day", ("start", start)),
            recent = Query("SELECT d.Id,d.Kind,d.Status,d.Total,d.CreatedAt,p.Name AS PartyName FROM Documents d LEFT JOIN Parties p ON p.Id=d.PartyId ORDER BY d.Id DESC LIMIT 6")
        };
    }

    private object Reports(JsonElement input)
    {
        var from = Str(input, "from", DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var to = Str(input, "to", DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var first) || !DateOnly.TryParseExact(to, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var last) || first > last) throw Error("الفترة غير صحيحة.");
        var kind = Str(input, "kind", "income");
        const string period = " d.Status='Posted' AND substr(d.CreatedAt,1,10)>=$from AND substr(d.CreatedAt,1,10)<=$to ";
        var args = new (string, object?)[] { ("from", from), ("to", to) };
        return kind switch
        {
            "income" => Query("SELECT l.Account,SUM(l.Debit) AS Debit,SUM(l.Credit) AS Credit FROM Ledger l JOIN Documents d ON d.Id=l.DocumentId WHERE " + period + " AND l.Account IN ('Sales','CostOfSales','Expense','CashSurplus','InventorySurplus') GROUP BY l.Account", args),
            "trial" => Query("SELECT l.Account,SUM(l.Debit) AS Debit,SUM(l.Credit) AS Credit FROM Ledger l JOIN Documents d ON d.Id=l.DocumentId WHERE d.Status='Posted' AND substr(d.CreatedAt,1,10)<=$to GROUP BY l.Account", ("to", to)),
            "journal" => Query("SELECT d.Id AS Document,d.CreatedAt,d.Kind,l.Account,l.Debit,l.Credit,p.Name AS Party FROM Ledger l JOIN Documents d ON d.Id=l.DocumentId LEFT JOIN Parties p ON p.Id=l.PartyId WHERE " + period + " ORDER BY d.Id DESC,l.Id LIMIT 1000", args),
            "profit" => Query("SELECT l.ProductId,l.Name,SUM(l.BaseQuantity) AS Quantity,SUM(l.Total) AS Sales,SUM(l.Cost) AS Cost,SUM(l.Total-l.Cost) AS Profit FROM Lines l JOIN Documents d ON d.Id=l.DocumentId WHERE " + period + " AND d.Kind='Sale' GROUP BY l.ProductId,l.Name ORDER BY Profit DESC", args),
            "suppliers" => Query("SELECT l.Name,p.Name AS Supplier,l.UnitName,l.Quantity,l.BaseQuantity,l.Total,d.CreatedAt FROM Lines l JOIN Documents d ON d.Id=l.DocumentId JOIN Parties p ON p.Id=d.PartyId WHERE " + period + " AND d.Kind='Purchase' ORDER BY l.ProductId,d.CreatedAt DESC LIMIT 1000", args),
            "debts" => Query("SELECT p.Name,p.Kind,d.Id AS Invoice,d.DueDate,d.Total-d.Paid-COALESCE((SELECT SUM(a.Amount) FROM Allocations a WHERE a.InvoiceId=d.Id),0) AS Remaining FROM Documents d JOIN Parties p ON p.Id=d.PartyId WHERE d.Status='Posted' AND d.Kind IN ('Sale','Purchase') AND d.Total-d.Paid-COALESCE((SELECT SUM(a.Amount) FROM Allocations a WHERE a.InvoiceId=d.Id),0)>0 ORDER BY d.DueDate,d.Id"),
            "stock" => Query("SELECT p.Name,p.BaseUnit,w.Name AS Warehouse,i.Quantity,i.Value,p.Minimum FROM Inventory i JOIN Products p ON p.Id=i.ProductId JOIN Warehouses w ON w.Id=i.WarehouseId ORDER BY p.Name"),
            "expiry" => Query("SELECT p.Name,w.Name AS Warehouse,l.Batch,l.Expiry,l.Remaining FROM Lots l JOIN Products p ON p.Id=l.ProductId JOIN Warehouses w ON w.Id=l.WarehouseId WHERE l.Remaining>0 AND l.Expiry<>'' ORDER BY l.Expiry"),
            _ => throw Error("نوع التقرير غير معروف.")
        };
    }

    private object Assistant(string question)
    {
        // Deliberately deterministic and offline: no language-model or cloud claims.
        var alerts = new List<string>();
        var expired = Long("SELECT COUNT(*) FROM Lots WHERE Remaining>0 AND Expiry<>'' AND Expiry<$today", ("today", DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        if (expired > 0) alerts.Add($"توجد {expired} دفعات منتهية. البيع يستبعدها؛ راجع تقرير الصلاحية وسجّل التسوية بسبب واضح.");
        var shifts = Long("SELECT COUNT(*) FROM Shifts WHERE Status='Review'");
        if (shifts > 0) alerts.Add("هناك فرق صندوق بانتظار اعتماد المدير.");
        var overdue = Long("SELECT COUNT(*) FROM Documents d WHERE d.Status='Posted' AND d.Kind='Sale' AND d.DueDate<>'' AND d.DueDate<$today AND d.Total-d.Paid-COALESCE((SELECT SUM(Amount) FROM Allocations WHERE InvoiceId=d.Id),0)>0", ("today", DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        if (overdue > 0) alerts.Add($"توجد {overdue} فواتير بيع متأخرة. راجع الاستحقاقات قبل التحصيل.");
        var answer = question.Contains("تكلف") || question.Contains("ربح")
            ? "تكلفة المخزون بالمتوسط المرجّح. عند البيع يُسجَّل الإيراد وتكلفة البضاعة معاً؛ الربح ليس إجمالي المبيعات ولا رصيد الصندوق. راجع تقرير ربحية الأصناف."
            : question.Contains("دين") || question.Contains("آجل") || question.Contains("تحصيل")
            ? "الفاتورة الآجلة تثبت مديونية الطرف. التحصيل يزيد النقد ويقلل مديونية العميل دون تسجيل بيع جديد. السداد للمورد يقلل النقد والمديونية، ويوزّع على أقدم الاستحقاقات."
            : question.Contains("شراء") || question.Contains("مخزون")
            ? "أدخل البضاعة بفاتورة شراء وبالوحدة الصحيحة. يتحول المخزون إلى الوحدة الأساسية؛ الكرتون بمعامل 12 يزيد المخزون 12 حبة. لا تستخدم تسوية المخزون بدلاً من فاتورة شراء."
            : question.Contains("نسخ") || question.Contains("استعاد")
            ? "النسخة الاحتياطية تشمل قاعدة البيانات والمرفقات. احفظ نسخة خارج الهاتف، وتحقق من تاريخ آخر نسخة. استعادة نسخة تستبدل البيانات الحالية بعد التحقق؛ احتفظ بنسخة من الوضع الحالي أولاً."
            : "ابدأ بفتح وردية الصندوق، ثم تعريف المنتجات ووحداتها والأطراف. التمويل حركة رأس مال، والشراء يزيد المخزون، والبيع يسجل الإيراد والتكلفة. استخدم وضع التدريب لتجربة الخطوات بعيداً عن البيانات الحقيقية.";
        return new { answer, alerts, engine = "مساعد محلي إرشادي قائم على قواعد وبيانات التطبيق؛ لا يولّد نصوصاً بنموذج لغوي ولا يقرأ صور الفواتير." };
    }
}
