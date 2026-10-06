using System.Data;
namespace SupermarketAccounting;

public sealed class InvoiceForm : Form
{
    private readonly string kind;
    private readonly Guid requestId=Guid.NewGuid();
    private readonly DataTable lines=new();
    private readonly DataGridView grid=Ui.Grid();
    private readonly ComboBox product=new() {Width=280,DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Display",ValueMember="Id"};
    private readonly ComboBox party=new() {Width=240,DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Name",ValueMember="Id"};
    private readonly NumericUpDown quantity=Ui.Money(3),price=Ui.Money(),paid=Ui.Money();
    private readonly Label totalLabel=new() {AutoSize=true,Padding=new Padding(8)};
    private readonly TextBox barcode=new() {Width=160,PlaceholderText="الباركود ثم Enter"};
    private readonly TextBox note=new() {Width=260,MaxLength=500};
    private readonly FlowLayoutPanel top=Ui.Flow();
    private readonly CheckBox cash=new() {Text="دفع كامل",Checked=true,AutoSize=true};
    private bool frozen;
    public static DataTable ItemsTable() {
        var table=new DataTable();table.Columns.Add("ProductId",typeof(int));table.Columns.Add("Quantity",typeof(decimal));table.Columns.Add("UnitPrice",typeof(decimal));return table;
    }
    public InvoiceForm(string type)
    {
        kind=type;Text=type=="Sale"?"فاتورة بيع":"فاتورة شراء";Width=1150;Height=700;
        RightToLeft=RightToLeft.Yes;RightToLeftLayout=true;StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
        lines.Columns.Add("الرقم",typeof(int));lines.Columns.Add("المنتج",typeof(string));lines.Columns.Add("الكمية",typeof(decimal));lines.Columns.Add("السعر",typeof(decimal));lines.Columns.Add("الإجمالي",typeof(decimal));grid.DataSource=lines;
        var products=Database.Query("SELECT Id,Barcode,Name,SalePrice,AverageCost,Stock,Name+N' | '+Barcode AS Display FROM dbo.Products WHERE IsActive=1 ORDER BY Name");
        product.DataSource=products;
        product.SelectedIndexChanged+=(_,_)=>SetPrice();SetPrice();quantity.Value=1;
        barcode.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Ui.Guard(()=>{
            var found=products.AsEnumerable().FirstOrDefault(r=>r.Field<string>("Barcode")==barcode.Text.Trim());
            if(found==null)throw new Exception("الباركود غير موجود.");product.SelectedValue=found["Id"];AddLine();barcode.Clear();
        });}};
        var parties=Database.Query("SELECT Id,Name FROM dbo.Parties WHERE Kind=@Kind ORDER BY Name",Database.P("@Kind",kind=="Sale"?"Customer":"Supplier"));
        var empty=parties.NewRow();empty["Id"]=DBNull.Value;empty["Name"]="نقدي بدون طرف";parties.Rows.InsertAt(empty,0);party.DataSource=parties;
        Ui.Field(top,"الطرف",party);Ui.Field(top,"الباركود",barcode);Ui.Field(top,"المنتج",product);Ui.Field(top,"الكمية",quantity);Ui.Field(top,"السعر",price);
        top.Controls.Add(Ui.Button("إضافة صنف",AddLine));
        top.Controls.Add(Ui.Button("حذف السطر",()=>{if(grid.CurrentRow!=null){lines.Rows.RemoveAt(grid.CurrentRow.Index);UpdateTotal();}}));
        var bottom=Ui.Flow();bottom.Dock=DockStyle.Bottom;
        bottom.Controls.Add(totalLabel);bottom.Controls.Add(cash);Ui.Field(bottom,"المدفوع",paid);Ui.Field(bottom,"ملاحظة",note);
        paid.Enabled=false;cash.CheckedChanged+=(_,_)=>{paid.Enabled=!cash.Checked;UpdateTotal();};
        bottom.Controls.Add(Ui.Button("حفظ وترحيل / إعادة المحاولة",Save));
        bottom.Controls.Add(new Label {AutoSize=true,Text="بعد محاولة الترحيل تُقفل البيانات؛ عند انقطاع الاتصال أعد المحاولة لنفس الفاتورة."});
        Controls.Add(grid);Controls.Add(top);Controls.Add(bottom);UpdateTotal();
    }
    private void SetPrice()
    {
        if(product.SelectedItem is DataRowView row)price.Value=Convert.ToDecimal(row[kind=="Sale"?"SalePrice":"AverageCost"]);
    }
    private void AddLine()
    {
        if(product.SelectedItem is not DataRowView selected||quantity.Value<=0)throw new Exception("اختر المنتج وأدخل كمية موجبة.");
        if(lines.AsEnumerable().Any(r=>r.Field<int>(0)==Convert.ToInt32(selected["Id"])))throw new Exception("الصنف مضاف بالفعل. احذف السطر وأعد إضافته بالكمية المطلوبة.");
        var total=decimal.Round(quantity.Value*price.Value,2,MidpointRounding.AwayFromZero);
        lines.Rows.Add(selected["Id"],selected["Name"],quantity.Value,price.Value,total);UpdateTotal();
    }
    private decimal Total()=>lines.AsEnumerable().Sum(row=>row.Field<decimal>(4));
    private void UpdateTotal(){totalLabel.Text=$"الإجمالي: {Total():N2}";if(cash.Checked)paid.Value=Math.Min(paid.Maximum,Total());}
    private void Save()
    {
        if(lines.Rows.Count==0||Total()<=0)throw new Exception("أضف أصنافاً بإجمالي موجب.");
        if(paid.Value>Total())throw new Exception("المدفوع أكبر من إجمالي الفاتورة.");
        int? partyId=party.SelectedValue is int id?id:null;
        if(paid.Value<Total()&&partyId==null)throw new Exception("اختر عميلاً أو مورداً للبيع أو الشراء الآجل.");
        if(!frozen){top.Enabled=false;cash.Enabled=false;paid.Enabled=false;note.Enabled=false;frozen=true;}
        var items=ItemsTable();foreach(DataRow row in lines.Rows)items.Rows.Add(row[0],row[2],row[3]);
        var documentId=Database.Post(kind,partyId,paid.Value,note.Text,0,requestId,items);
        MessageBox.Show($"تم حفظ وترحيل الفاتورة رقم {documentId}","تم الحفظ");DialogResult=DialogResult.OK;Close();
    }
}
