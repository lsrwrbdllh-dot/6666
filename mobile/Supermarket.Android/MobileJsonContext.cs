using System.Text.Json;
using System.Text.Json.Serialization;
using Supermarket.Contracts;
namespace Supermarket.AndroidApp;

// Preserve model metadata in trimmed Android Release builds without reflection.
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true)]
[JsonSerializable(typeof(List<Product>))]
[JsonSerializable(typeof(List<Party>))]
[JsonSerializable(typeof(List<Metric>))]
[JsonSerializable(typeof(List<Document>))]
[JsonSerializable(typeof(DocumentDetail))]
[JsonSerializable(typeof(List<Dictionary<string,JsonElement>>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Posted))]
[JsonSerializable(typeof(ProductInput))]
[JsonSerializable(typeof(PartyInput))]
[JsonSerializable(typeof(InvoiceRequest))]
[JsonSerializable(typeof(CashRequest))]
[JsonSerializable(typeof(PendingOperation))]
internal partial class MobileJsonContext : JsonSerializerContext { }
