using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PubgScore
{
    public sealed class GameView
    {
        public ScoreState State;public bool Connected,Authenticated,IsAdmin,CanEdit,Running;
        public GameView():this(true){}
        internal GameView(bool includeState){if(includeState)State=new ScoreState();}
        public string RoomId="",RoomName="",RoomStatus="",Mode="";public int ModeVersion;public bool IsBj;
        public int RoundCount;public long Revision; public int Navigation; public bool FullState=true;
        public string Uid="",Info="자동 인증 및 연결 대기";public long Sequence,Session=-1,Generation=-1;
    }
    public interface IGameClient : IDisposable
    {
        GameView View();Task AutoLogin(bool administrator);Task ConnectAnonymous();
        Task LoginAdministrator(string email,string password);void Logout();Task SetRunning(bool running);
        Task ResetAll(long expectedGeneration);Task<CellValue> Write(string key,string value,int version,long generation,long session);
    }
    internal sealed class HttpReply
    {
        public int Status;public string Body,ETag,Date;public long Elapsed;
    }
    internal static class FirebaseHttp
    {
        internal static JavaScriptSerializer Json(){return new JavaScriptSerializer {MaxJsonLength=4*1024*1024};}
        internal static StringBuilder ResetEventBuffer(StringBuilder buffer)
        {
            // A complete room can be large. Do not retain its buffer for the entire SSE connection.
            if(buffer.Capacity>64*1024)return new StringBuilder();
            buffer.Clear();return buffer;
        }
        internal static HttpWebRequest Request(string url,string method,int timeout)
        {
            var request=(HttpWebRequest)WebRequest.Create(url);request.Method=method;request.Timeout=timeout;request.ReadWriteTimeout=timeout;request.AllowAutoRedirect=true;request.KeepAlive=true;request.CachePolicy=new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);return request;
        }
        internal static HttpReply Send(string url,string method,object payload,string etag,bool getTag,int timeout)
        {
            if(method=="GET")Interlocked.Increment(ref NetworkMetrics.Gets);else Interlocked.Increment(ref NetworkMetrics.Writes);var watch=Stopwatch.StartNew();var request=Request(url,method,timeout);
            if(etag!=null)request.Headers["if-match"]=etag;if(getTag)request.Headers["X-Firebase-ETag"]="true";
            if(payload!=null)
            {
                string form=payload as string;byte[] body=Encoding.UTF8.GetBytes(form??Json().Serialize(payload));Interlocked.Add(ref NetworkMetrics.SentJsonBytes,body.Length);request.ContentType=form!=null?"application/x-www-form-urlencoded":"application/json";request.ContentLength=body.Length;using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);
            }
            HttpWebResponse response;
            try{response=(HttpWebResponse)request.GetResponse();}
            catch(WebException ex){response=ex.Response as HttpWebResponse;if(response==null)throw new IOException("Firebase 연결을 확인할 수 없어 편집을 잠갔습니다.");}
            using(response)using(var reader=new StreamReader(response.GetResponseStream())){var text=reader.ReadToEnd();Interlocked.Add(ref NetworkMetrics.ReceivedJsonBytes,Encoding.UTF8.GetByteCount(text));return new HttpReply {Status=(int)response.StatusCode,Body=text,ETag=response.Headers["ETag"],Date=response.Headers["Date"],Elapsed=watch.ElapsedMilliseconds};}
        }
        internal static Dictionary<string,object> Object(object value){return value as Dictionary<string,object>??new Dictionary<string,object>();}
        internal static object CompactStoredCell(object raw)
        {
            var cell=raw as Dictionary<string,object>;if(cell==null)return raw;
            // The local read model uses only these three fields. Write receipts and security
            // metadata remain in the server/payload; retaining them here duplicates every cell.
            if(cell.Count<=3&&cell.Keys.All(key=>key=="value"||key=="version"||key=="generation"))return cell;
            var compact=new Dictionary<string,object>(3,StringComparer.Ordinal);object value;
            if(cell.TryGetValue("value",out value))compact["value"]=value;
            if(cell.TryGetValue("version",out value))compact["version"]=value;
            if(cell.TryGetValue("generation",out value))compact["generation"]=value;
            return compact;
        }
        internal static void CompactStoredCells(Dictionary<string,object> root,HashSet<string> changes,bool full)
        {
            object raw;var cells=root.TryGetValue("cells",out raw)?raw as Dictionary<string,object>:null;if(cells==null)return;
            if(full){foreach(string key in cells.Keys.ToArray())cells[key]=CompactStoredCell(cells[key]);return;}
            foreach(string path in changes){if(!path.StartsWith("cells/",StringComparison.Ordinal))continue;
                string key=path.Substring(6);if(cells.TryGetValue(key,out raw))cells[key]=CompactStoredCell(raw);
            }
        }
        // REST multi-location PATCH replies can be expanded into nested objects.
        internal static bool TryResponseObject(Dictionary<string,object> response,string location,out object value)
        {
            value=null;if(response==null||String.IsNullOrEmpty(location))return false;
            object current;if(response.TryGetValue(location,out current)){value=current;return current is Dictionary<string,object>;}
            current=response;
            foreach(string part in location.Split('/')) {
                var node=current as Dictionary<string,object>;
                if(part.Length==0||node==null||!node.TryGetValue(part,out current))return false;
            }
            value=current;return current is Dictionary<string,object>;
        }
        internal static string Text(Dictionary<string,object> value,string key){object result;return value.TryGetValue(key,out result)&&result!=null?Convert.ToString(result,CultureInfo.InvariantCulture):"";}
    }
    internal sealed class FirebaseAuth
    {
        readonly FirebaseConfig config;readonly object gate=new object();volatile string token="",uid="",verifiedAdministratorUid="";string refresh="";DateTime expires;bool anonymous;
        internal FirebaseAuth(FirebaseConfig value){config=value;ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;}
        internal string Uid {get{return uid;}} internal string Token {get{return token;}}
        internal bool IsVerifiedAdministrator {get{var verified=verifiedAdministratorUid;return verified.Length>0&&verified==uid&&token.Length>0;}}
        internal int Generation {get;private set;}
        string FilePath {get{return config.IdentityFile();}}
        byte[] Entropy {get{return Encoding.UTF8.GetBytes(config.DatabaseUrl+"|participant");}}
        void Clear(){token="";uid="";verifiedAdministratorUid="";refresh="";expires=DateTime.MinValue;}
        void ReadSaved(){refresh=anonymous?Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath),Entropy,DataProtectionScope.CurrentUser)):CredentialVault.Read(config.CredentialTarget);}
        internal void Anonymous()
        {
            lock(gate){anonymous=true;verifiedAdministratorUid="";try{ReadSaved();Refresh();Persist();return;}catch(Exception){Clear();}
                Authenticate("accounts:signUp",new {returnSecureToken=true});Persist();}
        }
        internal void SetupAdministrator(string email,string password)
        {lock(gate){anonymous=false;verifiedAdministratorUid="";try{Authenticate("accounts:signInWithPassword",new {email=email,password=password,returnSecureToken=true});VerifyAdministrator();Persist();}catch{Clear();throw;}}}
        internal void RestoreAdministrator()
        {
            lock(gate){anonymous=false;verifiedAdministratorUid="";try{ReadSaved();if(refresh.Length==0)throw new UnauthorizedAccessException("BJ 로그인이 필요합니다.");Refresh();VerifyAdministrator();Persist();}
                catch(UnauthorizedAccessException){CredentialVault.Delete(config.CredentialTarget);Clear();throw;}
                catch{Clear();throw;}}
        }
        void VerifyAdministrator()
        {
            var reply=FirebaseHttp.Send(config.DatabaseEndpoint("roles/admins/"+uid,token),"GET",null,null,false,8000);
            if(reply.Status!=200||reply.Body.Trim()!="true")throw new UnauthorizedAccessException("BJ 목록에 등록된 계정만 로그인할 수 있습니다. Firebase BJ UID 목록을 확인하세요.");
            verifiedAdministratorUid=uid;
        }
        void Accept(Dictionary<string,object> data,bool renewed)
        {
            string nextUid=FirebaseHttp.Text(data,renewed?"user_id":"localId");
            string nextToken=FirebaseHttp.Text(data,renewed?"id_token":"idToken"),nextRefresh=FirebaseHttp.Text(data,renewed?"refresh_token":"refreshToken");
            int seconds;if(nextUid.Length==0||nextToken.Length==0||nextRefresh.Length==0||!Int32.TryParse(FirebaseHttp.Text(data,renewed?"expires_in":"expiresIn"),out seconds))throw new InvalidOperationException("인증 응답을 확인할 수 없습니다.");
            if(verifiedAdministratorUid.Length>0&&nextUid!=verifiedAdministratorUid)
                throw new UnauthorizedAccessException("BJ 인증 계정이 변경되었습니다. 다시 로그인하세요.");
            token=nextToken;uid=nextUid;refresh=nextRefresh;expires=DateTime.UtcNow.AddSeconds(seconds);Generation++;
        }
        void Authenticate(string operation,object body)
        {
            var reply=FirebaseHttp.Send(config.AuthBase+operation+"?key="+Uri.EscapeDataString(config.ApiKey),"POST",body,null,false,8000);
            if(reply.Status!=200)throw new UnauthorizedAccessException("로그인 실패. 이메일·비밀번호와 Firebase 인증 설정을 확인하세요.");
            Accept(FirebaseHttp.Json().Deserialize<Dictionary<string,object>>(reply.Body),false);
        }
        void Refresh()
        {
            var reply=FirebaseHttp.Send(config.TokenBase+"?key="+Uri.EscapeDataString(config.ApiKey),"POST","grant_type=refresh_token&refresh_token="+Uri.EscapeDataString(refresh),null,false,8000);
            if(reply.Status==400||reply.Status==401||reply.Status==403)throw new UnauthorizedAccessException("저장된 인증이 만료되거나 폐기되었습니다. 다시 로그인하세요.");
            if(reply.Status!=200)throw new IOException("인증 서버 연결을 확인하세요.");
            Accept(FirebaseHttp.Json().Deserialize<Dictionary<string,object>>(reply.Body),true);
        }
        internal void RequireRefresh(){lock(gate)expires=DateTime.MinValue;}
        internal void Ensure()
        {
            lock(gate){if(token.Length==0)throw new UnauthorizedAccessException("다시 로그인하세요.");
                if(DateTime.UtcNow.AddMinutes(5)<expires)return;
                try{Refresh();Persist();}catch(UnauthorizedAccessException){Clear();if(anonymous){Authenticate("accounts:signUp",new {returnSecureToken=true});Persist();}else{CredentialVault.Delete(config.CredentialTarget);throw;}}}
        }
        internal void Logout(){lock(gate){if(!anonymous)CredentialVault.Delete(config.CredentialTarget);Clear();}}
        void Persist()
        {
            if(!anonymous){CredentialVault.Save(config.CredentialTarget,uid,refresh);return;}
            string file=FilePath;Directory.CreateDirectory(Path.GetDirectoryName(file));byte[] bytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(refresh),Entropy,DataProtectionScope.CurrentUser);
            string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllBytes(temporary,bytes);if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
    public sealed partial class FirebaseGameClient : IGameClient
    {
        readonly FirebaseConfig config;readonly FirebaseAuth auth;readonly object gate=new object();readonly SemaphoreSlim resetOperation=new SemaphoreSlim(1,1);
        readonly SemaphoreSlim cellWriteOperation=new SemaphoreSlim(1,1);WriteRateLedger writeLedger;string writeLedgerUid="";
        readonly Stopwatch proof=new Stopwatch();HttpWebRequest stream;Dictionary<string,object> tree=new Dictionary<string,object>();ScoreState state=new ScoreState();
        bool stopped,started,adminMode,streamReady,healthy,running;long session=-1,generation=-1,minimumGeneration=-1,sequence;int streamSerial,modeVersion;string info="자동 인증 대기",roomMode="";
        public FirebaseGameClient(FirebaseConfig value):this(value,new SemaphoreSlim(1,1)){}
        internal FirebaseGameClient(FirebaseConfig value,SemaphoreSlim connectionSlot)
        {
            config=value;config.Validate();auth=new FirebaseAuth(config);streamSlot=connectionSlot;
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;ServicePointManager.DefaultConnectionLimit=64;
            NetworkChange.NetworkAvailabilityChanged+=NetworkChanged;healthTimer=new System.Threading.Timer(CheckHealth,null,1000,1000);
        }
        ResultSnapshot lastResult;
        public ResultSnapshot LastResult {get{lock(gate){if(roomId.Length>0&&streamReady)return CaptureResult();return lastResult==null?null:lastResult.Copy();}}}
        string Url(string location){return config.DatabaseEndpoint(location,auth.Token);}
        static long Number(Dictionary<string,object> data,string key,long fallback){object value;try{return data.TryGetValue(key,out value)?Convert.ToInt64(value,CultureInfo.InvariantCulture):fallback;}catch{return fallback;}}
        public GameView View(){lock(gate){var result=Status();result.State=state.Copy();return result;}}
        public GameView Status(){lock(gate){
            bool bj=!stopped&&adminMode&&auth.IsVerifiedAdministrator;
            bool activeStreamReady=roomId.Length==0?lobbyReady&&lobbyProof.IsRunning&&lobbyProof.ElapsedMilliseconds<45000:
                streamReady&&healthy&&proof.IsRunning&&proof.ElapsedMilliseconds<45000&&generation>=minimumGeneration&&generation>=0;
            bool connected=started&&!stopped&&auth.Token.Length>0&&(!adminMode||bj)&&activeStreamReady;
            bool expired=roomUnavailable;
            return new GameView(false) {RoundCount=state.RoundCount,Revision=stateRevision,Navigation=roomNavigation,RoomId=roomId,RoomName=roomName,RoomStatus=roomStatus,Mode=roomId.Length>0?roomMode:"",ModeVersion=roomId.Length>0?modeVersion:0,IsBj=bj,Uid=auth.Uid,Generation=generation,Session=session,Running=running,Sequence=sequence,Authenticated=auth.Token.Length>0,IsAdmin=bj&&roomId.Length>0,Connected=connected&&!expired,CanEdit=connected&&!expired&&roomId.Length>0&&auth.Token.Length>0,Info=expired?"방이 만료되었습니다.":connected?(roomId.Length==0?"방을 선택하세요":"입력 가능"):info+" · 연결 확인 전 읽기 전용"};}}
        public async Task AutoLogin(bool administrator){if(started)return;adminMode=administrator;await Task.Run(()=>{if(administrator)auth.RestoreAdministrator();else auth.Anonymous();});Begin();}
        public async Task ConnectAnonymous(){if(started){RestartStream();return;}if(auth.Token.Length==0)await Task.Run(()=>auth.Anonymous());adminMode=false;Begin();}
        public async Task LoginAdministrator(string email,string password)
        {if(started)throw new InvalidOperationException("먼저 로그아웃하세요.");adminMode=true;await Task.Run(()=>auth.SetupAdministrator(email,password));Begin();}
        public void Logout(){Dispose();auth.Logout();}
        public async Task SetRunning(bool running)
        {
            var current=Status();if(!current.IsAdmin||!current.Connected||roomOwner!=auth.Uid)throw new UnauthorizedAccessException("해당 방을 만든 BJ만 종료할 수 있습니다.");
            var patch=new Dictionary<string,object>();
            if(running){patch["rooms/"+current.RoomId+"/game/control"]=new {running=true,session=current.Session+1};patch["lobby/"+current.RoomId+"/running"]=true;patch["lobby/"+current.RoomId+"/session"]=current.Session+1;}
            else {patch["rooms/"+current.RoomId]=null;patch["lobby/"+current.RoomId]=null;patch["closed/"+current.RoomId]=true;}
            await PatchSpark(patch);
        }
        void Begin()
        {
            lock(gate){if(stopped||started)return;started=true;info="실시간 연결 중";RestartStream();}
            Task.Run((Action)RunStreamWorker);
            Task.Run((Func<Task>)AuthenticationLoop);
        }
        void Lost(string reason){lock(gate){healthy=false;info=reason;sequence++;QueueChanged();}}
        internal static void SetPath(Dictionary<string,object> root,string path,object value){string[] parts=path.Trim('/').Split('/');var node=root;for(int i=0;i<parts.Length-1;i++){object next;if(!node.TryGetValue(parts[i],out next)||!(next is Dictionary<string,object>))node[parts[i]]=next=new Dictionary<string,object>();node=(Dictionary<string,object>)next;}if(value==null)node.Remove(parts[parts.Length-1]);else node[parts[parts.Length-1]]=value;}
        void ApplyEvent(string kind,string json,int serial)
        {
            var message=FirebaseHttp.Json().Deserialize<Dictionary<string,object>>(json);string location=FirebaseHttp.Text(message,"path");object data;
            if(!message.TryGetValue("data",out data))throw new IOException("경기 데이터 응답을 확인할 수 없습니다.");
            lock(gate){if(stopped||serial!=streamSerial||roomUnavailable)return;
                if(kind=="put"&&location=="/"&&data==null){MarkRoomUnavailable(serial);return;}
                bool full=kind=="put"&&(location=="/"||location.Trim('/')=="cells");var changed=new HashSet<string>(StringComparer.Ordinal);
                CollectChanged(location,kind,data,changed,ref full);
                if(kind=="put"&&location=="/")tree=FirebaseHttp.Object(data);else if(kind=="put")SetPath(tree,location,data);else foreach(var pair in FirebaseHttp.Object(data))SetPath(tree,location.TrimEnd('/')+"/"+pair.Key,pair.Value);
                FirebaseHttp.CompactStoredCells(tree,changed,full);
                if(full||!streamReady||changed.Contains("generation")||changed.Contains("roundCount"))RebuildState();else ApplyIncremental(changed);}
        }
        void RebuildState()
        {
            long incoming=Number(tree,"generation",-1);if(incoming<minimumGeneration||incoming<generation){streamReady=false;return;}
            if(incoming<0){streamReady=false;info="Firebase generation 설정이 필요합니다. 적용 안내를 확인하세요.";sequence++;QueueChanged();return;}
            object controlRaw;var control=tree.TryGetValue("control",out controlRaw)?FirebaseHttp.Object(controlRaw):new Dictionary<string,object>();
            long nextSession=Number(control,"session",-1);object runningRaw;if(nextSession<0||!control.TryGetValue("running",out runningRaw)||!(runningRaw is bool)){streamReady=false;info="Firebase control 설정이 필요합니다.";sequence++;QueueChanged();return;}
            session=nextSession;running=(bool)runningRaw;
            ReadRoomMode(incoming);
            object roomRaw;var metadata=tree.TryGetValue("room",out roomRaw)?FirebaseHttp.Object(roomRaw):new Dictionary<string,object>();roomName=FirebaseHttp.Text(metadata,"name");roomStatus=running?"running":"waiting";roomOwner=FirebaseHttp.Text(metadata,"ownerUid");
            int rounds=(int)Number(tree,"roundCount",ScoreRules.DefaultRounds);if(rounds<ScoreRules.DefaultRounds||rounds>ScoreRules.MaximumRounds){streamReady=false;info="저장된 라운드 수를 확인하세요.";sequence++;QueueChanged();return;}
            bool newGeneration=incoming!=generation;var next=new ScoreState();object obj;var cells=tree.TryGetValue("cells",out obj)?FirebaseHttp.Object(obj):new Dictionary<string,object>();
            foreach(var cell in cells){var item=FirebaseHttp.Object(cell.Value);if(Number(item,"generation",incoming==0?0:-1)!=incoming)continue;string key=cell.Key.Replace('_','.');object raw;item.TryGetValue("value",out raw);
                try{ScoreRules.ValidateRound(key,rounds);string value=ScoreRules.NormalizeStored(key,raw);next.Cells[key]=!newGeneration&&state.Version(key)>Number(item,"version",0)?state.Cells[key]:new CellValue {Value=value,Version=(int)Number(item,"version",0)};}
                catch(ArgumentException){streamReady=false;info="저장 데이터 오류 ("+key+"): 숫자 또는 판수 범위를 확인하세요. 데이터는 삭제하지 않았습니다.";sequence++;QueueChanged();return;}}
            next.Cells["roundCount"]=new CellValue {Value=rounds.ToString(CultureInfo.InvariantCulture),Version=0};state=next;generation=incoming;minimumGeneration=Math.Max(minimumGeneration,incoming);streamReady=true;healthy=true;sequence++;QueueChanged();
            FullStateChanged();
        }
        HttpReply Database(string location,string method,object body,int timeout=2000,Func<bool> valid=null){auth.Ensure();if(valid!=null&&!valid())throw new InvalidOperationException("이전 연결의 미전송 요청은 폐기합니다.");var result=FirebaseHttp.Send(Url(location),method,body,null,false,timeout);activityClock.Observe(result.Date);if(result.Status==401||result.Status==403)throw new UnauthorizedAccessException("Firebase에서 요청을 거부했습니다. 인증·초기화 세대·Rules를 확인하세요.");return result;}
        // This timer checks only local token expiry. It never polls the database.
        async Task AuthenticationLoop()
        {
            int tokenGeneration=auth.Generation;
            while(!stopped){
                try{auth.Ensure();if(tokenGeneration!=auth.Generation){tokenGeneration=auth.Generation;RestartStream();}}
                catch(Exception){Lost("자동 인증을 복구하지 못했습니다. 연결 또는 로그인을 확인하세요.");}
                try{await Task.Delay(15000,lifetime.Token);}catch(OperationCanceledException){return;}
            }
        }
        static object Timestamp(){return new Dictionary<string,object>{{".sv","timestamp"}};}
        public async Task<CellValue> Write(string key,string value,int version,long capturedGeneration,long capturedSession)
        {
            value=ScoreRules.Normalize(key,value);if(key=="roundCount"||key.EndsWith(".reference")||key.EndsWith(".status")||System.Text.RegularExpressions.Regex.IsMatch(key,@"^t[0-9]\.name$"))throw new ArgumentException("변경할 수 없는 항목입니다.");
            var view=Status();ScoreRules.ValidateRound(key,view.RoundCount);if(!view.CanEdit||view.Generation!=capturedGeneration||view.Session!=capturedSession)throw new InvalidOperationException("초기화 이전 입력 또는 연결 확인 전 입력은 전송하지 않습니다.");
            bool ownerSetting=key=="buddySize";
            bool splitInput=key.EndsWith(".kills",StringComparison.Ordinal)||key.EndsWith(".damage",StringComparison.Ordinal);
            int capturedModeVersion=view.ModeVersion;
            if(splitInput&&view.Mode!="killdeal")throw new InvalidOperationException("킬과 딜은 킬딜 모드에서만 입력할 수 있습니다.");
            if(ownerSetting&&!view.IsAdmin)throw new UnauthorizedAccessException("인증된 BJ만 깐부모드를 변경할 수 있습니다.");
            string capturedRoom=view.RoomId,capturedPath="spark/rooms/"+view.RoomId+"/game";int capturedNavigation=roomNavigation,capturedStream=streamSerial;
            await cellWriteOperation.WaitAsync();
            try{return await Task.Run(()=>{try{view=Status();if(capturedStream!=streamSerial||capturedNavigation!=roomNavigation||view.RoomId!=capturedRoom||!view.CanEdit||view.Generation!=capturedGeneration||view.Session!=capturedSession)throw new InvalidOperationException("초기화 세대 변경 또는 연결 끊김");
                if(ownerSetting&&!view.IsAdmin)throw new UnauthorizedAccessException("BJ 권한을 다시 확인하세요.");
                if(splitInput&&(view.Mode!="killdeal"||view.ModeVersion!=capturedModeVersion))throw new InvalidOperationException("종목이 변경되어 이전 킬·딜 입력은 전송하지 않습니다.");
                if(key.EndsWith(".chicken",StringComparison.Ordinal)&&value.Length>0&&Array.IndexOf(PlacementOptions.SharedValues(view.Mode),value)<0)
                    throw new ArgumentException("선택한 종목의 치킨 점수 목록에서 값을 선택하세요.");
                object wire=value;decimal number;if(key.EndsWith(".check"))wire=value=="1";else if(!key.EndsWith(".name")&&value.Length>0&&ScoreRules.TryDecimal(value,out number))wire=number;
                var payload=new Dictionary<string,object>{{"value",wire},{"version",version+1},{"generation",capturedGeneration},{"session",capturedSession},{"writerUid",auth.Uid},{"updatedAt",Timestamp()}};
                string uid=auth.Uid,field=key.Replace('.','_'),ratePath="writeRates/"+uid,cellPath="rooms/"+capturedRoom+"/game/cells/"+field;
                if(writeLedger==null||writeLedgerUid!=uid){var existing=Database("spark/"+ratePath,"GET",null);if(existing.Status!=200)throw new IOException("저장 확인 정보를 읽지 못했습니다.");writeLedger=WriteRateLedger.Read(FirebaseHttp.Json().DeserializeObject(existing.Body));writeLedgerUid=uid;}
                var patch=new Dictionary<string,object>{{cellPath,payload},{ratePath,writeLedger.Next(capturedRoom,field,version+1)}};
                bool wroteActivity=AddCellActivity(patch,capturedRoom,key,value,version);
                var result=Database("spark","PATCH",patch,2000,()=>roomNavigation==capturedNavigation&&streamSerial==capturedStream&&Status().CanEdit&&(!ownerSetting||Status().IsAdmin)&&(!splitInput||(Status().Mode=="killdeal"&&Status().ModeVersion==capturedModeVersion))&&auth.Uid==uid);if(result.Status!=200)throw new IOException("저장 실패");
                var response=FirebaseHttp.Object(FirebaseHttp.Json().DeserializeObject(result.Body));object savedRate,savedCell;if(!FirebaseHttp.TryResponseObject(response,ratePath,out savedRate)||!FirebaseHttp.TryResponseObject(response,cellPath,out savedCell))throw new IOException("저장 응답을 확인하지 못했습니다.");
                writeLedger=WriteRateLedger.Read(savedRate);if(wroteActivity)AcknowledgeActivity(capturedRoom,response);
                var saved=new CellValue {Value=value,Version=version+1};lock(gate){if(roomNavigation==capturedNavigation&&roomId==capturedRoom&&session==capturedSession&&generation==capturedGeneration&&minimumGeneration<=capturedGeneration&&state.Version(key)<=saved.Version){if(state.Version(key)!=saved.Version||state.Get(key,"")!=saved.Value){state.Cells[key]=saved;CellChanged(key);}SetPath(tree,"cells/"+key.Replace('.','_'),FirebaseHttp.CompactStoredCell(savedCell));sequence++;QueueChanged();}}return saved;
            }catch(UnauthorizedAccessException){writeLedger=null;if(roomNavigation!=capturedNavigation)throw;var current=Database(capturedPath+"/generation","GET",null);long latestGeneration;if(Int64.TryParse(current.Body,out latestGeneration)&&latestGeneration!=capturedGeneration){lock(gate){if(roomNavigation==capturedNavigation){minimumGeneration=Math.Max(minimumGeneration,latestGeneration);sequence++;QueueChanged();}}throw new InvalidOperationException("전체 초기화 이전 입력이 거부됐습니다. 자동 재전송하지 않습니다.");}
                var latest=Database(capturedPath+"/cells/"+key.Replace('.','_'),"GET",null);var data=FirebaseHttp.Json().Deserialize<Dictionary<string,object>>(latest.Body);if(data!=null&&Number(data,"version",0)!=version)throw new ConflictException(FirebaseHttp.Text(data,"value"),(int)Number(data,"version",0));throw new UnauthorizedAccessException("저장이 거부되었습니다. 인증·Firebase 규칙·다른 기기의 동시 입력 여부를 확인한 뒤 해당 칸을 다시 입력하세요.");
            }catch(IOException){writeLedger=null;if(roomNavigation==capturedNavigation){Lost("저장 연결 실패");RestartStream();}throw;}});}
            finally{cellWriteOperation.Release();}
        }
        public void Dispose()
        {
            lock(gate)
            {
                if(stopped)return;
                stopped=true;healthy=false;streamReady=false;lobbyReady=false;streamSerial++;roomNavigation++;
                if(stream!=null)stream.Abort();
            }
            lifetime.Cancel();streamWake.Set();healthTimer.Dispose();
            NetworkChange.NetworkAvailabilityChanged-=NetworkChanged;
        }
    }
}
