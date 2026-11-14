using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PubgScore;

class ResourceUiTests
{
    static int checks;
    static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object o,string n){return (T)o.GetType().GetField(n,flags).GetValue(o);}
    static void Set(object o,string n,object value){o.GetType().GetField(n,flags).SetValue(o,value);}
    static void Call(object o,string n,params object[] values){o.GetType().GetMethod(n,flags).Invoke(o,values);}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    static void Pump(int ms){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(2);}}
    static void CheckDisplayedRows(DataGridView grid,string message)
    {
        int count=0;
        for(int r=grid.Rows.GetFirstRow(DataGridViewElementStates.Displayed);r>=0;r=grid.Rows.GetNextRow(r,DataGridViewElementStates.Displayed))
        {
            Check(Convert.ToString(grid.Rows[r].Cells[2].Value)==(r+1).ToString(),message+" round "+(r+1));count++;
        }
        Check(count>0,message+" has visible rows");
    }
    [STAThread] static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            var state=new ScoreState();state.Cells["roundCount"]=new CellValue{Value="179",Version=1};
            state.Cells["t0.p0.name"]=new CellValue{Value="Alpha",Version=1};
            for(int r=1;r<=179;r++)state.Cells["t0.r"+r+".p0.score"]=new CellValue{Value=r.ToString(),Version=1};
            state.Cells["t0.r1.chicken"]=new CellValue{Value="800",Version=1};
            var client=new TestGameClient(Path.Combine(Path.GetTempPath(),"pubg-resource-ui-"+Guid.NewGuid().ToString("N")+".json")){Administrator=true,Mode="match"};
            Set(client,"empty",state);
            using(var form=new MainForm(true,client))
            {
                form.Show();Pump(150);
                var inputs=Field<Dictionary<string,TextBox>>(form,"setupInputs");
                Check(inputs["t0.p0.name"].Text=="Alpha","initial setup values visible");
                Call(form,"NavigateToTeam",0);Pump(100);
                var grid=Field<DataGridView>(form,"grid");
                Check(grid.RowCount==179,"maximum round count retained");
                client.Write("t0.p0.name","Beta",1,0).Wait();Call(form,"RefreshView");
                Check(inputs["t0.p0.name"].Text=="Alpha","hidden setup input receives no remote text assignment");
                Call(form,"ShowSetupPage");Pump(50);
                Check(inputs["t0.p0.name"].Text=="Beta","setup navigation receives latest name");
                Check(Field<Button>(form,"setupTab").BackColor!=Color.White,"selected setup tab remains highlighted");
                Call(form,"NavigateToTeam",0);Pump(50);
                Check(Field<Button>(form,"setupTab").BackColor==Color.White,"score navigation removes setup highlight");
                CheckDisplayedRows(grid,"initial viewport");
                grid.FirstDisplayedScrollingRowIndex=178;Pump(80);
                Check(grid.Rows[178].Displayed&&Convert.ToString(grid.Rows[178].Cells[2].Value)=="179","scroll populates last round");
                grid.FirstDisplayedScrollingRowIndex=0;form.Size=new Size(1100,760);Pump(80);
                int before=grid.DisplayedRowCount(true);form.Size=new Size(1240,1100);Pump(100);
                Check(grid.DisplayedRowCount(true)>before,"resize expands viewport");
                CheckDisplayedRows(grid,"expanded viewport");
                grid.FirstDisplayedScrollingRowIndex=0;Pump(30);
                var chicken=(DataGridViewComboBoxCell)grid.Rows[0].Cells[1];
                Check(Convert.ToString(chicken.Value)=="800"&&chicken.Items.Contains("800"),"legacy chicken score remains visible");
                Call(form,"Render");Check(chicken.Items.Count==PlacementOptions.Values("match").Length+1,"legacy item is not duplicated by renders");
                client.Write("t0.r1.chicken","10",1,0).Wait();Call(form,"RefreshView");
                Check(Convert.ToString(chicken.Value)=="10"&&!chicken.Items.Contains("800"),"obsolete legacy option removed after normal value");
                client.Mode="kills";client.Change();Call(form,"RefreshView");
                Check(Convert.ToString(chicken.Value)=="10"&&chicken.Items.Contains("10"),"mode change preserves stored value");
                grid.CurrentCell=chicken;grid.BeginEdit(true);Pump(30);
                Check(((ComboBox)grid.EditingControl).Items.Cast<string>().SequenceEqual(PlacementOptions.Values("kills")),"editor only offers current mode options");grid.CancelEdit();
                string[] mutable=PlacementOptions.Values("kills");mutable[1]="changed";
                Check(PlacementOptions.Values("kills")[1]=="800","public option arrays cannot corrupt shared options");
                client.Connected=false;client.Change();Call(form,"RefreshView");
                Check(grid.ReadOnly,"disconnect still locks score input");
                Call(form,"ShowSetupPage");Check(inputs["t0.p0.name"].ReadOnly,"setup navigation preserves disconnection lock");
                form.Close();
            }
            Console.WriteLine("PASS "+checks+" resource UI checks");return 0;
        }
        catch(Exception exception){Console.WriteLine(exception);return 1;}
    }
}
