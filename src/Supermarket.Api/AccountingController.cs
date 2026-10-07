using Microsoft.AspNetCore.Mvc;
using Supermarket.Contracts;
namespace Supermarket.Api;

[ApiController,Route("api")]
public sealed class AccountingController(Store store) : ControllerBase
{
 private static bool Precision(decimal value,int digits)=>decimal.Round(value,digits)==value;
 [HttpGet("health")]
 public async Task<IActionResult> Health(CancellationToken token)
 {
  var rows=await store.Rows("SELECT OBJECT_ID('dbo.PostDocument') AS Posting,OBJECT_ID('dbo.PostSettlement') AS Settlement",token);
  return rows[0]["Posting"]!=null && rows[0]["Settlement"]!=null ? Ok(new {message="الاتصال وقاعدة البيانات جاهزان."}) : StatusCode(503,new {message="نفذ Setup.sql على قاعدة البيانات أولاً."});
 }
 [HttpGet("dashboard")]
 public async Task<IActionResult> Dashboard(CancellationToken token)=>Ok(await store.Rows("""
  SELECT N'رصيد الصندوق' AS Name,COALESCE(SUM(Debit-Credit),0) AS Value FROM dbo.Journal WHERE AccountCode=1100
  UNION ALL SELECT N'ذمم العملاء',COALESCE(SUM(Debit-Credit),0) FROM dbo.Journal WHERE AccountCode=1200
  UNION ALL SELECT N'ذمم الموردين',COALESCE(SUM(Credit-Debit),0) FROM dbo.Journal WHERE AccountCode=2100
  UNION ALL SELECT N'قيمة المخزون',COALESCE(SUM(Debit-Credit),0) FROM dbo.Journal WHERE AccountCode=1300
  UNION ALL SELECT N'صافي الربح منذ البداية',COALESCE(SUM(Credit-Debit),0) FROM dbo.Journal WHERE AccountCode IN (4100,5100,5200)
  """,token));
 [HttpGet("products")]
 public async Task<IActionResult> Products([FromQuery]string? search,[FromQuery]bool lowStock,CancellationToken token)=>Ok(await store.Rows("""
  SELECT Id,Barcode,Name,SalePrice,AverageCost,Stock,StockValue,MinimumStock,IsActive FROM dbo.Products
  WHERE (@Search=N'' OR Name LIKE @Like OR Barcode LIKE @Like) AND (@Low=0 OR (IsActive=1 AND Stock<=MinimumStock)) ORDER BY Name
  """,token,Store.P("@Search",search??""),Store.P("@Like","%"+(search??"")+"%"),Store.P("@Low",lowStock)));
 [HttpPost("products")]
 public async Task<IActionResult> CreateProduct(ProductInput input,CancellationToken token)
 {
  if(!ValidProduct(input))return BadRequest(new {message="الاسم والباركود مطلوبان؛ السعر بمنزلتين والحد بثلاث منازل."});
  var rows=await store.Rows("INSERT dbo.Products(Barcode,Name,SalePrice,MinimumStock,IsActive) OUTPUT INSERTED.Id VALUES(@Barcode,@Name,@Price,@Minimum,@Active)",token,Store.P("@Barcode",input.Barcode.Trim()),Store.P("@Name",input.Name.Trim()),Store.P("@Price",input.SalePrice),Store.P("@Minimum",input.MinimumStock),Store.P("@Active",input.IsActive));
  return Ok(rows[0]);
 }
 [HttpPut("products/{id:int}")]
 public async Task<IActionResult> UpdateProduct(int id,ProductInput input,CancellationToken token)
 {
  if(!ValidProduct(input))return BadRequest(new {message="بيانات المنتج غير صحيحة."});
  var rows=await store.Rows("UPDATE dbo.Products SET Barcode=@Barcode,Name=@Name,SalePrice=@Price,MinimumStock=@Minimum,IsActive=@Active OUTPUT INSERTED.Id WHERE Id=@Id",token,Store.P("@Barcode",input.Barcode.Trim()),Store.P("@Name",input.Name.Trim()),Store.P("@Price",input.SalePrice),Store.P("@Minimum",input.MinimumStock),Store.P("@Active",input.IsActive),Store.P("@Id",id));
  return rows.Count==0?NotFound():Ok(rows[0]);
 }
 private static bool ValidProduct(ProductInput input)=>!string.IsNullOrWhiteSpace(input.Name)&&!string.IsNullOrWhiteSpace(input.Barcode)&&Precision(input.SalePrice,2)&&Precision(input.MinimumStock,3);
 [HttpGet("parties")]
 public async Task<IActionResult> Parties([FromQuery]string? kind,CancellationToken token)
 {
  if(kind!=null && kind is not "Customer" and not "Supplier")return BadRequest();
  return Ok(await store.Rows("""
   SELECT p.Id,p.Name,p.Kind,p.Phone,COALESCE(SUM(CASE WHEN p.Kind='Customer' THEN j.Debit-j.Credit ELSE j.Credit-j.Debit END),0) AS Balance
   FROM dbo.Parties p LEFT JOIN dbo.Journal j ON j.PartyId=p.Id AND j.AccountCode IN (1200,2100)
   WHERE @Kind IS NULL OR p.Kind=@Kind GROUP BY p.Id,p.Name,p.Kind,p.Phone ORDER BY p.Name
   """,token,Store.P("@Kind",kind)));
 }
 [HttpPost("parties")]
 public async Task<IActionResult> CreateParty(PartyInput input,CancellationToken token)
 {
  if(string.IsNullOrWhiteSpace(input.Name))return BadRequest(new {message="الاسم مطلوب."});
  var rows=await store.Rows("INSERT dbo.Parties(Name,Kind,Phone) OUTPUT INSERTED.Id VALUES(@Name,@Kind,@Phone)",token,Store.P("@Name",input.Name.Trim()),Store.P("@Kind",input.Kind),Store.P("@Phone",input.Phone.Trim()));return Ok(rows[0]);
 }
 [HttpPut("parties/{id:int}")]
 public async Task<IActionResult> UpdateParty(int id,PartyInput input,CancellationToken token)
 {
  if(string.IsNullOrWhiteSpace(input.Name))return BadRequest(new {message="الاسم مطلوب."});
  var rows=await store.Rows("UPDATE dbo.Parties SET Name=@Name,Phone=@Phone OUTPUT INSERTED.Id WHERE Id=@Id AND Kind=@Kind",token,Store.P("@Name",input.Name.Trim()),Store.P("@Kind",input.Kind),Store.P("@Phone",input.Phone.Trim()),Store.P("@Id",id));
  return rows.Count==0?NotFound(new {message="الطرف غير موجود أو تغير نوعه."}):Ok(rows[0]);
 }
 [HttpPost("invoices")]
 public async Task<IActionResult> Invoice(InvoiceRequest input,CancellationToken token)
 {
  if(input.RequestId==Guid.Empty || !Precision(input.Paid,2) || input.Items.Any(i=>!Precision(i.Quantity,3)||!Precision(i.UnitPrice,2)) || input.Items.Select(i=>i.ProductId).Distinct().Count()!=input.Items.Count)
   return BadRequest(new {message="معرف الطلب أو دقة المبالغ والكميات غير صحيحة، أو يوجد صنف مكرر."});
  return Ok(new Posted {Id=await store.Post(input,null,token)});
 }
 [HttpPost("cash")]
 public async Task<IActionResult> Cash(CashRequest input,CancellationToken token)
 {
  if(input.RequestId==Guid.Empty||!Precision(input.Amount,2)||string.IsNullOrWhiteSpace(input.Note))return BadRequest(new {message="بيانات الحركة غير صحيحة."});
  if((input.Kind is "Receipt" or "Payment") && input.PartyId is not >0)return BadRequest(new {message="اختر الطرف."});
  if((input.Kind is "Expense" or "Capital") && input.PartyId!=null)return BadRequest(new {message="هذه الحركة لا تحتوي طرفاً."});
  return Ok(new Posted {Id=await store.Post(null,input,token)});
 }
 [HttpGet("documents")]
 public async Task<IActionResult> Documents([FromQuery]DateOnly? from,[FromQuery]DateOnly? to,CancellationToken token)
 {
  var start=from??DateOnly.FromDateTime(DateTime.Today.AddDays(-30));var end=to??DateOnly.FromDateTime(DateTime.Today);
  if(start>end || end==DateOnly.MaxValue)return BadRequest(new {message="الفترة غير صحيحة."});
  return Ok(await store.Rows("""
   SELECT TOP(500) d.Id,d.Kind,d.CreatedAt,p.Name AS PartyName,d.Total,d.Paid,d.Note FROM dbo.Documents d
   LEFT JOIN dbo.Parties p ON p.Id=d.PartyId WHERE d.CreatedAt>=@From AND d.CreatedAt<@To ORDER BY d.Id DESC
   """,token,Store.P("@From",start.ToDateTime(TimeOnly.MinValue)),Store.P("@To",end.AddDays(1).ToDateTime(TimeOnly.MinValue))));
 }
 [HttpGet("documents/{id:int}")]
 public async Task<IActionResult> Detail(int id,CancellationToken token)
 {
  var header=await store.Rows("SELECT d.Id,d.Kind,d.CreatedAt,p.Name AS PartyName,d.Total,d.Paid,d.Note FROM dbo.Documents d LEFT JOIN dbo.Parties p ON p.Id=d.PartyId WHERE d.Id=@Id",token,Store.P("@Id",id));
  if(header.Count==0)return NotFound();
  var lines=await store.Rows("SELECT p.Name,p.Barcode,l.Quantity,l.UnitPrice,l.LineTotal FROM dbo.DocumentLines l JOIN dbo.Products p ON p.Id=l.ProductId WHERE l.DocumentId=@Id",token,Store.P("@Id",id));
  return Ok(new {header=header[0],lines});
 }
 [HttpGet("reports/{kind}")]
 public async Task<IActionResult> Report(string kind,[FromQuery]DateOnly from,[FromQuery]DateOnly to,CancellationToken token)
 {
  if(from>to || to==DateOnly.MaxValue)return BadRequest(new {message="الفترة غير صحيحة."});
  string? sql=kind switch {
   "trial"=>"""
    SELECT a.Code AS [الحساب],a.Name AS [الاسم],COALESCE(SUM(j.Debit),0) AS [مدين],COALESCE(SUM(j.Credit),0) AS [دائن],COALESCE(SUM(j.Debit-j.Credit),0) AS [الرصيد]
    FROM dbo.Accounts a LEFT JOIN (SELECT j.* FROM dbo.Journal j JOIN dbo.Documents d ON d.Id=j.DocumentId WHERE d.CreatedAt<@To) j ON j.AccountCode=a.Code GROUP BY a.Code,a.Name ORDER BY a.Code
    """,
   "income"=>"""
    SELECT a.Name AS [الحساب],COALESCE(SUM(CASE WHEN a.Code=4100 THEN j.Credit-j.Debit ELSE j.Debit-j.Credit END),0) AS [المبلغ]
    FROM dbo.Accounts a LEFT JOIN (SELECT j.* FROM dbo.Journal j JOIN dbo.Documents d ON d.Id=j.DocumentId WHERE d.CreatedAt>=@From AND d.CreatedAt<@To) j ON j.AccountCode=a.Code
    WHERE a.Code IN (4100,5100,5200) GROUP BY a.Code,a.Name
    UNION ALL SELECT N'صافي الربح',COALESCE(SUM(j.Credit-j.Debit),0) FROM dbo.Journal j JOIN dbo.Documents d ON d.Id=j.DocumentId WHERE j.AccountCode IN (4100,5100,5200) AND d.CreatedAt>=@From AND d.CreatedAt<@To
    """,
   "journal"=>"""
    SELECT TOP(1000) d.Id AS [العملية],d.CreatedAt AS [التاريخ],a.Name AS [الحساب],p.Name AS [الطرف],j.Debit AS [مدين],j.Credit AS [دائن],d.Note AS [الوصف]
    FROM dbo.Journal j JOIN dbo.Documents d ON d.Id=j.DocumentId JOIN dbo.Accounts a ON a.Code=j.AccountCode LEFT JOIN dbo.Parties p ON p.Id=j.PartyId WHERE d.CreatedAt>=@From AND d.CreatedAt<@To ORDER BY d.Id DESC,j.Id
    """,
   _=>null
  };
  return sql==null?NotFound():Ok(await store.Rows(sql,token,Store.P("@From",from.ToDateTime(TimeOnly.MinValue)),Store.P("@To",to.AddDays(1).ToDateTime(TimeOnly.MinValue))));
 }
}
