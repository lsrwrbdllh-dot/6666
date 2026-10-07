using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Supermarket.AndroidApp;
using Supermarket.Contracts;

if(JsonSerializer.IsReflectionEnabledByDefault)throw new Exception("Run with JsonSerializerIsReflectionEnabledByDefault=false.");
var options=new JsonSerializerOptions(JsonSerializerDefaults.Web){TypeInfoResolver=MobileJsonContext.Default};
var request=new InvoiceRequest{RequestId=Guid.NewGuid(),Kind="Sale",Paid=12.5m,Note="اختبار",Items=new(){new(){ProductId=1,Quantity=1.25m,UnitPrice=10}}};
object body=request;
var json=JsonSerializer.Serialize(body,options.GetTypeInfo(body.GetType()));
var copy=JsonSerializer.Deserialize(json,MobileJsonContext.Default.InvoiceRequest)!;
if(copy.RequestId!=request.RequestId||copy.Items[0].Quantity!=1.25m||copy.Paid!=12.5m)throw new Exception("Invoice round trip failed.");
var pending=new PendingOperation{Route="invoices",Endpoint="https://store.example.com",Body=json};
var saved=JsonSerializer.Serialize(pending,MobileJsonContext.Default.PendingOperation);
var restored=JsonSerializer.Deserialize(saved,MobileJsonContext.Default.PendingOperation)!;
if(restored.Body!=json||restored.Endpoint!=pending.Endpoint)throw new Exception("Pending accounting request changed.");
var products=JsonSerializer.Deserialize("[{\"Id\":1,\"Name\":\"حليب\",\"Stock\":7,\"IsActive\":true}]",(JsonTypeInfo<List<Product>>)options.GetTypeInfo(typeof(List<Product>)))!;
if(products[0].Name!="حليب"||products[0].Stock!=7||!products[0].IsActive)throw new Exception("Product metadata or case-insensitive mapping failed.");
var rows=JsonSerializer.Deserialize("[{\"المبلغ\":7}]",(JsonTypeInfo<List<Dictionary<string,JsonElement>>>)options.GetTypeInfo(typeof(List<Dictionary<string,JsonElement>>)))!;
if(rows[0]["المبلغ"].GetDecimal()!=7)throw new Exception("Arabic report mapping failed.");
foreach(var type in new[]{typeof(List<Party>),typeof(List<Metric>),typeof(List<Document>),typeof(DocumentDetail),typeof(JsonElement),typeof(Posted),typeof(ProductInput),typeof(PartyInput),typeof(CashRequest)})
 if(options.GetTypeInfo(type)==null)throw new Exception($"Missing metadata for {type.Name}");
Console.WriteLine("PASS: Android JSON metadata, invoice persistence, pending retry snapshot and report models with reflection disabled and trimming enabled.");
