using System.Text.Json;
using System.Text.Json.Nodes;
using Madar.Core;
using Madar.AI;

// Test-only stdin bridge: the shipped Android application has no HTTP server.
if (args.Length != 1) throw new ArgumentException("Pass an isolated test data directory.");
using var store = new Store(args[0]);
var intelligence = new LocalAssistant(Path.Combine(args[0], "ai"));
string? line;
while ((line = Console.ReadLine()) is not null)
{
    long id = 0;
    try
    {
        using var request = JsonDocument.Parse(line);
        id = request.RootElement.GetProperty("id").GetInt64();
        var action = request.RootElement.GetProperty("action").GetString()!;
        object? value;
        if (action == "ai.status")
        {
            store.Execute("state", JsonSerializer.SerializeToElement(new { }));
            value = new { status = intelligence.Status(), recommended = ModelCatalog.Light, device = "اختبار المتصفح", memoryMb = 0 };
        }
        else value = store.Execute(action, request.RootElement.GetProperty("args"));
        if (action == "state")
        {
            var state = JsonNode.Parse(JsonSerializer.Serialize(value, Store.Json))!;
            state["training"] = false; state["lastBackup"] = ""; state["legacyPending"] = false;
            value = state;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { id, ok = true, data = value }, Store.Json));
    }
    catch (Exception error) { Console.WriteLine(JsonSerializer.Serialize(new { id, ok = false, message = error.Message }, Store.Json)); }
}
