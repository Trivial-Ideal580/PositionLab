using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PositionLab {
partial class Terminal {
    static Label LabelOf(string text,Font font,Color color) {
        return new Label {Text=text,Font=font,ForeColor=color,AutoSize=true,Margin=new Padding(0,4,0,4),UseMnemonic=false};
    }

    static Button ButtonOf(string label,Action action,bool primary=false,int width=105) {
        var button=new Button {Text=label,Width=Math.Max(width,TextRenderer.MeasureText(label,Theme.Small).Width+22),Height=34,FlatStyle=FlatStyle.Flat,BackColor=primary?Theme.Accent:Theme.Surface,
            ForeColor=primary?Theme.Card:Theme.Text,Font=Theme.Small,Margin=new Padding(0,3,8,3),Cursor=Cursors.Hand,UseMnemonic=false};
        button.FlatAppearance.BorderSize=0;
        button.FlatAppearance.MouseOverBackColor=primary?Theme.AccentHover:Theme.Border;
        button.Click+=(s,e)=>action(); return button;
    }

    static FlowLayoutPanel Flow() { return new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty,Padding=Padding.Empty}; }

    static TableLayoutPanel Rows(Control parent,params int[] sizes) {
        var panel=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=sizes.Length,Margin=Padding.Empty,Padding=Padding.Empty};
        foreach(int n in sizes) panel.RowStyles.Add(new RowStyle(n<0?SizeType.Percent:SizeType.Absolute,Math.Abs(n)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); parent.Controls.Add(panel); return panel;
    }

    static Action<int> ViewTabs(Control container,string[] names,params Control[] pages) {
        var holder=new Panel{Dock=DockStyle.Fill,BackColor=Theme.Card,Padding=new Padding(14,8,14,9)};
        container.Controls.Add(holder);
        var r=Rows(holder,44,-100);
        var bar=Flow(); r.Controls.Add(bar,0,0);
        var content=PanelIn(r,1);
        var buttons=new Button[names.Length];
        Action<int> select=index=>{
            for(int j=0;j<pages.Length;j++) {
                pages[j].Visible=j==index;
                TabStyle(buttons[j],j==index);
            }
        };
        for(int i=0;i<pages.Length;i++) {
            int index=i;
            buttons[i]=ButtonOf(names[i],()=>select(index),false,91);
            bar.Controls.Add(buttons[i]); content.Controls.Add(pages[i]);
        }
        select(0);
        return select;
    }

    static void TabStyle(Button button,bool active) {
        button.BackColor=active?Theme.Accent:Theme.Card;
        button.ForeColor=active?Theme.Card:Theme.Muted;
        button.FlatAppearance.MouseOverBackColor=active?Theme.AccentHover:Theme.Surface;
    }

    static Panel PanelOf() { return new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty}; }

    static TableLayoutPanel Card(Control parent,string name) {
        var panel=new Panel {Dock=DockStyle.Fill, BackColor=Theme.Card,Margin=Padding.Empty,Padding=new Padding(14,9,14,9)};
        parent.Controls.Add(panel);
        var r=Rows(panel,36,-100);
        r.Controls.Add(LabelOf(name,Theme.Section,Theme.Text),0,0); return r;
    }

    static TableLayoutPanel CardAt(TableLayoutPanel parent,int row,string name) { var holder=PanelOf(); parent.Controls.Add(holder,0,row); return Card(holder,name); }

    static void Cards(Control parent,params string[] items) {
        var t=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=items.Length/2,RowCount=1,Margin=Padding.Empty};
        parent.Controls.Add(t);
        for(int i=0;i<items.Length;i+=2) {
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,200f/items.Length));
            var p=new Panel{BackColor=Theme.Card,Dock=DockStyle.Fill,Margin=new Padding(0,0,i==items.Length-2?0:10,10)};
            var title=LabelOf(items[i],Theme.Small,Theme.Muted); title.Location=new Point(16,9); p.Controls.Add(title);
            var value=LabelOf(items[i+1],Theme.Metric, i==0?Theme.Accent:Theme.Text); value.AutoSize=false; value.AutoEllipsis=true; value.TextAlign=ContentAlignment.MiddleLeft;
            Action fit=()=>{
                int available=Math.Max(1,p.ClientSize.Width-28);
                value.Font=TextRenderer.MeasureText(value.Text,Theme.Metric).Width<=available?Theme.Metric:Theme.MetricSmall;
                value.SetBounds(14,28,available,47);
            };
            p.Controls.Add(value); p.Resize+=(o,e)=>fit(); fit();
            t.Controls.Add(p,i/2,0);
        }
    }

    static DataGridView Grid(params string[] columns) {
        var g=new DataGridView {Dock=DockStyle.Fill,ReadOnly=true,RowHeadersVisible=false,BackgroundColor=Theme.Card, BorderStyle=BorderStyle.None,
            AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,EnableHeadersVisualStyles=false,ColumnHeadersHeight=37,GridColor=Theme.Grid,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None};
        g.DefaultCellStyle.BackColor=Theme.Card; g.DefaultCellStyle.ForeColor=Theme.Text; g.DefaultCellStyle.SelectionBackColor=Theme.Border;
        g.DefaultCellStyle.SelectionForeColor=Theme.Text; g.DefaultCellStyle.Font=Theme.Small;
        g.AlternatingRowsDefaultCellStyle.BackColor=Theme.Background; g.DefaultCellStyle.Padding=new Padding(7,1,7,1);
        g.ColumnHeadersDefaultCellStyle.BackColor=Theme.Surface; g.ColumnHeadersDefaultCellStyle.ForeColor=Theme.Muted;
        g.ColumnHeadersDefaultCellStyle.Font=Theme.Small; g.ColumnHeadersDefaultCellStyle.Padding=new Padding(7,0,7,0); g.RowTemplate.Height=34;
        foreach(string s in columns) {int i=g.Columns.Add(s,s); g.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable; g.Columns[i].MinimumWidth=TextRenderer.MeasureText(s,Theme.Small).Width+21;}
        return g;
    }

    static void AlignNumbers(DataGridView grid,params int[] indexes) {
        foreach(int i in indexes) {
            grid.Columns[i].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;
            grid.Columns[i].HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleRight;
        }
    }

    static void MinWidths(DataGridView grid,params int[] widths) {
        for(int i=0;i<widths.Length;i++) grid.Columns[i].MinimumWidth=Math.Max(grid.Columns[i].MinimumWidth,widths[i]);
    }

    static TextBox Input(string s,int width=90,bool multiline=false) {
        return new TextBox {Text=s, BackColor=Theme.Surface,ForeColor=Theme.Text,BorderStyle=BorderStyle.FixedSingle,Width=width,
            Multiline=multiline,ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None,Margin=new Padding(0,3,12,3)};
    }

    static Label Note(string text) {
        return new Label {Text=text,Font=Theme.Small,ForeColor=Theme.Muted,Dock=DockStyle.Fill,UseMnemonic=false,TextAlign=ContentAlignment.MiddleLeft};
    }

    static void Field(FlowLayoutPanel f,string label,Control control) {
        var l=LabelOf(label,Theme.Small,Theme.Muted); l.Margin=new Padding(0,9,6,0); f.Controls.Add(l); f.Controls.Add(control);
    }

    static string Num(double v,string pattern="N2") {
        return double.IsNaN(v)||double.IsInfinity(v) ? "—" : v.ToString(pattern,CultureInfo.InvariantCulture);
    }

    static string Pct(double v) {return Num(v,"P1");}

    static string Value(string name,double n) {
        return new[]{"RET1","MOM20","VOL20"}.Contains(name) ? Pct(n) : Num(n,"#,0.######");
    }

    static void ExportGrid(DataGridView grid,string name,Terminal owner) {
        using(var file=new SaveFileDialog { Filter="CSV|*.csv",FileName=name+".csv",RestoreDirectory=true }) {
            if(file.ShowDialog(owner)!=DialogResult.OK) return;
            try {
                using(var writer=new StreamWriter(file.FileName,false,new UTF8Encoding(true))) {
                    writer.WriteLine(string.Join(",",grid.Columns.Cast<DataGridViewColumn>().Select(c=>Data.Escape(c.HeaderText))));
                    foreach(DataGridViewRow row in grid.Rows) writer.WriteLine(string.Join(",",row.Cells.Cast<DataGridViewCell>().Select(c=>Data.Escape(Convert.ToString(c.Value)))));
                }
                owner.SetStatus("已导出 "+Path.GetFileName(file.FileName)+"。");
            } catch(Exception error) { MessageBox.Show(owner,error.Message,"导出失败"); }
        }
    }

    static DateTimePicker DateInput(DateTime date) {
        return new DateTimePicker {Value=date,Width=135,Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd",Margin=new Padding(0,4,6,0)};
    }

    static DateTime PickerDate(DateTime date) {
        return date<DateTimePicker.MinimumDateTime?DateTimePicker.MinimumDateTime:date>DateTimePicker.MaximumDateTime?DateTimePicker.MaximumDateTime:date;
    }

    static Panel PanelIn(TableLayoutPanel p,int row) {var q=PanelOf(); p.Controls.Add(q,0,row); return q;}

}
}
