using System.Text.Json;
using Supermarket.Contracts;
namespace Supermarket.AndroidApp;

public sealed class InvoicePage : Screen
{
 private readonly string kind;private readonly Picker product=new(){Title="المنتج"},party=new(){Title="الطرف"};
 private readonly Entry barcode,quantity,price,paid,note;
 private readonly Switch full=new(){IsToggled=true};private readonly Label total;
 private readonly VerticalStackLayout basket=new(){Spacing=8};
 private List<Product> products=new();private readonly List<(Product Product,InvoiceItem Item)> items=new();
 private Guid lastRequest;
 public InvoicePage(string type):base(type=="Sale"?"فاتورة بيع":"فاتورة شراء")
 {
  kind=type;barcode=Input("باركود ثم Enter");quantity=Input("الكمية","1",true);price=Input("سعر الوحدة","0",true);paid=Input("المدفوع","0",true);paid.IsEnabled=false;
  note=Input("ملاحظات");note.MaxLength=500;total=Text("الإجمالي: 0",22);
  Body.Add(party);Body.Add(Button("تحديث المنتجات والأطراف",Load));Body.Add(barcode);Body.Add(product);Body.Add(quantity);Body.Add(price);
  product.SelectedIndexChanged+=(_,_)=>{if(product.SelectedItem is Product p)price.Text=(kind=="Sale"?p.SalePrice:p.AverageCost).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);};
  barcode.Completed+=async(_,_)=>await Run(()=>{
   var found=products.FirstOrDefault(p=>p.Barcode==barcode.Text?.Trim());if(found==null)throw new Exception("الباركود غير موجود أو المنتج غير نشط.");product.SelectedItem=found;Add();barcode.Text="";return Task.CompletedTask;
  });
  Body.Add(Button("إضافة صنف",()=>{Add();return Task.CompletedTask;}));Body.Add(basket);Body.Add(total);
  Body.Add(new HorizontalStackLayout{Children={Text("دفع كامل"),full},Spacing=10});full.Toggled+=(_,_)=>{paid.IsEnabled=!full.IsToggled;UpdateBasket();};
  Body.Add(paid);Body.Add(note);Body.Add(Button("حفظ وترحيل الفاتورة",Save));
  Body.Add(Text("عند انقطاع الاتصال، يوجد الطلب نفسه في الملخص لإعادة إرساله دون تكرار الفاتورة."));
 }
 private async Task Load()
 {
  if(lastRequest!=Guid.Empty && Preferences.Default.Get("last-posted-request","")==lastRequest.ToString())Reset();
  products=(await Api.Get<List<Product>>("products")).Where(p=>p.IsActive).ToList();product.ItemsSource=products;product.SelectedIndex=products.Count>0?0:-1;
  var parties=await Api.Get<List<Party>>($"parties?kind={(kind=="Sale"?"Customer":"Supplier")}");parties.Insert(0,new Party{Id=0,Name="نقدي بدون طرف"});party.ItemsSource=parties;party.SelectedIndex=0;
 }
 private void Add()
 {
  if(product.SelectedItem is not Product selected)throw new Exception("اختر المنتج.");
  decimal q=Number(quantity,3),p=Number(price);if(q<=0)throw new Exception("الكمية يجب أن تكون موجبة.");
  if(items.Any(i=>i.Product.Id==selected.Id))throw new Exception("المنتج مضاف. احذف السطر وأعد إضافته بالكمية المطلوبة.");
  if(items.Count>=200)throw new Exception("الحد الأقصى 200 صنف في الفاتورة.");
  items.Add((selected,new InvoiceItem{ProductId=selected.Id,Quantity=q,UnitPrice=p}));UpdateBasket();
 }
 private decimal Total()=>items.Sum(i=>decimal.Round(i.Item.Quantity*i.Item.UnitPrice,2,MidpointRounding.AwayFromZero));
 private void UpdateBasket()
 {
  basket.Clear();foreach(var row in items.ToList())basket.Add(Card(Text(row.Product.Name),Text($"{row.Item.Quantity:N3} × {Money(row.Item.UnitPrice)} = {Money(decimal.Round(row.Item.Quantity*row.Item.UnitPrice,2,MidpointRounding.AwayFromZero))}"),Button("حذف السطر",()=>{items.Remove(row);UpdateBasket();return Task.CompletedTask;})));
  total.Text=$"الإجمالي: {Money(Total())}";if(full.IsToggled)paid.Text=Total().ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
 }
 private async Task Save()
 {
  var value=Total();if(items.Count==0||value<=0||value>100000000)throw new Exception("أضف منتجات بإجمالي موجب ضمن الحد المسموح.");
  decimal payment=full.IsToggled?value:Number(paid);if(payment>value)throw new Exception("المدفوع أكبر من الإجمالي.");
  int? partyId=party.SelectedItem is Party p && p.Id>0?p.Id:null;
  if(payment<value&&partyId==null)throw new Exception("اختر عميلاً أو مورداً للفواتير الآجلة.");
  lastRequest=Guid.NewGuid();var request=new InvoiceRequest{Kind=kind,RequestId=lastRequest,PartyId=partyId,Paid=payment,Note=note.Text??"",Items=items.Select(x=>new InvoiceItem{ProductId=x.Item.ProductId,Quantity=x.Item.Quantity,UnitPrice=x.Item.UnitPrice}).ToList()};
  var result=await Api.PostAccounting("invoices",request);Reset();await DisplayAlertAsync("تم الحفظ",$"رقم الفاتورة {result.Id}","حسناً");await Load();
 }
 private void Reset(){items.Clear();note.Text="";lastRequest=Guid.Empty;UpdateBasket();}
 protected override async void OnAppearing(){base.OnAppearing();await Run(Load);}
}
public sealed class DocumentsPage : Screen
{
 private readonly DatePicker from=new(){Date=DateTime.Today.AddDays(-30)},to=new(){Date=DateTime.Today};private readonly VerticalStackLayout list=new(){Spacing=10};
 public DocumentsPage():base("سجل العمليات")
 {
  Body.Add(Text("من"));Body.Add(from);Body.Add(Text("إلى"));Body.Add(to);Body.Add(Button("عرض",Reload));Body.Add(Text("يعرض آخر 500 عملية خلال الفترة."));Body.Add(list);
 }
 public static string Date(DatePicker picker)=>picker.Date is DateTime date?date.ToString("yyyy-MM-dd"):throw new Exception("اختر التاريخ.");
 private async Task Reload()
 {
  var rows=await Api.Get<List<Document>>($"documents?from={Date(from)}&to={Date(to)}");list.Clear();foreach(var row in rows)list.Add(Card(Text($"#{row.Id} · {Kind(row.Kind)}",20),Text($"{row.CreatedAt:g} · {row.PartyName}"),Text($"الإجمالي: {Money(row.Total)} · المدفوع: {Money(row.Paid)} · الآجل: {Money(row.Total-row.Paid)}"),Text(row.Note),Button("التفاصيل / مشاركة",()=>Navigation.PushAsync(new DetailPage(row.Id)))));
  if(rows.Count==0)list.Add(Text("لا توجد عمليات خلال الفترة."));
 }
 protected override async void OnAppearing(){base.OnAppearing();await Run(Reload);}
}
public sealed class DetailPage : Screen
{
 private readonly int id;private string receipt="";
 public DetailPage(int value):base($"عملية #{value}"){id=value;}
 private async Task Reload()
 {
  var detail=await Api.Get<DocumentDetail>($"documents/{id}");var row=detail.Header;Body.Clear();
  receipt=$"عملية #{id} · {Kind(row.Kind)}\n{row.CreatedAt:g}\n{row.PartyName}\nالإجمالي {Money(row.Total)} · المدفوع {Money(row.Paid)}\n{row.Note}\n";
  Body.Add(Text(receipt));foreach(var line in detail.Lines){string text=$"{line.Name} · {line.Barcode}\n{line.Quantity:N3} × {Money(line.UnitPrice)} = {Money(line.LineTotal)}";receipt+=text+"\n";Body.Add(Card(Text(text)));}
  Body.Add(Button("مشاركة تفاصيل العملية",()=>Share.Default.RequestAsync(new ShareTextRequest{Title=$"عملية {id}",Text=receipt})));
 }
 protected override async void OnAppearing(){base.OnAppearing();await Run(Reload);}
}
public sealed class ReportsPage : Screen
{
 private readonly Picker kind=new(){Title="التقرير",ItemsSource=new List<string>{"ميزان المراجعة","قائمة الدخل","دفتر اليومية"},SelectedIndex=0};
 private readonly DatePicker from=new(){Date=DateTime.Today.AddDays(-30)},to=new(){Date=DateTime.Today};private readonly VerticalStackLayout list=new(){Spacing=10};
 private List<Dictionary<string,JsonElement>> rows=new();private readonly string[] kinds={"trial","income","journal"};
 public ReportsPage():base("التقارير")
 {
  Body.Add(kind);Body.Add(Text("من"));Body.Add(from);Body.Add(Text("إلى"));Body.Add(to);Body.Add(Button("عرض التقرير",Reload));Body.Add(Button("تصدير CSV ومشاركته",()=>ExportRows(rows)));
  Body.Add(Text("ميزان المراجعة تراكمي حتى النهاية. قائمة الدخل واليومية للفترة. اليومية تعرض آخر 1000 سطر."));Body.Add(list);
 }
 private async Task Reload()
 {
  rows=await Api.Get<List<Dictionary<string,JsonElement>>>($"reports/{kinds[kind.SelectedIndex]}?from={DocumentsPage.Date(from)}&to={DocumentsPage.Date(to)}");
  list.Clear();foreach(var row in rows)list.Add(Card(row.Select(v=>Text($"{v.Key}: {v.Value}")).Cast<View>().ToArray()));if(rows.Count==0)list.Add(Text("لا توجد بيانات."));
 }
 protected override async void OnAppearing(){base.OnAppearing();await Run(Reload);}
}
