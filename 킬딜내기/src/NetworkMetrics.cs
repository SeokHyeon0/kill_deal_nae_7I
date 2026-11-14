using System;
using System.Threading;
namespace PubgScore
{
    // Bounded aggregate diagnostics. No tokens, payloads, histories, or background logging.
    internal static class NetworkMetrics
    {
        internal static long Gets,Writes,SentJsonBytes,ReceivedJsonBytes,SseUtf8Bytes,ActiveStreams,PeakStreams;
        internal static IDisposable Stream()
        {
            long count=Interlocked.Increment(ref ActiveStreams),old;
            do{old=Interlocked.Read(ref PeakStreams);if(old>=count)break;}while(Interlocked.CompareExchange(ref PeakStreams,count,old)!=old);
            return new StreamLease();
        }
        sealed class StreamLease:IDisposable {public void Dispose(){Interlocked.Decrement(ref ActiveStreams);}}
    }
}
