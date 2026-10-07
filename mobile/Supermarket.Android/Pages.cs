using Supermarket.Contracts;
namespace Supermarket.AndroidApp;

public sealed class SettingsPage : Screen
{
 private readonly Entry url,key;
 public SettingsPage():base("إعداد الاتصال")
 {
  Body.Add(Text("اتصال الهاتف بالخادم",22));Body.Add(Text("أدخل عنوان API ومفتاح الاتصال الذي أعدّه مسؤول المتجر."));
  url=Input("https://store.example.com:5443");key=Input("مفتاح الاتصال؛ اتركه فارغاً للإبقاء على الحالي");key.IsPassword=true;
  Body.Add(url);Body.Add(key);
  Body.Add(Button("حفظ واختبار الاتصال",async()=>{await Api.SaveSettings(url.Text??"",key.Text??"");await Api.Get<System.Text.Json.JsonElement>("health");key.Text="";await DisplayAlertAsync("تم","الخادم وقاعدة البيانات متصلان.","حسناً");}));
  Body.Add(Text("نسخة التشغيل تستخدم HTTPS. بيانات SQL Server تحفظ على الخادم فقط."));
 }
 protected override async void OnAppearing(){base.OnAppearing();await Run(async()=>{await Api.Load();url.Text=Api.Endpoint;});}
}
public sealed class SummaryPage : Screen
{
 private readonly VerticalStackLayout metrics=new(){Spacing=10};
 private readonly Label pending;
 public SummaryPage():base("الملخص")
 {
  Body.Add(Text("متابعة المتجر",24));Body.Add(Button("تحديث",Reload));pending=Text("");Body.Add(pending);
  Body.Add(Button("إعادة إرسال الطلب المعلّق",async()=>{var result=await Api.Retry();await DisplayAlertAsync("تم الحفظ",$"رقم العملية {result.Id}","حسناً");await Reload();}));
  Body.Add(Button("إزالة طلب معلّق بعد التحقق",async()=>{
   if(await Api.Pending()==null)throw new Exception("لا يوجد طلب معلّق.");
   if(await DisplayAlertAsync("تحقق من سجل العمليات أولاً","قد يكون الخادم حفظ العملية بالفعل. إزالة الطلب تمنع إعادة إرساله بهذا المعرف. هل تحققت من السجل؟","تحققت؛ إزالة","إلغاء")){await Api.ClearPending();await Reload();}
  }));Body.Add(metrics);
 }
 private async Task Reload()
 {
  pending.Text=await Api.Pending()==null?"لا توجد عمليات حفظ معلقة.":"توجد عملية معلّقة؛ أعد إرسالها بنفس المعرف أو تحقق من سجل العمليات.";
  var rows=await Api.Get<List<Metric>>("dashboard");metrics.Clear();foreach(var row in rows)metrics.Add(Card(Text(row.Name),Text(Money(row.Value),24)));
 }
 protected override async void OnAppearing(){base.OnAppearing();await Run(Reload);}
}
public sealed class ProductsPage : Screen
{
 private readonly Entry search;private readonly Switch low=new();private readonly VerticalStackLayout list=new(){Spacing=10};
 public ProductsPage():base("المنتجات والمخزون")
 {
  search=Input("بحث بالاسم أو الباركود");Body.Add(search);Body.Add(new HorizontalStackLayout{Children={Text("أصناف عند حد المخزون"),low},Spacing=10});
  Body.Add(Button("بحث / تحديث",Reload));Body.Add(Button("إضافة منتج",()=>Navigation.PushAsync(new ProductEditPage(null))));Body.Add(list);
 }
 private async Task Reload()
 {
  var rows=await Api.Get<List<Product>>($"products?search={Uri.EscapeDataString(search.Text??"")}&lowStock={low.IsToggled.ToString().ToLowerInvariant()}");
  list.Clear();foreach(var product in rows)list.Add(Card(Text(product.Name,20),Text($"باركود: {product.Barcode}"),Text($"بيع: {Money(product.SalePrice)} · المخزون: {product.Stock:N3}"),Text($"التكلفة: {product.AverageCost:N4} · القيمة: {Money(product.StockValue)} · {(product.IsActive?"نشط":"معطل")}"),Button("تعديل",()=>Navigation.PushAsync(new ProductEditPage(product)))));
  if(rows.Count==0)list.Add(Text("لا توجد منتجات تطابق البحث."));
 }
 protected override async void OnAppearing(){base.OnAppearing();await Run(Reload);}
}
public sealed class ProductEditPage : Screen
{
 public ProductEditPage(Product? product):base(product==null?"منتج جديد":"تعديل المنتج")
 {
  var name=Input("اسم المنتج",product?.Name??"");name.MaxLength=150;
  var barcode=Input("الباركود",product?.Barcode??"");barcode.MaxLength=50;
  var price=Input("سعر البيع",(product?.SalePrice??0).ToString(System.Globalization.CultureInfo.InvariantCulture),true);
  var minimum=Input("حد المخزون",(product?.MinimumStock??0).ToString(System.Globalization.CultureInfo.InvariantCulture),true);
  var active=new Switch{IsToggled=product?.IsActive??true};Body.Add(name);Body.Add(barcode);Body.Add(price);Body.Add(minimum);
  Body.Add(new HorizontalStackLayout{Children={Text("منتج نشط"),active},Spacing=10});Body.Add(Text("تتغير الكمية والتكلفة من خلال الفواتير فقط."));
  Body.Add(Button("حفظ",async()=>{
   if(string.IsNullOrWhiteSpace(name.Text)||string.IsNullOrWhiteSpace(barcode.Text))throw new Exception("أدخل الاسم والباركود.");
   var input=new ProductInput{Name=name.Text.Trim(),Barcode=barcode.Text.Trim(),SalePrice=Number(price),MinimumStock=Number(minimum,3),IsActive=active.IsToggled};
   await Api.Write<Posted>(product==null?"products":$"products/{product.Id}",input,product!=null);await Navigation.PopAsync();
  }));
 }
}
public sealed class PartiesPage : Screen
{
 private readonly VerticalStackLayout list=new(){Spacing=10};
 public PartiesPage():base("العملاء والموردون")
 {
  Body.Add(Button("تحديث",Reload));Body.Add(Button("إضافة عميل أو مورد",()=>Navigation.PushAsync(new PartyEditPage(null))));Body.Add(list);
 }
 private async Task Reload()
 {
  var rows=await Api.Get<List<Party>>("parties");list.Clear();foreach(var row in rows)list.Add(Card(Text(row.Name,20),Text($"{(row.Kind=="Customer"?"عميل":"مورد")} · {row.Phone}"),Text($"المستحق: {Money(row.Balance)}"),Button("تعديل",()=>Navigation.PushAsync(new PartyEditPage(row)))));
 }
 protected override async void OnAppearing(){base.OnAppearing();await Run(Reload);}
}
public sealed class PartyEditPage : Screen
{
 public PartyEditPage(Party? party):base(party==null?"طرف جديد":"تعديل الطرف")
 {
  var name=Input("الاسم",party?.Name??"");name.MaxLength=150;var phone=Input("الهاتف",party?.Phone??"");phone.MaxLength=40;
  var kind=new Picker{Title="نوع الطرف",ItemsSource=new List<string>{"عميل","مورد"},SelectedIndex=party?.Kind=="Supplier"?1:0,IsEnabled=party==null};
  Body.Add(name);Body.Add(kind);Body.Add(phone);Body.Add(Button("حفظ",async()=>{
   if(string.IsNullOrWhiteSpace(name.Text))throw new Exception("أدخل الاسم.");
   await Api.Write<Posted>(party==null?"parties":$"parties/{party.Id}",new PartyInput{Name=name.Text.Trim(),Kind=kind.SelectedIndex==0?"Customer":"Supplier",Phone=phone.Text?.Trim()??""},party!=null);await Navigation.PopAsync();
  }));
 }
}
public sealed class CashPage : Screen
{
 private readonly Picker type=new(){Title="نوع الحركة",ItemsSource=new List<string>{"مصروف","تمويل الصندوق","تحصيل من عميل","سداد لمورد"},SelectedIndex=0};
 private readonly Picker party=new(){Title="الطرف"};private readonly Entry amount,note;
 private readonly string[] kinds={"Expense","Capital","Receipt","Payment"};
 private Guid lastRequest;
 public CashPage():base("الصندوق")
 {
  amount=Input("المبلغ","",true);note=Input("وصف الحركة");note.MaxLength=500;
  Body.Add(type);Body.Add(party);Body.Add(amount);Body.Add(note);Body.Add(Button("تحديث الأطراف",LoadParties));
  type.SelectedIndexChanged+=async(_,_)=>await Run(LoadParties);
  Body.Add(Button("حفظ الحركة",async()=>{
   var value=Number(amount);if(value<=0||string.IsNullOrWhiteSpace(note.Text))throw new Exception("أدخل مبلغاً موجباً ووصف الحركة.");
   if(type.SelectedIndex>=2&&party.SelectedItem is not Party)throw new Exception("اختر الطرف.");
   lastRequest=Guid.NewGuid();var request=new CashRequest{RequestId=lastRequest,Kind=kinds[type.SelectedIndex],Amount=value,Note=note.Text.Trim(),PartyId=type.SelectedIndex>=2?(party.SelectedItem as Party)?.Id:null};
   var result=await Api.PostAccounting("cash",request);amount.Text="";note.Text="";await DisplayAlertAsync("تم الحفظ",$"رقم العملية {result.Id}","حسناً");await LoadParties();
  }));Body.Add(Text("عند انقطاع الاتصال، أعد إرسال الطلب المعلّق من صفحة الملخص."));
 }
 private async Task LoadParties()
 {
  if(lastRequest!=Guid.Empty&&Preferences.Default.Get("last-posted-request","")==lastRequest.ToString()){amount.Text="";note.Text="";lastRequest=Guid.Empty;}
  int index=type.SelectedIndex;party.IsVisible=index>=2;
  if(index<2){party.ItemsSource=null;return;}
  var rows=await Api.Get<List<Party>>($"parties?kind={(index==2?"Customer":"Supplier")}");
  if(type.SelectedIndex==index){party.ItemsSource=rows;party.SelectedIndex=rows.Count>0?0:-1;}
 }
 protected override async void OnAppearing(){base.OnAppearing();await Run(LoadParties);}
}
