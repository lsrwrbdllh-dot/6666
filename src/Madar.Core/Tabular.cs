using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Madar.Core;

public static class Tabular
{
    private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static List<Dictionary<string, string>> Read(Stream input, string extension)
    {
        List<List<string>> rows;
        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) rows = ReadXlsx(input);
        else if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(input, Encoding.UTF8, true);
            var content = reader.ReadToEnd();
            if (content.Length > 10_000_000) throw new InvalidOperationException("الملف أكبر من المسموح.");
            rows = ReadCsv(content);
        }
        else throw new InvalidOperationException("اختر CSV بترميز UTF-8 أو Excel بصيغة XLSX.");
        if (rows.Count < 2 || rows.Count > 10_001) throw new InvalidOperationException("الملف يحتاج عناوين وصف بيانات واحداً على الأقل، وبحد أقصى 10000 صف.");
        var headers = rows[0].Select(s => s.Trim().TrimStart('\uFEFF')).ToList();
        if (headers.Count != headers.Distinct().Count()) throw new InvalidOperationException("عناوين الأعمدة مكررة.");
        return rows.Skip(1).Where(r => r.Any(s => s.Length > 0)).Select(row => headers.Select((name, i) => (name, value: i < row.Count ? row[i].Trim() : "")).ToDictionary(x => x.name, x => x.value)).ToList();
    }

    private static List<List<string>> ReadCsv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (quoted || cell.Length == 0) quoted = !quoted;
                else throw new InvalidOperationException("علامة اقتباس غير صحيحة في CSV.");
            }
            else if (c == ',' && !quoted) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c is '\r' or '\n' && !quoted)
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); rows.Add(row); row = new(); cell.Clear();
                if (rows.Count > 10_001) throw new InvalidOperationException("عدد الصفوف أكبر من المسموح.");
            }
            else cell.Append(c);
        }
        if (quoted) throw new InvalidOperationException("اقتباس CSV غير مغلق.");
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }

    private static XDocument Xml(ZipArchiveEntry entry)
    {
        if (entry.Length > 30_000_000) throw new InvalidOperationException("ورقة Excel أكبر من المسموح.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 30_000_000 });
        return XDocument.Load(reader);
    }
    private static List<List<string>> ReadXlsx(Stream input)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
        if (archive.Entries.Sum(e => e.Length) > 60_000_000 || archive.Entries.Count > 200) throw new InvalidOperationException("ملف Excel أكبر أو أعقد من المسموح.");
        var strings = archive.GetEntry("xl/sharedStrings.xml") is { } shared ? Xml(shared).Descendants(Sheet + "si").Select(s => string.Concat(s.Descendants(Sheet + "t").Select(t => t.Value))).ToList() : new List<string>();
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml") ?? throw new InvalidOperationException("ضع البيانات في الورقة الأولى.");
        var rows = new List<List<string>>();
        foreach (var record in Xml(entry).Descendants(Sheet + "row"))
        {
            var row = new List<string>();
            foreach (var cell in record.Elements(Sheet + "c"))
            {
                if (cell.Element(Sheet + "f") is not null) throw new InvalidOperationException("استبدل صيغ Excel بقيم ثابتة قبل الاستيراد.");
                var reference = (string?)cell.Attribute("r") ?? "A"; var index = 0;
                foreach (var c in reference.TakeWhile(char.IsLetter)) index = checked(index * 26 + char.ToUpperInvariant(c) - 'A' + 1);
                if (index is < 1 or > 50) throw new InvalidOperationException("الأعمدة أكبر من المسموح.");
                while (row.Count < index) row.Add("");
                var value = cell.Element(Sheet + "v")?.Value ?? string.Concat(cell.Descendants(Sheet + "t").Select(t => t.Value));
                if ((string?)cell.Attribute("t") == "s")
                {
                    if (!int.TryParse(value, out var item) || item < 0 || item >= strings.Count) throw new InvalidOperationException("نصوص Excel غير سليمة.");
                    value = strings[item];
                }
                row[index - 1] = value;
            }
            rows.Add(row);
            if (rows.Count > 10_001) throw new InvalidOperationException("عدد الصفوف أكبر من المسموح.");
        }
        return rows;
    }

    public static void Write(Stream output, IReadOnlyList<Dictionary<string, string>> rows, bool xlsx)
    {
        if (rows.Count == 0) throw new InvalidOperationException("لا توجد بيانات للتصدير.");
        var headers = rows.SelectMany(r => r.Keys).Distinct().ToList();
        if (!xlsx)
        {
            using var writer = new StreamWriter(output, new UTF8Encoding(true), leaveOpen: true);
            static string Cell(string value)
            {
                if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
                return '"' + value.Replace("\"", "\"\"") + '"';
            }
            writer.WriteLine(string.Join(',', headers.Select(Cell)));
            foreach (var row in rows) writer.WriteLine(string.Join(',', headers.Select(h => Cell(row.GetValueOrDefault(h, "")))));
            return;
        }
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
        void Add(string name, string text) { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
        Add("[Content_Types].xml", """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
        Add("_rels/.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
        Add("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="مدار سوفت" sheetId="1" r:id="r1"/></sheets></workbook>""");
        Add("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
        var xml = new XDocument(new XElement(Sheet + "worksheet", new XElement(Sheet + "sheetViews", new XElement(Sheet + "sheetView", new XAttribute("workbookViewId", "0"), new XAttribute("rightToLeft", "1"))), new XElement(Sheet + "sheetData")));
        var data = xml.Root!.Element(Sheet + "sheetData")!; var number = 0;
        foreach (var values in new[] { headers }.Concat(rows.Select(row => headers.Select(h => row.GetValueOrDefault(h, "")).ToList())))
        {
            number++; var row = new XElement(Sheet + "row", new XAttribute("r", number));
            for (var column = 0; column < values.Count; column++)
            {
                var index = column + 1; var label = "";
                while (index > 0) { index--; label = (char)('A' + index % 26) + label; index /= 26; }
                row.Add(new XElement(Sheet + "c", new XAttribute("r", label + number), new XAttribute("t", "inlineStr"), new XElement(Sheet + "is", new XElement(Sheet + "t", values[column]))));
            }
            data.Add(row);
        }
        Add("xl/worksheets/sheet1.xml", xml.ToString());
    }
}

public sealed partial class Store
{
    private long ImportProducts(JsonElement input)
    {
        Owner();
        var preview = PrepareProducts(input);
        if (preview.Errors.Count > 0) throw Error(string.Join("\n", preview.Errors.Take(12)));
        foreach (var row in preview.Rows) SaveProduct(JsonSerializer.SerializeToElement(row, Json));
        return preview.Rows.Count;
    }
    private sealed record ProductImport(List<object> Rows, List<string> Errors);
    private ProductImport PrepareProducts(JsonElement input)
    {
        Owner();
        if (!input.TryGetProperty("rows", out var records) || records.ValueKind != JsonValueKind.Array || records.GetArrayLength() is < 1 or > 10_000) throw Error("ملف الاستيراد فارغ أو كبير جداً.");
        var parsed = new List<object>(); var errors = new List<string>(); var barcodes = new HashSet<string>(StringComparer.Ordinal);
        var index = 1;
        foreach (var row in records.EnumerateArray())
        {
            index++;
            try
            {
                string Cell(string english, string arabic) => Str(row, english, Str(row, arabic));
                decimal Number(string english, string arabic)
                {
                    var value = Cell(english, arabic); if (value == "") return 0;
                    value = new string(value.Replace('٫', '.').Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c).ToArray());
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) throw Error("قيمة رقمية غير صحيحة."); return amount;
                }
                var name = Cell("name", "الاسم"); var barcode = Cell("barcode", "الباركود"); var unit = Cell("baseUnit", "الوحدة");
                if (name.Length is < 1 or > 500 || unit.Length is < 1 or > 100) throw Error("الاسم والوحدة مطلوبان.");
                if (barcode != "" && (!barcodes.Add(barcode) || Long("SELECT COUNT(*) FROM Products WHERE Barcode=$code", ("code", barcode)) > 0)) throw Error("الباركود مكرر: " + barcode);
                var price = Number("price", "السعر"); var minimum = Number("minimum", "حد المخزون");
                Money(price); Quantity(minimum);
                parsed.Add(new { name, barcode, baseUnit = unit, category = Cell("category", "التصنيف"), price, minimum, enabled = true });
            }
            catch (Exception error) when (error is InvalidOperationException or OverflowException) { errors.Add($"صف {index}: {error.Message}"); }
        }
        return new(parsed, errors);
    }
}
