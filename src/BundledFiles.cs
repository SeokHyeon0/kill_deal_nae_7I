using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PubgScore
{
#if SINGLEFILE || VIEWER_SINGLEFILE
    internal static class BundledFiles
    {
        internal const string FirebaseResource="PubgScore.Bundled.firebase.config.json";
        internal const string LicenseResource="PubgScore.Bundled.ICON-LICENSE.txt";
        internal const string UpdaterLicenseResource="PubgScore.Bundled.AutoUpdater.NET-LICENSE.txt";
        internal const string SummaryFontLicenseResource="PubgScore.Bundled.NanumSquare-OFL.txt";
        internal const string ResultFontLicenseResource="PubgScore.Bundled.DoHyeon-OFL.txt";
        internal static string ReadText(string name)
        {
            using(var stream=typeof(BundledFiles).Assembly.GetManifestResourceStream(name))
            {
                if(stream==null)throw new InvalidOperationException("프로그램 내장 리소스가 없습니다. 배포 파일을 다시 확인하세요.");
                using(var reader=new StreamReader(stream,Encoding.UTF8,true))return reader.ReadToEnd();
            }
        }
        internal static bool HasSupportedFramework()
        {
            try
            {
                object release=Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full","Release",null);
                return release!=null&&Convert.ToInt32(release)>=461808;
            }
            catch(Exception){return false;}
        }
    }
    internal sealed class InformationTabControl : UserControl
    {
        readonly TableLayoutPanel navigation=new TableLayoutPanel();
        readonly DialogSurface pageHost=new DialogSurface();
        readonly List<Control> pages=new List<Control>();
        readonly List<DesignButton> buttons=new List<DesignButton>();
        int selectedIndex=-1;
        internal InformationTabControl()
        {
            BackColor=DialogPalette.Background;ForeColor=DialogPalette.Text;TabStop=false;
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=BackColor};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(layout);
            navigation.Dock=DockStyle.Fill;navigation.ColumnCount=3;navigation.RowCount=1;navigation.Margin=new Padding(0,0,0,14);navigation.Padding=Padding.Empty;navigation.BackColor=BackColor;
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,8));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            navigation.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.Controls.Add(navigation,0,0);
            pageHost.Dock=DockStyle.Fill;pageHost.Margin=Padding.Empty;pageHost.Padding=new Padding(18);pageHost.CornerRadius=14;pageHost.BackColor=DialogPalette.Surface;layout.Controls.Add(pageHost,0,1);
        }
        internal int SelectedIndex
        {
            get{return selectedIndex;}
            set
            {
                if(value<0||value>=pages.Count||selectedIndex==value)return;
                selectedIndex=value;
                for(int i=0;i<pages.Count;i++)
                {
                    bool selected=i==value;pages[i].Visible=selected;
                    buttons[i].BackColor=selected?DialogPalette.AccentSoft:DialogPalette.Surface;
                    buttons[i].ForeColor=selected?DialogPalette.Accent:DialogPalette.Muted;
                    buttons[i].TabStop=selected;buttons[i].AccessibleDescription=selected?"선택된 탭":"탭 선택";
                }
                pages[value].BringToFront();
            }
        }
        internal void AddPage(string title,Control page)
        {
            int index=pages.Count;
            var button=new DesignButton {Text=title,Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(4,0,4,0),Font=Font,CornerRadius=12,BackColor=DialogPalette.Surface,ForeColor=DialogPalette.Muted,AccessibleRole=AccessibleRole.PageTab,AccessibleName=title,TabIndex=index,TabStop=false};
            button.FlatAppearance.BorderSize=0;button.Click+=delegate {SelectedIndex=index;};
            buttons.Add(button);navigation.Controls.Add(button,index*2,0);
            page.Dock=DockStyle.Fill;page.Margin=Padding.Empty;page.Visible=false;page.TabIndex=index;pages.Add(page);pageHost.Controls.Add(page);
            if(selectedIndex<0)SelectedIndex=0;
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData)
        {
            if(pages.Count>0)
            {
                int next=-1;
                if(keyData==(Keys.Control|Keys.Tab)||navigation.ContainsFocus&&keyData==Keys.Right)next=(selectedIndex+1)%pages.Count;
                else if(keyData==(Keys.Control|Keys.Shift|Keys.Tab)||navigation.ContainsFocus&&keyData==Keys.Left)next=(selectedIndex+pages.Count-1)%pages.Count;
                else if(navigation.ContainsFocus&&keyData==Keys.Home)next=0;
                else if(navigation.ContainsFocus&&keyData==Keys.End)next=pages.Count-1;
                if(next>=0){SelectedIndex=next;buttons[next].Focus();return true;}
            }
            return base.ProcessCmdKey(ref message,keyData);
        }
    }
    public sealed partial class MainForm
    {
        Form deploymentInformation;
        void ShowDeploymentInformation()
        {
            if(deploymentInformation!=null&&!deploymentInformation.IsDisposed){deploymentInformation.Activate();return;}
            var assembly=typeof(MainForm).Assembly;
            string version=assembly.GetName().Version.ToString(3);
            var tabs=new InformationTabControl {Dock=DockStyle.Fill,Font=UiFont(9.5F)};
            var dialog=new Form {Text=ProgramDisplayName+" · 정보",Font=UiFont(9F),StartPosition=FormStartPosition.CenterParent,Size=new Size(760,590),MinimumSize=new Size(540,400),ShowInTaskbar=false,Icon=Icon,BackColor=DialogPalette.Background,ForeColor=DialogPalette.Text};
            string role=adminMode?"BJ용":"시청자용";
            string storage=adminMode?"BJ 자동 로그인 정보는 기존 Windows 자격 증명 저장소에 저장됩니다.":"익명 로그인 유지 정보는 기존처럼 사용자별 AppData에 저장됩니다.";
            AddInformationPage(tabs,"프로그램 정보",ProgramDisplayName+"\r\n종류: "+role+"\r\n버전: V"+version+"\r\n제작자 : karist\r\n\r\n실행 환경: Windows, .NET Framework 4.7.2 이상\r\nFirebase 설정과 아이콘·AutoUpdater.NET 라이선스는 실행 파일에 내장되어 있습니다.\r\n"+storage+"\r\n\r\n내장 라이선스는 옆 탭에서 확인할 수 있습니다.");
            string licenses="아이콘 라이선스\r\n\r\n"+BundledFiles.ReadText(BundledFiles.LicenseResource)+"\r\n\r\nAutoUpdater.NET 라이선스\r\n\r\n"+BundledFiles.ReadText(BundledFiles.UpdaterLicenseResource)+"\r\n\r\n나눔스퀘어 글꼴 라이선스\r\n\r\n"+BundledFiles.ReadText(BundledFiles.SummaryFontLicenseResource);
            if(assembly.GetManifestResourceInfo(BundledFiles.ResultFontLicenseResource)!=null)
                licenses+="\r\n\r\nDo Hyeon 글꼴 라이선스\r\n\r\n"+BundledFiles.ReadText(BundledFiles.ResultFontLicenseResource);
            AddInformationPage(tabs,"아이콘 라이선스",licenses);
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(24,20,24,16),Margin=Padding.Empty,BackColor=DialogPalette.Background};
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,74));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,56));dialog.Controls.Add(root);
            var header=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=DialogPalette.Background};header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,106));header.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(header,0,0);
            var titles=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0,0,12,12),Padding=Padding.Empty,BackColor=DialogPalette.Background};titles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));titles.RowStyles.Add(new RowStyle(SizeType.Absolute,32));titles.RowStyles.Add(new RowStyle(SizeType.Percent,100));header.Controls.Add(titles,0,0);
            titles.Controls.Add(new Label {Text="프로그램 정보",Dock=DockStyle.Fill,Margin=Padding.Empty,Font=UiFont(18F,FontStyle.Bold),ForeColor=DialogPalette.Text,TextAlign=ContentAlignment.MiddleLeft},0,0);
            titles.Controls.Add(new Label {Text=ProgramDisplayName,Dock=DockStyle.Fill,Margin=Padding.Empty,Font=UiFont(9.5F),ForeColor=DialogPalette.Muted,TextAlign=ContentAlignment.MiddleLeft},0,1);
            var versionCard=new DialogSurface {Size=new Size(106,32),Anchor=AnchorStyles.Top|AnchorStyles.Right,Margin=new Padding(0,8,0,0),CornerRadius=10,BackColor=DialogPalette.AccentSoft,DrawBorder=false};
            versionCard.Controls.Add(new Label {Text="V"+version,Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleCenter,Font=UiFont(9.5F),ForeColor=DialogPalette.Accent});header.Controls.Add(versionCard,1,0);
            root.Controls.Add(tabs,0,1);
            var footer=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=DialogPalette.Background};root.Controls.Add(footer,0,2);
            var close=DialogPalette.Button("닫기",true);close.Font=UiFont(9.5F);close.Size=new Size(96,40);close.Anchor=AnchorStyles.Top|AnchorStyles.Right;close.DialogResult=DialogResult.Cancel;
            Action alignClose=delegate {close.Location=new Point(Math.Max(0,footer.ClientSize.Width-close.Width),16);};
            footer.Controls.Add(close);footer.SizeChanged+=delegate {alignClose();};alignClose();
            dialog.CancelButton=close;close.Click+=delegate {dialog.Close();};
            deploymentInformation=dialog;
            dialog.FormClosed+=delegate {deploymentInformation=null;};
            dialog.Show(this);
        }
        Button CreateDeploymentInformationButton(Color background,Color foreground)
        {
            var button=new Button {Name="deploymentInformationButton",Text="정보",Size=new Size(48,24),Anchor=AnchorStyles.Bottom|AnchorStyles.Right,Font=UiFont(8F),FlatStyle=FlatStyle.Flat,BackColor=background,ForeColor=foreground,Margin=Padding.Empty,Padding=Padding.Empty,Cursor=Cursors.Hand,CausesValidation=false,AccessibleName="프로그램 정보 및 라이선스"};
            button.FlatAppearance.BorderSize=0;
            button.Click+=delegate {ShowDeploymentInformation();};
            return button;
        }
        void AddInformationPage(InformationTabControl tabs,string title,string text)
        {
            var page=new Panel {BackColor=DialogPalette.Surface,ForeColor=DialogPalette.Text,Padding=Padding.Empty};
            var box=new DesignTextView {Dock=DockStyle.Fill,Text=text,BackColor=DialogPalette.Surface,ForeColor=DialogPalette.Text,Font=UiFont(9.5F),TabStop=true};
            page.Controls.Add(box);tabs.AddPage(title,page);
        }
    }
#endif
}
