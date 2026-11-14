using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PubgScore
{
    // Small lobby metadata only; this is never an authorization or editing gate.
    internal static class RoomActivity
    {
        internal const long IdleMilliseconds=30L*60*1000;
        internal const int MaximumContent=80+170*179;
        static readonly Regex ContentKey=new Regex(@"\A(t[0-9]\.p[0-3]\.(name|penalty)|t[0-9]\.r([1-9]|[1-9][0-9]|1[0-7][0-9])\.(p[0-3]\.(score|kills|damage|check)|chicken))\z",RegexOptions.Compiled);
        internal static bool IsContentField(string key){return key!=null&&ContentKey.IsMatch(key);}
        internal static int ContentValue(string key,string value)
        {return !IsContentField(key)?0:key.EndsWith(".check",StringComparison.Ordinal)?(value=="1"?1:0):String.IsNullOrWhiteSpace(value)?0:1;}
        internal static int Count(ScoreState state)
        {int count=0;foreach(var cell in state.Cells)count+=ContentValue(cell.Key,cell.Value.Value);return count;}
        internal static bool IsActive(long count,long lastInput,long now)
        {return count>0&&lastInput>0&&now>=lastInput&&now-lastInput<IdleMilliseconds;}
        internal static object Counter(long? previousCount,int currentCount,int delta)
        {return previousCount.HasValue?(object)new Dictionary<string,object>{{".sv",new Dictionary<string,object>{{"increment",delta}}}}:currentCount+delta;}
        internal static long? Integer(object value,long maximum)
        {
            if(!(value is int||value is long||value is decimal||value is double))return null;
            decimal number;try{number=Convert.ToDecimal(value,CultureInfo.InvariantCulture);}catch(Exception){return null;}
            return number>=0&&number<=maximum&&Decimal.Truncate(number)==number?(long?)number:null;
        }
        internal static void Read(RoomInfo room,object raw)
        {
            var row=FirebaseHttp.Object(raw);object count,at;row.TryGetValue("contentCount",out count);row.TryGetValue("at",out at);
            room.ContentCount=Integer(count,MaximumContent);room.LastInputAt=Integer(at,9007199254740989L)??0;room.ActivityActive=false;
        }
    }
    // HTTP Date is supplied by the existing connection. No time endpoint or heartbeat is used.
    internal sealed class ServerClock
    {
        readonly object gate=new object();readonly Stopwatch elapsed=Stopwatch.StartNew();bool observed;
        long anchor=(long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;
        internal void Observe(string httpDate)
        {
            DateTimeOffset parsed;if(!DateTimeOffset.TryParse(httpDate,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out parsed))return;
            // Date has one-second precision. Its upper bound avoids making a fresh server timestamp appear future-dated.
            lock(gate){long received=(long)(parsed.UtcDateTime-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds+999;anchor=observed?Math.Max(received,anchor+elapsed.ElapsedMilliseconds):received;observed=true;elapsed.Restart();}
        }
        internal long NowMilliseconds {get{lock(gate)return anchor+elapsed.ElapsedMilliseconds;}}
    }
    public sealed partial class FirebaseGameClient
    {
        readonly ServerClock activityClock=new ServerClock();
        readonly HashSet<string> acknowledgedActivity=new HashSet<string>(StringComparer.Ordinal);
        long? ActivityCounter(string room)
        {RoomInfo info;return rooms.TryGetValue(room,out info)&&info.ContentCount.HasValue?info.ContentCount:acknowledgedActivity.Contains(room)?(long?)0:null;}
        // The lobby isn't subscribed while playing. An older room can get its
        // first counter from another writer after we read its entry on joining.
        void RefreshMissingActivityCounter(string room)
        {
            int navigation;
            lock(gate)
            {
                if(stopped||roomId!=room||ActivityCounter(room).HasValue)return;
                navigation=roomNavigation;
            }
            var reply=Database("spark/lobby/"+room+"/activity","GET",null,2000,()=>!stopped&&roomNavigation==navigation);
            if(reply.Status!=200)throw new System.IO.IOException("사용 상태를 읽지 못했습니다.");
            var current=new RoomInfo();
            RoomActivity.Read(current,FirebaseHttp.Json().DeserializeObject(reply.Body));
            if(current.ContentCount.HasValue)
                lock(gate){if(!stopped&&roomNavigation==navigation)acknowledgedActivity.Add(room);}
        }
        Dictionary<string,object> ActivityPayload(string room,string key,int version,int delta,int initialCount)
        {return new Dictionary<string,object>{{"contentCount",RoomActivity.Counter(ActivityCounter(room),initialCount,delta)},{"at",Timestamp()},{"writerUid",auth.Uid},{"field",key.Replace('.','_')},{"version",version}};}
        bool AddCellActivity(Dictionary<string,object> patch,string room,string key,string value,int version)
        {
            if(!RoomActivity.IsContentField(key))return false;
            RefreshMissingActivityCounter(room);
            lock(gate){string old=state.Get(key,key.EndsWith(".check",StringComparison.Ordinal)?"0":"");
                if(state.Version(key)!=version)throw new ConflictException(old,state.Version(key));
                if(old==value)return false;
                int delta=RoomActivity.ContentValue(key,value)-RoomActivity.ContentValue(key,old);
                patch["lobby/"+room+"/activity"]=ActivityPayload(room,key,version+1,delta,ActivityCounter(room).HasValue?0:RoomActivity.Count(state));return true;}
        }
        void AcknowledgeActivity(string room,Dictionary<string,object> response)
        {
            object raw;if(!FirebaseHttp.TryResponseObject(response,"lobby/"+room+"/activity",out raw))throw new System.IO.IOException("사용 상태 저장 응답을 확인하지 못했습니다.");
            var parsed=new RoomInfo();RoomActivity.Read(parsed,raw);if(!parsed.ContentCount.HasValue)throw new System.IO.IOException("사용 상태 저장 응답이 올바르지 않습니다.");
            lock(gate)acknowledgedActivity.Add(room);
        }
        bool RefreshRoomActivity()
        {
            lock(gate){bool changed=false;long now=activityClock.NowMilliseconds;
                foreach(var room in rooms.Values){bool active=RoomActivity.IsActive(room.ContentCount??0,room.LastInputAt,now);
                    if(room.ActivityActive!=active){room.ActivityActive=active;changed=true;}}
                if(changed)lobbySequence++;return changed;}
        }
    }
}
