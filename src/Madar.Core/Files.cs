using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Madar.Core;

public sealed partial class Store
{
    public string Backup(string destination, bool requireOwner = true)
    {
        lock (gate)
        {
            if (requireOwner) AuthorizeFiles(true);
            var temporary = destination + ".tmp";
            var snapshot = Path.Combine(DirectoryPath, "backup-snapshot.db");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
            try
            {
                File.Delete(temporary);
                File.Delete(snapshot);
                using (var source = Open())
                using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Pooling = false }.ToString()))
                {
                    target.Open(); source.BackupDatabase(target);
                    using var checkpoint = target.CreateCommand();
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;";
                    checkpoint.ExecuteNonQuery();
                }
                var hashes = new Dictionary<string, string>();
                using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
                {
                    void Add(string path, string name)
                    {
                        zip.CreateEntryFromFile(path, name, CompressionLevel.Fastest);
                        using var file = File.OpenRead(path);
                        hashes[name] = Convert.ToHexString(SHA256.HashData(file));
                    }
                    Add(snapshot, "madar.db");
                    foreach (var file in Directory.EnumerateFiles(Path.Combine(DirectoryPath, "attachments"))) Add(file, "attachments/" + Path.GetFileName(file));
                    using var manifest = new StreamWriter(zip.CreateEntry("manifest.json").Open(), Encoding.UTF8);
                    manifest.Write(JsonSerializer.Serialize(new { version = SchemaVersion, createdAt = Now(), files = hashes }, Json));
                }
                File.Move(temporary, destination, true);
                return destination;
            }
            finally { File.Delete(snapshot); File.Delete(temporary); }
        }
    }

    public void Restore(string archivePath)
    {
        lock (gate)
        {
            AuthorizeFiles(true);
            var stage = Path.Combine(Path.GetDirectoryName(DirectoryPath)!, "restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    if (archive.Entries.Count > 10_000 || archive.Entries.Sum(e => e.Length) > 512L * 1024 * 1024) throw Error("حجم النسخة أو عدد ملفاتها غير مسموح.");
                    var manifestEntry = archive.GetEntry("manifest.json") ?? throw Error("النسخة لا تحتوي ملف تحقق.");
                    using var manifestStream = manifestEntry.Open(); using var manifest = JsonDocument.Parse(manifestStream);
                    if (manifest.RootElement.GetProperty("version").GetInt32() != SchemaVersion) throw Error("إصدار النسخة غير مدعوم.");
                    var hashes = manifest.RootElement.GetProperty("files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
                    if (!hashes.ContainsKey("madar.db")) throw Error("قاعدة البيانات غير موجودة في النسخة.");
                    foreach (var item in hashes)
                    {
                        if (item.Key != "madar.db" && !(item.Key.StartsWith("attachments/", StringComparison.Ordinal) && item.Key.Count(c => c == '/') == 1 && item.Key.Split('/')[1] == Path.GetFileName(item.Key))) throw Error("النسخة تحتوي مساراً غير مسموح.");
                        var entry = archive.GetEntry(item.Key) ?? throw Error("أحد ملفات النسخة مفقود.");
                        var target = Path.GetFullPath(Path.Combine(stage, item.Key));
                        if (!target.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw Error("مسار غير صالح.");
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        using (var file = File.Create(target)) using (var source = entry.Open()) source.CopyTo(file);
                        using var check = File.OpenRead(target);
                        if (Convert.ToHexString(SHA256.HashData(check)) != item.Value) throw Error("فشل التحقق من سلامة النسخة؛ لم تتغير البيانات الحالية.");
                    }
                }
                using (var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(stage, "madar.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
                {
                    check.Open(); using var command = check.CreateCommand();
                    command.CommandText = "PRAGMA quick_check";
                    if (Convert.ToString(command.ExecuteScalar()) != "ok") throw Error("قاعدة النسخة تالفة.");
                    command.CommandText = "PRAGMA user_version";
                    if (Convert.ToInt32(command.ExecuteScalar()) != SchemaVersion) throw Error("قاعدة النسخة بإصدار غير مدعوم.");
                    command.CommandText = "PRAGMA foreign_key_check";
                    using (var reader = command.ExecuteReader()) if (reader.Read()) throw Error("روابط بيانات النسخة غير سليمة.");
                    command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type IN ('trigger','view')";
                    if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw Error("تركيب النسخة غير متوافق.");
                    command.CommandText = "SELECT COUNT(*) FROM Users WHERE Role='Owner' AND Enabled=1";
                    if (Convert.ToInt64(command.ExecuteScalar()) < 1) throw Error("النسخة لا تحتوي حساب مدير مفعلاً.");
                    command.CommandText = "SELECT COUNT(*) FROM (SELECT DocumentId FROM Ledger GROUP BY DocumentId HAVING SUM(Debit-Credit)<>0)";
                    if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw Error("قيود النسخة غير متوازنة.");
                }
                Backup(Path.Combine(DirectoryPath, "backups", "before-restore.zip"));
                Directory.CreateDirectory(Path.Combine(stage, "attachments"));
                var retired = DirectoryPath + ".before-restore-" + Guid.NewGuid().ToString("N");
                Directory.Move(DirectoryPath, retired);
                try { Directory.Move(stage, DirectoryPath); }
                catch { Directory.Move(retired, DirectoryPath); throw; }
                // Retain the previous directory until the next successful manual housekeeping.
                session = null;
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }
    }

    public long Attach(long document, string fileName, Stream source)
    {
        lock (gate)
        {
            AuthorizeFiles();
            if (session!.Role == "Viewer") throw Error("لا تملك صلاحية إضافة المرفقات.");
            var suffix = Path.GetExtension(fileName).ToLowerInvariant();
            if (suffix is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".pdf")) throw Error("المرفق يجب أن يكون صورة أو PDF.");
            var stored = Guid.NewGuid().ToString("N") + suffix;
            var path = Path.Combine(DirectoryPath, "attachments", stored);
            try
            {
                using (var file = File.Create(path))
                {
                    var buffer = new byte[65536]; long total = 0; int read;
                    while ((read = source.Read(buffer)) > 0)
                    { total += read; if (total > 20 * 1024 * 1024) throw Error("الحد الأقصى للمرفق 20 ميغابايت."); file.Write(buffer, 0, read); }
                }
                using var connection = Open(); db = connection;
                if (Long("SELECT COUNT(*) FROM Documents WHERE Id=$id", ("id", document)) != 1) throw Error("احفظ الفاتورة أولاً.");
                Run("INSERT INTO Attachments(DocumentId,FileName,StoredName,CreatedAt,UserId) VALUES($document,$name,$stored,$date,$user)", ("document", document), ("name", Path.GetFileName(fileName)), ("stored", stored), ("date", Now()), ("user", session.Id));
                var id = LastId(); Audit("إضافة مرفق", document.ToString()); return id;
            }
            catch { File.Delete(path); throw; }
            finally { db = null!; }
        }
    }

    public string Attachment(long id)
    {
        lock (gate)
        {
            AuthorizeFiles();
            using var connection = Open(); db = connection;
            try
            {
                var stored = Scalar("SELECT StoredName FROM Attachments WHERE Id=$id", ("id", id));
                if (stored == "" || Path.GetFileName(stored) != stored) throw Error("المرفق غير موجود.");
                return Path.Combine(DirectoryPath, "attachments", stored);
            }
            finally { db = null!; }
        }
    }

    private void AuthorizeFiles(bool owner = false)
    {
        using var connection = Open(); db = connection;
        try { ValidateSession(); if (owner) Owner(); }
        finally { db = null!; }
    }

    public string Receipt(long id)
    {
        using var empty = JsonDocument.Parse("{}");
        using var request = JsonDocument.Parse("{\"id\":" + id + "}");
        var detail = JsonSerializer.SerializeToElement(Execute("document", request.RootElement), Json);
        var header = detail.GetProperty("header");
        var settings = detail.GetProperty("settings").EnumerateArray().ToDictionary(x => x.GetProperty("key").GetString()!, x => x.GetProperty("value").GetString()!);
        static string Escape(string? value) => WebUtility.HtmlEncode(value ?? "");
        var lines = new StringBuilder();
        foreach (var line in detail.GetProperty("lines").EnumerateArray())
            lines.Append($"<tr><td>{Escape(line.GetProperty("name").GetString())}<br>{Escape(line.GetProperty("unitName").GetString())}</td><td>{line.GetProperty("quantity").GetInt64()/1000m:0.###}</td><td>{line.GetProperty("total").GetInt64()/100m:0.00}</td></tr>");
        var paper = settings.GetValueOrDefault("paper", "80"); var width = paper == "58" ? "58mm" : paper == "A4" ? "190mm" : "80mm";
        return $"<!doctype html><html dir='rtl' lang='ar'><meta charset='utf-8'><style>@page{{size:{(paper == "A4" ? "A4" : width + " 250mm")};margin:4mm}}body{{font:14px sans-serif;width:{width};max-width:100%;margin:auto}}h1{{font-size:22px}}table{{width:100%;border-collapse:collapse}}td,th{{text-align:right;border-bottom:1px solid #ddd;padding:6px}}small{{color:#555}}</style><h1>{Escape(settings.GetValueOrDefault("storeName"))}</h1><p>{Escape(settings.GetValueOrDefault("address"))} {Escape(settings.GetValueOrDefault("phone"))}</p><h2>فاتورة #{id}</h2><p>{Escape(header.GetProperty("createdAt").GetString())}</p><p>{Escape(header.GetProperty("partyName").ValueKind == JsonValueKind.Null ? "نقدي" : header.GetProperty("partyName").GetString())}</p><table><tr><th>الصنف</th><th>الكمية</th><th>الإجمالي</th></tr>{lines}</table><p>الإجمالي: {header.GetProperty("total").GetInt64()/100m:0.00} {Escape(settings.GetValueOrDefault("currency"))}</p><p>المدفوع: {header.GetProperty("paid").GetInt64()/100m:0.00}</p><p>{Escape(header.GetProperty("note").GetString())}</p><small>مدار سوفت — {Escape(header.GetProperty("status").GetString())}</small></html>";
    }
}
