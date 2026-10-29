namespace Madar.AI;

public sealed record ModelProfile(string Id, string Name, string FileName, long Bytes, string Sha256, string DownloadUrl);

public static class ModelCatalog
{
    public const string LlamaCommit = "815a2a5915f22ce6a760c676389c5dfe8535c08f";
    public static readonly ModelProfile Light = new(
        "qwen3-06b-q4km", "Qwen3 0.6B — خفيف", "Qwen_Qwen3-0.6B-Q4_K_M.gguf", 484220320,
        "9acfc1e001311f34b4252001b626f2e466d592a42065f66571bff3790d4e1b14",
        "https://huggingface.co/bartowski/Qwen_Qwen3-0.6B-GGUF/resolve/60b85c0e3d8fe0f6474f406922a26d12aca4550d/Qwen_Qwen3-0.6B-Q4_K_M.gguf?download=true");
    public static readonly ModelProfile Official = new(
        "qwen3-06b-q8", "Qwen3 0.6B — Q8", "Qwen3-0.6B-Q8_0.gguf", 639446688,
        "9465e63a22add5354d9bb4b99e90117043c7124007664907259bd16d043bb031",
        "https://huggingface.co/Qwen/Qwen3-0.6B-GGUF/resolve/23749fefcc72300e3a2ad315e1317431b06b590a/Qwen3-0.6B-Q8_0.gguf?download=true");
    public static IReadOnlyList<ModelProfile> All { get; } = [Light, Official];
}
