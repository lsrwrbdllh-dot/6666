using System.Text.Json;
using Madar.AI;
using Madar.Core;

namespace Supermarket.AndroidApp;

public sealed partial class MainPage
{
    private readonly LocalAssistant intelligence = new(Path.Combine(FileSystem.AppDataDirectory, "madar-ai"));
    private int aiEpoch;
    private CancellationTokenSource aiContext = new();

    private void CancelAiContext()
    {
        Interlocked.Increment(ref aiEpoch);
        var previous = Interlocked.Exchange(ref aiContext, new CancellationTokenSource());
        previous.Cancel(); previous.Dispose();
        intelligence.Cancel();
    }

    private async Task<bool> TryProcessAi(string raw)
    {
        JsonDocument request;
        try { request = JsonDocument.Parse(raw); }
        catch (JsonException) { return false; }
        using (request)
        {
            if (!request.RootElement.TryGetProperty("action", out var named) || named.ValueKind != JsonValueKind.String) return false;
            var action = named.GetString()!;
            if (!action.StartsWith("ai.", StringComparison.Ordinal)) return false;
            long id = 0;
            try
            {
                id = request.RootElement.GetProperty("id").GetInt64();
                var args = request.RootElement.GetProperty("args");
                if (action == "ai.cancel") { intelligence.Cancel(); await Reply(id, true, true, null); return true; }
                object? result;
                if (action == "ai.ask")
                {
                    var capture = await CaptureAi(args);
                    var question = args.GetProperty("question").GetString() ?? "";
                    var info = DeviceMemory();
                    if (!Environment.Is64BitProcess || info.TotalMb < 2700) throw new InvalidOperationException("المساعد المحلي يحتاج هاتف 64 بت بذاكرة 3 غيغابايت أو أكثر.");
                    if (info.AvailableMb < 850) throw new InvalidOperationException("الذاكرة المتاحة قليلة؛ أغلق بعض التطبيقات ثم أعد المحاولة.");
                    var nativePath = Android.App.Application.Context.ApplicationInfo?.NativeLibraryDir;
                    LocalAssistant.ConfigureNative(Path.Combine(nativePath ?? "", "libllama.so"));
                    var answer = await Task.Run(() => intelligence.AskAsync(question,
                        (capture.Training ? "بيانات تدريب منفصلة عن المتجر الحقيقي.\n" : "") + capture.Facts.GetProperty("summary").GetString(),
                        text => NotifyAi(id, text, capture.Epoch), cancellation: capture.Cancellation), capture.Cancellation);
                    if (capture.Epoch != Volatile.Read(ref aiEpoch)) throw new InvalidOperationException("توقف الرد بعد تغيير الجلسة أو وضع التدريب.");
                    result = new { answer.Answer, answer.Model, answer.ElapsedMs, answer.Tokens, answer.Stopped, facts = capture.Facts, training = capture.Training };
                }
                else
                {
                    int authorizedEpoch;
                    CancellationToken authorizedContext;
                    // Hold the app request lock only for short authenticated reads, not model inference.
                    await requests.WaitAsync();
                    try
                    {
                        Current.Execute("state", JsonSerializer.SerializeToElement(new { }));
                        if (action is "ai.import" or "ai.remove") production.Execute("users", JsonSerializer.SerializeToElement(new { }));
                        authorizedEpoch = Volatile.Read(ref aiEpoch);
                        authorizedContext = aiContext.Token;
                    }
                    finally { requests.Release(); }
                    switch (action)
                    {
                        case "ai.status":
                            var memory = DeviceMemory();
                            result = new { status = intelligence.Status(), recommended = ModelCatalog.Light, memoryMb = memory.TotalMb, availableMb = memory.AvailableMb, device = DeviceInfo.Current.Model, context = LocalAssistant.ContextTokens, maxOutput = LocalAssistant.OutputTokens }; break;
                        case "ai.download":
                            await MainThread.InvokeOnMainThreadAsync(() => Browser.Default.OpenAsync(ModelCatalog.Light.DownloadUrl, BrowserLaunchMode.External)); result = true; break;
                        case "ai.import":
                            var file = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "اختر ملف الذكاء GGUF بعد اكتمال تنزيله" }));
                            if (file is null) { result = false; break; }
                            if (!file.FileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("اختر ملف GGUF؛ فك ضغط ZIP أولاً إن كان مضغوطاً.");
                            await requests.WaitAsync(authorizedContext);
                            try
                            {
                                authorizedContext.ThrowIfCancellationRequested();
                                if (authorizedEpoch != Volatile.Read(ref aiEpoch)) throw new OperationCanceledException(authorizedContext);
                                production.Execute("users", JsonSerializer.SerializeToElement(new { }));
                            }
                            finally { requests.Release(); }
                            using (var stream = await file.OpenReadAsync()) result = await intelligence.ImportAsync(stream, cancellation: authorizedContext);
                            break;
                        case "ai.remove":
                            await requests.WaitAsync(authorizedContext);
                            try
                            {
                                production.Execute("users", JsonSerializer.SerializeToElement(new { }));
                                await intelligence.RemoveAsync(); result = true;
                            }
                            finally { requests.Release(); }
                            break;
                        default: throw new InvalidOperationException("عملية المساعد غير معروفة.");
                    }
                }
                await Reply(id, true, result, null);
            }
            catch (Exception error)
            {
                var message = error is OperationCanceledException ? "توقفت العملية بعد تغيير الجلسة أو وضع التدريب." : error is InvalidOperationException ? error.Message : "تعذر تشغيل المساعد المحلي. تحقق من ملف النموذج والذاكرة المتاحة ثم أعد المحاولة.";
                await Reply(id, false, null, message);
            }
            return true;
        }
    }

    private async Task<(JsonElement Facts, int Epoch, bool Training, CancellationToken Cancellation)> CaptureAi(JsonElement args)
    {
        await requests.WaitAsync();
        try
        {
            var facts = JsonSerializer.SerializeToElement(Current.Execute("assistant.facts", args), Store.Json);
            return (facts, Volatile.Read(ref aiEpoch), training is not null, aiContext.Token);
        }
        finally { requests.Release(); }
    }

    private Task NotifyAi(long id, string text, int epoch) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (epoch != Volatile.Read(ref aiEpoch)) return;
        var payload = JsonSerializer.Serialize(new { id, text }, Store.Json);
        await browser.EvaluateJavaScriptAsync("window.madarAiToken && window.madarAiToken(" + payload + ")");
    });
    private static (long TotalMb, long AvailableMb) DeviceMemory()
    {
        var manager = (Android.App.ActivityManager)Android.App.Application.Context.GetSystemService(Android.Content.Context.ActivityService)!;
        var info = new Android.App.ActivityManager.MemoryInfo(); manager.GetMemoryInfo(info);
        return (info.TotalMem / 1048576, info.AvailMem / 1048576);
    }
}
