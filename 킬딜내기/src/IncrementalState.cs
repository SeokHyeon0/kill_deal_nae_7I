using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace PubgScore
{
    public sealed partial class FirebaseGameClient
    {
        public event Action Changed;
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        readonly System.Threading.Timer healthTimer;
        readonly Dictionary<string,long> changedCells=new Dictionary<string,long>(StringComparer.Ordinal);
        long stateRevision,fullRevision;
        int notificationQueued;
        string previousHealth="";
        internal long FullRebuilds,IncrementalCells,SnapshotCells;

        void QueueChanged()
        {
            if(stopped||Interlocked.Exchange(ref notificationQueued,1)!=0)return;
            ThreadPool.QueueUserWorkItem(delegate {
                Interlocked.Exchange(ref notificationQueued,0);
                var callback=Changed;if(!stopped&&callback!=null)callback();
            });
        }
        void CheckHealth(object unused)
        {
            if(stopped)return;
            if(RefreshRoomActivity())QueueChanged();
            var status=Status();string current=status.Connected+"|"+status.Authenticated+"|"+status.CanEdit+"|"+status.Info;
            if(current!=previousHealth){previousHealth=current;QueueChanged();}
        }
        void FullStateChanged(){fullRevision=++stateRevision;changedCells.Clear();FullRebuilds++;}
        void CellChanged(string key){changedCells[key]=++stateRevision;IncrementalCells++;}
        public GameView ViewSince(long revision,int navigation)
        {
            lock(gate){var result=Status();result.FullState=navigation!=roomNavigation||revision<fullRevision||revision<0;
                if(result.FullState){result.State=state.Copy();SnapshotCells+=state.Cells.Count;}
                else if(revision!=stateRevision){result.State=new ScoreState();foreach(var pair in changedCells){
                    if(pair.Value<=revision)continue;CellValue value;result.State.Cells[pair.Key]=state.Cells.TryGetValue(pair.Key,out value)?new CellValue {Value=value.Value,Version=value.Version}:null;SnapshotCells++;
                }}return result;}
        }
        GameView RosterView()
        {
            lock(gate){var result=Status();result.State=new ScoreState();
                for(int team=0;team<ScoreRules.TeamCount;team++)for(int player=0;player<4;player++)foreach(string suffix in new[]{"name","penalty"}){
                    string key="t"+team+".p"+player+"."+suffix;CellValue value;
                    if(state.Cells.TryGetValue(key,out value))result.State.Cells[key]=new CellValue {Value=value.Value,Version=value.Version};
                }return result;}
        }
        ResultSnapshot CaptureResult()
        {return roomId.Length==0||generation<0?null:new ResultSnapshot {State=state.Copy(),RoomName=roomName,Mode=roomMode,CapturedAt=DateTime.Now};}
        void FreezeResult(){var snapshot=CaptureResult();if(snapshot!=null)lastResult=snapshot;}
        static void CollectChanged(string path,string kind,object value,HashSet<string> changes,ref bool full)
        {
            string where=path.Trim('/');
            if(kind=="patch"){
                foreach(var pair in FirebaseHttp.Object(value))AddChanged((where.Length>0?where+"/":"")+pair.Key,changes,ref full);
            }else AddChanged(where,changes,ref full);
        }
        static void AddChanged(string path,HashSet<string> changes,ref bool full)
        {
            string[] parts=path.Trim('/').Split('/');
            if(parts[0].Length==0||path=="cells"){full=true;return;}
            changes.Add(parts[0]=="cells"&&parts.Length>1?"cells/"+parts[1]:parts[0]);
        }
        void ApplyIncremental(HashSet<string> changes)
        {
            if(changes.Contains("control")){var control=FirebaseHttp.Object(tree.ContainsKey("control")?tree["control"]:null);
                long next=Number(control,"session",-1);object flag;
                if(next<session)return;
                if(next<0||!control.TryGetValue("running",out flag)||!(flag is bool)){streamReady=false;Lost("Firebase control 설정을 확인하세요.");return;}
                session=next;running=(bool)flag;roomStatus=running?"running":"waiting";
            }
            if(changes.Contains("mode"))ReadRoomMode(generation);
            if(changes.Contains("room")){var room=FirebaseHttp.Object(tree.ContainsKey("room")?tree["room"]:null);roomName=FirebaseHttp.Text(room,"name");roomOwner=FirebaseHttp.Text(room,"ownerUid");}
            var cells=FirebaseHttp.Object(tree.ContainsKey("cells")?tree["cells"]:null);
            foreach(string path in changes){if(!path.StartsWith("cells/",StringComparison.Ordinal))continue;
                string wire=path.Substring(6),key=wire.Replace('_','.');object raw;
                if(!cells.TryGetValue(wire,out raw)||raw==null){if(state.Cells.Remove(key))CellChanged(key);continue;}
                var item=FirebaseHttp.Object(raw);if(Number(item,"generation",generation==0?0:-1)!=generation)continue;
                int version=(int)Number(item,"version",0);if(state.Version(key)>version)continue;
                try{ScoreRules.ValidateRound(key,state.RoundCount);object value;item.TryGetValue("value",out value);string normalized=ScoreRules.NormalizeStored(key,value);
                    CellValue old;if(state.Cells.TryGetValue(key,out old)&&old.Version==version&&old.Value==normalized)continue;
                    state.Cells[key]=new CellValue {Value=normalized,Version=version};CellChanged(key);
                }catch(ArgumentException){streamReady=false;Lost("저장 데이터 오류 ("+key+"): 숫자 또는 판수 범위를 확인하세요. 데이터는 삭제하지 않았습니다.");return;}
            }
            healthy=true;sequence++;QueueChanged();
        }
    }

    public sealed partial class MainForm
    {
        FirebaseGameClient subscribedClient;
        int updateQueued;
        bool visualPending;
        long displayedRevision=-1;int displayedNavigation=-1;
        readonly TeamSummary[] summaries=new TeamSummary[ScoreRules.TeamCount];
        internal long SummaryCalculations,UiPasses,CellAssignments;
        GameView CurrentStatus(){var firebase=backend as FirebaseGameClient;return firebase!=null?firebase.Status():backend==null?new GameView():backend.View();}
        GameView NextView(){var firebase=backend as FirebaseGameClient;if(firebase==null)return backend.View();var next=firebase.ViewSince(displayedRevision,displayedNavigation);displayedRevision=next.Revision;displayedNavigation=next.Navigation;return next;}
        void UnbindUpdates(){if(subscribedClient!=null){subscribedClient.Changed-=ScheduleUpdate;var saved=subscribedClient.LastResult;if(saved!=null)lastExportResult=saved;}subscribedClient=null;}
        void BindUpdates()
        {
            var client=backend as FirebaseGameClient;if(client==subscribedClient&&client!=null)return;
            UnbindUpdates();lastSequence=-1;displayedRevision=-1;displayedNavigation=-1;subscribedClient=client;
            if(client!=null){timer.Stop();client.Changed+=ScheduleUpdate;}else timer.Start();
            ScheduleUpdate();
        }
        void ScheduleUpdate()
        {
            if(closing||!IsHandleCreated||Interlocked.Exchange(ref updateQueued,1)!=0)return;
            try{BeginInvoke((MethodInvoker)delegate {Interlocked.Exchange(ref updateQueued,0);if(!closing&&!timer.Enabled)timer.Start();});}
            catch(InvalidOperationException){Interlocked.Exchange(ref updateQueued,0);}
        }
        protected override void OnShown(EventArgs e){base.OnShown(e);ScheduleUpdate();}
        protected override void OnResize(EventArgs e){base.OnResize(e);if(WindowState!=FormWindowState.Minimized&&visualPending){visualPending=false;lastSequence=-1;ScheduleUpdate();}}
        void InvalidateSummaries(){Array.Clear(summaries,0,summaries.Length);}
        void InvalidateSummary(string key){if(key.Length>2&&key[0]=='t'&&key[1]>='0'&&key[1]<='9')summaries[key[1]-'0']=null;else InvalidateSummaries();}
        TeamSummary Summary(int index){if(summaries[index]==null){SummaryCalculations++;summaries[index]=ScoreMath.Calculate(state,index,view.Mode);}return summaries[index];}
        void SetCell(DataGridViewCell cell,object value){if(Object.Equals(cell.Value,value))return;cell.Value=value;CellAssignments++;}
        static void SetText(Control control,string value){if(control.Text!=value)control.Text=value;}
    }
}
