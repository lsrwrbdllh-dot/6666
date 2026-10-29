using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace Madar.AI;

public sealed record AssistantResult(string Answer, string Model, long ElapsedMs, int Tokens, bool Stopped);
public sealed record ModelStatus(bool Ready, ModelProfile? Model, bool Running);

/// <summary>A bounded CPU-only model. It receives read-only facts, never a database or write tools.</summary>
public sealed class LocalAssistant
{
    private readonly string directory;
    private readonly SemaphoreSlim operation = new(1);
    private CancellationTokenSource? active;
    private static readonly object nativeGate = new();
    private static bool configured;
    private static CancellationTokenSource? foregroundGeneration;
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    public const int ContextTokens = 1536;
    public const int OutputTokens = 128;

    public LocalAssistant(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
    }

    public static void ConfigureNative(string libraryPath)
    {
        lock (nativeGate)
        {
            if (configured) return;
            if (!File.Exists(libraryPath)) throw new InvalidOperationException("محرك الذكاء المحلي غير موجود في هذه الحزمة.");
            NativeLibraryConfig.All.WithCuda(false).WithVulkan(false).WithAutoFallback(false).WithLogCallback((LLamaLogLevel _, string _) => { });
            NativeLibraryConfig.LLama.WithLibrary(Path.GetFullPath(libraryPath));
            configured = true;
        }
    }

    public ModelStatus Status()
    {
        try
        {
            var info = JsonSerializer.Deserialize<ModelProfile>(File.ReadAllText(Path.Combine(directory, "model.json")), json);
            var known = ModelCatalog.All.SingleOrDefault(p => p.Id == info?.Id && p.Sha256 == info.Sha256);
            var file = new FileInfo(Path.Combine(directory, "model.gguf"));
            return new(known is not null && file.Exists && file.Length == known.Bytes, known, active is not null);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(false, null, active is not null); }
    }

    public async Task<ModelProfile> ImportAsync(Stream source, Action<long>? progress = null, CancellationToken cancellation = default)
    {
        if (!await operation.WaitAsync(0, cancellation)) throw new InvalidOperationException("أوقف الرد الجاري قبل تغيير ملف الذكاء.");
        var stage = Path.Combine(directory, "import.gguf.tmp");
        try
        {
            long size = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var file = File.Create(stage))
            {
                var buffer = new byte[131072]; int count;
                while ((count = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    size += count;
                    if (size > ModelCatalog.All.Max(p => p.Bytes)) throw new InvalidOperationException("ملف النموذج أكبر من النماذج المدعومة.");
                    hash.AppendData(buffer, 0, count);
                    await file.WriteAsync(buffer.AsMemory(0, count), cancellation);
                    progress?.Invoke(size);
                }
                await file.FlushAsync(cancellation);
            }
            var checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var profile = ModelCatalog.All.SingleOrDefault(p => p.Sha256 == checksum && p.Bytes == size)
                ?? throw new InvalidOperationException("ملف النموذج غير مطابق أو لم يكتمل تنزيله. اختر ملف GGUF الأصلي من الرابط المعروض.");
            // Replace only after a complete byte-for-byte integrity check. Keep the old model on failure.
            var marker = Path.Combine(directory, "model.json.tmp");
            await File.WriteAllTextAsync(marker, JsonSerializer.Serialize(profile, json), cancellation);
            File.Move(stage, Path.Combine(directory, "model.gguf"), true);
            File.Move(marker, Path.Combine(directory, "model.json"), true);
            return profile;
        }
        finally { File.Delete(stage); operation.Release(); }
    }

    public async Task RemoveAsync()
    {
        if (!await operation.WaitAsync(0)) throw new InvalidOperationException("أوقف الرد الجاري قبل حذف النموذج.");
        try { File.Delete(Path.Combine(directory, "model.json")); File.Delete(Path.Combine(directory, "model.gguf")); }
        finally { operation.Release(); }
    }

