using System;
using System.Collections.Generic;
using System.Linq;

namespace PubgScore
{
    // The room catalog contains Firebase data only. No default IDs, names, or creation writes.
    internal static class SharedRooms
    {
        internal const string Owner="__shared_bj__";
        internal static List<RoomInfo> Project(IEnumerable<RoomInfo> source)
        {
            var result=new List<RoomInfo>();
            foreach(var room in source.Where(r=>r!=null).OrderBy(r=>r.Name,StringComparer.Ordinal).ThenBy(r=>r.Id,StringComparer.Ordinal)) {
                string error=LobbyEntryError(room);
                result.Add(new RoomInfo {Id=room.Id,Name=String.IsNullOrWhiteSpace(room.Name)?"이름 없는 방":room.Name,
                    DatabaseName=room.Name,OwnerUid=room.OwnerUid,Status=room.Status,IsShared=room.OwnerUid==Owner,
                    ContentCount=room.ContentCount,LastInputAt=room.LastInputAt,ActivityActive=room.ActivityActive,
                    Available=room.Available&&error.Length==0,PreparationError=error});
            }
            return result;
        }
        internal static bool IsValidRoomId(string id)
        {return id!=null&&System.Text.RegularExpressions.Regex.IsMatch(id,"\\A[a-f0-9]{32}\\z");}
        static bool ValidName(string name)
        {return !String.IsNullOrWhiteSpace(name)&&name.Length<=60&&!name.Any(Char.IsControl);}
        static string LobbyEntryError(RoomInfo room)
        {
            if(!String.IsNullOrEmpty(room.PreparationError))return room.PreparationError;
            if(!IsValidRoomId(room.Id)||!ValidName(room.Name)||String.IsNullOrWhiteSpace(room.OwnerUid))
                return "방 목록의 ID·이름·관리자 정보를 확인하세요.";
            return "";
        }
        internal static RoomInfo ReadLobbyRoom(string id,object raw)
        {
            var row=FirebaseHttp.Object(raw);
            var room=new RoomInfo {Id=id,Name=FirebaseHttp.Text(row,"name"),OwnerUid=FirebaseHttp.Text(row,"ownerUid"),
                Status=Object.Equals(Get(row,"running"),true)?"running":"waiting"};
            room.PreparationError=!(Get(row,"name") is string)||!(Get(row,"ownerUid") is string)?"방 목록의 이름·관리자는 문자열이어야 합니다.":LobbyEntryError(room);
            if(room.PreparationError.Length==0&&(!(Get(row,"running") is bool)||!WholeNumber(Get(row,"session"),0,9007199254740989L)))
                room.PreparationError="방 목록의 running·session 정보를 확인하세요.";
            RoomActivity.Read(room,Get(row,"activity"));
            room.Available=room.PreparationError.Length==0;
            return room;
        }
        static bool WholeNumber(object value,long minimum,long maximum)
        {return value is int?(int)value>=minimum&&(int)value<=maximum:value is long?(long)value>=minimum&&(long)value<=maximum:value is decimal?(decimal)value>=minimum&&(decimal)value<=maximum&&Decimal.Truncate((decimal)value)==(decimal)value:value is double?(double)value>=minimum&&(double)value<=maximum&&Math.Floor((double)value)==(double)value:false;}
        static object Get(Dictionary<string,object> data,string key)
        {object value;return data.TryGetValue(key,out value)?value:null;}
        internal static void ValidateExisting(RoomInfo listed,object gameRaw,Dictionary<string,object> lobby)
        {
            if(!(gameRaw is Dictionary<string,object>))throw new InvalidOperationException("방 목록과 실제 데이터가 일치하지 않습니다. Firebase 방 정보를 확인하세요.");
            var game=FirebaseHttp.Object(gameRaw);var metadata=FirebaseHttp.Object(Get(game,"room"));var control=FirebaseHttp.Object(Get(game,"control"));
            if(!listed.Available||LobbyEntryError(listed).Length>0||!(Get(lobby,"name") is string)||!(Get(lobby,"ownerUid") is string)||
                !(Get(metadata,"name") is string)||!(Get(metadata,"ownerUid") is string)||FirebaseHttp.Text(lobby,"name")!=listed.Name||
                FirebaseHttp.Text(lobby,"ownerUid")!=listed.OwnerUid||FirebaseHttp.Text(metadata,"name")!=listed.Name||
                FirebaseHttp.Text(metadata,"ownerUid")!=listed.OwnerUid||!(Get(control,"running") is bool)||!(Get(lobby,"running") is bool)||
                !Object.Equals(Get(control,"running"),Get(lobby,"running"))||!WholeNumber(Get(control,"session"),0,9007199254740989L)||
                !WholeNumber(Get(lobby,"session"),0,9007199254740989L)||Convert.ToInt64(Get(control,"session"))!=Convert.ToInt64(Get(lobby,"session"))||
                !WholeNumber(Get(game,"generation"),0,9007199254740989L)||!WholeNumber(Get(game,"roundCount"),50,179))
                throw new InvalidOperationException("방 목록과 실제 방 정보가 서로 다릅니다. Firebase 방 정보를 확인하세요.");
        }
    }
}
