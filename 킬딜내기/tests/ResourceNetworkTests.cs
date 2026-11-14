using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using PubgScore;

// Local, deterministic checks. Never starts authentication or an HTTP/SSE connection.
class ResourceNetworkTests
{
    static int checks;
    static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool passed,string name)
    {
        if(!passed)throw new Exception(name);
        checks++;Console.WriteLine("PASS "+name);
    }
    static object Field(object value,string name)
    {return value.GetType().GetField(name,Fields).GetValue(value);}
    static void Set(object value,string name,object data)
    {value.GetType().GetField(name,Fields).SetValue(value,data);}

    static int Main()
    {
        try {
            var small=new StringBuilder(1024).Append("an ordinary score update");
            var reused=FirebaseHttp.ResetEventBuffer(small);
            Check(Object.ReferenceEquals(small,reused)&&reused.Length==0,"small SSE buffer is cleared and reused");

            var boundary=new StringBuilder(64*1024).Append("boundary");
            Check(Object.ReferenceEquals(boundary,FirebaseHttp.ResetEventBuffer(boundary)),"64 KB capacity remains reusable");

            var large=new StringBuilder(64*1024+1).Append('x',64*1024+1);
            var released=FirebaseHttp.ResetEventBuffer(large);
            Check(!Object.ReferenceEquals(large,released)&&released.Length==0&&released.Capacity<=64*1024,"large SSE buffer is replaced after processing");
            released.Append("next event");
            Check(released.ToString()=="next event","replacement starts the next event without previous payload");

            var normal=new GameView();
            Check(normal.State!=null&&normal.State.Cells!=null,"default GameView retains non-null state compatibility");
            Check(new GameView(false).State==null,"metadata GameView skips state allocation");

            var config=new FirebaseConfig {
                DatabaseUrl="http://127.0.0.1:9000",ApiKey="demo",ProjectId="demo-resource-tests",
                EmulatorMode=true,AuthEmulatorUrl="http://127.0.0.1:9099"
            };
            using(var client=new FirebaseGameClient(config)) {
                Check(client.Status().State==null,"Status remains a metadata-only view");
                var rooms=(Dictionary<string,RoomInfo>)Field(client,"rooms");
                rooms.Add("z",new RoomInfo {Id="z",Name="Z"});
                rooms.Add("a",new RoomInfo {Id="a",Name="A"});
                Set(client,"lobbyReady",true);Set(client,"lobbySequence",17L);Set(client,"lobbyError","local marker");
                ((Stopwatch)Field(client,"lobbyProof")).Start();

                var full=client.Lobby();
                var metadata=client.Lobby(false);
                Check(full.Rooms.Count==2&&full.Rooms[0].Name=="A"&&full.Rooms[1].Name=="Z","default lobby returns the original sorted snapshot");
                Check(metadata.Rooms.Count==0,"metadata lobby skips room list construction");
                Check(metadata.Ready==full.Ready&&metadata.Connected==full.Connected&&metadata.Sequence==full.Sequence&&metadata.Error==full.Error,"metadata lobby preserves readiness, health, sequence and error");
                Check(client.Lobby(true).Rooms.Count==2&&rooms.Count==2,"metadata lookup leaves the next full lobby intact");
            }
            Console.WriteLine("PASS "+checks+" resource network checks");return 0;
        } catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
}
