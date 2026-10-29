using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Madar.Core;

if (args.Contains("--arabic"))
{
    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ar-SA");
    System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ar-SA");
}

var directory = Path.Combine(Path.GetTempPath(), "madar-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var root = Path.Combine(directory, "production");
using var store = new Store(root);
JsonElement Call(string action, object? input = null, Store? instance = null) => JsonSerializer.SerializeToElement((instance ?? store).Execute(action, JsonSerializer.SerializeToElement(input ?? new { }, Store.Json)), Store.Json);
long Number(string action, object input) => Call(action, input).GetInt64();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
void Reject(string action, object input, string message)
{
    try { Call(action, input); }
    catch (Exception e) when (e is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { Check(true, message); return; }
    throw new Exception("Expected rejection: " + message);
}
object Invoice(string kind, object[] lines, long party = 0, decimal paid = 0, bool full = true, string? request = null) => new { kind, lines, partyId = party, warehouseId = 1, paid, full, requestId = request ?? Guid.NewGuid().ToString(), note = "تحقق" };
object Line(long unit, decimal quantity, decimal price, string expiry = "") => new { unitId = unit, quantity, price, expiry, batch = "دفعة عربية" };
long Product(string name, string code, decimal price) => Number("products.save", new { name, barcode = code, baseUnit = "حبة", category = "غذاء", minimum = 2, price });
long Unit(long product) => Call("products").GetProperty("units").EnumerateArray().First(x => x.GetProperty("productId").GetInt64() == product).GetProperty("id").GetInt64();
long Stock(long product) => Call("products").GetProperty("items").EnumerateArray().First(x => x.GetProperty("id").GetInt64() == product).GetProperty("quantity").GetInt64();
long Cash() => Call("dashboard").GetProperty("cash").GetInt64();

try
{
    Check(!Call("status").GetProperty("initialized").GetBoolean(), "fresh installation has no default credentials");
    Reject("bootstrap", new { name = "admin", pin = "123", storeName = "متجر التجربة" }, "weak PIN rejected");
    Call("bootstrap", new { name = "مدير", pin = "123456", storeName = "متجر التجربة", currency = "ر.س" });
    Reject("bootstrap", new { name = "ثان", pin = "123456" }, "bootstrap cannot replace owner");
    var milk = Product("حليب طازج", "1001", 5);
    var baseUnit = Unit(milk);
    var carton = Number("units.save", new { productId = milk, name = "كرتون", factor = 10, price = 45 });
    var customer = Number("parties.save", new { name = "عميل عربي", kind = "Customer", dueDays = 30 });
    var supplier = Number("parties.save", new { name = "مورد عربي", kind = "Supplier", dueDays = 7 });
    Reject("invoice.post", Invoice("Purchase", [Line(carton, 2, 30)], supplier, 20, false), "posting needs an open shift");
    Call("shift.open");
    Call("cash.post", new { kind = "Capital", amount = 1000, note = "رأس المال", requestId = Guid.NewGuid().ToString() });
    var purchase = Number("invoice.post", Invoice("Purchase", [Line(carton, 2, 30)], supplier, 20, false));
    Check(Stock(milk) == 20_000 && Cash() == 98_000, "multi-unit purchase changes base stock and cash atomically");
    var request = Guid.NewGuid().ToString();
    var saleInput = Invoice("Sale", [Line(baseUnit, 3, 5)], customer, 5, false, request);
    var sale = Number("invoice.post", saleInput);
    Check(Number("invoice.post", saleInput) == sale && Stock(milk) == 17_000 && Cash() == 98_500, "repeated request does not duplicate inventory or money");
    Check(Call("dashboard").GetProperty("sales").GetInt64() == 1500, "daily totals use Gregorian storage regardless of device culture");
    var profit = Call("reports", new { kind = "profit" })[0];
    var facts = Call("assistant.facts", new { days = 30 });
    Check(facts.GetProperty("cash").GetInt64() == Cash() && facts.GetProperty("sales").GetInt64() == 1500 && facts.GetProperty("netProfit").GetInt64() == 600, "assistant facts use posted ledger totals without model arithmetic");
    Check(profit.GetProperty("cost").GetInt64() == 900 && profit.GetProperty("profit").GetInt64() == 600, "weighted inventory cost and sale profit are recorded");
    Reject("invoice.post", Invoice("Sale", [Line(baseUnit, 1, 5), Line(baseUnit, 100, 5)]), "failure on a later line rolls back earlier stock consumption");
    Check(Stock(milk) == 17_000 && Cash() == 98_500, "failed invoice leaves both cash and stock unchanged");
    Reject("cash.post", new { kind = "Expense", amount = 2000, note = "غير مسموح", requestId = Guid.NewGuid().ToString() }, "negative cash is rejected");
    Call("cash.post", new { kind = "Receipt", amount = 10, partyId = customer, note = "تحصيل", requestId = Guid.NewGuid().ToString() });
    Call("cash.post", new { kind = "Payment", amount = 40, partyId = supplier, note = "سداد", requestId = Guid.NewGuid().ToString() });
    var parties = Call("parties").EnumerateArray().ToList();
    Check(parties.All(p => p.GetProperty("balance").GetInt64() == 0) && Cash() == 95_500, "receipts and payments settle debts without another sale");
    Check(Call("reports", new { kind = "debts" }).GetArrayLength() == 0, "settled invoices leave overdue list");
    Reject("cash.post", new { kind = "Receipt", amount = 1, partyId = customer, note = "زائد", requestId = Guid.NewGuid().ToString() }, "collection cannot exceed customer debt");
    var expired = Product("منتج منتهي", "2002", 3);
    var expiredUnit = Unit(expired);
    Call("invoice.post", Invoice("Purchase", [Line(expiredUnit, 2, 1, DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))]));
    Reject("invoice.post", Invoice("Sale", [Line(expiredUnit, 1, 3)]), "expired batches cannot be sold");
    Call("stock.adjust", new { productId = expired, warehouseId = 1, quantity = 0, reason = "إتلاف بسبب انتهاء الصلاحية", unitCost = 0 });
    Check(Stock(expired) == 0, "approved stock adjustment removes expired quantity with accounting entry");
    Call("shift.close", new { counted = Cash()/100m + 5, reason = "فرق العد" });
    Reject("invoice.post", Invoice("Sale", [Line(baseUnit, 1, 5)]), "review shift blocks further posting");
    var shift = Call("shifts")[0].GetProperty("id").GetInt64();
    Call("shift.approve", new { id = shift, reason = "اعتماد فرق موثّق" });
    Check(Call("reports", new { kind = "trial" }).EnumerateArray().Single(x => x.GetProperty("account").GetString() == "CashSurplus").GetProperty("credit").GetInt64() == 500, "cash surplus is income rather than owner capital");
    Call("shift.open");
    Call("users.save", new { name = "عرض", role = "Viewer", pin = "654321", enabled = true });
    Call("logout");
    Reject("login", new { name = "مدير", pin = "000000" }, "wrong PIN rejected");
    Call("login", new { name = "عرض", pin = "654321" });
    Reject("products.save", new { name = "ممنوع", barcode = "x", baseUnit = "حبة", minimum = 0, price = 1 }, "read-only role enforced by storage layer");
    Reject("users", new { }, "employee list restricted to owner");
    Call("logout"); Call("login", new { name = "مدير", pin = "123456" });
    var secondOwner = Call("users.save", new { name = "مدير آخر", role = "Owner", pin = "456789", enabled = true });
    using (var administrator = new Store(root))
    {
        Call("login", new { name = "مدير آخر", pin = "456789" }, administrator);
        Call("users.save", new { id = 1, name = "مدير", role = "Viewer", enabled = true }, administrator);
        Reject("products.save", new { name = "صلاحية قديمة", baseUnit = "حبة", price = 1 }, "role changes take effect in an existing session");
        try { store.Backup(Path.Combine(directory, "forbidden.zip")); throw new Exception("Revoked owner exported a backup"); }
        catch (InvalidOperationException) { Check(true, "backup checks current owner permissions"); }
        Call("users.save", new { id = 1, name = "مدير", role = "Owner", enabled = true }, administrator);
    }
    using (var concurrent = new Store(root))
    {
        Call("login", new { name = "مدير", pin = "123456" }, concurrent);
        var input1 = Invoice("Sale", [Line(baseUnit, 9, 5)]); var input2 = Invoice("Sale", [Line(baseUnit, 9, 5)]);
        var outcomes = await Task.WhenAll(Task.Run(() => TryPost(store, input1)), Task.Run(() => TryPost(concurrent, input2)));
        Check(outcomes.Count(x => x) == 1 && Stock(milk) == 8_000, "two connections cannot oversell the last stock");
    }
    var backup = Path.Combine(directory, "checked.zip");
    using (var file = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 test attachment"))) store.Attach(sale, "فاتورة.pdf", file);
    store.Backup(backup);
    var savedCash = Cash();
    Call("cash.post", new { kind = "Capital", amount = 10, note = "بعد النسخة", requestId = Guid.NewGuid().ToString() });
    var corrupt = Path.Combine(directory, "corrupt.zip"); File.Copy(backup, corrupt);
    using (var zip = ZipFile.Open(corrupt, ZipArchiveMode.Update))
    {
        zip.GetEntry("madar.db")!.Delete();
        using var writer = new StreamWriter(zip.CreateEntry("madar.db").Open()); writer.Write("damaged");
    }
    try { store.Restore(corrupt); throw new Exception("Corrupt restore unexpectedly accepted"); }
    catch (InvalidOperationException) { Check(Cash() == savedCash + 1000, "corrupt backup cannot change live database"); }
    store.Restore(backup);
    Check(!Call("status").GetProperty("user").ValueKind.Equals(JsonValueKind.Object), "restore requires fresh sign-in");
    Call("login", new { name = "مدير", pin = "123456" });
    Check(Cash() == savedCash, "restored database matches backup");
    Check(Call("document", new { id = sale }).GetProperty("attachments").GetArrayLength() == 1, "backup retains invoice attachments");
    var duplicateRows = new { rows = new[] { new { name = "تكرار", barcode = "1001", baseUnit = "حبة", price = "1" } } };
    Reject("products.import", duplicateRows, "duplicate imports are rejected atomically");
    foreach (var xlsx in new[] { false, true })
    {
        var rows = new List<Dictionary<string, string>> { new() { ["name"] = "أرز، ممتاز", ["barcode"] = "3003", ["baseUnit"] = "كيس", ["price"] = "25", ["minimum"] = "2" } };
        using var stream = new MemoryStream(); Tabular.Write(stream, rows, xlsx); stream.Position = 0;
        var decoded = Tabular.Read(stream, xlsx ? ".xlsx" : ".csv");
        Check(decoded[0]["name"] == "أرز، ممتاز", "Arabic " + (xlsx ? "XLSX" : "CSV") + " round trip");
    }
    using (var reopened = new Store(root))
    {
        Call("login", new { name = "مدير", pin = "123456" }, reopened);
        var dashboard = Call("dashboard", new { }, reopened);
        Check(dashboard.GetProperty("cash").GetInt64() == savedCash, "data survives complete store reopen without server");
    }
    var trial = Call("reports", new { kind = "trial" }).EnumerateArray().ToList();
    Check(trial.Sum(r => r.GetProperty("debit").GetInt64()) == trial.Sum(r => r.GetProperty("credit").GetInt64()), "all ledger accounts balance after all scenarios");
    SimulatorChecks.Run(directory, Check);
    Console.WriteLine("ALL OFFLINE CHECKS PASSED");
}
finally { store.Dispose(); Directory.Delete(directory, true); }

bool TryPost(Store instance, object input)
{
    try { Call("invoice.post", input, instance); return true; }
    catch (Exception e) when (e is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { return false; }
}
