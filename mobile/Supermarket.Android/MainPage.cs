using System.Text.Json;
using System.Text.Json.Nodes;
using Android.Webkit;
using Java.Interop;
using Madar.Core;

namespace Supermarket.AndroidApp;

public sealed partial class MainPage : ContentPage
{
    private readonly Microsoft.Maui.Controls.WebView browser;
    private readonly Store production;
    private Store? training;
    private readonly SemaphoreSlim requests = new(1);
    private JsonElement? preparedImport;
    private string? importToken;
    private string? backupWarning;
    private Store Current => training ?? production;

    public MainPage()
    {
        BackgroundColor = Color.FromArgb("#F2F7FF");
        production = new Store(Path.Combine(FileSystem.AppDataDirectory, "madar-production"));
        browser = new Microsoft.Maui.Controls.WebView();
        browser.HandlerChanged += (_, _) =>
        {
            if (browser.Handler?.PlatformView is not Android.Webkit.WebView native) return;
            native.Settings.JavaScriptEnabled = true;
            native.Settings.AllowFileAccess = false;
            native.Settings.AllowContentAccess = false;
            native.Settings.AllowFileAccessFromFileURLs = false;
            native.Settings.AllowUniversalAccessFromFileURLs = false;
            native.AddJavascriptInterface(new LocalBridge(this), "MadarNative");
#if DEBUG
            Android.Webkit.WebView.SetWebContentsDebuggingEnabled(true);
#endif
            browser.Source = "file:///android_asset/www/index.html";
        };
        browser.Navigating += (_, e) =>
        {
            if (!(e.Url.StartsWith("file:///android_asset/www/", StringComparison.Ordinal) || e.Url == "about:blank")) e.Cancel = true;
        };
        Content = browser;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = browser.EvaluateJavaScriptAsync("window.madarBack && window.madarBack()");
        return true;
    }

    private sealed class LocalBridge(MainPage page) : Java.Lang.Object
    {
        [JavascriptInterface, Export("invoke")]
        public void Invoke(string request) => _ = page.Process(request);
    }

