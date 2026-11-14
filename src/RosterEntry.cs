using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PubgScore
{
    internal sealed class RosterImport
    {
        internal readonly string[,] Names = new string[ScoreRules.PlayerCount, ScoreRules.TeamCount];
        internal int Teams;
        internal bool HasTeam(int team)
        {
            for(int player=0;player<ScoreRules.PlayerCount;player++)
                if(!String.IsNullOrWhiteSpace(Names[player,team]))return true;
            return false;
        }

        internal static RosterImport Parse(string text)
        {
            var lines = (text ?? "").Split(new[] {'\r','\n'},StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            if(lines.Count > 0 && Regex.IsMatch(lines[0], @"^1팀(?:\s|$)"))
                lines.RemoveAt(0); // Pasted column headings are optional, never stored as names.
            if(lines.Count != ScoreRules.PlayerCount)
                throw new ArgumentException("이름을 위에서부터 1~4티어, 총 4줄로 입력하세요.");
            var result = new RosterImport();
            for(int player=0;player<ScoreRules.PlayerCount;player++)
            {
                string line=Regex.Replace(lines[player],"^"+(player+1)+@"티어(?:\s+|$)","");
                string[] names=line.IndexOf('\t')>=0?line.Split('\t'):Regex.Split(line.Trim(),@"\s+");
                if(names.Length<1||names.Length>ScoreRules.TeamCount)
                    throw new ArgumentException((player+1)+"티어 줄에는 팀 이름을 1~10개 입력하세요.");
                if(result.Teams==0)result.Teams=names.Length;
                if(result.Teams!=names.Length)
                    throw new ArgumentException("네 줄의 팀 수가 같아야 합니다. 빈 이름은 탭 사이를 비워 두세요.");
                for(int team=0;team<names.Length;team++)
                {
                    string name=names[team].Trim();
                    result.Names[player,team]=ScoreRules.Normalize("t"+team+".p"+player+".name",name);
                }
            }
            return result;
        }
    }

    internal static class RosterWindowPreferences
    {
        static string overridePath;
        internal static string PathOverride {get{return overridePath;}set{overridePath=value;}}
        static string FilePath {get{return PathOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PubgScoreFirebase","roster-window.txt");}}

        internal static bool TryRead(out Size size,out float zoom,out bool maximized)
        {
            size=Size.Empty;zoom=1F;maximized=false;
            try
            {
                string[] parts=File.ReadAllText(FilePath).Split('|');
                int width,height;float parsedZoom;
                if(parts.Length!=5||parts[0]!="v1"||!Int32.TryParse(parts[1],out width)||!Int32.TryParse(parts[2],out height)||
                   !Single.TryParse(parts[3],NumberStyles.Float,CultureInfo.InvariantCulture,out parsedZoom)||
                   width<650||height<430||width>5000||height>5000||parsedZoom<0.5F||parsedZoom>3F)return false;
                size=new Size(width,height);zoom=parsedZoom;maximized=parts[4]=="1";return true;
            }
            catch(Exception){return false;} // Optional UI preference must never block game entry.
        }

        internal static void Write(Size size,float zoom,bool maximized)
        {
            try
            {
                string path=FilePath;Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,"v1|"+size.Width+"|"+size.Height+"|"+zoom.ToString(CultureInfo.InvariantCulture)+"|"+(maximized?"1":"0"));
            }
            catch(Exception){} // Unwritable preferences must not block closing the editor.
        }
    }

    internal sealed class RosterEntryDialog : Form
    {
        readonly RosterEditorBox editor=new RosterEditorBox();
        readonly Label message=new Label();readonly Button save=DialogPalette.Button("저장",true);bool busy;

        internal RosterEntryDialog(Func<RosterImport,Task> apply,Icon applicationIcon=null)
        {
            const string lineLimitMessage="선수 이름은 1~4티어, 최대 4줄까지만 입력할 수 있습니다.";
            Text="선수 이름 일괄 입력";StartPosition=FormStartPosition.CenterParent;
            if(applicationIcon!=null)Icon=applicationIcon;
            MinimumSize=new Size(650,600);ClientSize=new Size(870,570);
            Font=new Font("Malgun Gothic",10F);BackColor=DialogPalette.Surface;ForeColor=DialogPalette.Text;
            var header=new Panel {Dock=DockStyle.Top,Height=76,Padding=new Padding(26,20,26,12),BackColor=DialogPalette.Surface};
            var heading=new TableLayoutPanel {Dock=DockStyle.Top,Height=38,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,156));
            var title=new Label {Text="선수 이름 일괄 입력",Dock=DockStyle.Fill,Margin=Padding.Empty,Font=new Font("Malgun Gothic",16F,FontStyle.Bold),ForeColor=DialogPalette.Text,TextAlign=ContentAlignment.MiddleLeft};
            var chip=new DialogSurface {Dock=DockStyle.Top,Height=30,Margin=new Padding(0,4,0,0),Padding=new Padding(8,0,8,0),CornerRadius=8,DrawBorder=false,BackColor=DialogPalette.AccentSoft};
            var count=new Label {Text="10팀 · 0 / 40명",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Malgun Gothic",9F),ForeColor=DialogPalette.Accent};
            chip.Controls.Add(count);heading.Controls.Add(title,0,0);heading.Controls.Add(chip,1,0);
            header.Controls.Add(heading);
            editor.Dock=DockStyle.Fill;editor.Margin=Padding.Empty;editor.BackColor=DialogPalette.Field;editor.ForeColor=DialogPalette.Text;
            var editorHost=new Panel {Dock=DockStyle.Fill,BackColor=DialogPalette.Surface,Padding=new Padding(26,0,26,20)};
            var editorSurface=new DialogSurface {Dock=DockStyle.Fill,BackColor=DialogPalette.Field,Padding=new Padding(10,1,10,1),CornerRadius=12,BorderColor=DialogPalette.Border};
            editorSurface.Controls.Add(editor);editorHost.Controls.Add(editorSurface);
            editor.TextChanged+=delegate {
                var lines=(editor.Text??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(line=>line.Trim()).Where(line=>line.Length>0).ToList();
                if(lines.Count>0&&Regex.IsMatch(lines[0],@"^1팀(?:\s|$)"))lines.RemoveAt(0);
                int names=0;
                for(int tier=0;tier<Math.Min(ScoreRules.PlayerCount,lines.Count);tier++)
                {
                    string line=Regex.Replace(lines[tier],"^"+(tier+1)+@"티어(?:\s+|$)","");
                    string[] entries=line.IndexOf('\t')>=0?line.Split('\t'):Regex.Split(line.Trim(),@"\s+");
                    names+=entries.Take(ScoreRules.TeamCount).Count(name=>!String.IsNullOrWhiteSpace(name));
                }
                count.Text="10팀 · "+names+" / 40명";
                if(message.Text==lineLimitMessage)message.Text="선수 이름만 입력한 뒤 저장하세요.";
            };
            var bottom=new TableLayoutPanel {Dock=DockStyle.Bottom,Height=80,ColumnCount=3,RowCount=1,Padding=new Padding(26,16,26,20),BackColor=DialogPalette.Background};
            bottom.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(DialogPalette.Border))e.Graphics.DrawLine(pen,0,0,bottom.Width,0);};
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,104));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,104));
            message.Dock=DockStyle.Fill;message.Margin=new Padding(0,0,12,0);message.ForeColor=DialogPalette.Muted;message.Font=new Font("Malgun Gothic",9F);message.TextAlign=ContentAlignment.MiddleLeft;message.Text="선수 이름만 입력한 뒤 저장하세요.";
            editor.InputRejected+=delegate {message.Text=lineLimitMessage;};
            save.Dock=DockStyle.Fill;save.Margin=new Padding(4,0,4,0);
            var cancel=DialogPalette.Button("닫기",false);cancel.Dock=DockStyle.Fill;cancel.Margin=new Padding(4,0,0,0);
            bottom.Controls.Add(message,0,0);bottom.Controls.Add(save,1,0);bottom.Controls.Add(cancel,2,0);
            Controls.Add(editorHost);Controls.Add(bottom);Controls.Add(header);
            Size rememberedSize;float rememberedZoom;bool rememberedMaximized;
            if(RosterWindowPreferences.TryRead(out rememberedSize,out rememberedZoom,out rememberedMaximized))
            {
                Rectangle bounds=Screen.PrimaryScreen.WorkingArea;
                Size=new Size(Math.Min(rememberedSize.Width,bounds.Width),Math.Min(rememberedSize.Height,bounds.Height));
                if(rememberedMaximized)Shown+=delegate {WindowState=FormWindowState.Maximized;};
            }
            bool sizingRows=false;
            Action keepFourRowsVisible=delegate {
                if(sizingRows||!IsHandleCreated||IsDisposed)return;
                sizingRows=true;
                try
                {
                    int textHeight=ScoreRules.PlayerCount*editor.MinimumRowHeightPixels;
                    int requiredHeight=Height-ClientSize.Height+header.Height+Math.Max(126,bottom.Height)+editorHost.Padding.Vertical+editorSurface.Padding.Vertical+textHeight;
                    int minimumHeight=Math.Max(600,requiredHeight);
                    if(MinimumSize.Height!=minimumHeight)MinimumSize=new Size(MinimumSize.Width,minimumHeight);
                }
                finally{sizingRows=false;}
            };
            Shown+=delegate {keepFourRowsVisible();};editor.Resize+=delegate {keepFourRowsVisible();};
            AcceptButton=null;CancelButton=cancel;
            Func<Task> submit=async ()=>{
                if(busy)return;
                RosterImport names;try{names=RosterImport.Parse(editor.Text);}catch(Exception ex){message.Text=ex.Message;return;}
                busy=true;save.Enabled=cancel.Enabled=editor.Enabled=false;message.Text="기존 기록 초기화 및 새 팀 저장 중…";
                try{await apply(names);busy=false;DialogResult=DialogResult.OK;Close();}
                catch(Exception ex){bottom.Height=126;message.Text="저장 실패: "+ex.Message+" 자동 재전송하지 않습니다.";}
                finally{busy=false;if(!IsDisposed)save.Enabled=cancel.Enabled=editor.Enabled=true;}
            };
            save.Click+=async delegate {await submit();};
            cancel.Click+=delegate {Close();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){
                if(busy){e.Cancel=true;return;}
                RosterWindowPreferences.Write(WindowState==FormWindowState.Maximized?RestoreBounds.Size:Size,1F,WindowState==FormWindowState.Maximized);
            };
        }

    }
}
