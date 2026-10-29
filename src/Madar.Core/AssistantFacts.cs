using System.Globalization;
using System.Text.Json;

namespace Madar.Core;

public sealed partial class Store
{
    private object AssistantFacts(JsonElement input)
    {
        var days = (int)Id(input, false, "days");
        if (days is not (1 or 7 or 30)) days = 30;
        var to = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var from = DateTime.Today.AddDays(1-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var args = new (string, object?)[] { ("from", from), ("to", to) };
        const string period = "d.Status='Posted' AND substr(d.CreatedAt,1,10)>=$from AND substr(d.CreatedAt,1,10)<=$to";
        var sales = Long("SELECT COALESCE(SUM(Total),0) FROM Documents d WHERE " + period + " AND Kind='Sale'", args);
        var purchases = Long("SELECT COALESCE(SUM(Total),0) FROM Documents d WHERE " + period + " AND Kind='Purchase'", args);
        var net = Long("SELECT COALESCE(SUM(l.Credit-l.Debit),0) FROM Ledger l JOIN Documents d ON d.Id=l.DocumentId WHERE " + period + " AND l.Account IN ('Sales','CostOfSales','Expense','CashSurplus','InventorySurplus')", args);
        var receivable = Long("SELECT COALESCE(SUM(Debit-Credit),0) FROM Ledger WHERE Account='Receivable'");
        var payable = Long("SELECT COALESCE(SUM(Credit-Debit),0) FROM Ledger WHERE Account='Payable'");
        var currency = Scalar("SELECT Value FROM Settings WHERE Key='currency'");
        string Amount(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture) + " " + currency;
        var cash = CashBalance();
        var summary = $"الفترة: {from} إلى {to}.\nمبيعات الفترة: {Amount(sales)}.\nمشتريات الفترة: {Amount(purchases)}.\nصافي ربح الفترة: {Amount(net)}.\nرصيد الصندوق الحالي: {Amount(cash)}.\nمديونية العملاء الحالية: {Amount(receivable)}.\nمستحقات الموردين الحالية: {Amount(payable)}.\nالأرقام محسوبة من الفواتير المعتمدة؛ الربح يختلف عن المبيعات والنقد.";
        return new { from, to, days, capturedAt = Now(), sales, purchases, netProfit = net, cash, receivable, payable, currency, summary };
    }
}
