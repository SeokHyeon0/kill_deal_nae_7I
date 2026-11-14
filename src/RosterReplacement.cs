using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace PubgScore
{
    internal sealed class ResetSynchronizationException : InvalidOperationException
    {
        internal ResetSynchronizationException(Exception error)
            :base("초기화는 서버에 반영됐지만 화면 동기화를 확인하지 못했습니다. "+error.Message,error){}
    }
    // Kept by one input dialog so a failed second step never repeats a successful reset.
    internal sealed class RosterSaveSession
    {
        internal string Room;
        internal long Generation, Session;
        internal int Navigation, Stream;
        internal bool ResetConfirmed, Completed;
        internal long TargetGeneration { get { return Generation + 1; } }
    }

    public sealed partial class FirebaseGameClient
    {
        internal RosterSaveSession BeginRosterSave()
        {
            lock(gate){var current=Status();
                if(!current.IsAdmin||!current.Connected)throw new UnauthorizedAccessException("연결된 방에서 인증된 BJ만 팀을 설정할 수 있습니다.");
                return new RosterSaveSession {Room=current.RoomId,Generation=current.Generation,Session=current.Session,Navigation=roomNavigation,Stream=streamSerial};}
        }
        bool SameRosterRoom(RosterSaveSession attempt)
        {return !stopped&&roomNavigation==attempt.Navigation&&roomId==attempt.Room&&session==attempt.Session;}
        bool CanSendRoster(RosterSaveSession attempt,long expected)
        {lock(gate){var current=Status();return SameRosterRoom(attempt)&&streamSerial==attempt.Stream&&current.Connected&&current.IsAdmin&&current.Generation==expected;}}

        static Dictionary<string,string> RosterValues(RosterImport names,bool killMode)
        {return RosterValues(names,killMode?"kills":"match");}
        internal static Dictionary<string,string> RosterValues(RosterImport names,string mode)
        {
            if(names==null||names.Teams<1||names.Teams>ScoreRules.TeamCount)throw new ArgumentException("선수 이름을 확인하세요.");
            if(!GameModes.IsValid(mode))throw new ArgumentException("경딜, 딜킬, 킬딜, 킬내기 또는 딜내기를 선택하세요.");
            var values=new Dictionary<string,string>();int[] deductions=GameModes.Deductions(mode);
            for(int team=0;team<ScoreRules.TeamCount;team++) {
                bool hasTeam=false;
                for(int player=0;player<ScoreRules.PlayerCount;player++) {
                    string key="t"+team+".p"+player+".name";
                    string name=ScoreRules.Normalize(key,team<names.Teams?names.Names[player,team]??"":"");
                    values[key]=name;if(name.Length>0)hasTeam=true;
                }
                for(int player=0;player<ScoreRules.PlayerCount;player++)
                    values["t"+team+".p"+player+".penalty"]=mode!="match"&&hasTeam?deductions[player].ToString(CultureInfo.InvariantCulture):"";
            }
            return values;
        }
        internal Task SaveRoster(RosterImport names)
        {return SaveRoster(names,BeginRosterSave());}
        internal async Task SaveRoster(RosterImport names,RosterSaveSession attempt)
        {
            // Parse/normalize every input before the first destructive request.
            // A roster never selects a mode or preloads deductions. Selection is a
            // separate, once-per-generation operation after these names are saved.
            var values=RosterValues(names,"match");
            if(attempt==null)throw new ArgumentNullException("attempt");
            if(!resetOperation.Wait(0))throw new InvalidOperationException("초기화 또는 팀 저장이 진행 중입니다.");
            try {
                if(attempt.Completed)throw new InvalidOperationException("이미 새 팀을 적용한 입력 창입니다.");
                lock(gate){var current=Status();
                    // A retry after a confirmed reset may use a reconnected stream in the same generation.
                    if(attempt.ResetConfirmed&&SameRosterRoom(attempt)&&current.Generation==attempt.TargetGeneration&&current.Connected)attempt.Stream=streamSerial;
                    if(!CanSendRoster(attempt,attempt.ResetConfirmed?attempt.TargetGeneration:attempt.Generation))
                        throw new InvalidOperationException("방 또는 게임 상태가 변경되었습니다. 창을 닫고 팀 짜기를 다시 열어 주세요.");
                }
                try {
                    if(!attempt.ResetConfirmed)await Task.Run(()=>ResetRosterGeneration(attempt));
                    await WaitForRosterReset(attempt);
                    await Task.Run(()=>ApplyReplacementRoster(attempt,values));
                    attempt.Completed=true;
                }
                catch(Exception error) {
                    if(error is IOException&&SameRosterRoom(attempt)){Lost("팀 변경 연결 확인 필요");RestartStream();}
                    if(attempt.ResetConfirmed)
                        throw new InvalidOperationException("초기화 완료 · 새 팀 저장 실패. 입력은 유지됩니다. 다시 저장하면 초기화를 반복하지 않습니다. "+error.Message,error);
                    throw new InvalidOperationException("초기화 성공을 확인하지 못해 새 팀을 저장하지 않았습니다. 방 상태를 확인해 주세요. "+error.Message,error);
                }
            }finally{resetOperation.Release();}
        }
        void ResetRosterGeneration(RosterSaveSession attempt)
        {
            if(attempt.Generation==Int64.MaxValue)throw new InvalidOperationException("초기화 횟수 한도에 도달했습니다.");
            var patch=new Dictionary<string,object> {
                {"rooms/"+attempt.Room+"/game/generation",attempt.TargetGeneration},
                {"rooms/"+attempt.Room+"/game/cells",null},
                {"lobby/"+attempt.Room+"/activity",new Dictionary<string,object>{{"contentCount",0}}}
            };
            var reply=Database("spark","PATCH",patch,12000,()=>CanSendRoster(attempt,attempt.Generation));
            if(reply.Status!=200)throw new IOException("초기화를 서버에서 확인하지 못했습니다.");
            lock(gate)acknowledgedActivity.Add(attempt.Room);attempt.ResetConfirmed=true;
        }
        async Task WaitForRosterReset(RosterSaveSession attempt)
        {
            var wait=Stopwatch.StartNew();
            while(wait.ElapsedMilliseconds<8000) {
                lock(gate){var current=Status();
                    if(!SameRosterRoom(attempt)||streamSerial!=attempt.Stream||current.Generation>attempt.TargetGeneration)
                        throw new InvalidOperationException("초기화 중 방 또는 연결 상태가 변경되었습니다.");
                    if(current.Generation==attempt.TargetGeneration&&current.Connected&&current.IsAdmin)return;
                }
                await Task.Delay(20);
            }
            throw new IOException("초기화된 데이터를 아직 수신하지 못했습니다. 연결을 확인한 뒤 다시 시도하세요.");
        }
        void ApplyReplacementRoster(RosterSaveSession attempt,Dictionary<string,string> values)
        {
            GameView current;int initialCount;
            lock(gate){if(!CanSendRoster(attempt,attempt.TargetGeneration))throw new InvalidOperationException("새 팀을 적용하기 전에 방 상태가 변경되었습니다.");current=RosterView();initialCount=ActivityCounter(attempt.Room).HasValue?0:RoomActivity.Count(state);}
            var patch=new Dictionary<string,object>();int delta=0;string activityKey=null;int activityVersion=0;
            foreach(var item in values) {
                string key=item.Key,value=item.Value;
                if(current.State.Get(key,"")==value)continue;
                delta+=RoomActivity.ContentValue(key,value)-RoomActivity.ContentValue(key,current.State.Get(key,""));
                int version=current.State.Version(key);if(version==Int32.MaxValue)throw new InvalidOperationException("수정 횟수 한도에 도달한 입력이 있습니다.");
                if(activityKey==null){activityKey=key;activityVersion=version+1;}
                object wire=value;decimal number;
                if(key.EndsWith(".penalty",StringComparison.Ordinal)&&value.Length>0&&ScoreRules.TryDecimal(value,out number))wire=number;
                patch["rooms/"+attempt.Room+"/game/cells/"+key.Replace('.','_')]=new Dictionary<string,object> {
                    {"value",wire},{"version",version+1},{"generation",attempt.TargetGeneration},
                    {"session",attempt.Session},{"writerUid",auth.Uid},{"updatedAt",Timestamp()}
                };
            }
            if(patch.Count==0)return;
            if(activityKey!=null)lock(gate)patch["lobby/"+attempt.Room+"/activity"]=ActivityPayload(attempt.Room,activityKey,activityVersion,delta,initialCount);
            var reply=Database("spark","PATCH",patch,12000,()=>CanSendRoster(attempt,attempt.TargetGeneration));
            if(reply.Status!=200)throw new IOException("서버가 새 팀을 저장하지 않았습니다.");
            var response=FirebaseHttp.Object(FirebaseHttp.Json().DeserializeObject(reply.Body));
            if(activityKey!=null)AcknowledgeActivity(attempt.Room,response);
            ApplyConfirmedValues(attempt.Room,attempt.TargetGeneration,attempt.Session,attempt.Navigation,values,current.State,response);
        }
        public async Task ResetAll(long expectedGeneration)
        {
            if(!resetOperation.Wait(0))throw new InvalidOperationException("초기화 또는 팀 저장이 진행 중입니다.");
            try {
                var attempt=BeginRosterSave();
                if(attempt.Generation!=expectedGeneration)throw new InvalidOperationException("이미 다른 초기화가 진행되었습니다.");
                await Task.Run(()=>ResetRosterGeneration(attempt));
                try{await WaitForRosterReset(attempt);}
                catch(Exception error){throw new ResetSynchronizationException(error);}
            }finally{resetOperation.Release();}
        }
    }
}
