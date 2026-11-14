using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PubgScore
{
    internal sealed class NavigationTabButton : DesignButton
    {
        bool selected;
        internal bool Selected {get{return selected;}set{if(selected==value)return;selected=value;Invalidate();}}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if(!Selected||Width<28)return;
            float scale=DeviceDpi/96F;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var line=DesignTheme.Round(new RectangleF(12*scale,Height-8*scale,Math.Max(1,Width-24*scale),3*scale),1.5F*scale))
            using(var fill=new SolidBrush(DialogPalette.Accent))e.Graphics.FillPath(fill,line);
        }
    }

    internal sealed class StatusNotice : Label
    {
        internal StatusNotice(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnPaint(PaintEventArgs e)
        {
            Color canvas=Parent==null?DialogPalette.Background:Parent.BackColor;
            using(var fill=new SolidBrush(canvas))e.Graphics.FillRectangle(fill,ClientRectangle);
            if(String.IsNullOrEmpty(Text)||Width<40||Height<12)return;
            bool warning=Text.Contains("실패")||Text.Contains("끊김")||Text.Contains("확인 필요")||Text.Contains("읽기 전용");
            bool complete=!warning&&(Text.Contains("완료")||Text.Contains("적용"));
            Color tint=warning?Color.FromArgb(253,241,216):complete?Color.FromArgb(227,245,236):DialogPalette.Surface;
            Color ink=warning?Color.FromArgb(154,100,0):complete?Color.FromArgb(35,136,91):DialogPalette.Muted;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=DesignTheme.Round(new RectangleF(.5F,2.5F,Width-1,Height-5),9))
            {using(var fill=new SolidBrush(tint))e.Graphics.FillPath(fill,shape);using(var pen=new Pen(DialogPalette.Border))e.Graphics.DrawPath(pen,shape);}
            using(var pen=new Pen(ink,1.6F){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            {
                if(complete)e.Graphics.DrawLines(pen,new[]{new Point(12,Height/2),new Point(16,Height/2+4),new Point(23,Height/2-4)});
                else{e.Graphics.DrawEllipse(pen,11,Height/2-7,14,14);e.Graphics.DrawLine(pen,18,Height/2-3,18,Height/2+1);e.Graphics.DrawLine(pen,18,Height/2+4,18,Height/2+4.5F);}
            }
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(34,0,Math.Max(0,Width-44),Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ConnectionStatusLabel : Label
    {
        internal ConnectionStatusLabel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
        protected override void OnPaint(PaintEventArgs e)
        {
            using(var fill=new SolidBrush(BackColor))e.Graphics.FillRectangle(fill,ClientRectangle);
            int split=Text.LastIndexOf(" · ",StringComparison.Ordinal);
            if(split<0){base.OnPaint(e);return;}
            string status=Text.Substring(0,split),editing=Text.Substring(split+3);
            int chipWidth=TextRenderer.MeasureText(editing,Font).Width+20;
            int textWidth=TextRenderer.MeasureText(status,Font).Width;
            if(textWidth+chipWidth+12>Width){TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);return;}
            var chip=new Rectangle(Width-chipWidth,(Height-28)/2,chipWidth,28);
            bool editable=editing=="입력 가능";
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=DesignTheme.Round(chip,8))
            using(var fill=new SolidBrush(editable?Color.FromArgb(227,245,236):DialogPalette.Background))e.Graphics.FillPath(fill,shape);
            TextRenderer.DrawText(e.Graphics,editing,Font,chip,editable?Color.FromArgb(35,136,91):DialogPalette.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics,status,Font,new Rectangle(0,0,Math.Max(0,chip.Left-12),Height),DialogPalette.Muted,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class LightMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground {get{return DialogPalette.Surface;}}
        public override Color ImageMarginGradientBegin {get{return DialogPalette.Surface;}}
        public override Color ImageMarginGradientMiddle {get{return DialogPalette.Surface;}}
        public override Color ImageMarginGradientEnd {get{return DialogPalette.Surface;}}
        public override Color MenuBorder {get{return DialogPalette.Border;}}
        public override Color MenuItemBorder {get{return DialogPalette.AccentSoft;}}
        public override Color MenuItemSelected {get{return DialogPalette.AccentSoft;}}
        public override Color MenuItemSelectedGradientBegin {get{return DialogPalette.AccentSoft;}}
        public override Color MenuItemSelectedGradientEnd {get{return DialogPalette.AccentSoft;}}
        public override Color CheckBackground {get{return DialogPalette.AccentSoft;}}
        public override Color CheckSelectedBackground {get{return DialogPalette.AccentSoft;}}
        public override Color CheckPressedBackground {get{return DialogPalette.AccentSoft;}}
    }
    internal sealed class LightMenuRenderer : ToolStripProfessionalRenderer
    {
        internal LightMenuRenderer():base(new LightMenuColors()){RoundedEdges=true;}
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using(var fill=new SolidBrush(DialogPalette.Surface))e.Graphics.FillRectangle(fill,e.ToolStrip.ClientRectangle);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var bounds=e.ToolStrip.ClientRectangle;if(bounds.Width<2||bounds.Height<2)return;
            float scale=e.ToolStrip.DeviceDpi/96F;var previous=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=DesignTheme.Round(new RectangleF(.5F,.5F,bounds.Width-1,bounds.Height-1),12*scale))
            using(var border=new Pen(DialogPalette.Border,scale))e.Graphics.DrawPath(border,shape);
            e.Graphics.Restore(previous);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            float scale=e.Item.Owner==null?1F:e.Item.Owner.DeviceDpi/96F;
            using(var pen=new Pen(DialogPalette.Border,scale))e.Graphics.DrawLine(pen,6*scale,e.Item.Height/2F,Math.Max(6*scale,e.Item.Width-6*scale),e.Item.Height/2F);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            float scale=e.Item.Owner==null?1F:e.Item.Owner.DeviceDpi/96F;
            var previous=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var fill=new SolidBrush(DialogPalette.Surface))e.Graphics.FillRectangle(fill,new Rectangle(Point.Empty,e.Item.Size));
            var item=e.Item as ToolStripMenuItem;bool selected=e.Item.Selected||item!=null&&item.Checked;
            if(selected)using(var shape=DesignTheme.Round(new RectangleF(1,1,e.Item.Width-2,e.Item.Height-2),8*scale))
            using(var fill=new SolidBrush(DialogPalette.AccentSoft))e.Graphics.FillPath(fill,shape);
            if(item!=null&&item.Checked)using(var pen=new Pen(DialogPalette.Accent,1.8F*scale){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            {float x=e.Item.Width-26*scale,y=e.Item.Height/2F;e.Graphics.DrawLines(pen,new[]{new PointF(x,y),new PointF(x+4*scale,y+4*scale),new PointF(x+11*scale,y-5*scale)});}
            e.Graphics.Restore(previous);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {var item=e.Item as ToolStripMenuItem;float scale=e.Item.Owner==null?1F:e.Item.Owner.DeviceDpi/96F;e.TextColor=!e.Item.Enabled?DialogPalette.Muted:item!=null&&item.Checked?DialogPalette.Accent:DialogPalette.Text;e.TextRectangle=new Rectangle((int)(12*scale),0,Math.Max(1,e.Item.Width-(int)(44*scale)),e.Item.Height);e.TextFormat=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix;base.OnRenderItemText(e);}
    }

    internal sealed class FeedbackBadge : Control
    {
        readonly MessageBoxIcon kind;
        internal FeedbackBadge(MessageBoxIcon icon)
        {
            kind=icon;Size=new Size(44,44);TabStop=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            bool warning=kind==MessageBoxIcon.Warning,error=kind==MessageBoxIcon.Error,success=kind==MessageBoxIcon.Information;
            Color tint=error?DialogPalette.DangerSoft:warning?Color.FromArgb(253,241,216):success?Color.FromArgb(227,245,236):DialogPalette.AccentSoft;
            Color ink=error?DialogPalette.Danger:warning?Color.FromArgb(154,100,0):success?Color.FromArgb(35,136,91):DialogPalette.Accent;
            using(var canvas=new SolidBrush(Parent==null?DialogPalette.Surface:Parent.BackColor))e.Graphics.FillRectangle(canvas,ClientRectangle);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=DesignTheme.Round(new RectangleF(0,0,Width,Height),12))using(var fill=new SolidBrush(tint))e.Graphics.FillPath(fill,shape);
            var state=e.Graphics.Save();e.Graphics.TranslateTransform((Width-24)/2F,(Height-24)/2F);
            using(var pen=new Pen(ink,1.8F){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            {
                if(success)e.Graphics.DrawLines(pen,new[]{new Point(4,12),new Point(10,18),new Point(20,7)});
                else if(warning||error)
                {e.Graphics.DrawPolygon(pen,new[]{new Point(12,3),new Point(22,21),new Point(2,21)});e.Graphics.DrawLine(pen,12,10,12,15);e.Graphics.DrawLine(pen,12,18,12,18.2F);}
                else
                {e.Graphics.DrawLines(pen,new[]{new Point(4,8),new Point(20,8),new Point(16,4)});e.Graphics.DrawLines(pen,new[]{new Point(20,16),new Point(4,16),new Point(8,20)});}
            }
            e.Graphics.Restore(state);
        }
    }

    // This changes the presentation of existing messages, not their results or callers.
    internal static class DesignMessageBox
    {
        internal static DialogResult Show(IWin32Window owner,string text,string caption)
        {return Show(owner,text,caption,MessageBoxButtons.OK,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
        internal static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon)
        {return Show(owner,text,caption,buttons,icon,MessageBoxDefaultButton.Button1);}
        internal static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton defaultButton)
        {
            if(buttons!=MessageBoxButtons.OK&&buttons!=MessageBoxButtons.YesNo)return MessageBox.Show(owner,text,caption,buttons,icon,defaultButton);
            using(var dialog=new LightMessageDialog(text,caption,buttons,icon))return dialog.ShowDialog(owner);
        }
    }

    internal sealed class LightMessageDialog : Form
    {
        internal LightMessageDialog(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon)
        {
            Text=caption;Font=new Font("Malgun Gothic",10F);BackColor=DialogPalette.Surface;ForeColor=DialogPalette.Text;
            StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
            bool question=buttons==MessageBoxButtons.YesNo;
            ControlBox=!question;KeyPreview=true;
            string heading=caption,body=text??"";
            int paragraph=body.IndexOf("\n\n",StringComparison.Ordinal);
            if(question&&paragraph>0){heading=body.Substring(0,paragraph);body=body.Substring(paragraph+2);}
            bool help=caption=="사용 방법";
            int contentWidth=help?520:400;
            int measured=TextRenderer.MeasureText(body,Font,new Size(contentWidth,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl|TextFormatFlags.NoPrefix).Height;
            int contentHeight=Math.Max(help?240:48,Math.Min(help?420:300,measured+16));
            ClientSize=new Size(help?628:532,contentHeight+150);
            var badge=new FeedbackBadge(icon){Location=new Point(24,24)};Controls.Add(badge);
            var title=new Label {Text=heading,Font=new Font(Font.FontFamily,12F,FontStyle.Bold),ForeColor=DialogPalette.Text,Location=new Point(84,24),Size=new Size(ClientSize.Width-108,42),AutoEllipsis=true};Controls.Add(title);
            var details=new DesignTextView {Text=body,Font=Font,BackColor=DialogPalette.Surface,ForeColor=DialogPalette.Muted,TabStop=false,Location=new Point(help?24:84,76),Size=new Size(help?ClientSize.Width-48:contentWidth,contentHeight)};
            if(help)
            {
                badge.Visible=false;title.Location=new Point(24,24);title.Width=ClientSize.Width-48;
                var steps=new HelpStepsPanel(body,Font);var scroll=new DesignScrollHost(steps){Dock=DockStyle.None,Bounds=new Rectangle(24,76,ClientSize.Width-48,contentHeight),BackColor=DialogPalette.Surface};Controls.Add(scroll);details.Dispose();
            }
            else Controls.Add(details);
            int top=ClientSize.Height-68;
            Button affirmativeButton=null,negativeButton=null;
            if(question)
            {
                string affirmative=caption=="모드 변경"?"예, 변경":caption=="게임 종료 · 방 삭제"?"예, 종료":"예";
                var yes=new ConfirmationChoiceButton(affirmative);yes.DialogResult=DialogResult.Yes;yes.SetBounds(ClientSize.Width-250,top,122,44);
                if(caption=="게임 종료 · 방 삭제")yes.SelectionColor=DialogPalette.Danger;
                var no=new ConfirmationChoiceButton("아니요");no.DialogResult=DialogResult.No;no.SetBounds(ClientSize.Width-120,top,96,44);
                Controls.Add(yes);Controls.Add(no);ConfirmationChoiceButton.Bind(this,yes,no);
                if(caption=="모드 변경")CancelButton=no;
                affirmativeButton=yes;negativeButton=no;
            }
            else
            {
                var close=DialogPalette.Button(help?"닫기":"확인",!help);close.DialogResult=DialogResult.OK;close.SetBounds(ClientSize.Width-128,top,104,44);Controls.Add(close);AcceptButton=CancelButton=close;ActiveControl=close;
                FormClosing+=delegate {DialogResult=DialogResult.OK;};
            }
            KeyDown+=delegate(object sender,KeyEventArgs e){
                if(question&&!e.Control&&(e.Modifiers==Keys.None||e.Alt)&&(e.KeyCode==Keys.Y||e.KeyCode==Keys.N))
                {(e.KeyCode==Keys.Y?affirmativeButton:negativeButton).PerformClick();e.Handled=e.SuppressKeyPress=true;return;}
                if(e.Control&&e.KeyCode==Keys.C){try{Clipboard.SetText(caption+Environment.NewLine+Environment.NewLine+(text??""));}catch(System.Runtime.InteropServices.ExternalException){}e.Handled=true;}
            };
        }
    }

    internal sealed class HelpStepsPanel : Panel
    {
        readonly Label[] paragraphs;
        readonly DialogSurface[] numbers;
        internal HelpStepsPanel(string text,Font font)
        {
            SuspendLayout();
            Font=font;BackColor=DialogPalette.Surface;AutoScroll=true;
            string[] lines=(text??"").Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries);
            paragraphs=new Label[lines.Length];numbers=new DialogSurface[lines.Length];
            for(int index=0;index<lines.Length;index++)
            {
                var label=new Label {Text=lines[index],AutoSize=true,Font=font,ForeColor=DialogPalette.Text,Margin=Padding.Empty};paragraphs[index]=label;Controls.Add(label);
                var chip=new DialogSurface {Size=new Size(28,28),CornerRadius=8,BackColor=DialogPalette.AccentSoft,DrawBorder=false,Padding=new Padding(6,0,6,0)};
                chip.Controls.Add(new Label {Text=(index+1).ToString(),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=DialogPalette.Accent,Font=font});numbers[index]=chip;Controls.Add(chip);
            }
            ResumeLayout(true);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);if(paragraphs==null)return;
            int top=AutoScrollPosition.Y;
            for(int index=0;index<paragraphs.Length;index++)
            {
                var label=paragraphs[index];if(label==null||numbers[index]==null)continue;label.MaximumSize=new Size(Math.Max(80,ClientSize.Width-58),0);label.Location=new Point(40,top+3);numbers[index].Location=new Point(0,top);
                top+=Math.Max(28,label.PreferredHeight)+16;
            }
        }
    }
}
