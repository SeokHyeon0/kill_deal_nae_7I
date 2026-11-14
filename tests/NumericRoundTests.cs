using System;
using System.Collections.Generic;
using System.Reflection;
using PubgScore;

class NumericRoundTests
{
    static int count;
    static void Check(bool yes,string label){if(!yes)throw new Exception(label);Console.WriteLine("PASS "+label);count++;}
    static void Reject(Action action,string label){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,label);}
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object o,string name){return o.GetType().GetField(name,Flags).GetValue(o);}
    static void Set(object o,string name,object value){o.GetType().GetField(name,Flags).SetValue(o,value);}
    static Dictionary<string,object> Map(params object[] a){var d=new Dictionary<string,object>();for(int i=0;i<a.Length;i+=2)d[(string)a[i]]=a[i+1];return d;}
    static void Snapshot(string key,object value,bool valid)
    {
        var cfg=new FirebaseConfig{DatabaseUrl="http://127.0.0.1:9000",ApiKey="demo",ProjectId="demo-numeric",AuthDomain="demo",EmulatorMode=true,AuthEmulatorUrl="http://127.0.0.1:9099"};
        using(var c=new FirebaseGameClient(cfg)){
            Set(c,"tree",Map("generation",0,"roundCount",50,"control",Map("session",1,"running",true),"cells",Map(key,Map("value",value,"generation",0,"version",1))));
            typeof(FirebaseGameClient).GetMethod("RebuildState",Flags).Invoke(c,null);
            Check((bool)Get(c,"streamReady")==valid,"snapshot "+key+" ready="+valid);
            if(valid){var s=(ScoreState)Get(c,"state");Check(s.GetDecimal(key.Replace('_','.'),-1)==0.0000001m,"SSE exponent retained in calculation");}
            else Check(((string)Get(c,"info")).Contains(key.Replace('_','.')),"invalid stored record reported, not silently discarded");
        }
    }
    static int Main(){
        string score="t0.r1.p0.score";
        Check(ScoreRules.NormalizeStored(score,1e-7)=="0.0000001","scientific wire value formatted");
        Check(ScoreRules.NormalizeStored(score,1e-28)=="0.0000000000000000000000000001","minimum positive decimal");
        Check(ScoreRules.NormalizeStored(score,-1e-28)=="-0.0000000000000000000000000001","minimum negative decimal");
        Reject(()=>ScoreRules.NormalizeStored(score,1e-100),"wire underflow rejected");
        Reject(()=>ScoreRules.Normalize(score,"0.00000000000000000000000000001"),"input underflow rejected");
        Reject(()=>ScoreRules.NormalizeStored(score,true),"boolean score rejected");
        foreach(string suffix in new[]{"p0.score","p0.check","chicken"}){
            ScoreRules.ValidateRound("t0.r50."+suffix,50);count++;
            Reject(()=>ScoreRules.ValidateRound("t0.r51."+suffix,50),"51 of 50 rejected "+suffix);
            ScoreRules.ValidateRound("t0.r179."+suffix,179);count++;
        }
        Snapshot("t0_r1_p0_score",1e-7,true);Snapshot("t0_r1_p0_score",1e-100,false);Snapshot("t0_r51_p0_score",1,false);
        var state=new ScoreState();state.Cells[score]=new CellValue{Value="12.5"};state.Cells["t0.p0.penalty"]=new CellValue{Value="2.5"};state.Cells["t0.r1.p0.check"]=new CellValue{Value="1"};
        Check(ScoreMath.Calculate(state,0).Total==10m,"existing score/penalty calculation unchanged");
        Console.WriteLine("PASS total "+count);return 0;
    }
}
