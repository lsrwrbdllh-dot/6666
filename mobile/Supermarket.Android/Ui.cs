using System.Globalization;
using System.Text.Json;
namespace Supermarket.AndroidApp;

public class Screen : ContentPage
{
 protected readonly VerticalStackLayout Body=new(){Spacing=12,Padding=18};
 protected readonly ApiClient Api=ApiClient.Current;
 public Screen(string title)
 {
  Title=title;FlowDirection=FlowDirection.RightToLeft;BackgroundColor=Color.FromArgb("#F3F6F6");
  Content=new ScrollView{Content=Body};
 }
 protected Label Text(string value,int size=16)=>new(){Text=value,FontSize=size,TextColor=Color.FromArgb("#173C40")};
 protected Entry Input(string hint,string value="",bool number=false)=>new(){Placeholder=hint,Text=value,Keyboard=number?Keyboard.Numeric:Keyboard.Default,ClearButtonVisibility=ClearButtonVisibility.WhileEditing};
 protected Button Button(string text,Func<Task> action)
 {
  var button=new Button{Text=text,BackgroundColor=Color.FromArgb("#125C60"),TextColor=Colors.White,CornerRadius=12};
  button.Clicked+=async(_,_)=>{button.IsEnabled=false;try{await Run(action);}finally{button.IsEnabled=true;}};return button;
 }
 protected async Task Run(Func<Task> action)
 {
  try{await action();}
  catch(TaskCanceledException){await DisplayAlertAsync("الاتصال","انتهت مهلة الاتصال. إذا كانت عملية حفظ، افتح الملخص لإعادة إرسال الطلب المعلّق.","حسناً");}
  catch(HttpRequestException){await DisplayAlertAsync("الاتصال","تعذر الوصول إلى الخادم. تحقق من الشبكة والعنوان؛ عمليات الحفظ المعلقة تظهر في الملخص.","حسناً");}
  catch(Exception error){await DisplayAlertAsync("تنبيه",error.Message,"حسناً");}
 }
 protected Border Card(params View[] views)=>new Border{Padding=14,BackgroundColor=Colors.White,Stroke=Color.FromArgb("#DCE7E7"),Content=new VerticalStackLayout{Spacing=6},StrokeThickness=1}.With(views);
 protected static decimal Number(Entry input,int digits=2)
 {
  var text=(input.Text??"").Trim().Replace('٫','.');
  text=new string(text.Select(c=>c is >= '٠' and <= '٩' ? (char)('0'+c-'٠') : c is >= '۰' and <= '۹' ? (char)('0'+c-'۰') : c).ToArray());
  if(!decimal.TryParse(text,NumberStyles.Number,CultureInfo.CurrentCulture,out var value)&&!decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out value))throw new Exception("أدخل رقماً صحيحاً.");
  if(value<0||value>100000000||decimal.Round(value,digits)!=value)throw new Exception($"أدخل مبلغاً موجباً أو صفراً بدقة لا تتجاوز {digits} منازل.");return value;
 }
 protected static string Money(decimal value)=>value.ToString("N2");
 protected async Task ExportRows(List<Dictionary<string,JsonElement>> rows)
 {
  if(rows.Count==0)throw new Exception("لا توجد بيانات للتصدير.");
  var keys=rows[0].Keys.ToList();
  static string Cell(string text){if(text.Length>0&&"=+-@\t\r\n".Contains(text[0]))text="'"+text;return "\""+text.Replace("\"","\"\"")+"\"";}
  var content=string.Join(",",keys.Select(Cell))+"\n"+string.Join("\n",rows.Select(row=>string.Join(",",keys.Select(k=>Cell(row[k].ToString())))));
  var path=Path.Combine(FileSystem.CacheDirectory,$"report-{DateTime.Now:yyyyMMdd-HHmmss}.csv");await File.WriteAllTextAsync(path,content,new System.Text.UTF8Encoding(true));
  await Share.Default.RequestAsync(new ShareFileRequest{Title="تصدير التقرير",File=new ShareFile(path,"text/csv")});
 }
 public static string Kind(string value)=>value switch{"Sale"=>"بيع","Purchase"=>"شراء","Expense"=>"مصروف","Capital"=>"تمويل","Receipt"=>"تحصيل","Payment"=>"سداد",_=>value};
}
internal static class CardExtensions
{
 public static Border With(this Border border,IEnumerable<View> children){var panel=(VerticalStackLayout)border.Content;foreach(var v in children)panel.Children.Add(v);return border;}
}
public sealed class RootPage : Shell
{
 public RootPage()
 {
  FlowDirection=FlowDirection.RightToLeft;FlyoutHeader=new Label{Text="محاسبة السوبر ماركت",Padding=24,FontSize=22,TextColor=Colors.White,BackgroundColor=Color.FromArgb("#125C60")};
  Add("الملخص",()=>new SummaryPage());Add("المنتجات والمخزون",()=>new ProductsPage());Add("العملاء والموردون",()=>new PartiesPage());
  Add("فاتورة بيع",()=>new InvoicePage("Sale"));Add("فاتورة شراء",()=>new InvoicePage("Purchase"));Add("الصندوق",()=>new CashPage());
  Add("سجل العمليات",()=>new DocumentsPage());Add("التقارير",()=>new ReportsPage());Add("إعداد الاتصال",()=>new SettingsPage());
 }
 private void Add(string title,Func<ContentPage> create)
 {
  var item=new FlyoutItem{Title=title};var section=new ShellSection{Title=title};section.Items.Add(new ShellContent{Title=title,ContentTemplate=new DataTemplate(()=>create())});item.Items.Add(section);Items.Add(item);
 }
}
