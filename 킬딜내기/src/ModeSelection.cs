using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace PubgScore
{
    public sealed partial class FirebaseGameClient
    {
        // The stored mode survives a reset for compatibility. Its generation is
        // used to distinguish the current round set; version prevents a stale
        // confirmation or concurrent selection from overwriting a newer mode.
        void ReadRoomMode(long currentGeneration)
        {
            var raw=FirebaseHttp.Object(tree.ContainsKey("mode")?tree["mode"]:null);
            string value=FirebaseHttp.Text(raw,"value");
            roomMode=GameModes.IsValid(value)&&Number(raw,"generation",-1)==currentGeneration?value:"";
            long version=Number(raw,"version",0);
            modeVersion=version>=0&&version<=Int32.MaxValue?(int)version:0;
        }

        bool SameModeContext(GameView captured,int navigation,int serial)
        {
            lock(gate)
            {
                var current=Status();
                return !stopped&&roomNavigation==navigation&&streamSerial==serial&&roomId==captured.RoomId&&
                    generation==captured.Generation&&session==captured.Session&&minimumGeneration<=captured.Generation&&
                    current.Connected&&current.IsAdmin&&current.Uid==captured.Uid&&
                    current.ModeVersion==captured.ModeVersion&&current.Mode==captured.Mode;
            }
        }

        public async Task SelectMode(string selected,GameView expected=null)
        {
            if(!GameModes.IsValid(selected))throw new ArgumentException("경딜, 딜킬, 킬딜, 킬내기 또는 딜내기를 선택하세요.");
            GameView requested;int requestedNavigation,requestedStream;
            lock(gate){requested=Status();requestedNavigation=roomNavigation;requestedStream=streamSerial;}
            if(!requested.IsAdmin||!requested.Connected||requested.RoomId.Length==0)throw new UnauthorizedAccessException("연결된 방에서 인증된 BJ만 종목을 선택할 수 있습니다.");
            if(expected!=null&&(requested.RoomId!=expected.RoomId||requested.Navigation!=expected.Navigation||
                requested.Generation!=expected.Generation||requested.Session!=expected.Session||requested.Uid!=expected.Uid||
                requested.ModeVersion!=expected.ModeVersion||requested.Mode!=expected.Mode))
                throw new InvalidOperationException("확인하는 동안 방 또는 종목이 변경되었습니다. 현재 화면에서 다시 선택하세요.");
            if(!resetOperation.Wait(0))throw new InvalidOperationException("초기화, 팀 저장 또는 종목 선택이 진행 중입니다.");
            bool cellLock=false,requestSent=false;string capturedRoom=null;
            try
            {
                await cellWriteOperation.WaitAsync();cellLock=true;
                GameView captured;int navigation,serial;
                lock(gate)
                {
                    if(!SameModeContext(requested,requestedNavigation,requestedStream))throw new InvalidOperationException("종목 선택을 기다리는 동안 방 또는 연결 상태가 변경되었습니다.");
                    captured=RosterView();navigation=roomNavigation;serial=streamSerial;capturedRoom=captured.RoomId;
                    if(!captured.IsAdmin||!captured.Connected||captured.RoomId.Length==0)throw new UnauthorizedAccessException("연결된 방에서 인증된 BJ만 종목을 선택할 수 있습니다.");
                    if(!GameModes.HasPlayers(captured.State))throw new InvalidOperationException("팀 짜기에서 선수를 먼저 저장한 뒤 종목을 선택하세요.");
                    if(captured.ModeVersion==Int32.MaxValue)throw new InvalidOperationException("종목 선택 횟수 한도에 도달했습니다.");
                }
                var values=new Dictionary<string,string>();int[] deductions=GameModes.Deductions(selected);
                bool useDefaultDeductions=selected!="match";
                for(int team=0;team<ScoreRules.TeamCount;team++)
                {
                    bool active=false;for(int player=0;player<4;player++)if(!String.IsNullOrWhiteSpace(captured.State.Get("t"+team+".p"+player+".name","")))active=true;
                    for(int player=0;player<4;player++)values["t"+team+".p"+player+".penalty"]=active&&useDefaultDeductions?deductions[player].ToString(CultureInfo.InvariantCulture):"";
                }
                await Task.Run(delegate
                {
                    var patch=new Dictionary<string,object>();string activityKey=null;int activityVersion=0,delta=0;
                    lock(gate)
                    {
                        if(!SameModeContext(captured,navigation,serial))throw new InvalidOperationException("종목 선택 전에 방 또는 연결 상태가 변경되었습니다.");
                    }
                    foreach(var entry in values)
                    {
                        if(captured.State.Get(entry.Key,"")==entry.Value)continue;
                        int oldVersion=captured.State.Version(entry.Key);
                        if(oldVersion==Int32.MaxValue)throw new InvalidOperationException("차감 점수 수정 횟수 한도에 도달했습니다.");
                        if(activityKey==null){activityKey=entry.Key;activityVersion=oldVersion+1;}
                        delta+=RoomActivity.ContentValue(entry.Key,entry.Value)-RoomActivity.ContentValue(entry.Key,captured.State.Get(entry.Key,""));
                        object value=entry.Value;decimal number;
                        if(entry.Value.Length>0&&ScoreRules.TryDecimal(entry.Value,out number))value=number;
                        patch["rooms/"+captured.RoomId+"/game/cells/"+entry.Key.Replace('.','_')]=new Dictionary<string,object>{
                            {"value",value},{"version",oldVersion+1},{"generation",captured.Generation},
                            {"session",captured.Session},{"writerUid",captured.Uid},{"updatedAt",Timestamp()}
                        };
                    }
                    int nextModeVersion=captured.ModeVersion+1;
                    string modePath="rooms/"+captured.RoomId+"/game/mode";
                    patch[modePath]=new Dictionary<string,object>{
                        {"value",selected},{"version",nextModeVersion},{"generation",captured.Generation},
                        {"session",captured.Session},{"writerUid",captured.Uid},{"updatedAt",Timestamp()}
                    };
                    if(activityKey!=null)
                    {
                        RefreshMissingActivityCounter(captured.RoomId);
                        lock(gate)
                        {
                            if(!SameModeContext(captured,navigation,serial))throw new InvalidOperationException("종목 선택 전에 방 또는 연결 상태가 변경되었습니다.");
                            int initialCount=ActivityCounter(captured.RoomId).HasValue?0:RoomActivity.Count(state);
                            patch["lobby/"+captured.RoomId+"/activity"]=ActivityPayload(captured.RoomId,activityKey,activityVersion,delta,initialCount);
                        }
                    }
                    requestSent=true;
                    var reply=Database("spark","PATCH",patch,12000,()=>SameModeContext(captured,navigation,serial));
                    if(reply.Status!=200)throw new IOException("서버가 종목과 차감 점수를 저장하지 않았습니다.");
                    var response=FirebaseHttp.Object(FirebaseHttp.Json().DeserializeObject(reply.Body));
                    var cells=ValidateConfirmedValues(captured.RoomId,captured.Generation,captured.Session,captured.Uid,values,captured.State,response);
                    object rawMode;
                    if(!FirebaseHttp.TryResponseObject(response,modePath,out rawMode))throw new IOException("종목 저장 응답을 확인하지 못했습니다.");
                    var savedMode=FirebaseHttp.Object(rawMode);
                    if(FirebaseHttp.Text(savedMode,"value")!=selected||Number(savedMode,"version",-1)!=nextModeVersion||
                        Number(savedMode,"generation",-1)!=captured.Generation||Number(savedMode,"session",-1)!=captured.Session||
                        FirebaseHttp.Text(savedMode,"writerUid")!=captured.Uid||Number(savedMode,"updatedAt",-1)<=0)
                        throw new IOException("종목 저장 확인 정보가 일치하지 않습니다.");
                    if(activityKey!=null)AcknowledgeActivity(captured.RoomId,response);
                    MergeConfirmedValues(captured.RoomId,captured.Generation,captured.Session,navigation,cells,savedMode);
                });
            }
            catch(UnauthorizedAccessException error)
            {
                if(!requestSent)throw;
                if(capturedRoom!=null&&roomId==capturedRoom){Lost("종목 선택 상태를 다시 확인합니다.");RestartStream();}
                throw new UnauthorizedAccessException("종목 저장이 거부됐습니다. 다른 BJ의 선택, 초기화 여부 또는 새 Firebase 규칙을 확인하세요.",error);
            }
            catch(IOException)
            {
                if(capturedRoom!=null&&roomId==capturedRoom){Lost("종목 저장 연결 확인 필요");RestartStream();}
                throw;
            }
            finally{if(cellLock)cellWriteOperation.Release();resetOperation.Release();}
        }

        sealed class ConfirmedRosterCell
        {
            internal string Key;internal CellValue Value;internal object Stored;
        }

        List<ConfirmedRosterCell> ValidateConfirmedValues(string room,long expectedGeneration,long expectedSession,string uid,
            Dictionary<string,string> values,ScoreState before,Dictionary<string,object> response)
        {
            var result=new List<ConfirmedRosterCell>();
            foreach(var entry in values)
            {
                if(before.Get(entry.Key,"")==entry.Value)continue;
                object raw;
                if(!FirebaseHttp.TryResponseObject(response,"rooms/"+room+"/game/cells/"+entry.Key.Replace('.','_'),out raw))
                    throw new IOException("팀 또는 차감 점수 저장 응답을 확인하지 못했습니다.");
                var saved=FirebaseHttp.Object(raw);object value;saved.TryGetValue("value",out value);
                int version=before.Version(entry.Key)+1;
                if(Number(saved,"version",-1)!=version||Number(saved,"generation",-1)!=expectedGeneration||
                    Number(saved,"session",-1)!=expectedSession||FirebaseHttp.Text(saved,"writerUid")!=uid||
                    Number(saved,"updatedAt",-1)<=0||ScoreRules.NormalizeStored(entry.Key,value)!=entry.Value)
                    throw new IOException("팀 또는 차감 점수 저장 확인 정보가 일치하지 않습니다.");
                result.Add(new ConfirmedRosterCell{Key=entry.Key,Value=new CellValue{Value=entry.Value,Version=version},Stored=FirebaseHttp.CompactStoredCell(saved)});
            }
            return result;
        }

        void MergeConfirmedValues(string room,long expectedGeneration,long expectedSession,int navigation,List<ConfirmedRosterCell> cells,Dictionary<string,object> mode)
        {
            lock(gate)
            {
                if(stopped||roomNavigation!=navigation||roomId!=room||generation!=expectedGeneration||session!=expectedSession||minimumGeneration>expectedGeneration)
                    throw new InvalidOperationException("저장 후 방 또는 초기화 상태가 변경되었습니다. 현재 화면을 확인하세요.");
                foreach(var cell in cells)
                {
                    if(state.Version(cell.Key)>cell.Value.Version)continue;
                    if(state.Version(cell.Key)!=cell.Value.Version||state.Get(cell.Key,"")!=cell.Value.Value){state.Cells[cell.Key]=cell.Value;CellChanged(cell.Key);}
                    SetPath(tree,"cells/"+cell.Key.Replace('.','_'),cell.Stored);
                }
                if(mode!=null&&modeVersion<=Number(mode,"version",0)){SetPath(tree,"mode",mode);ReadRoomMode(expectedGeneration);}
                sequence++;QueueChanged();
            }
        }

        void ApplyConfirmedValues(string room,long expectedGeneration,long expectedSession,int navigation,
            Dictionary<string,string> values,ScoreState before,Dictionary<string,object> response)
        {
            var confirmed=ValidateConfirmedValues(room,expectedGeneration,expectedSession,auth.Uid,values,before,response);
            MergeConfirmedValues(room,expectedGeneration,expectedSession,navigation,confirmed,null);
        }
    }
}

