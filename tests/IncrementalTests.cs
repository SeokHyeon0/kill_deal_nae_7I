using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PubgScore;

class IncrementalTests
{
    static int checks;
    static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static object Field(object o,string n){return o.GetType().GetField(n,flags).GetValue(o);}
    static void Set(object o,string n,object v){o.GetType().GetField(n,flags).SetValue(o,v);}
    static void Pump(int ms){var w=Stopwatch.StartNew();while(w.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(5);}}
    static void Event(FirebaseGameClient c,string kind,string path,object data,int serial=0){typeof(FirebaseGameClient).GetMethod("ApplyEvent",flags).Invoke(c,new object[]{kind,FirebaseHttp.Json().Serialize(new {path=path,data=data}),serial});}
    static object Cell(object value,int version=1,long generation=0){return new {value=value,version=version,generation=generation,session=0,writerUid="test",updatedAt=1};}
    static FirebaseGameClient Client()
    {
        var c=new FirebaseGameClient(new FirebaseConfig {DatabaseUrl="http://127.0.0.1:9000",ApiKey="demo",ProjectId="demo-test",EmulatorMode=true,AuthEmulatorUrl="http://127.0.0.1:9099"});
        Set(c,"roomId",new string('a',32));Set(c,"started",true);Set(c,"lobbyReady",true);
        ((Stopwatch)Field(c,"lobbyProof")).Start();((Stopwatch)Field(c,"proof")).Start();
        Set(Field(c,"auth"),"token","test");Set(Field(c,"auth"),"uid","test");
        ((Dictionary<string,RoomInfo>)Field(c,"rooms")).Add(new string('a',32),new RoomInfo {Id=new string('a',32),Name="Test",OwnerUid="owner",Status="running"});
        return c;
    }
    [STAThread] static int Main()
    {
        try{Application.EnableVisualStyles();
        using(var c=Client()){
            Event(c,"put","/",new {generation=0,roundCount=179,control=new {running=true,session=0},room=new {name="Test",ownerUid="owner"},cells=new Dictionary<string,object>{{"t0_p0_name",Cell("Alpha")},{"t0_r179_p0_score",Cell(3)},{"t1_p0_name",Cell("Bravo")}}});
            var first=c.ViewSince(-1,-1);Check(first.FullState&&first.State.Get("t0.r179.p0.score")=="3","full 179-round snapshot");long rebuilds=c.FullRebuilds;
            Event(c,"put","/cells/t0_r179_p0_score",Cell(9,2));
            var delta=c.ViewSince(first.Revision,first.Navigation);Check(!delta.FullState&&delta.State.Cells.Count==1&&delta.State.Get("t0.r179.p0.score")=="9","single cell emits one detached delta");
            delta.State.Cells["t0.r179.p0.score"].Value="mutated";Check(c.View().State.Get("t0.r179.p0.score")=="9","UI delta cannot mutate receiver cache");
            Event(c,"patch","/cells/t0_r179_p0_score",new {value=12,version=3});Check(c.View().State.Get("t0.r179.p0.score")=="12","nested cell patch");
            Event(c,"patch","/",new Dictionary<string,object>{{"cells/t1_r2_p1_check",Cell(true)},{"cells/t1_r2_p1_score",Cell(20)}});
            Check(c.View().State.Get("t1.r2.p1.check")=="1","multipath patch check true");
            Event(c,"put","/cells/t1_r2_p1_check",Cell(false,2));Check(c.View().State.Get("t1.r2.p1.check")=="0","check false distinct from delete");
            Event(c,"put","/cells/t1_r2_p1_score",Cell("",2));Check(c.View().State.Cells.ContainsKey("t1.r2.p1.score")&&c.View().State.Get("t1.r2.p1.score")=="","empty retained");
            Event(c,"put","/cells/t1_r2_p1_score",Cell(0,3));Check(c.View().State.Get("t1.r2.p1.score")=="0","numeric zero retained");
            Event(c,"put","/cells/t1_r2_p1_score",null);Check(!c.View().State.Cells.ContainsKey("t1.r2.p1.score"),"null removes field");
            Check(c.FullRebuilds==rebuilds,"cell updates never rebuild whole state");
            Event(c,"patch","/cells",new Dictionary<string,object>{{"t2_p0_name",Cell("PatchTeam")},{"t2_r1_p0_score",Cell(-5)}});
            Check(c.FullRebuilds==rebuilds&&c.View().State.Get("t2.p0.name")=="PatchTeam","cells subtree patch stays incremental");
            Event(c,"put","/cells",new Dictionary<string,object>{{"t0_p0_name",Cell("Alpha")},{"t1_p0_name",Cell("Bravo")}});
            Check(c.FullRebuilds==rebuilds+1&&c.View().State.Get("t2.p0.name")=="","cells subtree replacement rebuilds and removes old fields");
            var latest=c.ViewSince(-1,-1);Check(c.ViewSince(latest.Revision,latest.Navigation).State==null,"unchanged view copies no cells");
            using(var form=new MainForm(false,c)){
                form.Show();Pump(300);form.GetType().GetMethod("NavigateToTeam",flags).Invoke(form,new object[]{0});Pump(200);
                var grid=(DataGridView)Field(form,"grid");Check(grid.RowCount==179,"maximum rounds still supported");
                long summaries=form.SummaryCalculations,assignments=form.CellAssignments;Pump(350);
                Check(form.SummaryCalculations==summaries&&form.CellAssignments==assignments,"idle skips calculations and cell assignments");
                Event(c,"put","/cells/t0_r1_p0_score",Cell(10));var updateWait=Stopwatch.StartNew();while(form.SummaryCalculations==summaries&&updateWait.ElapsedMilliseconds<2000)Pump(20);
                Check(form.SummaryCalculations==summaries+1,"only changed team is recalculated");
                grid.CurrentCell=grid.Rows[0].Cells[2];grid.BeginEdit(true);((TextBox)grid.EditingControl).Text="123";((TextBox)grid.EditingControl).Select(1,1);
                Event(c,"put","/cells/t1_r2_p1_score",Cell(25));Pump(250);
                Check(grid.IsCurrentCellInEditMode&&((TextBox)grid.EditingControl).Text=="123"&&((TextBox)grid.EditingControl).SelectionStart==1,"remote change preserves draft and cursor");
                grid.CancelEdit();form.WindowState=FormWindowState.Minimized;Pump(200);summaries=form.SummaryCalculations;
                Event(c,"put","/cells/t0_r1_p0_score",Cell(88,2));Pump(250);
                Check(form.SummaryCalculations==summaries,"minimized state performs no display calculation");
                Event(c,"patch","/",new Dictionary<string,object>{{"generation",1},{"cells",null}});Pump(250);
                Check(!grid.IsCurrentCellInEditMode&&c.View().Generation==1,"minimized reset invalidates old input");
                form.WindowState=FormWindowState.Normal;Pump(300);Check(Convert.ToString(grid.Rows[0].Cells[2].Value)=="","restore reflects current reset state");
                Event(c,"put","/cells/t0_p0_name",Cell("Stale",1,0));Check(c.View().State.Get("t0.p0.name")=="","stale generation cannot restore cleared name");
                Event(c,"put","/cells/t0_p0_name",Cell("New",1,1));var saved=c.LastResult;
                Event(c,"put","/",null);Check(c.LastResult.State.Get("t0.p0.name")=="New","room deletion retains last valid result");
                saved.State.Cells["t0.p0.name"].Value="wrong";Check(c.LastResult.State.Get("t0.p0.name")=="New","retained result is detached");
                long seq=c.Status().Sequence;Event(c,"put","/cells/t0_p0_name",Cell("Late",2,1),99);Check(c.Status().Sequence==seq,"old connection event ignored");
                Set(form,"backend",null);form.Close();
            }
        }
        Console.WriteLine("PASS "+checks+" incremental checks");return 0;
        }catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
