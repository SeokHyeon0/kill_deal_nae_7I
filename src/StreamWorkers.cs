using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

namespace PubgScore
{
    public sealed partial class FirebaseGameClient
    {
        readonly AutoResetEvent streamWake=new AutoResetEvent(false);
        readonly SemaphoreSlim streamSlot;
        readonly Random retryRandom=new Random(Guid.NewGuid().GetHashCode());
        int RetryDelay(int failures){lock(retryRandom)return Math.Min(30000,500*(1<<Math.Min(6,Math.Max(0,failures-1))))+retryRandom.Next(0,251);}

        void NetworkChanged(object sender,NetworkAvailabilityEventArgs e)
        {
            lock(gate)
            {
                if(stopped)return;
                if(!e.IsAvailable)info="네트워크 연결 끊김";
                if(started)RestartStream();
            }
        }

        void RestartStream()
        {
            lock(gate)
            {
                if(stopped)return;
                streamSerial++;
                if(stream!=null)stream.Abort();
                streamReady=false;healthy=false;proof.Reset();
                lobbyReady=false;lobbyProof.Reset();
                if(roomId.Length==0)
                {
                    rooms.Clear();lobbySequence++;
                    lobbyError="방 목록을 불러오는 중…";
                }
                sequence++;QueueChanged();
            }
            streamWake.Set();
        }

        // Only this worker opens an SSE response. Every route change and retry
        // waits for the previous response to be disposed by ReadStream.
        void RunStreamWorker()
        {
            int previousSerial=-1,failures=0;
            while(true)
            {
                int serial;
                string room;
                bool paused;
                lock(gate)
                {
                    if(stopped)return;
                    serial=streamSerial;room=roomId;
                    paused=roomUnavailable;
                }
                if(paused){streamWake.WaitOne();continue;}
                if(previousSerial!=serial){previousSerial=serial;failures=0;}
                bool receivedData=false,checkRoom=false;
                try{ReadStream(serial,room,ref receivedData,ref checkRoom);}
                catch(Exception error)
                {
                    if(checkRoom&&room.Length>0&&IsCurrentStream(serial))ConfirmRoomAvailability(room,serial);
                    lock(gate)
                    {
                        if(stopped||serial!=streamSerial||roomUnavailable)continue;
                        if(room.Length==0)
                        {
                            lobbyReady=false;lobbySequence++;
                            lobbyError="방 목록 연결 오류: "+((error is IOException||error is UnauthorizedAccessException)?error.Message:"응답 처리 실패 · 다시 연결해 주세요.");
                        }
                        else streamReady=false;
                        Lost("실시간 연결 끊김");
                    }
                }
                lock(gate){if(stopped||serial!=streamSerial||roomUnavailable)continue;}
                if(receivedData)failures=0;
                streamWake.WaitOne(RetryDelay(++failures));
            }
        }

        bool IsCurrentStream(int serial)
        {
            lock(gate)return !stopped&&serial==streamSerial&&!roomUnavailable;
        }

        void ReadStream(int serial,string room,ref bool receivedData,ref bool checkRoom)
        {
            bool inLobby=room.Length==0;
            var path=inLobby?"spark/lobby":"spark/rooms/"+room+"/game";
            auth.Ensure();
            var request=FirebaseHttp.Request(Url(path),"GET",8000);
            request.Accept="text/event-stream";request.ReadWriteTimeout=45000;
            var lobbySnapshot=inLobby?new Dictionary<string,object>():null;
            streamSlot.Wait(lifetime.Token);
            try
            {
                lock(gate)
                {
                    if(stopped||serial!=streamSerial)return;
                    stream=request;
                }
                using(NetworkMetrics.Stream())
                using(var response=(HttpWebResponse)request.GetResponse())
                using(var reader=new StreamReader(response.GetResponseStream()))
                {
                    activityClock.Observe(response.Headers["Date"]);
                    string kind="",line;
                    var data=new StringBuilder();
                    while(IsCurrentStream(serial)&&(line=reader.ReadLine())!=null)
                    {
                        Interlocked.Add(ref NetworkMetrics.SseUtf8Bytes,Encoding.UTF8.GetByteCount(line)+1);
                        lock(gate)
                        {
                            if(stopped||serial!=streamSerial)return;
                            if(inLobby)lobbyProof.Restart();else proof.Restart();
                        }
                        if(line.Length==0)
                        {
                            if(kind=="auth_revoked")
                            {
                                auth.RequireRefresh();
                                throw new IOException("인증을 다시 확인하세요.");
                            }
                            if(kind=="cancel")
                            {
                                checkRoom=true;
                                throw new UnauthorizedAccessException("Firebase 실시간 연결이 거부되었습니다.");
                            }
                            if((kind=="put"||kind=="patch")&&data.Length>0)
                            {
                                if(inLobby)ApplyLobbyEvent(kind,data.ToString(),serial,ref lobbySnapshot);
                                else ApplyEvent(kind,data.ToString(),serial);
                                receivedData=true;
                            }
                            kind="";data=FirebaseHttp.ResetEventBuffer(data);continue;
                        }
                        if(line.StartsWith("event:"))kind=line.Substring(6).Trim();
                        if(line.StartsWith("data:"))
                        {
                            data.Append(line.Substring(5).TrimStart());
                            if(data.Length>(inLobby?4:16)*1024*1024)throw new IOException("응답 크기 초과");
                        }
                    }
                    if(IsCurrentStream(serial))throw new IOException("실시간 연결이 끊겼습니다. 다시 시도하세요.");
                }
            }
            catch(WebException error)
            {
                using(var response=error.Response as HttpWebResponse)
                {
                    if(response!=null)
                    {
                        int status=(int)response.StatusCode;
                        checkRoom=status==401||status==403||status==404;
                    }
                    throw new IOException(DescribeStreamFailure(error),error);
                }
            }
            finally
            {
                request.Abort();
                lock(gate){if(ReferenceEquals(stream,request))stream=null;}
                streamSlot.Release();
            }
        }

        void ConfirmRoomAvailability(string room,int serial)
        {
            try
            {
                var reply=Database("spark/lobby/"+room,"GET",null,8000,()=>IsCurrentStream(serial));
                if(reply.Status==200&&FirebaseHttp.Json().DeserializeObject(reply.Body)==null)
                    MarkRoomUnavailable(serial);
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
            catch(InvalidOperationException){}
            catch(ArgumentException){}
        }

        void MarkRoomUnavailable(int serial)
        {
            lock(gate)
            {
                if(stopped||serial!=streamSerial||roomId.Length==0||roomUnavailable)return;
                FreezeResult();roomUnavailable=true;streamReady=false;healthy=false;
                sequence++;QueueChanged();
            }
        }
    }
}
