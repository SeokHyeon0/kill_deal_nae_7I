using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using PubgScore;

class ResultExportTests
{
    const int ExportScale=2;
    static int count;
    static readonly DateTime CapturedAt = new DateTime(2026,10,11,1,20,55);
    static void Check(bool value,string label)
    {
        if(!value)throw new Exception(label);
        Console.WriteLine("PASS "+label);count++;
    }
    static void Put(ScoreState s,string key,string value){s.Cells[key]=new CellValue {Value=value,Version=1};}
    static Dictionary<string,object> Map(params object[] v)
    {
        var d=new Dictionary<string,object>();for(int i=0;i<v.Length;i+=2)d[(string)v[i]]=v[i+1];return d;
    }
    static void Rejects<T>(Action action,string label) where T:Exception
    {
        try{action();}catch(T){Check(true,label);return;}
        throw new Exception(label+" did not throw "+typeof(T).Name);
    }
    static bool SameCard(Bitmap actual,int column,Bitmap expected)
    {
        int left=1+column*184;
        for(int y=23*ExportScale;y<162*ExportScale;y++)
        for(int x=(left+1)*ExportScale;x<(left+183)*ExportScale;x++)
            if(actual.GetPixel(x,y).ToArgb()!=expected.GetPixel(x,y).ToArgb())return false;
        return true;
    }
    static Color ReferencePixel(Bitmap image,int x,int y)
    {
        return image.GetPixel(x*ExportScale,y*ExportScale);
    }
    static ScoreState TeamInSlot(ScoreState source,int team,int slot)
    {
        var target=new ScoreState();string prefix="t"+team+".";
        foreach(var pair in source.Cells)
            if(pair.Key.StartsWith(prefix,StringComparison.Ordinal)||pair.Key=="roundCount")
                target.Cells[pair.Key]=new CellValue {Value=pair.Value.Value,Version=pair.Value.Version};
        // Keep each team in the same column so the right frame's one-pixel inset also matches.
        var otherTeams=Enumerable.Range(0,team).Take(slot)
            .Concat(Enumerable.Range(team+1,ScoreRules.TeamCount-team-1).Take(2-slot));
        foreach(int otherTeam in otherTeams)Put(target,"t"+otherTeam+".p0.name","비교팀");
        return target;
    }
    static bool IsDivider(Color color)
    {
        return color.R>=170&&color.R<=240&&Math.Abs(color.R-color.G)<=16&&Math.Abs(color.G-color.B)<=16;
    }
    static ScoreState Fixture()
    {
        var s=new ScoreState();
        for(int t=0;t<ScoreRules.TeamCount;t++)
        {
            decimal expected=0;
            for(int p=0;p<4;p++)
            {
                string prefix="t"+t;int score=190+t*45+p*20,penalty=400-p*100;
                Put(s,prefix+".p"+p+".name","선수"+(t+1)+"-"+(p+1));
                Put(s,prefix+".p"+p+".penalty",penalty.ToString());
                Put(s,prefix+".r1.p"+p+".score",score.ToString());
                Put(s,prefix+".r1.p"+p+".check","1");
                Put(s,prefix+".r50.p"+p+".score","10.5");
                expected+=score-penalty+10.5m;
            }
            Put(s,"t"+t+".r50.chicken","300");expected+=300;
            Check(ScoreMath.Calculate(s,t).Total==expected,"all rounds and checked deductions accumulated team "+(t+1));
        }
        return s;
    }
    static void WriteExample(string directory)
    {
        var state=new ScoreState();
        string[,] names={{"지천","유난","혜주","기장"},{"필두","바앙","폴린","밥밥"}};
        int[,] raw={{10,3,0,4},{0,4,10,2}};
        int[,] deductionRounds={{2,6,5,4},{3,2,2,2}};
        for(int team=0;team<2;team++)for(int player=0;player<4;player++)
        {
            string prefix="t"+team;
            Put(state,prefix+".p"+player+".name",names[team,player]);
            Put(state,prefix+".p"+player+".penalty",(4-player).ToString());
            Put(state,prefix+".r1.p"+player+".kills",raw[team,player].ToString());
            for(int round=1;round<=deductionRounds[team,player];round++)
                Put(state,prefix+".r"+round+".p"+player+".check","1");
        }
        Put(state,"t1.r1.chicken","5");
        Check(ScoreMath.Calculate(state,0,"killdeal").Total==-23&&ScoreMath.Calculate(state,1,"killdeal").Total==-3,"example uses calculated negative team totals");
        using(var image=ResultImage.Render(state,"남뚱 킬내기","killdeal",CapturedAt))
        {
            Check(image.Size==new Size(742,334),"two-team result has twice the reference pixel density");
            Check(ReferencePixel(image,1,45).R<50&&ReferencePixel(image,368,45).R<50&&
                ReferencePixel(image,5,2).R<50&&ReferencePixel(image,5,163).R<50,"outer frame matches the reference position");
            Check(Enumerable.Range(1,7).All(row=>IsDivider(ReferencePixel(image,7,2+row*20))),
                "title, headings, totals, players and chicken use the reference 20px rows");
            Check(IsDivider(ReferencePixel(image,185,45))&&IsDivider(ReferencePixel(image,47,65))&&IsDivider(ReferencePixel(image,231,65)),
                "team and player-name dividers match the reference columns");
            image.Save(Path.Combine(directory,"result-copy-preview.png"),ImageFormat.Png);
        }
    }
    [STAThread] static int Main(string[] args)
    {
        var full=Fixture();var serializer=new JavaScriptSerializer();
        string before=serializer.Serialize(full);
        Check(ResultImage.Title("남뚱 킬내기","killdeal")=="남뚱 킬딜내기","title replaces the prior contest suffix with the selected mode");
        Check(ResultImage.Title("남뚱 킬딜내기","killdeal")=="남뚱 킬딜내기","title does not repeat the selected mode");
        Check(ResultImage.Title("남뚱","damage")=="남뚱 딜내기","title uses room name and contest name");
        Check(ResultImage.ActiveTeams(full).SequenceEqual(Enumerable.Range(0,10)),"all populated teams remain in original order");
        foreach(int active in new[]{1,2,5,6,10})
        {
            var state=full.Copy();
            for(int t=active;t<10;t++)for(int p=0;p<4;p++)Put(state,"t"+t+".p"+p+".name","");
            using(var image=ResultImage.Render(state,"남뚱 킬내기","damage",CapturedAt))
            {
                int expectedWidth=Math.Min(active,5)*184+3;
                int expectedHeight=27+((active+4)/5)*140;
                Check(image.Width==expectedWidth*ExportScale&&image.Height==expectedHeight*ExportScale,active+" active teams use reference-sized cells at twice the pixel density");
                Check(image.GetPixel(0,0).A==255,"opaque result background for "+active+" teams");
                if(active==6)Check(ReferencePixel(image,186,165).ToArgb()==Color.White.ToArgb()&&ReferencePixel(image,367,200).ToArgb()==Color.White.ToArgb(),"six teams leave no seventh card or border");
            }
        }
        Check(before==serializer.Serialize(full),"rendering does not modify input score data");

        var blank=new ScoreState();
        Put(blank,"t0.p0.name"," \t\r\n");Put(blank,"t1.p2.name","\u3000");
        Put(blank,"t2.name","이름만 남은 팀");Put(blank,"t3.p0.penalty","400");
        Put(blank,"t4.r50.p0.score","999");Put(blank,"t4.r50.chicken","10");
        Check(ResultImage.ActiveTeams(blank).Length==0,"blank whitespace team-label penalty and score-only teams excluded");
        Rejects<InvalidOperationException>(delegate{using(var image=ResultImage.Render(blank,"빈 방","match",CapturedAt)){}},"empty result is rejected before copying");
        Rejects<ArgumentNullException>(delegate{ResultImage.ActiveTeams(null);},"null active-team source rejected");
        Rejects<ArgumentNullException>(delegate{using(var image=ResultImage.Render(null,"방","match",CapturedAt)){}},"null render source rejected");
        Rejects<ArgumentNullException>(delegate{ResultImage.CopyToClipboard(null);},"null clipboard image rejected before clipboard access");

        var sparse=full.Copy();
        for(int t=0;t<10;t++)if(t!=0&&t!=4&&t!=9)
            for(int p=0;p<4;p++)Put(sparse,"t"+t+".p"+p+".name","");
        Put(sparse,"t4.p0.name","");
        Check(ResultImage.ActiveTeams(sparse).SequenceEqual(new[]{0,4,9}),"nonconsecutive teams and named second-tier player included");
        before=serializer.Serialize(sparse);
        using(var actual=ResultImage.Render(sparse,"남뚱 킬내기","kills",CapturedAt))
        {
            Check(actual.Width==1110&&actual.Height==334,"three sparse teams have no empty card slots");
            int i=0;
            foreach(int team in new[]{0,4,9})
                using(var expected=ResultImage.Render(TeamInSlot(sparse,team,i),"남뚱 킬내기","kills",CapturedAt))
                    Check(SameCard(actual,i++,expected),"sparse card preserves original team "+(team+1)+" label scores deductions and last round");
            if(args.Contains("--emit-fixture"))
                using(var stream=new MemoryStream())
                {
                    actual.Save(stream,ImageFormat.Png);
                    Console.WriteLine("RESULT_FIXTURE_PNG "+Convert.ToBase64String(stream.ToArray()));
                }
        }
        Check(before==serializer.Serialize(sparse),"filtering and rendering do not change sparse score snapshot");

        var config=new FirebaseConfig {DatabaseUrl="http://127.0.0.1:9000",ApiKey="demo",ProjectId="demo-export",AuthDomain="demo",EmulatorMode=true,AuthEmulatorUrl="http://127.0.0.1:9099"};
        using(var client=new FirebaseGameClient(config))
        {
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            typeof(FirebaseGameClient).GetField("roomId",flags).SetValue(client,new string('a',32));
            var tree=Map("generation",0,"roundCount",50,"control",Map("session",1,"running",true),"room",Map("name","최종 방","ownerUid","local"),"cells",Map(
                "t9_r50_p3_score",Map("value",123.5,"version",1,"generation",0),
                "t9_p3_name",Map("value","마지막 선수","version",1,"generation",0)));
            var method=typeof(FirebaseGameClient).GetMethod("ApplyEvent",flags);
            method.Invoke(client,new object[]{"put",serializer.Serialize(Map("path","/","data",tree)),0});
            Check(client.LastResult.State.GetDecimal("t9.r50.p3.score",0)==123.5m,"capture receives tenth team final round");
            method.Invoke(client,new object[]{"put","{\"path\":\"/\",\"data\":null}",0});
            Check(client.LastResult.State.GetDecimal("t9.r50.p3.score",0)==123.5m,"result retained after room deletion event");
            client.LeaveRoom();Check(client.LastResult.RoomName=="최종 방","result retained in lobby");
            using(var image=ResultImage.Render(client.LastResult.State,client.LastResult.RoomName,"match",CapturedAt))
                Check(image.Width==374&&image.Height==334,"retained snapshot still renders only its populated team");
            var copy=client.LastResult;copy.State.Cells.Clear();
            Check(client.LastResult.State.Cells.Count>0,"retained snapshot isolated from consumer edits");
            var snapshot=client.LastResult;var isolated=snapshot.Copy();isolated.State.Cells.Clear();
            Check(snapshot.State.Cells.Count>0,"ResultSnapshot.Copy detaches score cells");
        }
        if(args.Length>0&&Directory.Exists(args[0]))WriteExample(args[0]);
        Console.WriteLine("PASS total "+count);return 0;
    }
}
