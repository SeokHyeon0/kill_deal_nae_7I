using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PubgScore
{
    // Retain the existing atomic write ledger for concurrent-save validation.
    // This ledger does not impose a per-second or per-minute input quota.
    internal sealed class WriteRateLedger
    {
        internal long Sequence;internal readonly long[] Times=new long[60];
        internal static WriteRateLedger Read(object raw)
        {
            var result=new WriteRateLedger();if(raw==null)return result;
            var value=raw as Dictionary<string,object>;object sequence,rawTimes;
            if(value==null||!value.TryGetValue("sequence",out sequence)||!value.TryGetValue("times",out rawTimes))throw new IOException("저장 확인 정보를 확인하지 못했습니다.");
            var times=rawTimes as Dictionary<string,object>;
            if(times==null||times.Count!=60)throw new IOException("저장 확인 기록이 올바르지 않습니다.");
            try {
                result.Sequence=Convert.ToInt64(sequence,CultureInfo.InvariantCulture);
                if(result.Sequence<1||result.Sequence>=9007199254740990L)throw new ArgumentException();
                for(int i=0;i<60;i++) {
                    object timestamp;if(!times.TryGetValue(i.ToString("D2",CultureInfo.InvariantCulture),out timestamp))throw new ArgumentException();
                    long time=Convert.ToInt64(timestamp,CultureInfo.InvariantCulture);
                    if(time<0||(i>0&&time<result.Times[i-1]))throw new ArgumentException();result.Times[i]=time;
                }
            }catch(Exception error){throw new IOException("저장 확인 기록이 올바르지 않습니다.",error);}
            return result;
        }
        internal Dictionary<string,object> Next(string room,string field,int version)
        {
            if(Sequence>=9007199254740989L)throw new InvalidOperationException("저장 확인 기록의 최대 횟수에 도달했습니다.");
            var times=new Dictionary<string,object>();
            for(int i=0;i<59;i++)times[i.ToString("D2",CultureInfo.InvariantCulture)]=Times[i+1];
            times["59"]=new Dictionary<string,object>{{".sv","timestamp"}};
            return new Dictionary<string,object>{{"room",room},{"field",field},{"version",version},{"sequence",Sequence+1},{"times",times}};
        }
    }
}