    private async Task Process(string raw)
    {
        if (raw.Length > 2_000_000) return;
        if (await TryProcessAi(raw)) return;
        long id = 0;
        await requests.WaitAsync();
        try
        {
            using var request = JsonDocument.Parse(raw);
            id = request.RootElement.GetProperty("id").GetInt64();
            var action = request.RootElement.GetProperty("action").GetString() ?? "";
            var args = request.RootElement.GetProperty("args");
            if (action is "logout" or "training.enter" or "training.exit" or "files.restore") CancelAiContext();
            object? result;
            switch (action)
            {
                case "logout": training?.Dispose(); training = null; result = production.Execute("logout", args); break;
                case "app.exit": await MainThread.InvokeOnMainThreadAsync(() => Platform.CurrentActivity?.Finish()); result = true; break;
                case "training.enter":
                    production.Execute("state", JsonSerializer.SerializeToElement(new { }));
                    training ??= new Store(Path.Combine(FileSystem.AppDataDirectory, "madar-training"));
                    var pin = Preferences.Default.Get("training-pin", "");
                    if (pin == "") { pin = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); Preferences.Default.Set("training-pin", pin); }
                    var status = JsonSerializer.SerializeToElement(training.Execute("status", JsonSerializer.SerializeToElement(new { })), Store.Json);
                    if (!status.GetProperty("initialized").GetBoolean())
                    {
                        training.Execute("bootstrap", JsonSerializer.SerializeToElement(new { name = "متدرّب", pin, storeName = "متجر التدريب", currency = "وحدة تدريب" }));
                        training.Execute("shift.open", JsonSerializer.SerializeToElement(new { }));
                        training.Execute("cash.post", JsonSerializer.SerializeToElement(new { kind = "Capital", amount = 1000, note = "بيانات تدريب فقط", requestId = Guid.NewGuid().ToString() }));
                        foreach (var item in new[] { ("حليب طازج", "1001", 5), ("أرز", "1002", 25), ("سكر", "1003", 8) })
                        {
                            training.Execute("products.save", JsonSerializer.SerializeToElement(new { name = item.Item1, barcode = item.Item2, baseUnit = "عبوة", category = "تدريب", price = item.Item3, minimum = 2 }));
                        }
                        var products = JsonSerializer.SerializeToElement(training.Execute("products", JsonSerializer.SerializeToElement(new { })), Store.Json);
                        var lines = products.GetProperty("units").EnumerateArray().Select(u => new { unitId = u.GetProperty("id").GetInt64(), quantity = 12, price = 2 }).ToArray();
                        training.Execute("invoice.post", JsonSerializer.SerializeToElement(new { kind = "Purchase", warehouseId = 1, lines, full = true, requestId = Guid.NewGuid().ToString(), note = "بيانات تدريب فقط" }));
                    }
                    else training.Execute("login", JsonSerializer.SerializeToElement(new { name = "متدرّب", pin }));
                    result = true; break;
                case "training.exit": training?.Dispose(); training = null; result = true; break;
                case "training":
                case "training.save": result = production.Execute(action, args); break;
                case "simulator.catalog":
                case "simulator.get":
                case "simulator.start":
                case "simulator.answer":
                    var practiceOwner = SimulatorOwner();
                    if (action is "simulator.start" or "simulator.answer") CancelAiContext();
                    result = simulator.Execute(practiceOwner, action, args); break;
                case "files.backup":
                    EnsureProduction();
                    var backup = production.Backup(Path.Combine(FileSystem.CacheDirectory, "Madar-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"));
                    await MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareFileRequest { Title = "نسخة مدار سوفت الاحتياطية", File = new ShareFile(backup, "application/zip") }));
                    result = true; break;
                case "files.restore":
                    EnsureProduction();
                    var restore = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "اختر نسخة مدار سوفت ZIP" }));
                    if (restore is null) { result = false; break; }
                    var local = Path.Combine(FileSystem.CacheDirectory, "madar-restore.zip");
                    try
                    {
                        using (var input = await restore.OpenReadAsync()) using (var output = File.Create(local))
                        {
                            var buffer = new byte[65536]; long copied = 0; int length;
                            while ((length = await input.ReadAsync(buffer)) > 0)
                            {
                                copied += length;
                                if (copied > 512L * 1024 * 1024) throw new InvalidOperationException("الحد الأقصى لملف النسخة 512 ميغابايت.");
                                await output.WriteAsync(buffer.AsMemory(0, length));
                            }
                        }
                        production.Restore(local); preparedImport = null; importToken = null; result = true;
                    }
                    finally { File.Delete(local); }
                    break;
                case "files.importPreview":
                    var chosen = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "اختر ملف المنتجات CSV أو XLSX" }));
                    if (chosen is null) { result = null; break; }
                    using (var file = await chosen.OpenReadAsync())
                    {
                        var rows = Tabular.Read(file, Path.GetExtension(chosen.FileName));
                        preparedImport = JsonSerializer.SerializeToElement(new { rows });
                        var preview = Current.Execute("products.importPreview", preparedImport.Value);
                        importToken = Guid.NewGuid().ToString();
                        result = new { token = importToken, preview };
                    }
                    break;
                case "files.importApply":
                    if (preparedImport is null || importToken != args.GetProperty("token").GetString()) throw new InvalidOperationException("أعد معاينة ملف الاستيراد.");
                    result = Current.Execute("products.import", preparedImport.Value); preparedImport = null; importToken = null; break;
                case "files.export":
                    Current.Execute("state", JsonSerializer.SerializeToElement(new { }));
                    var xlsx = args.GetProperty("format").GetString() == "xlsx";
                    var rowsToExport = args.GetProperty("rows").EnumerateArray().Take(10000).Select(r => r.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString())).ToList();
                    var export = Path.Combine(FileSystem.CacheDirectory, "Madar-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + (xlsx ? ".xlsx" : ".csv"));
                    using (var output = File.Create(export)) Tabular.Write(output, rowsToExport, xlsx);
                    await MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareFileRequest { Title = "تصدير مدار سوفت", File = new ShareFile(export) }));
                    result = true; break;
                case "files.attach":
                    Current.Execute("state", JsonSerializer.SerializeToElement(new { }));
                    var attachment = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "اختر صورة أو PDF" }));
                    if (attachment is null) { result = false; break; }
                    using (var file = await attachment.OpenReadAsync()) result = Current.Attach(args.GetProperty("documentId").GetInt64(), attachment.FileName, file);
                    break;
                case "files.open":
                    var attachmentPath = Current.Attachment(args.GetProperty("id").GetInt64());
                    await MainThread.InvokeOnMainThreadAsync(() => Launcher.Default.OpenAsync(new OpenFileRequest { File = new ReadOnlyFile(attachmentPath) }));
                    result = true; break;
                case "files.print":
                    var html = Current.Receipt(args.GetProperty("id").GetInt64());
                    await MainThread.InvokeOnMainThreadAsync(() => Print(html)); result = true; break;
                case "barcode.photo": result = await ReadBarcode(); break;
                default:
                    result = Current.Execute(action, args);
                    if (action == "state")
                    {
                        var state = JsonNode.Parse(JsonSerializer.Serialize(result, Store.Json))!;
                        state["training"] = training is not null;
                        state["lastBackup"] = Preferences.Default.Get("last-backup", "");
                        state["backupWarning"] = backupWarning;
                        state["legacyPending"] = await SecureStorage.Default.GetAsync("pending-post") is not null;
                        state["deviceMemoryMb"] = DeviceInfo.Current.Platform == DevicePlatform.Android ? Android.App.Application.Context.GetSystemService(Android.Content.Context.ActivityService) is Android.App.ActivityManager manager ? Memory(manager) : 0 : 0;
                        result = state;
                    }
                    break;
            }
            if (training is null && action is "invoice.post" or "cash.post" or "stock.adjust" or "shift.approve" or "files.attach" or "files.importApply")
            {
                try
                {
                    production.Backup(Path.Combine(production.DirectoryPath, "backups", "automatic.zip"), false);
                    Preferences.Default.Set("last-backup", DateTimeOffset.Now.ToString("O")); backupWarning = null;
                }
                catch { backupWarning = "حُفظت العملية، لكن تعذر تحديث النسخة الاحتياطية؛ أنشئ نسخة يدوية."; }
            }
            await Reply(id, true, result, null);
        }
        catch (Exception error)
        {
            var message = error is Microsoft.Data.Sqlite.SqliteException ? "تعذر حفظ البيانات. تحقق من التكرار وصحة الحقول والمساحة المتاحة." : error.Message;
            await Reply(id, false, null, message);
        }
        finally { requests.Release(); }
    }

    private void EnsureProduction()
    {
        if (training is not null) throw new InvalidOperationException("اخرج من وضع التدريب لإدارة نسخ البيانات الحقيقية.");
    }
    private Task Reply(long id, bool ok, object? data, string? message) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var json = JsonSerializer.Serialize(new { id, ok, data, message }, Store.Json);
        await browser.EvaluateJavaScriptAsync("window.madarReceive(" + json + ")");
    });
    private static long Memory(Android.App.ActivityManager manager)
    {
        var info = new Android.App.ActivityManager.MemoryInfo(); manager.GetMemoryInfo(info); return info.TotalMem / 1048576;
    }
    private Task Print(string html)
    {
        var context = Platform.CurrentActivity ?? throw new InvalidOperationException("تعذر فتح الطباعة.");
        var view = new Android.Webkit.WebView(context);
        view.SetWebViewClient(new PrintClient(context));
        view.LoadDataWithBaseURL(null, html, "text/html", "UTF-8", null);
        return Task.CompletedTask;
    }
    private sealed class PrintClient(Android.App.Activity context) : WebViewClient
    {
        private bool printed;
        public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
        {
            if (printed || view is null) return; printed = true;
            var printer = (Android.Print.PrintManager)context.GetSystemService(Android.Content.Context.PrintService)!;
            printer.Print("مدار سوفت", view.CreatePrintDocumentAdapter("Madar"), new Android.Print.PrintAttributes.Builder().Build());
        }
    }
    private async Task<string?> ReadBarcode()
    {
        Current.Execute("state", JsonSerializer.SerializeToElement(new { }));
        var file = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted) throw new InvalidOperationException("اسمح بالكاميرا لقراءة الباركود من صورة.");
            return await MediaPicker.Default.CapturePhotoAsync();
        });
        if (file is null) return null;
        using var stream = await file.OpenReadAsync();
        using var original = Android.Graphics.BitmapFactory.DecodeStream(stream) ?? throw new InvalidOperationException("تعذر قراءة الصورة.");
        var ratio = Math.Min(1, 1800d / Math.Max(original.Width, original.Height));
        using var bitmap = Android.Graphics.Bitmap.CreateScaledBitmap(original, (int)(original.Width * ratio), (int)(original.Height * ratio), true);
        var pixels = new int[bitmap.Width * bitmap.Height]; bitmap.GetPixels(pixels, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
        var bytes = new byte[pixels.Length * 3];
        for (var i = 0; i < pixels.Length; i++) { bytes[i*3]=(byte)(pixels[i]>>16); bytes[i*3+1]=(byte)(pixels[i]>>8); bytes[i*3+2]=(byte)pixels[i]; }
        var reader = new ZXing.BarcodeReaderGeneric { AutoRotate = true, Options = new ZXing.Common.DecodingOptions { TryHarder = true } };
        var result = reader.Decode(bytes, bitmap.Width, bitmap.Height, ZXing.RGBLuminanceSource.BitmapFormat.RGB24);
        return result?.Text ?? throw new InvalidOperationException("لم يظهر باركود واضح؛ قرّب الصورة أو أدخل الرقم يدوياً.");
    }
}
