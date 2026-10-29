using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Madar.Core;

public sealed partial class Store : IDisposable
{
    public const int SchemaVersion = 1;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object gate = new();
    public string DirectoryPath { get; }
    public string DatabasePath => Path.Combine(DirectoryPath, "madar.db");
    private SqliteConnection db = null!;
    private SqliteTransaction? transaction;
    private Session? session;
    public sealed record Session(long Id, string Name, string Role);

    public Store(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(DirectoryPath);
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "attachments"));
        using var connection = Open();
        db = connection;
        if (Long("PRAGMA user_version") > SchemaVersion) throw Error("قاعدة البيانات من إصدار أحدث؛ حدّث التطبيق قبل فتحها.");
        Run(Schema);
        Run("INSERT OR IGNORE INTO Settings(Key,Value) VALUES('storeName','متجري'),('currency','وحدة نقدية'),('paper','80')");
        Run("INSERT OR IGNORE INTO Settings(Key,Value) VALUES('storeInstanceId',$id)", ("id", Guid.NewGuid().ToString("N")));
        db = null!;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, ForeignKeys = true, Pooling = false, DefaultTimeout = 15
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=15000;";
        command.ExecuteNonQuery();
        return connection;
    }

    public object? Execute(string action, JsonElement input)
    {
        lock (gate)
        {
            using var connection = Open();
            db = connection;
            try
            {
                if (action == "status") return new { initialized = Long("SELECT COUNT(*) FROM Users") > 0, user = session };
                if (action == "login") return Login(input);
                if (action == "logout") { session = null; return true; }
                if (action == "bootstrap")
                {
                    using var initial = db.BeginTransaction(); transaction = initial;
                    if (Long("SELECT COUNT(*) FROM Users") != 0) throw Error("تم إعداد التطبيق بالفعل؛ سجّل الدخول.");
                    var user = SaveUser(input, true);
                    SetSetting("storeName", Str(input, "storeName", "متجري"));
                    SetSetting("currency", Str(input, "currency", "وحدة نقدية"));
                    Audit("إنشاء حساب المدير", user.Id.ToString());
                    initial.Commit(); transaction = null;
                    session = user;
                    return user;
                }
                if (session is null) throw Error("سجّل الدخول أولاً.");
                ValidateSession();

                return action switch
                {
                    "state" => new { user = session, settings = Settings(), shift = CurrentShift(), version = SchemaVersion },
                    "session.scope" => SessionScope(),
                    "dashboard" => Dashboard(),
                    "products" => Products(input),
                    "parties" => Parties(input),
                    "documents" => Documents(input),
                    "document" => Document(Id(input)),
                    "invoice.total" => InvoiceTotal(input),
                    "reports" => Reports(input),
                    "assistant" => Assistant(Str(input, "question")),
                    "assistant.facts" => AssistantFacts(input),
                    "shifts" => Query("SELECT * FROM Shifts ORDER BY Id DESC LIMIT 100"),
                    "warehouses" => Query("SELECT * FROM Warehouses ORDER BY Id"),
                    "users" => OwnerRead("SELECT Id,Name,Role,Enabled FROM Users ORDER BY Id"),
                    "audit" => OwnerRead("SELECT a.Id,a.CreatedAt,a.Action,a.Target,COALESCE(u.Name,'الإعداد') AS UserName FROM Audit a LEFT JOIN Users u ON u.Id=a.UserId ORDER BY a.Id DESC LIMIT 500"),
                    "training" => Query("SELECT Lesson,Step,Completed FROM Training WHERE UserId=$id", ("id", session.Id)),
                    "products.importPreview" => PrepareProducts(input),
                    _ => Change(action, input)
                };
            }
            finally { transaction?.Dispose(); transaction = null; db = null!; }
        }
    }

    private object? Change(string action, JsonElement input)
    {
        if (session!.Role == "Viewer" && action != "training.save") throw Error("هذا الحساب للعرض فقط.");
        using var change = db.BeginTransaction(); transaction = change;
        object? result = action switch
        {
            "products.save" => SaveProduct(input),
            "units.save" => SaveUnit(input),
            "parties.save" => SaveParty(input),
            "warehouses.save" => SaveWarehouse(input),
            "invoice.save" => SaveInvoice(input, false),
            "invoice.post" => SaveInvoice(input, true),
            "cash.post" => PostCash(input),
            "stock.adjust" => AdjustStock(input),
            "shift.open" => OpenShift(),
            "shift.close" => CloseShift(input),
            "shift.approve" => ApproveShift(input),
            "users.save" => SaveUser(input),
            "settings.save" => SaveSettings(input),
            "training.save" => SaveTraining(input),
            "products.import" => ImportProducts(input),
            _ => throw Error("العملية غير معروفة.")
        };
        Audit(action, result is long id ? id.ToString() : "");
        change.Commit(); transaction = null;
        return result;
    }

    private Session Login(JsonElement input)
    {
        var name = Required(input, "name", "اسم المستخدم");
        var pin = Required(input, "pin", "الرمز السري");
        var rows = Query("SELECT * FROM Users WHERE Name=$name COLLATE NOCASE AND Enabled=1", ("name", name));
        if (rows.Count != 1) throw Error("اسم المستخدم أو الرمز غير صحيح.");
        var row = rows[0];
        var locked = Convert.ToInt64(row["lockedUntil"]);
        if (locked > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw Error("محاولات كثيرة. انتظر دقيقة ثم أعد المحاولة.");
        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, (byte[])row["salt"]!, 150_000, HashAlgorithmName.SHA256, 32);
        if (!CryptographicOperations.FixedTimeEquals(actual, (byte[])row["pinHash"]!))
        {
            Run("UPDATE Users SET Failures=Failures+1,LockedUntil=CASE WHEN Failures>=4 THEN $until ELSE 0 END WHERE Id=$id", ("until", DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds()), ("id", row["id"]));
            throw Error("اسم المستخدم أو الرمز غير صحيح.");
        }
        Run("UPDATE Users SET Failures=0,LockedUntil=0 WHERE Id=$id", ("id", row["id"]));
        session = new Session(Convert.ToInt64(row["id"]), (string)row["name"]!, (string)row["role"]!);
        Audit("تسجيل دخول", session.Id.ToString());
        return session;
    }

    private Session SaveUser(JsonElement input, bool first = false)
    {
        if (!first) Owner();
        var id = Id(input, false);
        var name = Required(input, "name", "اسم المستخدم");
        var role = first ? "Owner" : Str(input, "role", "Cashier");
        if (role is not ("Owner" or "Cashier" or "Viewer")) throw Error("صلاحية الحساب غير صحيحة.");
        var pin = Str(input, "pin");
        if (id == 0 && pin.Length == 0) throw Error("أدخل رمزاً سرياً للحساب.");
        if (pin.Length > 0 && (pin.Length < 6 || pin.Length > 12 || pin.Any(c => !char.IsAsciiDigit(c))))
            throw Error("الرمز السري من 6 إلى 12 رقماً إنجليزياً.");
        var enabled = Bool(input, "enabled", true);
        if (id == session?.Id && (!enabled || role != session.Role)) throw Error("لا يمكنك إلغاء حسابك أو تغيير صلاحيتك أثناء استخدامه.");
        if (id == 0)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            Run("INSERT INTO Users(Name,Role,PinHash,Salt,Enabled) VALUES($name,$role,$hash,$salt,$enabled)", ("name", name), ("role", role), ("hash", Rfc2898DeriveBytes.Pbkdf2(pin, salt, 150_000, HashAlgorithmName.SHA256, 32)), ("salt", salt), ("enabled", enabled));
            id = LastId();
        }
        else
        {
            if (Long("SELECT COUNT(*) FROM Users WHERE Id=$id", ("id", id)) != 1) throw Error("الحساب غير موجود.");
            Run("UPDATE Users SET Name=$name,Role=$role,Enabled=$enabled WHERE Id=$id", ("name", name), ("role", role), ("enabled", enabled), ("id", id));
            if (pin.Length > 0)
            {
                var salt = RandomNumberGenerator.GetBytes(16);
                Run("UPDATE Users SET PinHash=$hash,Salt=$salt,Failures=0,LockedUntil=0 WHERE Id=$id", ("hash", Rfc2898DeriveBytes.Pbkdf2(pin, salt, 150_000, HashAlgorithmName.SHA256, 32)), ("salt", salt), ("id", id));
            }
        }
        return new Session(id, name, role);
    }

    private object SaveSettings(JsonElement input)
    {
        Owner();
        foreach (var key in new[] { "storeName", "currency", "phone", "address", "paper" })
            if (input.TryGetProperty(key, out var value)) SetSetting(key, value.GetString() ?? "");
        return Settings();
    }
    private object SaveTraining(JsonElement input)
    {
        Run("INSERT INTO Training(UserId,Lesson,Step,Completed) VALUES($user,$lesson,$step,$done) ON CONFLICT(UserId,Lesson) DO UPDATE SET Step=excluded.Step,Completed=excluded.Completed", ("user", session!.Id), ("lesson", Required(input, "lesson", "الدرس")), ("step", Math.Clamp(Id(input, false, "step"), 0, 100)), ("done", Bool(input, "completed")));
        return true;
    }
    private object SessionScope()
    {
        // Older restored backups may predate the per-store training identity.
        Run("INSERT OR IGNORE INTO Settings(Key,Value) VALUES('storeInstanceId',$id)", ("id", Guid.NewGuid().ToString("N")));
        return new { storeId = Scalar("SELECT Value FROM Settings WHERE Key='storeInstanceId'"), userId = session!.Id };
    }
    private void Owner() { if (session?.Role != "Owner") throw Error("هذه العملية تحتاج صلاحية المدير."); }
    private void ValidateSession()
    {
        if (session is null) throw Error("سجّل الدخول أولاً.");
        var users = Query("SELECT Id,Name,Role FROM Users WHERE Id=$id AND Enabled=1", ("id", session.Id));
        if (users.Count != 1) { session = null; throw Error("الحساب غير مفعّل. سجّل الدخول بحساب آخر."); }
        session = new Session(session.Id, (string)users[0]["name"]!, (string)users[0]["role"]!);
    }
    private object OwnerRead(string sql) { Owner(); return Query(sql); }
    private List<Dictionary<string, object?>> Settings() => Query("SELECT Key,Value FROM Settings ORDER BY Key");
    private void SetSetting(string key, string value)
    {
        if (value.Length > 500) throw Error("القيمة أطول من المسموح.");
        Run("INSERT INTO Settings(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value", ("key", key), ("value", value.Trim()));
    }
    private void Audit(string action, string target) => Run("INSERT INTO Audit(UserId,CreatedAt,Action,Target) VALUES($user,$date,$action,$target)", ("user", session?.Id), ("date", Now()), ("action", action), ("target", target));
    private SqliteCommand Command(string sql, params (string Name, object? Value)[] values)
    {
        var command = db.CreateCommand(); command.CommandText = sql; command.Transaction = transaction;
        foreach (var item in values) command.Parameters.AddWithValue("$" + item.Name, item.Value ?? DBNull.Value);
        return command;
    }
    private int Run(string sql, params (string Name, object? Value)[] values) { using var command = Command(sql, values); return command.ExecuteNonQuery(); }
    private long Long(string sql, params (string Name, object? Value)[] values) { using var command = Command(sql, values); return Convert.ToInt64(command.ExecuteScalar() is { } value && value != DBNull.Value ? value : 0); }
    private string Scalar(string sql, params (string Name, object? Value)[] values) { using var command = Command(sql, values); return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? ""; }
    private List<Dictionary<string, object?>> Query(string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(sql, values); using var reader = command.ExecuteReader();
        var result = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i); name = char.ToLowerInvariant(name[0]) + name[1..];
                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            result.Add(row);
        }
        return result;
    }
    private long LastId() => Long("SELECT last_insert_rowid()");
    private static string Now() => DateTimeOffset.Now.ToString("O");
    private static string Str(JsonElement e, string key, string fallback = "") => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : fallback;
    private static bool Bool(JsonElement e, string key, bool fallback = false) => e.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
    private static long Id(JsonElement e, bool required = true, string key = "id")
    {
        var id = e.TryGetProperty(key, out var value) && value.TryGetInt64(out var parsed) ? parsed : 0;
        if (id < 0 || required && id == 0) throw Error("الاختيار غير صحيح."); return id;
    }
    private static string Required(JsonElement e, string key, string label)
    {
        var value = Str(e, key); if (string.IsNullOrWhiteSpace(value) || value.Length > 500) throw Error($"أدخل {label} بطول مناسب."); return value;
    }
    private static decimal Decimal(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var parsed)) throw Error("أدخل قيمة رقمية صحيحة.");
        return parsed;
    }
    private static long Fixed(decimal value, int scale, bool zero = true)
    {
        if (value < 0 || !zero && value == 0 || value > 100_000_000 || decimal.Round(value * scale, 0) != value * scale) throw Error("القيمة سالبة أو دقتها أو حجمها غير مسموح.");
        return checked((long)(value * scale));
    }
    private static long Money(decimal amount) => Fixed(amount, 100);
    private static long Quantity(decimal amount, bool zero = true) => Fixed(amount, 1000, zero);
    private static long RoundedMoney(decimal amount) => checked((long)decimal.Round(amount, 0, MidpointRounding.AwayFromZero));
    private static InvalidOperationException Error(string message) => new(message);
    public void Dispose() { lock (gate) { session = null; } }

    private const string Schema = """
        PRAGMA foreign_keys=ON;
        CREATE TABLE IF NOT EXISTS Settings(Key TEXT PRIMARY KEY,Value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS Users(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL COLLATE NOCASE UNIQUE,Role TEXT NOT NULL CHECK(Role IN ('Owner','Cashier','Viewer')),PinHash BLOB NOT NULL,Salt BLOB NOT NULL,Enabled INTEGER NOT NULL DEFAULT 1,Failures INTEGER NOT NULL DEFAULT 0,LockedUntil INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS Warehouses(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL UNIQUE);
        INSERT OR IGNORE INTO Warehouses(Id,Name) VALUES(1,'المستودع الرئيسي');
        CREATE TABLE IF NOT EXISTS Products(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,Barcode TEXT NOT NULL DEFAULT '',Category TEXT NOT NULL DEFAULT '',BaseUnit TEXT NOT NULL,Minimum INTEGER NOT NULL DEFAULT 0 CHECK(Minimum>=0),Enabled INTEGER NOT NULL DEFAULT 1);
        CREATE UNIQUE INDEX IF NOT EXISTS ProductBarcode ON Products(Barcode) WHERE Barcode<>'';
        CREATE TABLE IF NOT EXISTS Units(Id INTEGER PRIMARY KEY AUTOINCREMENT,ProductId INTEGER NOT NULL REFERENCES Products(Id),Name TEXT NOT NULL,Factor INTEGER NOT NULL CHECK(Factor>0),Price INTEGER NOT NULL CHECK(Price>=0),UNIQUE(ProductId,Name));
        CREATE TABLE IF NOT EXISTS PriceHistory(Id INTEGER PRIMARY KEY AUTOINCREMENT,UnitId INTEGER NOT NULL REFERENCES Units(Id),OldPrice INTEGER NOT NULL,NewPrice INTEGER NOT NULL,CreatedAt TEXT NOT NULL,UserId INTEGER NOT NULL REFERENCES Users(Id));
        CREATE TABLE IF NOT EXISTS Inventory(ProductId INTEGER NOT NULL REFERENCES Products(Id),WarehouseId INTEGER NOT NULL REFERENCES Warehouses(Id),Quantity INTEGER NOT NULL DEFAULT 0 CHECK(Quantity>=0),Value INTEGER NOT NULL DEFAULT 0 CHECK(Value>=0),PRIMARY KEY(ProductId,WarehouseId));
        CREATE TABLE IF NOT EXISTS Parties(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,Kind TEXT NOT NULL CHECK(Kind IN ('Customer','Supplier')),Phone TEXT NOT NULL DEFAULT '',DueDays INTEGER NOT NULL DEFAULT 30 CHECK(DueDays BETWEEN 0 AND 3650));
        CREATE TABLE IF NOT EXISTS Shifts(Id INTEGER PRIMARY KEY AUTOINCREMENT,UserId INTEGER NOT NULL REFERENCES Users(Id),OpenedAt TEXT NOT NULL,ClosedAt TEXT,OpeningCash INTEGER NOT NULL,ExpectedCash INTEGER,CountedCash INTEGER,Status TEXT NOT NULL CHECK(Status IN ('Open','Review','Closed')),Reason TEXT NOT NULL DEFAULT '',ApprovedBy INTEGER REFERENCES Users(Id));
        CREATE UNIQUE INDEX IF NOT EXISTS OneOpenShift ON Shifts((1)) WHERE Status IN ('Open','Review');
        CREATE TABLE IF NOT EXISTS Documents(Id INTEGER PRIMARY KEY AUTOINCREMENT,RequestId TEXT NOT NULL UNIQUE,Kind TEXT NOT NULL,Status TEXT NOT NULL CHECK(Status IN ('Draft','Posted')),PartyId INTEGER REFERENCES Parties(Id),WarehouseId INTEGER NOT NULL REFERENCES Warehouses(Id),ShiftId INTEGER REFERENCES Shifts(Id),UserId INTEGER NOT NULL REFERENCES Users(Id),CreatedAt TEXT NOT NULL,DueDate TEXT NOT NULL DEFAULT '',Total INTEGER NOT NULL DEFAULT 0 CHECK(Total>=0),Paid INTEGER NOT NULL DEFAULT 0 CHECK(Paid>=0 AND Paid<=Total),Cost INTEGER NOT NULL DEFAULT 0 CHECK(Cost>=0),Note TEXT NOT NULL DEFAULT '');
        CREATE TABLE IF NOT EXISTS Lines(Id INTEGER PRIMARY KEY AUTOINCREMENT,DocumentId INTEGER NOT NULL REFERENCES Documents(Id) ON DELETE CASCADE,ProductId INTEGER NOT NULL REFERENCES Products(Id),UnitId INTEGER NOT NULL REFERENCES Units(Id),Name TEXT NOT NULL,UnitName TEXT NOT NULL,Quantity INTEGER NOT NULL CHECK(Quantity>0),BaseQuantity INTEGER NOT NULL CHECK(BaseQuantity>0),Price INTEGER NOT NULL CHECK(Price>=0),Total INTEGER NOT NULL CHECK(Total>=0),Cost INTEGER NOT NULL DEFAULT 0 CHECK(Cost>=0),Batch TEXT NOT NULL DEFAULT '',Expiry TEXT NOT NULL DEFAULT '');
        CREATE TABLE IF NOT EXISTS Lots(Id INTEGER PRIMARY KEY AUTOINCREMENT,ProductId INTEGER NOT NULL REFERENCES Products(Id),WarehouseId INTEGER NOT NULL REFERENCES Warehouses(Id),DocumentId INTEGER NOT NULL REFERENCES Documents(Id),Batch TEXT NOT NULL DEFAULT '',Expiry TEXT NOT NULL DEFAULT '',Remaining INTEGER NOT NULL CHECK(Remaining>=0));
        CREATE TABLE IF NOT EXISTS Ledger(Id INTEGER PRIMARY KEY AUTOINCREMENT,DocumentId INTEGER NOT NULL REFERENCES Documents(Id),Account TEXT NOT NULL,PartyId INTEGER REFERENCES Parties(Id),Debit INTEGER NOT NULL DEFAULT 0 CHECK(Debit>=0),Credit INTEGER NOT NULL DEFAULT 0 CHECK(Credit>=0),CHECK((Debit=0)!=(Credit=0)));
        CREATE TABLE IF NOT EXISTS Allocations(PaymentId INTEGER NOT NULL REFERENCES Documents(Id),InvoiceId INTEGER NOT NULL REFERENCES Documents(Id),Amount INTEGER NOT NULL CHECK(Amount>0),PRIMARY KEY(PaymentId,InvoiceId));
        CREATE TABLE IF NOT EXISTS Attachments(Id INTEGER PRIMARY KEY AUTOINCREMENT,DocumentId INTEGER NOT NULL REFERENCES Documents(Id),FileName TEXT NOT NULL,StoredName TEXT NOT NULL UNIQUE,CreatedAt TEXT NOT NULL,UserId INTEGER NOT NULL REFERENCES Users(Id));
        CREATE TABLE IF NOT EXISTS Training(UserId INTEGER NOT NULL REFERENCES Users(Id),Lesson TEXT NOT NULL,Step INTEGER NOT NULL,Completed INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(UserId,Lesson));
        CREATE TABLE IF NOT EXISTS Audit(Id INTEGER PRIMARY KEY AUTOINCREMENT,UserId INTEGER REFERENCES Users(Id),CreatedAt TEXT NOT NULL,Action TEXT NOT NULL,Target TEXT NOT NULL DEFAULT '');
        PRAGMA user_version=1;
        """;
}
