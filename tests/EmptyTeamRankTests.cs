using System;using System.IO;using System.Linq;using System.Reflection;using System.Windows.Forms;using PubgScore;
class EmptyTeamRankTests {
 static int count;static void Check(bool b){if(!b)throw new Exception("Check failed: "+(count+1));count++;}
 static T Field<T>(object o,string n){return (T)o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
 [STAThread]static void Main(){Application.EnableVisualStyles();var client=new TestGameClient(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));
 typeof(TestGameClient).GetField("empty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(client,new ScoreState());
 client.Write("t0.p0.name","말랑",0,0).Wait();client.Write("t1.p0.name","오구",0,0).Wait();client.Write("t0.r1.chicken","-5",0,0).Wait();
 using(var f=new MainForm(false,client)){f.Show();Field<Button[]>(f,"teamTabs")[0].PerformClick();Application.DoEvents();
 Action refresh=()=>{typeof(MainForm).GetMethod("RefreshView",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(f,null);Application.DoEvents();};
 var rows=Field<Label[,]>(f,"rankCells");Check(rows[0,1].Text=="오구팀"&&rows[0,0].Text=="1등");Check(rows[1,1].Text=="말랑팀"&&rows[1,0].Text=="2등");Check(Field<Label>(f,"summaryRank").Text=="2등");
 Check(Enumerable.Range(2,8).All(i=>rows[i,0].Text==""&&(int)rows[i,1].Tag==i));
 client.Write("t7.r50.p0.score","999",0,0).Wait();refresh();Check((int)rows[7,1].Tag==7&&rows[7,0].Text=="");Check(ScoreMath.Calculate(client.View().State,7).Total==999);Check(rows[7,2].Text=="999");
 client.Write("t1.r1.chicken","-5",0,0).Wait();refresh();Check(rows[0,0].Text=="1등"&&rows[1,0].Text=="1등");
 client.Write("t0.p0.name","",1,0).Wait();client.Write("t1.p0.name","  ",1,0).Wait();refresh();Check(Enumerable.Range(0,10).All(i=>rows[i,0].Text==""));Check(Field<Label>(f,"summaryRank").Text=="");
 client.Write("t3.p1.name","선수",0,0).Wait();refresh();Check((int)rows[0,1].Tag==3&&rows[0,0].Text=="1등");
 Action<string,string> write=(key,value)=>client.Write(key,value,client.View().State.Version(key),0).Wait();
 write("t7.r50.p0.score","");
 for(int t=0;t<10;t++){write("t"+t+".r1.chicken","");write("t"+t+".r1.p0.score",(10-t).ToString());}
 for(int players=1;players<=4;players++)for(int teams=0;teams<=10;teams++){
  for(int t=0;t<10;t++)for(int p=0;p<4;p++)write("t"+t+".p"+p+".name",t<teams&&p<players?"player"+t+"-"+p:"");
  refresh();
  Check(Enumerable.Range(0,10).Count(i=>rows[i,0].Text.Length>0)==teams);
  Check(Enumerable.Range(0,teams).All(i=>(int)rows[i,1].Tag==i&&rows[i,0].Text==(i+1)+"등"));
  Check(Enumerable.Range(teams,10-teams).All(i=>(int)rows[i,1].Tag==i&&rows[i,0].Text==""));
  Check(Field<Label>(f,"summaryRank").Text==(teams==0?"":"1등"));
 }
 }Console.WriteLine("PASS "+count+" empty-team ranking checks");}
}
