using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
namespace PubgScore
{
    public sealed class RoomInfo {
        public string Id="",Name="",OwnerUid="",Status="";
        public string DatabaseName="",PreparationError="";public bool Available=true,IsShared;
        internal long? ContentCount;internal long LastInputAt;internal bool ActivityActive;
        public string StatusText {get{return !Available?(PreparationError.Length>0?PreparationError:"방 정보를 확인하세요."):ActivityActive?"킬딜내기 진행중":"사용 안 함";}}
    }
    public sealed class LobbyView {public List<RoomInfo> Rooms=new List<RoomInfo>();public bool Ready,Connected;public long Sequence;public string Error="";}
    public sealed partial class FirebaseGameClient
    {
        string roomId="",roomName="",roomOwner="",roomStatus="";
        int roomNavigation;bool lobbyReady,roomUnavailable;string lobbyError="방 목록을 불러오는 중…";
        long lobbySequence;readonly Stopwatch lobbyProof=new Stopwatch();
        readonly Dictionary<string,RoomInfo> rooms=new Dictionary<string,RoomInfo>();
        static void CheckRoomId(string id){if(!SharedRooms.IsValidRoomId(id))throw new ArgumentException("방 ID를 확인하세요.");}
        public LobbyView Lobby(){return Lobby(true);}
        public LobbyView Lobby(bool includeRooms)
        {
            lock(gate){RefreshRoomActivity();var result=new LobbyView {Ready=lobbyReady,Connected=lobbyReady&&lobbyProof.ElapsedMilliseconds<45000,Sequence=lobbySequence,Error=lobbyError};
                if(includeRooms) {
                    result.Rooms=SharedRooms.Project(rooms.Values);
                    if(result.Error.Length==0)result.Error=result.Rooms.Where(r=>r.PreparationError.Length>0).Select(r=>r.PreparationError).FirstOrDefault()??"";
                }
                return result;}
        }
        async Task PatchSpark(Dictionary<string,object> patch){await Task.Run(()=>{if(stopped)throw new InvalidOperationException("연결 종료");var reply=Database("spark","PATCH",patch,8000);if(reply.Status!=200)throw new IOException("변경 내용을 서버에 저장하지 못했습니다.");});}
        public async Task CreateRoom(string name,string requestId)
        {
            name=(name??"").Trim();if(name.Length==0||name.Length>60||name.Any(Char.IsControl))throw new ArgumentException("방 이름을 1~60자로 입력하세요.");
            CheckRoomId(requestId);if(!Status().IsBj)throw new UnauthorizedAccessException("인증된 BJ만 방을 만들 수 있습니다.");
            int navigation=roomNavigation;string uid=auth.Uid;
            var control=new {running=true,session=0};
            var patch=new Dictionary<string,object>{{"lobby/"+requestId,new {name=name,ownerUid=uid,running=true,session=0}},{"rooms/"+requestId,new {game=new {generation=0,roundCount=50,control=control,room=new {name=name,ownerUid=uid}}}}};
            RoomInfo listed=null;
            Exception failure=null;
            try{await PatchSpark(patch);}catch(Exception ex){failure=ex;}
            if(failure!=null)
            {
                var existing=await ReadObject("spark/lobby/"+requestId);
                if(FirebaseHttp.Text(existing,"ownerUid")!=uid||FirebaseHttp.Text(existing,"name")!=name)throw failure;
                listed=SharedRooms.ReadLobbyRoom(requestId,existing);
            }
            lock(gate){if(stopped||navigation!=roomNavigation)return;SelectRoom(requestId,listed);}
        }
        Task<Dictionary<string,object>> ReadObject(string location){return Task.Run(()=>{var reply=Database(location,"GET",null,8000);if(reply.Status!=200)throw new IOException("서버 조회 실패");return FirebaseHttp.Object(FirebaseHttp.Json().DeserializeObject(reply.Body));});}
        public async Task JoinRoom(string id,string nickname,CancellationToken cancel)
        {
            CheckRoomId(id);cancel.ThrowIfCancellationRequested();int navigation=roomNavigation;
            var metadata=await ReadObject("spark/lobby/"+id);cancel.ThrowIfCancellationRequested();
            if(metadata.Count==0)throw new InvalidOperationException("이미 종료되어 삭제된 방입니다.");
            // Verify access before navigation. Never create admission records or wait for approval.
            var control=await ReadObject("spark/rooms/"+id+"/game/control");cancel.ThrowIfCancellationRequested();
            if(control.Count==0)throw new InvalidOperationException("이미 종료되어 삭제된 방입니다.");
            string path="spark/rooms/"+id+"/game/";
            var parts=await Task.WhenAll(ReadSharedValue(path+"room",cancel),ReadSharedValue(path+"generation",cancel),ReadSharedValue(path+"roundCount",cancel));cancel.ThrowIfCancellationRequested();
            var listed=SharedRooms.ReadLobbyRoom(id,metadata);
            var game=new Dictionary<string,object>{{"room",parts[0]},{"control",control},{"generation",parts[1]},{"roundCount",parts[2]}};
            SharedRooms.ValidateExisting(listed,game,metadata);
            lock(gate)
            {
                cancel.ThrowIfCancellationRequested();
                if(stopped||navigation!=roomNavigation)throw new OperationCanceledException();
                SelectRoom(id,listed);
            }
        }
        Task<object> ReadSharedValue(string path,CancellationToken cancel)
        {return Task.Run(()=>{cancel.ThrowIfCancellationRequested();var reply=Database(path,"GET",null,8000,()=>!stopped&&!cancel.IsCancellationRequested);cancel.ThrowIfCancellationRequested();if(reply.Status!=200)throw new IOException("공용 방 정보 조회 실패");return FirebaseHttp.Json().DeserializeObject(reply.Body);});}
        internal static string DescribeStreamFailure(WebException error)
        {
            var response=error.Response as HttpWebResponse;int status=response==null?0:(int)response.StatusCode;
            if(status==401||status==403)return "Firebase 접근 거부 (HTTP "+status+") · 새 방 보안 규칙을 게시한 뒤 다시 연결하세요. 계속되면 다시 로그인하세요.";
            if(status==404)return "Firebase DB 주소를 찾을 수 없습니다 (HTTP 404). firebase.config.json을 확인하세요.";
            if(status>=500)return "Firebase 서버 응답 오류 (HTTP "+status+") · 재연결 중…";
            if(status!=0)return "Firebase 연결 거부 (HTTP "+status+") · 설정을 확인하세요.";
            if(error.Status==WebExceptionStatus.TrustFailure||error.Status==WebExceptionStatus.SecureChannelFailure)return "보안 연결 실패 · Windows 날짜/시간과 인증서 설정을 확인하세요.";
            if(error.Status==WebExceptionStatus.Timeout)return "연결 시간 초과 · 인터넷과 방화벽을 확인하세요. 재연결 중…";
            return "네트워크 연결 실패 · 인터넷과 방화벽을 확인하세요. 재연결 중…";
        }
        // The caller holds gate so the old stream is invalidated with the room change.
        void SelectRoom(string id,RoomInfo listed)
        {
            if(!roomUnavailable)FreezeResult();
            roomNavigation++;roomId=id;roomUnavailable=false;roomName=roomStatus=roomOwner=roomMode="";
            modeVersion=0;state=new ScoreState();FullStateChanged();tree.Clear();
            generation=minimumGeneration=session=-1;running=false;
            if(listed!=null)
            {
                rooms[id]=listed;
                acknowledgedActivity.Remove(id);
            }
            RestartStream();
        }
        public void LeaveRoom(){lock(gate){if(!stopped)SelectRoom("",null);}}
        public bool RoomUnavailable {get{lock(gate)return roomUnavailable;}}
        void ApplyLobbyEvent(string kind,string json,int serial,ref Dictionary<string,object> snapshot)
        {
            var message=FirebaseHttp.Json().Deserialize<Dictionary<string,object>>(json);
            string path=FirebaseHttp.Text(message,"path");object raw;
            if(!message.TryGetValue("data",out raw))throw new IOException("방 목록 응답을 확인할 수 없습니다.");
            if(kind=="put"&&path=="/")snapshot=FirebaseHttp.Object(raw);
            else if(kind=="put")SetPath(snapshot,path,raw);
            else foreach(var pair in FirebaseHttp.Object(raw))SetPath(snapshot,path.TrimEnd('/')+"/"+pair.Key,pair.Value);
            lock(gate)
            {
                if(stopped||serial!=streamSerial)return;
                rooms.Clear();
                foreach(var pair in snapshot)rooms[pair.Key]=SharedRooms.ReadLobbyRoom(pair.Key,pair.Value);
                acknowledgedActivity.RemoveWhere(id=>!rooms.ContainsKey(id)||rooms[id].ContentCount.HasValue);
                RefreshRoomActivity();
                lobbyReady=true;lobbyError="";lobbySequence++;QueueChanged();
            }
        }
    }
}
