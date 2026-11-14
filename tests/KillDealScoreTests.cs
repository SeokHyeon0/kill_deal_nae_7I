using System;
using System.Globalization;
using System.IO;
using PubgScore;

internal static class KillDealScoreTests
{
    static int assertions;
    static void Equal<T>(T expected,T actual,string description)
    {
        assertions++;
        if(!Object.Equals(expected,actual))throw new Exception(description+": expected "+expected+", actual "+actual);
    }
    static void Reject(Action action,string description)
    {
        assertions++;
        try{action();}catch(ArgumentException){return;}
        throw new Exception(description+": invalid input accepted");
    }
    static void Put(ScoreState state,string key,string value){state.Cells[key]=new CellValue {Value=value,Version=1};}
    static void Main(string[] args)
    {
        string dir=Path.GetFullPath(args[0]);Directory.CreateDirectory(dir);
        var state=new ScoreState();
        int[] penalties={4,3,2,1};
        int[,] kills={{3,1,0,2},{0,2,1,0}};
        string[,] damage={{"199","250","99","100"},{"60","99","299.99","250"}};
        int[,] checks={{1,0,1,1},{0,1,0,1}};
        for(int player=0;player<4;player++){
            Put(state,"t0.p"+player+".name","P"+player);
            Put(state,"t0.p"+player+".penalty",penalties[player].ToString(CultureInfo.InvariantCulture));
            for(int round=1;round<=2;round++){
                string prefix="t0.r"+round+".p"+player;
                Put(state,prefix+".kills",kills[round-1,player].ToString(CultureInfo.InvariantCulture));
                Put(state,prefix+".damage",damage[round-1,player]);
                Put(state,prefix+".check",checks[round-1,player].ToString(CultureInfo.InvariantCulture));
            }
        }
        Put(state,"t0.r1.chicken","8");Put(state,"t0.r2.chicken","5");
        Put(state,"t0.r1.p0.score","700");Put(state,"t0.r2.p0.score","50");Put(state,"t0.r1.p1.score","11");
        var sum=ScoreMath.Calculate(state,0,"killdeal");
        Equal(11m,sum.RoundTotals[0],"round1 kill/damage, deductions, chicken");
        Equal(8m,sum.RoundTotals[1],"round2 kill/damage, deductions, chicken");
        Equal(19m,sum.Total,"combined net result");
        Equal(13m,sum.ChickenSum,"chicken sum");
        decimal[] raw={4m,5m,3m,5m},deduction={4m,3m,2m,2m},net={0m,2m,1m,3m};
        for(int p=0;p<4;p++){Equal(raw[p],sum.Raw[p],"raw player"+p);Equal(deduction[p],sum.Deductions[p],"deduction player"+p);Equal(net[p],sum.PlayerTotals[p],"net player"+p);}
        Equal(4m,ScoreMath.RawScore(state,0,1,0,"killdeal"),"3 kills and199 damage");
        Equal(3m,ScoreMath.RawScore(state,0,2,2,"killdeal"),"1 kill and299.99 damage");
        Put(state,"t0.r3.p0.damage","60");Put(state,"t0.r4.p0.damage","60");
        Equal(19m,ScoreMath.Calculate(state,0,"killdeal").Total,"60 damage does not carry across rounds");
        Put(state,"t0.r3.p0.damage","99.99999999999999999999999999");
        Equal(0m,ScoreMath.RawScore(state,0,3,0,"killdeal"),"below100 stays0 at decimal precision");
        Put(state,"t0.r3.p0.damage","100");
        Equal(1m,ScoreMath.RawScore(state,0,3,0,"killdeal"),"exact100 becomes1");
        foreach(string mode in new[]{"match","kills","kill","damage",""})
            Equal(763m,ScoreMath.Calculate(state,0,mode).Total,"legacy scores unchanged for "+mode);
        Equal(763m,ScoreMath.Calculate(state,0).Total,"existing two-argument calculation");
        Equal("700",state.Get("t0.r1.p0.score"),"legacy input preserved after calculations");
        Equal("3",state.Get("t0.r1.p0.kills"),"kills input preserved after switching calculations");
        Equal("199",state.Get("t0.r1.p0.damage"),"damage input preserved after switching calculations");
        Equal(0m,ScoreMath.RawScore(new ScoreState(),9,179,3,"killdeal"),"empty last team/round/player is0");

        string killKey="t9.r179.p3.kills",damageKey="t9.r179.p3.damage";
        Equal("",ScoreRules.Normalize(killKey,""),"blank kills");
        Equal("",ScoreRules.Normalize(damageKey,""),"blank damage");
        Equal("0",ScoreRules.Normalize(killKey,"0"),"zero kills");
        Equal("2",ScoreRules.Normalize(killKey,"2.0"),"whole number normalized");
        Equal("999999",ScoreRules.Normalize(killKey,"999999"),"maximum kills");
        Equal("99.99",ScoreRules.Normalize(damageKey,"99.99"),"fractional damage");
        Equal("299.99",ScoreRules.Normalize(damageKey,"299.99"),"ordinary fractional damage remains accepted");
        Equal("100",ScoreRules.Normalize(damageKey,"100"),"exact score boundary remains accepted");
        Equal("0.0000000000000000000000000001",ScoreRules.Normalize(damageKey,"0.0000000000000000000000000001"),"smallest representable decimal preserves zero score");
        Reject(()=>ScoreRules.Normalize(damageKey,"0.00000000000000000000000000001"),"underflow cannot silently normalize to zero");
        Reject(()=>ScoreRules.Normalize(damageKey,"99.999999999999999999"),"wire double cannot raise damage floor from zero to one");
        Reject(()=>ScoreRules.Normalize(damageKey,"299.9999999999999"),"stored invariant text cannot raise damage floor from two to three");
        Equal("999999",ScoreRules.Normalize(damageKey,"999999"),"maximum damage");
        Equal("-5.5",ScoreRules.Normalize("t0.r1.p0.score","-5.5"),"legacy negative score retained");
        Reject(()=>ScoreRules.Normalize(killKey,"-1"),"negative kills rejected");
        Reject(()=>ScoreRules.Normalize(killKey,"1.5"),"fractional kills rejected");
        Reject(()=>ScoreRules.Normalize(killKey,"1000000"),"oversize kills rejected");
        Reject(()=>ScoreRules.Normalize(damageKey,"-0.01"),"negative damage rejected");
        Reject(()=>ScoreRules.Normalize(damageKey,"1000000"),"oversize damage rejected");
        Reject(()=>ScoreRules.Normalize("t0.r180.p0.kills","1"),"round180 kills rejected");
        Reject(()=>ScoreRules.Normalize("t10.r1.p0.damage","1"),"team10 damage rejected");
        Reject(()=>ScoreRules.Normalize("t0.r1.p4.damage","1"),"player4 damage rejected");
        Reject(()=>ScoreRules.ValidateRound("t0.r51.p0.damage",50),"new field round boundary");
        Reject(()=>ScoreRules.Normalize(killKey,"NaN"),"non-numeric kills rejected");
        Reject(()=>ScoreRules.NormalizeStored(killKey,true),"boolean kills rejected");
        Equal("25.5",ScoreRules.NormalizeStored(damageKey,25.5m),"stored decimal damage");

        var content=new ScoreState();
        Put(content,"t0.p0.name","Name");Put(content,"t0.r1.p0.score","");
        Put(content,"t0.r1.p0.kills","0");Put(content,"t0.r1.p0.damage","0");Put(content,"t0.r1.p0.check","0");
        Equal(true,RoomActivity.IsContentField(killKey),"kills marks activity");
        Equal(true,RoomActivity.IsContentField(damageKey),"damage marks activity");
        Equal(3,RoomActivity.Count(content),"explicit zeros count as input");
        Put(content,"t0.r1.p0.check","1");Equal(4,RoomActivity.Count(content),"checked input count");
        Put(content,"t0.r1.p0.damage","");Equal(3,RoomActivity.Count(content),"cleared damage no longer counts");
        Equal(30510,RoomActivity.MaximumContent,"all preserved cell fields fit activity cap");
        Equal(false,RoomActivity.IsContentField("t0.r180.p0.kills"),"invalid round excluded from activity");
        Equal(false,RoomActivity.IsActive(1,1000,1000+RoomActivity.IdleMilliseconds),"idle behavior unchanged");

        string file=Path.Combine(dir,"killdeal-local-"+Guid.NewGuid().ToString("N")+".json");
        var store=new LocalStore(file);
        store.Apply(new ChangeRequest {Key=killKey,Value="2",Version=0,RequestId="kills"});
        store.Apply(new ChangeRequest {Key=damageKey,Value="199.5",Version=0,RequestId="damage"});
        var reopened=new LocalStore(file).Snapshot();
        Equal("2",reopened.Get(killKey),"kills persist separately");
        Equal("199.5",reopened.Get(damageKey),"damage persists separately");
        Equal(3m,ScoreMath.RawScore(reopened,9,179,3,"killdeal"),"reopened inputs recalculate");
        var copied=reopened.Copy();Put(copied,damageKey,"");
        Equal("199.5",reopened.Get(damageKey),"copied snapshot does not alter saved damage");
        Console.WriteLine("PASS "+assertions+" killdeal math, validation, activity and local persistence assertions; no network.");
    }
}