    public void Cancel() { try { Volatile.Read(ref active)?.Cancel(); } catch (ObjectDisposedException) { } }
    public static void CancelForeground() { try { Volatile.Read(ref foregroundGeneration)?.Cancel(); } catch (ObjectDisposedException) { } }

    public async Task<AssistantResult> AskAsync(string question, string facts, Func<string, Task>? onText = null, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 400) throw new InvalidOperationException("اكتب سؤالاً من 1 إلى 400 حرف.");
        if (facts.Length > 4000) throw new InvalidOperationException("ملخص البيانات أطول من المسموح.");
        if (!await operation.WaitAsync(0, cancellation)) throw new InvalidOperationException("هناك رد جارٍ؛ انتظر أو اضغط إيقاف.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stop.CancelAfter(TimeSpan.FromMinutes(3));
        active = stop; Interlocked.Exchange(ref foregroundGeneration, stop);
        var watch = Stopwatch.StartNew();
        var output = new StringBuilder(); int generated = 0;
        try
        {
            var status = Status();
            if (!status.Ready || status.Model is null) throw new InvalidOperationException("جهّز ملف الذكاء المحلي أولاً.");
            if (!configured) throw new InvalidOperationException("محرك الذكاء لم يُجهّز.");
            var parameters = new ModelParams(Path.Combine(directory, "model.gguf"))
            {
                ContextSize = ContextTokens, GpuLayerCount = 0, Threads = 2, BatchThreads = 2,
                BatchSize = 64, UBatchSize = 64, LoadMode = LLamaLoadMode.MemoryMap
            };
            // Short context, two threads and disposal after each response target 4 GB A50 devices.
            using var weights = await LLamaWeights.LoadFromFileAsync(parameters, stop.Token);
            var prompt = BuildPrompt(question, facts);
            if (weights.Tokenize(prompt, true, true, Encoding.UTF8).Length > ContextTokens - OutputTokens - 8)
                throw new InvalidOperationException("السؤال أو ملخصه طويل؛ اختصر السؤال أو اختر فترة أقصر.");
            var executor = new StatelessExecutor(weights, parameters);
            var inference = new InferenceParams
            {
                MaxTokens = OutputTokens, AntiPrompts = ["<|im_end|>", "<|im_start|>"],
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.5f, TopP = 0.8f, TopK = 20, PresencePenalty = 1.2f }
            };
            long last = 0;
            await foreach (var token in executor.InferAsync(prompt, inference, stop.Token))
            {
                output.Append(token); generated++;
                if (onText is not null && watch.ElapsedMilliseconds - last >= 200)
                { last = watch.ElapsedMilliseconds; await onText(Clean(output.ToString())); }
            }
            var answer = Clean(output.ToString());
            if (onText is not null) await onText(answer);
            if (answer.Length == 0 && !stop.IsCancellationRequested) throw new InvalidOperationException("لم ينتج النموذج جواباً؛ جرّب سؤالاً أقصر.");
            return new(answer, status.Model.Name, watch.ElapsedMilliseconds, generated, stop.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        { return new(Clean(output.ToString()), Status().Model?.Name ?? "Qwen3", watch.ElapsedMilliseconds, generated, true); }
        finally
        {
            Interlocked.CompareExchange(ref foregroundGeneration, null, stop);
            active = null; operation.Release();
        }
    }

    public static string BuildPrompt(string question, string facts)
    {
        static string Safe(string s) => s.Replace("<|", "＜|").Replace("|>", "|＞");
        return "<|im_start|>system\nأنت مساعد مدار سوفت. أجب بالعربية باختصار. استخدم الأرقام المرجعية عند السؤال عنها، ولا تخترع بيانات غير موجودة. اشرح فقط؛ لا تنفذ عمليات. /no_think\nالأرقام المرجعية من البرنامج:\n" + Safe(facts) +
            "<|im_end|>\n<|im_start|>user\n" + Safe(question.Trim()) + " /no_think<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n";
    }
    private static string Clean(string text) => Regex.Replace(text, "<think>.*?(</think>|$)", "", RegexOptions.Singleline).Replace("<|im_end|>", "").Replace("<|im_start|>", "").Trim();
}
