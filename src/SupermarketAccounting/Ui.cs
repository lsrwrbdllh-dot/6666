namespace SupermarketAccounting;
internal static class Ui
{
    public static Button Button(string text, Action action)
    {
        var button = new Button { Text=text, AutoSize=true, Height=36, Padding=new Padding(10,3,10,3), Margin=new Padding(5) };
        button.Click += (_,_) => Guard(action);
        return button;
    }
    public static void Guard(Action action)
    {
        try { action(); }
        catch(Exception error) { MessageBox.Show(error.Message,"تعذر تنفيذ العملية",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }
    public static DataGridView Grid() => new() { Dock=DockStyle.Fill, ReadOnly=true, AllowUserToAddRows=false, AllowUserToDeleteRows=false, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, SelectionMode=DataGridViewSelectionMode.FullRowSelect, MultiSelect=false, RowHeadersVisible=false, BackgroundColor=Color.White };
    public static NumericUpDown Money(int decimals=2) => new() { DecimalPlaces=decimals, Maximum=100000000, Minimum=0, Width=160, ThousandsSeparator=true };
    public static Form Dialog(string title, int width=520, int height=430) => new() { Text=title,Width=width,Height=height,StartPosition=FormStartPosition.CenterParent,RightToLeft=RightToLeft.Yes,RightToLeftLayout=true,Font=new Font("Segoe UI",10),MinimizeBox=false,MaximizeBox=false };
    public static FlowLayoutPanel Flow() => new() { Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new Padding(8) };
    public static void Field(FlowLayoutPanel panel, string title, Control input)
    {
        panel.Controls.Add(new Label {Text=title, AutoSize=true,Margin=new Padding(8,10,3,3)});
        panel.Controls.Add(input);
    }
}
