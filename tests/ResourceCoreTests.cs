using System;
using System.Globalization;
using PubgScore;
class ResourceCoreTests
{
    static int checks;
    static void Check(bool condition,string message) { if(!condition)throw new Exception(message);checks++; }
    static void ArrayEqual(decimal[] a,decimal[] b,string label) { Check(a.Length==b.Length,label+" length");for(int i=0;i<a.Length;i++)Check(a[i]==b[i],label+" "+i); }
    static int Main()
    {
        try {
            var random=new Random(741);
            foreach(int rounds in new[]{50,179})for(int fixture=0;fixture<6;fixture++) {
                var state=new ScoreState();state.Cells["roundCount"]=new CellValue {Value=rounds.ToString(),Version=1};
                for(int team=0;team<8;team++) {
                    string prefix="t"+team;
                    for(int player=0;player<4;player++) {
                        state.Cells[prefix+".p"+player+".name"]=new CellValue {Value="선수"+player,Version=1};
                        state.Cells[prefix+".p"+player+".penalty"]=new CellValue {Value=(random.Next(10000)/100m).ToString(CultureInfo.InvariantCulture),Version=1};
                    }
                    for(int round=1;round<=rounds;round++) {
                        string key=prefix+".r"+round;
                        state.Cells[key+".chicken"]=new CellValue {Value=(random.Next(16)-5).ToString(),Version=1};
                        for(int player=0;player<4;player++) {
                            if(random.Next(5)==0)continue;
                            state.Cells[key+".p"+player+".score"]=new CellValue {Value=(random.Next(-999999,999999)/100m).ToString(CultureInfo.InvariantCulture),Version=1};
                            state.Cells[key+".p"+player+".check"]=new CellValue {Value=random.Next(2).ToString(),Version=1};
                        }
                    }
                    TeamSummary a=ReferenceScoreMath.Calculate(state,team),b=ScoreMath.Calculate(state,team);
                    Check(a.Total==b.Total&&a.ChickenSum==b.ChickenSum&&a.Name==b.Name,"totals");
                    ArrayEqual(a.Raw,b.Raw,"raw");ArrayEqual(a.Deductions,b.Deductions,"deductions");ArrayEqual(a.PlayerTotals,b.PlayerTotals,"players");ArrayEqual(a.RoundTotals,b.RoundTotals,"rounds");
                }
            }
            decimal number;
            foreach(string value in new[]{"0","-0","+0.000","0.0000000000000000000000000000000000000"})Check(ScoreRules.TryDecimal(value,out number)&&number==0m,"zero");
            foreach(string value in new[]{"0.000000000000000000000000000001","-0.000000000000000000000000000001",null,"","NaN"," 0","1e-30"})Check(!ScoreRules.TryDecimal(value,out number),"invalid/underflow");
            var s=new ScoreState();Check(s.Get("t0.name")=="1팀"&&s.Get("t0.status")=="ready"&&s.Get("t0.reference")=="23"&&s.Get("t0.p0.name")==""&&s.Get("t0.r1.p0.check")=="0","missing defaults");
            s.Cells["t0.name"]=new CellValue {Value="",Version=1};Check(s.Get("t0.name")=="","explicit empty preserved");
            s.Cells["t0.name"]=new CellValue {Value=null,Version=1};Check(s.Get("t0.name")=="1팀","null default");
            Console.WriteLine("PASS "+checks+" resource core checks (96 reference summaries, numeric precision, defaults)");return 0;
        } catch(Exception error) { Console.Error.WriteLine(error);return 1; }
    }
}

    static class ReferenceScoreMath
    {
        public static TeamSummary Calculate(ScoreState state, int team)
        {
            if (state == null || team < 0 || team >= ScoreRules.TeamCount) throw new ArgumentOutOfRangeException("team");
            string prefix = "t" + team.ToString(CultureInfo.InvariantCulture);
            TeamSummary result = new TeamSummary {
                Name = state.Get(prefix + ".name"), Names = new string[4], Raw = new decimal[4],
                Deductions = new decimal[4], PlayerTotals = new decimal[4], RoundTotals = new decimal[state.RoundCount]
            };
            for (int player = 0; player < 4; player++) result.Names[player] = state.Get(prefix + ".p" + player + ".name");
            for (int round = 1; round <= state.RoundCount; round++)
            {
                string roundPrefix = prefix + ".r" + round.ToString(CultureInfo.InvariantCulture);
                decimal chicken = state.GetDecimal(roundPrefix + ".chicken", 0m);
                result.ChickenSum += chicken;
                result.RoundTotals[round - 1] = chicken;
                for (int player = 0; player < 4; player++)
                {
                    decimal raw = state.GetDecimal(roundPrefix + ".p" + player + ".score", 0m);
                    decimal deduction = state.Get(roundPrefix + ".p" + player + ".check", "0") == "1"
                        ? state.GetDecimal(prefix + ".p" + player + ".penalty", 0m) : 0m;
                    result.Raw[player] += raw;
                    result.Deductions[player] += deduction;
                    result.PlayerTotals[player] += raw - deduction;
                    result.RoundTotals[round - 1] += raw - deduction;
                }
                result.Total += result.RoundTotals[round - 1];
            }
            return result;
        }
    }

