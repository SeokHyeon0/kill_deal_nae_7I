using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Windows.Forms;

namespace PubgScore
{
    public sealed class ResultSnapshot
    {
        public ScoreState State;public string RoomName,Mode;public DateTime CapturedAt;
        public ResultSnapshot Copy(){return new ResultSnapshot {State=State.Copy(),RoomName=RoomName,Mode=Mode,CapturedAt=CapturedAt};}
    }
    public static class ResultImage
    {
        const int TableLeft=1;
        const int TableTop=2;
        const int RowHeight=20;
        const int TeamWidth=184;
        const int NameWidth=46;
        const int TeamHeight=(ScoreRules.PlayerCount+3)*RowHeight;
        const int ImageScale=2;
        const int LayoutDpi=96;
        const float TextSize=14.5F;
        static readonly string[] ContestSuffixes={"경딜내기","딜킬내기","킬딜내기","킬내기","딜내기"};

        static string N(decimal value){return value.ToString("0.############################",CultureInfo.InvariantCulture);}
        static Font TableFont(BundledFontResource font,float size)
        {
            return font==null?new Font("Malgun Gothic",size,FontStyle.Bold,GraphicsUnit.Pixel):new Font(font.Family,size,FontStyle.Bold,GraphicsUnit.Pixel);
        }
        static void Text(Graphics g,string value,Font font,Brush ink,RectangleF box,StringFormat format,float minimumSize=TextSize)
        {
            box.Inflate(-3,0);
            // Do Hyeon's line metrics leave more space below the visible letters.
            if(font.Name=="Do Hyeon")box.Offset(0,3F);
            if(minimumSize<font.Size)
            {
                float textWidth=g.MeasureString(value,font).Width;
                if(textWidth>box.Width)
                {
                    float fittedSize=Math.Max(minimumSize,font.Size*box.Width/textWidth);
                    using(var fitted=new Font(font.FontFamily,fittedSize,font.Style,GraphicsUnit.Pixel))
                        g.DrawString(value,fitted,ink,box,format);
                    return;
                }
            }
            g.DrawString(value,font,ink,box,format);
        }
        static void ChickenRow(Graphics g,int x,int y,int width,Font font,string score,StringFormat centered)
        {
            const string label="치킨";
            const int iconWidth=12;
            float labelWidth=g.MeasureString(label,font).Width+8;
            float scoreWidth=g.MeasureString(score,font).Width+8;
            float left=x+(width-labelWidth-iconWidth-scoreWidth)/2;
            Text(g,label,font,Brushes.Black,new RectangleF(left,y,labelWidth,RowHeight),centered);
            Text(g,score,font,Brushes.Black,new RectangleF(left+labelWidth+iconWidth,y,scoreWidth,RowHeight),centered);

            float iconX=left+labelWidth;
            float iconY=y+4;
            var previous=g.SmoothingMode;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var comb=new SolidBrush(Color.FromArgb(222,89,156)))
            using(var outline=new Pen(Color.FromArgb(183,183,188),0.7F))
            {
                g.FillEllipse(comb,iconX+2,iconY,8,5);
                g.FillPolygon(Brushes.Goldenrod,new[]{new PointF(iconX+2,iconY+5),new PointF(iconX,iconY+7),new PointF(iconX+3,iconY+8)});
                g.FillEllipse(Brushes.White,iconX+2,iconY+3,9,10);
                g.DrawEllipse(outline,iconX+2,iconY+3,9,10);
                g.FillEllipse(Brushes.Black,iconX+4,iconY+6,1.3F,1.3F);
                g.FillEllipse(comb,iconX+2,iconY+8,2,3);
            }
            g.SmoothingMode=previous;
        }
        internal static string Title(string room,string mode)
        {
            string roomName=(room??"").Trim();
            string contest=GameModes.Name(mode);
            if(contest.Length==0)
                return roomName.Length==0?"킬내기":roomName.EndsWith("내기",StringComparison.Ordinal)?roomName:roomName+" 킬내기";
            if(!contest.EndsWith("내기",StringComparison.Ordinal))contest+="내기";
            foreach(string suffix in ContestSuffixes)
            {
                if(roomName==suffix)return contest;
                if(!roomName.EndsWith(" "+suffix,StringComparison.Ordinal))continue;
                roomName=roomName.Substring(0,roomName.Length-suffix.Length).TrimEnd();
                break;
            }
            return roomName.Length==0?contest:roomName+" "+contest;
        }
        internal static int[] ActiveTeams(ScoreState state)
        {
            if(state==null)throw new ArgumentNullException("state");
            return Enumerable.Range(0,ScoreRules.TeamCount).Where(t=>Enumerable.Range(0,ScoreRules.PlayerCount).Any(p=>!String.IsNullOrWhiteSpace(state.Get("t"+t+".p"+p+".name", "")))).ToArray();
        }
        // The same scoring engine is used by the GUI and copied image.
        public static Bitmap Render(ScoreState source,string room,string mode,DateTime savedAt)
        {
            if(source==null)throw new ArgumentNullException("source");
            var state=source.Copy();
            var teams=ActiveTeams(state);
            if(teams.Length==0)throw new InvalidOperationException("선수 이름이 입력된 팀이 없습니다.");
            int columns=Math.Min(5,teams.Length),rows=(teams.Length+columns-1)/columns;
            int tableWidth=columns*TeamWidth-1;
            int width=TableLeft+tableWidth+3;
            int height=TableTop+RowHeight+rows*TeamHeight+5;
            var assembly=typeof(ResultImage).Assembly;
            string fontName=BundledFontResource.ResultFont;
            var bitmap=new Bitmap(width*ImageScale,height*ImageScale);
            bitmap.SetResolution(LayoutDpi*ImageScale,LayoutDpi*ImageScale);
            using(var font=assembly.GetManifestResourceInfo(fontName)==null?null:new BundledFontResource(fontName))
            using(var g=Graphics.FromImage(bitmap))
            using(var face=TableFont(font,TextSize))
            using(var headingFill=new SolidBrush(Color.FromArgb(240,215,235)))
            using(var border=new Pen(Color.Black))
            using(var line=new Pen(Color.FromArgb(205,205,205)))
            using(var centered=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})
            {
                g.Clear(Color.White);
                g.PageUnit=GraphicsUnit.Pixel;
                g.ScaleTransform(ImageScale,ImageScale);
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                int firstTeamY=TableTop+RowHeight;
                int tableRight=TableLeft+tableWidth;
                g.FillRectangle(headingFill,TableLeft,TableTop,tableWidth,RowHeight);
                Text(g,Title(room,mode),face,Brushes.Black,new RectangleF(TableLeft,TableTop,tableWidth,RowHeight),centered);
                g.DrawLine(border,TableLeft,TableTop,tableRight,TableTop);
                g.DrawLine(border,TableLeft,TableTop,TableLeft,firstTeamY);
                g.DrawLine(border,tableRight,TableTop,tableRight,firstTeamY);

                for(int i=0;i<teams.Length;i++)
                {
                    int team=teams[i];
                    int x=TableLeft+i%columns*TeamWidth;
                    int y=firstTeamY+i/columns*TeamHeight;
                    int cellWidth=(i%columns==columns-1||i==teams.Length-1)?TeamWidth-1:TeamWidth;
                    int right=x+cellWidth;
                    var summary=ScoreMath.Calculate(state,team,mode);
                    string leader=summary.Names[0];
                    string teamName=String.IsNullOrWhiteSpace(leader)?(team+1)+"팀":leader+" 팀";
                    g.FillRectangle(headingFill,x,y+1,cellWidth,RowHeight-1);
                    g.DrawLine(line,x,y,right,y);
                    Text(g,teamName,face,Brushes.Black,new RectangleF(x,y,cellWidth,RowHeight),centered);

                    int totalY=y+RowHeight;
                    g.DrawLine(line,x,totalY,right,totalY);
                    Text(g,N(summary.Total),face,Brushes.Black,new RectangleF(x,totalY,cellWidth,RowHeight),centered);

                    int playersY=totalY+RowHeight;
                    int chickenY=playersY+ScoreRules.PlayerCount*RowHeight;
                    for(int player=0;player<ScoreRules.PlayerCount;player++)
                    {
                        int rowY=playersY+player*RowHeight;
                        g.DrawLine(line,x,rowY,right,rowY);
                        Text(g,summary.Names[player],face,Brushes.Black,new RectangleF(x,rowY,NameWidth,RowHeight),centered,11F);
                        string score=N(summary.PlayerTotals[player])+" ("+N(summary.Raw[player])+" - "+N(summary.Deductions[player])+")";
                        Text(g,score,face,Brushes.Black,new RectangleF(x+NameWidth,rowY,cellWidth-NameWidth,RowHeight),centered,1F);
                    }
                    g.DrawLine(line,x+NameWidth,playersY,x+NameWidth,chickenY);
                    g.DrawLine(line,x,chickenY,right,chickenY);
                    string chicken=summary.ChickenSum>=0?"+ "+N(summary.ChickenSum):"- "+N(-summary.ChickenSum);
                    ChickenRow(g,x,chickenY,cellWidth,face,chicken,centered);
                }
                for(int row=0;row<rows;row++)
                {
                    int teamsInRow=Math.Min(columns,teams.Length-row*columns);
                    int y=firstTeamY+row*TeamHeight;
                    int right=TableLeft+teamsInRow*TeamWidth-1;
                    int bottom=y+TeamHeight+(row==rows-1?1:0);
                    for(int column=1;column<teamsInRow;column++)
                        g.DrawLine(line,TableLeft+column*TeamWidth,y,TableLeft+column*TeamWidth,bottom);
                    g.DrawLine(border,TableLeft,y,TableLeft,bottom);
                    g.DrawLine(border,right,y,right,bottom);
                    g.DrawLine(border,TableLeft,bottom,right,bottom);
                }
            }
            return bitmap;
        }
        internal static void EncodeClipboardImage(Bitmap image,Stream png,Stream dib)
        {
            if(image==null)throw new ArgumentNullException("image");
            if(png==null)throw new ArgumentNullException("png");
            if(dib==null)throw new ArgumentNullException("dib");
            // Publish complete pixel data, like a captured picture. There is no
            // reference to the live result, a GDI bitmap handle, or a temporary file.
            // A 24-bit DIB also avoids ambiguous alpha when pasted into Office.
            using(var pixels=new Bitmap(image.Width,image.Height,PixelFormat.Format24bppRgb))
            using(var bmp=new MemoryStream())
            {
                if(image.HorizontalResolution>0&&image.VerticalResolution>0)
                    pixels.SetResolution(image.HorizontalResolution,image.VerticalResolution);
                using(var graphics=Graphics.FromImage(pixels))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawImageUnscaled(image,0,0);
                }
                pixels.Save(png,ImageFormat.Png);
                pixels.Save(bmp,ImageFormat.Bmp);
                // CF_DIB begins at BITMAPINFOHEADER, not the BMP file header.
                bmp.Position=14;bmp.CopyTo(dib);
            }
        }
        public static void CopyToClipboard(Bitmap image)
        {
            using(var png=new MemoryStream())
            using(var dib=new MemoryStream())
            {
                EncodeClipboardImage(image,png,dib);
                png.Position=0;dib.Position=0;
                var data=new DataObject();
                data.SetData("PNG",false,png);
                data.SetData(DataFormats.Dib,false,dib);
                // Flush both formats to Windows before disposing their streams.
                Clipboard.SetDataObject(data,true,5,100);
            }
        }
    }

    public sealed partial class MainForm
    {
        readonly Button resultButton=new DesignButton();
        ResultSnapshot lastExportResult;
        void ExportResult()
        {
            if(!adminMode||hostBusy)return;
            DismissResultCopiedToast();
            if(!TryCommitSetupEdits()||!grid.EndEdit())return;
            if(pending.Count>0||drafts.Count>0||setupDirty.Count>0){DesignMessageBox.Show(this,"입력 저장을 완료한 뒤 결과 복사를 다시 눌러 주세요.","결과 복사");return;}
            var current=backend==null?null:backend.View();
            if(current!=null&&current.RoomId.Length>0&&!current.Connected&&!(backend is FirebaseGameClient&&((FirebaseGameClient)backend).RoomUnavailable)){
                DesignMessageBox.Show(this,"연결이 끊겨 최신 결과를 확인할 수 없습니다. 다시 연결한 뒤 복사해 주세요.","결과 복사");return;
            }
            var firebase=backend as FirebaseGameClient;
            var snapshot=firebase==null?lastExportResult:firebase.LastResult??lastExportResult;
            if(current!=null&&current.Connected&&current.RoomId.Length>0)snapshot=new ResultSnapshot {State=current.State.Copy(),RoomName=current.RoomName,Mode=current.Mode,CapturedAt=DateTime.Now};
            if(snapshot==null){DesignMessageBox.Show(this,"복사할 경기 결과가 없습니다.","결과 복사");return;}
            if(ResultImage.ActiveTeams(snapshot.State).Length==0){DesignMessageBox.Show(this,"선수 이름이 입력된 팀이 없습니다.","결과 복사");return;}
            try
            {
                using(var image=ResultImage.Render(snapshot.State,snapshot.RoomName,snapshot.Mode,snapshot.CapturedAt))ResultImage.CopyToClipboard(image);
                lastExportResult=snapshot.Copy();
                saveStatus="";Render();ShowResultCopiedToast();
            }
            catch(ExternalException){DesignMessageBox.Show(this,"다른 프로그램이 클립보드를 사용 중입니다. 잠시 후 결과 복사를 다시 눌러 주세요.","결과 복사 실패");}
            catch(Exception ex){DesignMessageBox.Show(this,"결과를 복사하지 못했습니다.\n"+ex.Message,"결과 복사 실패");}
        }
    }
}
