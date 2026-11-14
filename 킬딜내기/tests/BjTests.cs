using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Linq;using System.Reflection;using System.Threading.Tasks;using System.Windows.Forms;using PubgScore;
internal static class BjTests {
 static int checks;static T Field<T>(object o,string n){return (T)o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
 static object Call(object o,string n,params object[] a){return o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,a);}
 static void Check(bool b,string s){if(!b)throw new Exception(s);checks++;Console.WriteLine("PASS "+s);}
 [STAThread]static void Main(string[] args){
 Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var client=new TestGameClient(Path.Combine(Path.GetTempPath(),"bj-"+Guid.NewGuid().ToString("N")+".json")){Administrator=true};
 client.Write("t0.p0.name","우리",0,0).Wait();client.Write("t0.r50.p0.score","15",0,0).Wait();client.Write("t7.r50.p0.score","25",0,0).Wait();client.Write("t7.p0.name","상대",0,0).Wait();
 var bj=new MainForm(true,client);var viewer=new MainForm(false,client);Exception failure=null;
 bj.Shown+=async delegate {try{
 viewer.Show();Field<Button[]>(viewer,"teamTabs")[0].PerformClick();await Task.Delay(400);
 Check(bj.Text.Contains("BJ")&&!bj.Text.Contains("관리자"),"BJ title replaces admin");
 Check(Field<Button>(bj,"resetButton").Enabled&&Field<Button>(bj,"endButton").Enabled,"always-ready room enables owner reset and end");
 Check(typeof(MainForm).GetField("startButton",BindingFlags.NonPublic|BindingFlags.Instance)==null,"start button removed");
 var rows=Field<Label[,]>(viewer,"rankCells");Check(rows.GetLength(0)==8&&Enumerable.Range(0,8).Select(r=>(int)rows[r,1].Tag).Distinct().Count()==8,"all eight teams appear");
 Check((int)rows[0,1].Tag==7&&(int)rows[1,1].Tag==0&&rows[1,0].Text=="2등","own team included in correct rank");
 Check(rows[1,1].Text=="우리팀"&&rows[1,1].BackColor!=rows[0,1].BackColor,"own team shows tier-one name and remains highlighted");
 client.Write("t0.r50.p0.score","30",1,0).Wait();await Task.Delay(400);
 Check((int)rows[0,1].Tag==0&&rows[0,0].Text=="1등"&&rows[0,2].Text=="30","score update automatically reorders own team without navigation");
 await client.SetRunning(false);await Task.Delay(400);
 Check(Field<Button>(bj,"resetButton").Enabled&&!Field<Button>(bj,"endButton").Enabled,"reset remains available without a start button");
 Check(await (Task<bool>)Call(bj,"ResetInputs",0L),"ended reset succeeds");await Task.Delay(400);
 Check(!client.Running&&client.Generation==1&&client.View().State.Get("t0.p0.name")=="","reset preserves ended state and clears data");
 Check(Enumerable.Range(0,8).All(r=>rows[r,0].Text==""&&rows[r,2].Text=="0"),"all eight ranks and totals refresh after reset");
 Check(Field<Button>(viewer,"resetButton").Parent==null,"viewer has no reset button");
 using(var login=new AdminLoginDialog(client))Check(login.Text=="BJ 로그인","BJ login branding");
 if(args.Length>0){Field<Button[]>(bj,"teamTabs")[0].PerformClick();Application.DoEvents();using(var bmp=new Bitmap(bj.Width,bj.Height)){bj.DrawToBitmap(bmp,new Rectangle(Point.Empty,bj.Size));bmp.Save(args[0]);}}
 Console.WriteLine("PASS: "+checks+" BJ and eight-team UI checks.");
 }catch(Exception ex){failure=ex;Console.Error.WriteLine(ex);}finally{viewer.Dispose();bj.Dispose();Application.ExitThread();}};
 bj.Show();Application.Run();Environment.ExitCode=failure==null?0:1;
 }
}
