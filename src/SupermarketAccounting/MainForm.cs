using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
namespace SupermarketAccounting;

public sealed class MainForm : Form
{
    private readonly TabControl tabs = new() { Dock=DockStyle.Fill };
    private readonly List<Action> refreshers = new();
    private readonly DateTimePicker from = new() { Value=DateTime.Today.AddDays(-30), Format=DateTimePickerFormat.Short, Width=125 };
    private readonly DateTimePicker to = new() { Value=DateTime.Today, Format=DateTimePickerFormat.Short, Width=125 };
    public MainForm()
    {
        Text="نظام محاسبة السوبر ماركت"; Width=1280; Height=800;
        StartPosition=FormStartPosition.CenterScreen; RightToLeft=RightToLeft.Yes; RightToLeftLayout=true;
        Font=new Font("Segoe UI",10); BackColor=Color.FromArgb(242,245,249);
        var header=Ui.Flow();
        header.Controls.Add(Ui.Button("إعداد الاتصال",Configure));
        header.Controls.Add(Ui.Button("تحديث البيانات",RefreshAll));
        header.Controls.Add(Ui.Button("فاتورة بيع",()=>Invoice("Sale")));
        header.Controls.Add(Ui.Button("فاتورة شراء",()=>Invoice("Purchase")));
        header.Controls.Add(Ui.Button("مصروف",()=>CashEntry(false)));
        header.Controls.Add(Ui.Button("تمويل الصندوق",()=>CashEntry(true)));
        header.Controls.Add(Ui.Button("تحصيل من عميل",()=>Settlement(true)));
        header.Controls.Add(Ui.Button("سداد لمورد",()=>Settlement(false)));
        Controls.Add(tabs); Controls.Add(header);
        AddQueryTab("لوحة المتابعة", """
            SELECT N'رصيد الصندوق' AS [البيان], COALESCE(SUM(Debit-Credit),0) AS [القيمة] FROM dbo.Journal WHERE AccountCode=1100
            UNION ALL SELECT N'ذمم العملاء',COALESCE(SUM(Debit-Credit),0) FROM dbo.Journal WHERE AccountCode=1200
            UNION ALL SELECT N'ذمم الموردين',COALESCE(SUM(Credit-Debit),0) FROM dbo.Journal WHERE AccountCode=2100
            UNION ALL SELECT N'قيمة المخزون الدفترية',COALESCE(SUM(Debit-Credit),0) FROM dbo.Journal WHERE AccountCode=1300
            UNION ALL SELECT N'صافي الربح منذ البداية',COALESCE(SUM(Credit-Debit),0) FROM dbo.Journal WHERE AccountCode IN (4100,5100,5200)
            """);
        Products(); Parties();
        AddQueryTab("سجل العمليات", """
            SELECT d.Id AS [الرقم],d.CreatedAt AS [التاريخ],
            CASE d.Kind WHEN 'Sale' THEN N'بيع' WHEN 'Purchase' THEN N'شراء' WHEN 'Expense' THEN N'مصروف' WHEN 'Receipt' THEN N'تحصيل' WHEN 'Capital' THEN N'تمويل' ELSE N'سداد' END AS [النوع],
            p.Name AS [الطرف],d.Total AS [الإجمالي],d.Paid AS [المدفوع],d.Total-d.Paid AS [الآجل],d.Note AS [ملاحظات]
            FROM dbo.Documents d LEFT JOIN dbo.Parties p ON p.Id=d.PartyId ORDER BY d.Id DESC
            """, grid=>{
                if(grid.CurrentRow==null) return;
                ShowDocument(Convert.ToInt32(grid.CurrentRow.Cells[0].Value));
            });
        AddQueryTab("تنبيهات المخزون", "SELECT Barcode AS [الباركود],Name AS [المنتج],Stock AS [الرصيد],MinimumStock AS [الحد الأدنى] FROM dbo.Products WHERE IsActive=1 AND Stock<=MinimumStock ORDER BY Name");
        AddQueryTab("أرصدة الأطراف", """
            SELECT p.Id AS [الرقم],p.Name AS [الاسم],CASE p.Kind WHEN 'Customer' THEN N'عميل' ELSE N'مورد' END AS [النوع],
            COALESCE(SUM(CASE WHEN p.Kind='Customer' THEN j.Debit-j.Credit ELSE j.Credit-j.Debit END),0) AS [المبلغ المستحق]
            FROM dbo.Parties p LEFT JOIN dbo.Journal j ON j.PartyId=p.Id AND j.AccountCode IN (1200,2100)
            GROUP BY p.Id,p.Name,p.Kind ORDER BY p.Name
            """);
        Reports();
        Shown+=(_,_)=>Ui.Guard(()=>{
            try { RefreshAll(); }
            catch { MessageBox.Show("اضبط الاتصال ثم نفذ ملف database/Setup.sql على SQL Server. لا توجد قاعدة بيانات متصلة حالياً.","بدء التشغيل"); Configure(); }
        });
    }
    private void RefreshAll() { foreach(var refresh in refreshers) refresh(); }
    private TabPage Page(string title) { var page=new TabPage(title); tabs.TabPages.Add(page); return page; }
    private void AddQueryTab(string title,string sql,Action<DataGridView>? open=null)
    {
        var page=Page(title); var grid=Ui.Grid(); var tools=Ui.Flow();
        Action refresh=()=>grid.DataSource=Database.Query(sql);
        tools.Controls.Add(Ui.Button("تحديث",refresh));
        tools.Controls.Add(Ui.Button("تصدير CSV",()=>Export(grid)));
        if(open!=null) tools.Controls.Add(Ui.Button("تفاصيل العملية",()=>open(grid)));
        page.Controls.Add(grid);page.Controls.Add(tools);refreshers.Add(refresh);
    }
    private void Products()
    {
        var page=Page("المنتجات");var grid=Ui.Grid();var tools=Ui.Flow();
        var search=new TextBox { Width=220,PlaceholderText="اسم المنتج أو الباركود" };
        Action refresh=()=>grid.DataSource=Database.Query("""
            SELECT Id AS [الرقم],Barcode AS [الباركود],Name AS [الاسم],SalePrice AS [سعر البيع],AverageCost AS [متوسط التكلفة],Stock AS [المخزون],MinimumStock AS [الحد الأدنى],IsActive AS [نشط]
            FROM dbo.Products WHERE Name LIKE @Search OR Barcode LIKE @Search ORDER BY Name
            """,Database.P("@Search","%"+search.Text.Trim()+"%"));
        tools.Controls.Add(search);tools.Controls.Add(Ui.Button("بحث",refresh));
        tools.Controls.Add(Ui.Button("منتج جديد",()=>{EditProduct(null);refresh();}));
        tools.Controls.Add(Ui.Button("تعديل المنتج",()=>{ if(grid.CurrentRow!=null) { EditProduct(((DataRowView)grid.CurrentRow.DataBoundItem).Row);refresh(); } }));
        tools.Controls.Add(Ui.Button("تصدير CSV",()=>Export(grid)));
        page.Controls.Add(grid);page.Controls.Add(tools);refreshers.Add(refresh);
    }
    private void EditProduct(DataRow? row)
    {
        using var form=Ui.Dialog("بيانات المنتج",620,290);var panel=Ui.Flow();form.Controls.Add(panel);
        var barcode=new TextBox {Width=180,MaxLength=50,Text=row?[1].ToString()??""};
        var name=new TextBox {Width=240,MaxLength=150,Text=row?[2].ToString()??""};
        var price=Ui.Money();price.Value=row==null?0:Convert.ToDecimal(row[3]);
        var minimum=Ui.Money(3);minimum.Value=row==null?0:Convert.ToDecimal(row[6]);
        var active=new CheckBox {Text="منتج نشط",Checked=row==null || Convert.ToBoolean(row[7]),AutoSize=true};
        Ui.Field(panel,"الباركود",barcode);Ui.Field(panel,"الاسم",name);Ui.Field(panel,"سعر البيع",price);Ui.Field(panel,"حد المخزون",minimum);panel.Controls.Add(active);
        panel.Controls.Add(new Label {Text="يتغير رصيد المخزون والتكلفة من خلال فواتير الشراء والبيع فقط.",AutoSize=true,Padding=new Padding(5)});
        panel.Controls.Add(Ui.Button("حفظ",()=>{
            if(string.IsNullOrWhiteSpace(name.Text)||string.IsNullOrWhiteSpace(barcode.Text)) throw new Exception("أدخل الاسم والباركود.");
            var sql=row==null?"INSERT dbo.Products(Barcode,Name,SalePrice,MinimumStock,IsActive) VALUES(@Barcode,@Name,@Price,@Minimum,@Active)":"UPDATE dbo.Products SET Barcode=@Barcode,Name=@Name,SalePrice=@Price,MinimumStock=@Minimum,IsActive=@Active WHERE Id=@Id";
            Database.Execute(sql,Database.P("@Barcode",barcode.Text.Trim()),Database.P("@Name",name.Text.Trim()),Database.P("@Price",price.Value),Database.P("@Minimum",minimum.Value),Database.P("@Active",active.Checked),Database.P("@Id",row?[0]));
            form.Close();
        }));form.ShowDialog(this);
    }
    private void Parties()
    {
        var page=Page("العملاء والموردون");var grid=Ui.Grid();var tools=Ui.Flow();
        Action refresh=()=>grid.DataSource=Database.Query("SELECT Id AS [الرقم],Name AS [الاسم],Kind AS [النوع],Phone AS [الهاتف] FROM dbo.Parties ORDER BY Name");
        tools.Controls.Add(Ui.Button("إضافة",()=>{EditParty(null);refresh();}));
        tools.Controls.Add(Ui.Button("تعديل",()=>{if(grid.CurrentRow!=null){EditParty(((DataRowView)grid.CurrentRow.DataBoundItem).Row);refresh();}}));
        tools.Controls.Add(Ui.Button("تحديث",refresh));page.Controls.Add(grid);page.Controls.Add(tools);refreshers.Add(refresh);
    }
    private void EditParty(DataRow? row)
    {
        using var form=Ui.Dialog("عميل / مورد",560,250);var panel=Ui.Flow();form.Controls.Add(panel);
        var name=new TextBox {Width=220,MaxLength=150,Text=row?[1].ToString()??""};
        var phone=new TextBox {Width=180,MaxLength=40,Text=row?[3].ToString()??""};
        var kind=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=130,Enabled=row==null};kind.Items.AddRange(new object[]{"عميل","مورد"});kind.SelectedIndex=row==null || row[2].ToString()=="Customer"?0:1;
        Ui.Field(panel,"الاسم",name);Ui.Field(panel,"النوع",kind);Ui.Field(panel,"الهاتف",phone);
        panel.Controls.Add(Ui.Button("حفظ",()=>{
            if(string.IsNullOrWhiteSpace(name.Text)) throw new Exception("أدخل الاسم.");
            Database.Execute(row==null?"INSERT dbo.Parties(Name,Kind,Phone) VALUES(@Name,@Kind,@Phone)":"UPDATE dbo.Parties SET Name=@Name,Phone=@Phone WHERE Id=@Id",
                Database.P("@Name",name.Text.Trim()),Database.P("@Kind",kind.SelectedIndex==0?"Customer":"Supplier"),Database.P("@Phone",phone.Text.Trim()),Database.P("@Id",row?[0]));
            form.Close();
        }));form.ShowDialog(this);
    }
    private void Invoice(string kind)
    {
        using var form=new InvoiceForm(kind);form.ShowDialog(this);RefreshAll();
    }
    private void CashEntry(bool capital)
    {
        using var form=Ui.Dialog(capital?"تمويل الصندوق / رأس المال":"تسجيل مصروف نقدي",550,230);var panel=Ui.Flow();form.Controls.Add(panel);
        var amount=Ui.Money();var note=new TextBox {Width=330,MaxLength=500};
        Ui.Field(panel,"المبلغ",amount);Ui.Field(panel,"الوصف",note);
        var request=Guid.NewGuid();
        panel.Controls.Add(Ui.Button("تسجيل / إعادة المحاولة",()=>{
            if(amount.Value<=0||string.IsNullOrWhiteSpace(note.Text)) throw new Exception("أدخل مبلغاً موجباً ووصف المصروف.");
            amount.Enabled=false;note.Enabled=false;
            var id=Database.Post(capital?"Capital":"Expense",null,amount.Value,note.Text,amount.Value,request,InvoiceForm.ItemsTable());
            MessageBox.Show($"تم تسجيل العملية رقم {id}");form.Close();
        }));form.ShowDialog(this);RefreshAll();
    }
    private void Settlement(bool receipt)
    {
        using var form=Ui.Dialog(receipt?"تحصيل من عميل":"سداد لمورد",570,260);var panel=Ui.Flow();form.Controls.Add(panel);
        var party=new ComboBox {Width=260,DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Name",ValueMember="Id",DataSource=Database.Query("SELECT Id,Name FROM dbo.Parties WHERE Kind=@Kind ORDER BY Name",Database.P("@Kind",receipt?"Customer":"Supplier"))};
        var amount=Ui.Money();var note=new TextBox {Width=300,MaxLength=500};var request=Guid.NewGuid();
        Ui.Field(panel,"الطرف",party);Ui.Field(panel,"المبلغ",amount);Ui.Field(panel,"الوصف",note);
        panel.Controls.Add(Ui.Button("تسجيل / إعادة المحاولة",()=>{
            if(party.SelectedValue==null||amount.Value<=0) throw new Exception("اختر الطرف وأدخل المبلغ.");
            party.Enabled=false;amount.Enabled=false;note.Enabled=false;
            using var connection=new SqlConnection(Database.ConnectionString);connection.Open();
            using var command=new SqlCommand("dbo.PostSettlement",connection){CommandType=CommandType.StoredProcedure};
            command.Parameters.AddRange(new[]{Database.P("@Kind",receipt?"Receipt":"Payment"),Database.P("@PartyId",party.SelectedValue),Database.P("@Amount",amount.Value),Database.P("@Note",note.Text),Database.P("@RequestId",request)});
            var id=Convert.ToInt32(command.ExecuteScalar());MessageBox.Show($"تم تسجيل العملية رقم {id}");form.Close();
        }));form.ShowDialog(this);RefreshAll();
    }
    private void Reports()
    {
        var page=Page("التقارير المحاسبية");var panel=Ui.Flow();var grid=Ui.Grid();
        var report=new ComboBox {Width=180,DropDownStyle=ComboBoxStyle.DropDownList};report.Items.AddRange(new object[]{"ميزان المراجعة","قائمة الدخل","دفتر اليومية"});report.SelectedIndex=0;
        Ui.Field(panel,"من",from);Ui.Field(panel,"إلى",to);panel.Controls.Add(report);
        Action refresh=()=>{
            if(from.Value.Date>to.Value.Date) throw new Exception("تاريخ البداية يجب أن يسبق النهاية.");
            string sql=report.SelectedIndex switch {
                0=>"""
                SELECT a.Code AS [الحساب],a.Name AS [الاسم],COALESCE(SUM(j.Debit),0) AS [مدين],COALESCE(SUM(j.Credit),0) AS [دائن],COALESCE(SUM(j.Debit-j.Credit),0) AS [الرصيد]
                FROM dbo.Accounts a LEFT JOIN (SELECT j.* FROM dbo.Journal j JOIN dbo.Documents d ON d.Id=j.DocumentId WHERE d.CreatedAt<@To) j ON j.AccountCode=a.Code
                GROUP BY a.Code,a.Name ORDER BY a.Code
                """,
                1=>"""
                SELECT a.Name AS [الحساب],COALESCE(SUM(CASE WHEN a.Code=4100 THEN j.Credit-j.Debit ELSE j.Debit-j.Credit END),0) AS [المبلغ]
                FROM dbo.Accounts a LEFT JOIN (SELECT j.* FROM dbo.Journal j JOIN dbo.Documents d ON d.Id=j.DocumentId WHERE d.CreatedAt>=@From AND d.CreatedAt<@To) j ON j.AccountCode=a.Code
                WHERE a.Code IN (4100,5100,5200) GROUP BY a.Code,a.Name
                UNION ALL SELECT N'صافي الربح',COALESCE(SUM(j.Credit-j.Debit),0) FROM dbo.Journal j JOIN dbo.Documents d ON d.Id=j.DocumentId WHERE j.AccountCode IN (4100,5100,5200) AND d.CreatedAt>=@From AND d.CreatedAt<@To
                """,
                _=>"""
                SELECT d.Id AS [العملية],d.CreatedAt AS [التاريخ],a.Name AS [الحساب],p.Name AS [الطرف],j.Debit AS [مدين],j.Credit AS [دائن],d.Note AS [الوصف]
                FROM dbo.Journal j JOIN dbo.Documents d ON d.Id=j.DocumentId JOIN dbo.Accounts a ON a.Code=j.AccountCode LEFT JOIN dbo.Parties p ON p.Id=j.PartyId
                WHERE d.CreatedAt>=@From AND d.CreatedAt<@To ORDER BY d.Id,j.Id
                """
            };
            grid.DataSource=Database.Query(sql,Database.P("@From",from.Value.Date),Database.P("@To",to.Value.Date.AddDays(1)));
        };
        panel.Controls.Add(Ui.Button("عرض",refresh));panel.Controls.Add(Ui.Button("تصدير CSV",()=>Export(grid)));
        panel.Controls.Add(new Label {Text="ميزان المراجعة تراكمي حتى تاريخ النهاية؛ التقارير الأخرى للفترة المحددة.",AutoSize=true});
        page.Controls.Add(grid);page.Controls.Add(panel);refreshers.Add(refresh);
    }
    private void ShowDocument(int id)
    {
        using var form=Ui.Dialog($"تفاصيل العملية {id}",1000,620);var grid=Ui.Grid();var header=Ui.Flow();
        var doc=Database.Query("SELECT d.*,p.Name AS PartyName FROM dbo.Documents d LEFT JOIN dbo.Parties p ON p.Id=d.PartyId WHERE d.Id=@Id",Database.P("@Id",id)).Rows[0];
        header.Controls.Add(new Label {AutoSize=true,Text=$"العملية {id} | {doc["CreatedAt"]} | {doc["PartyName"]} | الإجمالي {doc["Total"]} | المدفوع {doc["Paid"]} | {doc["Note"]}"});
        var lines=Database.Query("SELECT p.Barcode AS [الباركود],p.Name AS [المنتج],l.Quantity AS [الكمية],l.UnitPrice AS [السعر],l.LineTotal AS [الإجمالي] FROM dbo.DocumentLines l JOIN dbo.Products p ON p.Id=l.ProductId WHERE l.DocumentId=@Id",Database.P("@Id",id));
        grid.DataSource=lines.Rows.Count>0?lines:Database.Query("SELECT a.Name AS [الحساب],j.Debit AS [مدين],j.Credit AS [دائن] FROM dbo.Journal j JOIN dbo.Accounts a ON a.Code=j.AccountCode WHERE j.DocumentId=@Id",Database.P("@Id",id));
        header.Controls.Add(Ui.Button("تصدير التفاصيل",()=>Export(grid)));
        header.Controls.Add(Ui.Button("معاينة وطباعة",()=>PrintDocument(id,doc,(DataTable)grid.DataSource!)));
        form.Controls.Add(grid);form.Controls.Add(header);form.ShowDialog(this);
    }
    private void PrintDocument(int id,DataRow document,DataTable detail)
    {
        using var print=new System.Drawing.Printing.PrintDocument();
        using var font=new Font("Segoe UI",11);
        using var format=new StringFormat {Alignment=StringAlignment.Far,FormatFlags=StringFormatFlags.DirectionRightToLeft};
        var rows=new List<string> {
            "نظام محاسبة السوبر ماركت",
            $"رقم العملية: {id}     التاريخ: {document["CreatedAt"]}",
            $"النوع: {document["Kind"]}     الطرف: {document["PartyName"]}",
            $"الإجمالي: {document["Total"]}     المدفوع: {document["Paid"]}",
            $"ملاحظات: {document["Note"]}",
            string.Join(" | ",detail.Columns.Cast<DataColumn>().Select(c=>c.ColumnName))
        };
        rows.AddRange(detail.Rows.Cast<DataRow>().Select(r=>string.Join(" | ",r.ItemArray)));
        int index=0;
        print.BeginPrint+=(_,_)=>index=0;
        print.PrintPage+=(_,e)=>{
            if(e.Graphics==null)return;
            float y=e.MarginBounds.Top;
            while(index<rows.Count) {
                var size=e.Graphics.MeasureString(rows[index],font,e.MarginBounds.Width,format);
                float height=Math.Min(size.Height+10,e.MarginBounds.Height);
                if(y+height>e.MarginBounds.Bottom && y>e.MarginBounds.Top)break;
                e.Graphics.DrawString(rows[index],font,Brushes.Black,new RectangleF(e.MarginBounds.Left,y,e.MarginBounds.Width,height),format);
                y+=height;index++;
            }
            e.HasMorePages=index<rows.Count;
        };
        using var preview=new PrintPreviewDialog {Document=print,Width=1000,Height=750};
        preview.ShowDialog(this);
    }
    private void Configure()
    {
        using var form=Ui.Dialog("اتصال SQL Server",710,310);var panel=Ui.Flow();form.Controls.Add(panel);
        var builder=new SqlConnectionStringBuilder(Database.ConnectionString);
        var server=new TextBox {Width=230,Text=builder.DataSource};var database=new TextBox {Width=230,Text=builder.InitialCatalog};
        var trust=new CheckBox {Text="الثقة بشهادة الخادم المحلية (للتطوير فقط)",AutoSize=true,Checked=builder.TrustServerCertificate};
        Ui.Field(panel,"الخادم",server);Ui.Field(panel,"قاعدة البيانات",database);panel.Controls.Add(trust);
        panel.Controls.Add(new Label {AutoSize=true,Text="يستخدم البرنامج مصادقة Windows. امنح حسابك صلاحيات القاعدة فقط."});
        panel.Controls.Add(Ui.Button("اختبار وحفظ",()=>{
            var value=new SqlConnectionStringBuilder {DataSource=server.Text.Trim(),InitialCatalog=database.Text.Trim(),IntegratedSecurity=true,Encrypt=SqlConnectionEncryptOption.Mandatory,TrustServerCertificate=trust.Checked,ConnectTimeout=5}.ConnectionString;
            using var connection=new SqlConnection(value);connection.Open();
            using var command=new SqlCommand("SELECT OBJECT_ID('dbo.PostDocument'),OBJECT_ID('dbo.PostSettlement')",connection);
            using var reader=command.ExecuteReader();reader.Read();
            if(reader.IsDBNull(0)||reader.IsDBNull(1)) throw new Exception("نفذ ملف Setup.sql أولاً على القاعدة المحددة.");
            Database.Save(value);form.Close();
        }));form.ShowDialog(this);
    }
    private static void Export(DataGridView grid)
    {
        if(grid.DataSource is not DataTable table) return;
        using var dialog=new SaveFileDialog {Filter="CSV (*.csv)|*.csv",FileName="report.csv"};if(dialog.ShowDialog()!=DialogResult.OK)return;
        // Prevent spreadsheet formula execution from product/customer input.
        static string Cell(object? value) {
            string text=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??"";
            if(text.Length>0 && "=+-@\t\r\n".Contains(text[0])) text="'"+text;
            return "\""+text.Replace("\"","\"\"")+"\"";
        }
        using var writer=new StreamWriter(dialog.FileName,false,new UTF8Encoding(true));
        writer.WriteLine(string.Join(",",table.Columns.Cast<DataColumn>().Select(c=>Cell(c.ColumnName))));
        foreach(DataRow row in table.Rows) writer.WriteLine(string.Join(",",row.ItemArray.Select(Cell)));
    }
}
