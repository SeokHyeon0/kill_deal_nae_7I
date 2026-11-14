using System;using System.Drawing;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;using PubgScore;
class PlacementTests
{
 static int count;static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
 static T Field<T>(object o,string n){return (T)o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 static void Call(object o,string n){o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);}
 [STAThread]static int Main(string[] args){try{
 Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
 var client=new TestGameClient(Path.Combine(args[0],Guid.NewGuid()+".json")){Administrator=true,Mode="match"};
 client.Write("t0.p0.name","가람",0,0).Wait();client.Write("t0.r1.chicken","800",0,0).Wait();
 using(var f=new MainForm(true,client)){
 f.Show();Field<Button[]>(f,"teamTabs")[0].PerformClick();Application.DoEvents();
 var button=Field<Button>(f,"resultButton");var toolbar=button.Parent;
 Check(!toolbar.Controls.Cast<Control>().Any(x=>x.Text=="사용 방법"),"help removed");
 Check(toolbar.Controls.IndexOf(button)>toolbar.Controls.IndexOf(Field<Button>(f,"resetButton")),"result button follows reset in the current toolbar");
 var table=Field<TableLayoutPanel>(f,"placementTable");Check(table.RowCount==13,"match table twelve rank groups");
 Check(table.Controls.Cast<Control>().Any(x=>x.Text=="14~16등"),"last rank visible");
 var grid=Field<DataGridView>(f,"grid");Check(Convert.ToString(grid.Rows[0].Cells[1].Value)=="800","legacy value preserved");
 grid.CurrentCell=grid.Rows[0].Cells[1];grid.BeginEdit(true);Application.DoEvents();
 Check(((ComboBox)grid.EditingControl).Items.Cast<string>().SequenceEqual(PlacementOptions.Values("match")),"match dropdown exact values including clear");grid.CancelEdit();
 using(var image=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(image,new Rectangle(Point.Empty,f.Size));image.Save(Path.Combine(args[0],"경쟁-점수표.png"));}
 int writes=client.Writes;client.Mode="kills";client.Change();Call(f,"RefreshView");Application.DoEvents();
 Check(client.Writes==writes,"switch does not rewrite history");Check(table.Parent.Visible&&table.RowCount==4,"kill-deal table shows three rank groups");Check(Field<TableLayoutPanel>(f,"overview").ColumnStyles[3].Width==158,"selected-mode score table retains its column");
 grid.CurrentCell=grid.Rows[1].Cells[1];grid.BeginEdit(true);Application.DoEvents();
 Check(((ComboBox)grid.EditingControl).Items.Cast<string>().SequenceEqual(new[]{"","800","500","300"}),"kill dropdown exact values including clear");grid.CancelEdit();
 using(var image=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(image,new Rectangle(Point.Empty,f.Size));image.Save(Path.Combine(args[0],"킬딜-점수표.png"));}
 client.Mode="match";client.Change();Call(f,"RefreshView");Check(table.Parent.Visible,"match table restored");f.Close();}
 Console.WriteLine("PASS total "+count);return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
