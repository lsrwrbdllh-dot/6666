using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Supermarket.Contracts;
namespace Supermarket.AndroidApp;

public sealed class ApiError(string message,int status) : Exception(message)
{
 public int Status { get; }=status;
}
public sealed class ApiClient
{
 public static ApiClient Current { get; }=new();
 public static JsonSerializerOptions Json { get; }=new(JsonSerializerDefaults.Web){TypeInfoResolver=MobileJsonContext.Default};
 private readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(30)};
 private readonly SemaphoreSlim gate=new(1,1);
 public string Endpoint { get; private set; }="";
 private string key="";
 private bool loaded;
 public async Task Load()
 {
  if(loaded)return;
  Endpoint=Preferences.Default.Get("api-url","");key=await SecureStorage.Default.GetAsync("api-key")??"";loaded=true;
 }
 public async Task SaveSettings(string url,string apiKey)
 {
  await Load();if(string.IsNullOrWhiteSpace(apiKey))apiKey=key;
  if(!Uri.TryCreate(url.Trim(),UriKind.Absolute,out var uri)||string.IsNullOrEmpty(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))throw new Exception("أدخل رابط الخادم الصحيح بدون مسار أو بيانات دخول.");
  if(uri.AbsolutePath!="/")throw new Exception("أدخل عنوان الخادم فقط، مثلاً https://store.example.com:5443");
#if DEBUG
  if(uri.Scheme is not "https" and not "http")throw new Exception("الرابط يجب أن يبدأ بـ http أو https.");
#else
  if(uri.Scheme!="https")throw new Exception("نسخة التشغيل تتطلب HTTPS.");
#endif
  if(apiKey.Trim().Length<32||apiKey.Trim().Length>256)throw new Exception("مفتاح الاتصال يجب أن يكون بين 32 و256 حرفاً.");
  var pending=await Pending();
  var normalized=uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
  if(pending!=null&&pending.Endpoint!=normalized)throw new Exception("يوجد طلب معلّق لخادم آخر. تحقّق منه قبل تغيير الخادم.");
  await SecureStorage.Default.SetAsync("api-key",apiKey.Trim());Preferences.Default.Set("api-url",normalized);
  Endpoint=normalized;key=apiKey.Trim();loaded=true;
 }
 public async Task<T> Get<T>(string route)=>await Send<T>(HttpMethod.Get,route,null);
 public async Task<T> Write<T>(string route,object body,bool update=false)=>await Send<T>(update?HttpMethod.Put:HttpMethod.Post,route,JsonSerializer.Serialize(body,Json.GetTypeInfo(body.GetType())));
 private async Task<T> Send<T>(HttpMethod method,string route,string? body)
 {
  await Load();if(string.IsNullOrEmpty(Endpoint)||string.IsNullOrEmpty(key))throw new Exception("افتح إعداد الاتصال وأدخل عنوان الخادم والمفتاح.");
  using var request=new HttpRequestMessage(method,Endpoint+"/api/"+route);request.Headers.Add("X-Api-Key",key);
  if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
  using var response=await http.SendAsync(request);
  if(!response.IsSuccessStatusCode) {
   var text=await response.Content.ReadAsStringAsync();string message="تعذر تنفيذ العملية.";
   try {
    using var json=JsonDocument.Parse(text);
    if(json.RootElement.TryGetProperty("message",out var m))message=m.GetString()??message;
    else if(json.RootElement.TryGetProperty("title",out var title))message=title.GetString()??message;
   } catch(JsonException) { }
   throw new ApiError(message,(int)response.StatusCode);
  }
  return await response.Content.ReadFromJsonAsync((JsonTypeInfo<T>)Json.GetTypeInfo(typeof(T)))??throw new Exception("استجابة الخادم فارغة.");
 }
 public async Task<PendingOperation?> Pending()
 {
  var text=await SecureStorage.Default.GetAsync("pending-post");
  return text==null?null:JsonSerializer.Deserialize(text,MobileJsonContext.Default.PendingOperation);
 }
 public async Task<Posted> PostAccounting(string route,object body)
 {
  await gate.WaitAsync();
  try {
   await Load();
   if(string.IsNullOrEmpty(Endpoint)||string.IsNullOrEmpty(key))throw new Exception("اضبط الاتصال أولاً.");
   if(await Pending()!=null)throw new Exception("يوجد طلب معلّق. افتح الملخص لإعادة إرساله قبل إنشاء عملية أخرى.");
   var pending=new PendingOperation{Route=route,Body=JsonSerializer.Serialize(body,Json.GetTypeInfo(body.GetType())),Endpoint=Endpoint};
   await SecureStorage.Default.SetAsync("pending-post",JsonSerializer.Serialize(pending,MobileJsonContext.Default.PendingOperation));
   return await Submit(pending);
  } finally {gate.Release();}
 }
 public async Task<Posted> Retry()
 {
  await gate.WaitAsync();
  try {var pending=await Pending()??throw new Exception("لا يوجد طلب معلّق.");return await Submit(pending);}
  finally {gate.Release();}
 }
 private async Task<Posted> Submit(PendingOperation pending)
 {
  await Load();if(pending.Endpoint!=Endpoint)throw new Exception("الطلب المعلّق يخص خادماً آخر.");
  try {
   var result=await Send<Posted>(HttpMethod.Post,pending.Route,pending.Body);
   using(var body=JsonDocument.Parse(pending.Body)) {
    if(body.RootElement.TryGetProperty("requestId",out var requestId))Preferences.Default.Set("last-posted-request",requestId.GetString()??"");
   }
   SecureStorage.Default.Remove("pending-post");return result;
  } catch(ApiError error) when(error.Status is 400 or 409) {
   // The API has confirmed rejection, so there is no uncertain commit to retry.
   SecureStorage.Default.Remove("pending-post");throw;
  }
 }
 public async Task ClearPending()
 {
  await gate.WaitAsync();try {SecureStorage.Default.Remove("pending-post");}finally {gate.Release();}
 }
}
