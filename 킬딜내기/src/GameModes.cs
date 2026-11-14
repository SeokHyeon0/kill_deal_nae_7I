using System;
using System.Linq;

namespace PubgScore
{
    public static class GameModes
    {
        public static bool IsValid(string mode){return mode=="match"||mode=="kills"||mode=="killdeal"||mode=="kill"||mode=="damage";}
        // Preserve the stored legacy identifier so existing scores keep their meaning.
        public static string Name(string mode){return mode=="match"?"경딜":mode=="kills"?"딜킬":mode=="killdeal"?"킬딜":mode=="kill"?"킬내기":mode=="damage"?"딜내기":"";}
        public static int[] Deductions(string mode)
        {
            if(mode=="match")return new[]{0,0,0,0};
            if(mode=="kills")return new[]{400,300,200,100};
            if(mode=="damage")return new[]{200,150,100,50};
            if(mode=="kill"||mode=="killdeal")return new[]{4,3,2,1};
            throw new ArgumentException("경딜, 딜킬, 킬딜, 킬내기 또는 딜내기를 선택하세요.");
        }
        public static bool HasPlayers(ScoreState state)
        {
            return state!=null&&Enumerable.Range(0,ScoreRules.TeamCount).Any(t=>Enumerable.Range(0,ScoreRules.PlayerCount).Any(p=>!String.IsNullOrWhiteSpace(state.Get("t"+t+".p"+p+".name",""))));
        }
    }
}
