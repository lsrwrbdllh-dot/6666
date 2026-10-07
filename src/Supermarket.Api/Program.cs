using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Supermarket.Api;

var builder = WebApplication.CreateBuilder(args);
var key = builder.Configuration["ApiKey"];
var connection = builder.Configuration.GetConnectionString("Supermarket");
if (string.IsNullOrWhiteSpace(key) || key.Length < 32 || key.Length > 256)
 throw new InvalidOperationException("Set ApiKey to a random secret of at least 32 characters using environment variables or user-secrets.");
if (string.IsNullOrWhiteSpace(connection))
 throw new InvalidOperationException("Set ConnectionStrings__Supermarket on the API host, never in the Android app.");
var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
builder.Services.AddSingleton(new Store(connection));
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.DictionaryKeyPolicy=System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddRateLimiter(options => {
 options.RejectionStatusCode = 429;
 options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
  context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
   PermitLimit=120, Window=TimeSpan.FromMinutes(1), QueueLimit=0, AutoReplenishment=true
  }));
});
var app = builder.Build();
app.Use(async (context,next) => {
 if (!app.Environment.IsDevelopment() && !context.Request.IsHttps) {
  context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new {message="استخدم اتصال HTTPS."});return;
 }
 var supplied=context.Request.Headers["X-Api-Key"].ToString();
 if(supplied.Length>256 || !CryptographicOperations.FixedTimeEquals(keyHash,SHA256.HashData(Encoding.UTF8.GetBytes(supplied)))) {
  context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(new {message="مفتاح الاتصال غير صحيح."});return;
 }
 try {await next(context);}
 catch(SqlException e) {
  context.Response.StatusCode=e.Number is >=50000 and <=50999 || e.Number is 2627 or 2601 ? 409 : 503;
  var message=e.Number is >=50000 and <=50999 ? e.Message : e.Number is 2627 or 2601 ? "الباركود أو الطلب مسجل بالفعل." : "تعذر الاتصال بقاعدة البيانات. تحقق من الخادم ثم أعد المحاولة.";
  if(context.Response.StatusCode==503) app.Logger.LogWarning("SQL operation failed with number {Number}",e.Number);
  await context.Response.WriteAsJsonAsync(new {message});
 }
 catch(Exception e) when(e is not OperationCanceledException) {
  app.Logger.LogError("Request failed with exception type {Type}",e.GetType().Name);
  context.Response.StatusCode=500;await context.Response.WriteAsJsonAsync(new {message="تعذر تنفيذ الطلب. تحقق من سجل الخادم."});
 }
});
app.UseRateLimiter();
app.MapControllers().RequireRateLimiting("api");
app.Run();
