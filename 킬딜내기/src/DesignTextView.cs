using System;
using System.Drawing;
using System.Windows.Forms;

namespace PubgScore
{
    // Plain text remains selectable; the themed bar only changes the viewport.
    internal sealed class DesignTextView : UserControl
    {
        sealed class PlainTextBox : RichTextBox
        {
            const int EmGetLineCount=0x00BA,EmGetFirstVisibleLine=0x00CE,EmLineScroll=0x00B6,WmMouseWheel=0x020A;

            internal int VisualLineCount
            {
                get
                {
                    if(!IsHandleCreated)return 1;
                    var message=Message.Create(Handle,EmGetLineCount,IntPtr.Zero,IntPtr.Zero);
                    base.WndProc(ref message);return Math.Max(1,message.Result.ToInt32());
                }
            }
            internal int FirstVisibleLine
            {
                get
                {
                    if(!IsHandleCreated)return 0;
                    var message=Message.Create(Handle,EmGetFirstVisibleLine,IntPtr.Zero,IntPtr.Zero);
                    base.WndProc(ref message);return Math.Max(0,message.Result.ToInt32());
                }
            }
            internal void NativeLineScroll(int lines)
            {
                if(!IsHandleCreated||lines==0)return;
                var message=Message.Create(Handle,EmLineScroll,IntPtr.Zero,new IntPtr(lines));
                base.WndProc(ref message);
            }
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;
                base.OnMouseWheel(e);
            }
            protected override void WndProc(ref Message message)
            {
                // Route each native wheel message once, with no native second pass.
                if(message.Msg==WmMouseWheel)
                {
                    int delta=unchecked((short)((message.WParam.ToInt64()>>16)&0xFFFF));
                    OnMouseWheel(new HandledMouseEventArgs(MouseButtons.None,0,0,0,delta));
                    message.Result=IntPtr.Zero;return;
                }
                base.WndProc(ref message);
            }
        }

        readonly PlainTextBox plain;
        readonly DesignScrollBar scrollBar;
        string sourceText=String.Empty;
        bool synchronizing,layoutDirty=true,barShown,assigningText,disposed;
        long wheelRemainder;

        internal DesignTextView()
        {
            TabStop=false;BackColor=Color.White;ForeColor=Color.FromArgb(29,36,51);
            plain=new PlainTextBox {
                BorderStyle=BorderStyle.None,ReadOnly=true,Multiline=true,WordWrap=true,
                ScrollBars=RichTextBoxScrollBars.None,DetectUrls=false,HideSelection=false,
                ShortcutsEnabled=true,AutoWordSelection=false,TabStop=false,CausesValidation=false,
                BackColor=BackColor,ForeColor=ForeColor,Font=Font
            };
            scrollBar=new DesignScrollBar {Visible=false,BackColor=BackColor,SmallChange=1};
            Controls.Add(plain);Controls.Add(scrollBar);
            plain.TextChanged+=PlainTextChanged;
            plain.Resize+=PlainGeometryChanged;
            plain.FontChanged+=PlainGeometryChanged;
            plain.HandleCreated+=PlainGeometryChanged;
            plain.VScroll+=PlainViewChanged;
            plain.SelectionChanged+=PlainViewChanged;
            plain.ContentsResized+=PlainContentsResized;
            plain.MouseWheel+=PlainMouseWheel;
            scrollBar.ValueChanged+=ScrollBarValueChanged;
            SynchronizeScrollBar();
        }

        internal RichTextBox TextBox {get{return plain;}}
        internal DesignScrollBar ScrollBar {get{return scrollBar;}}
        public override string Text
        {
            get{return sourceText??String.Empty;}
            set
            {
                string next=value??String.Empty;
                if(String.Equals(sourceText,next,StringComparison.Ordinal))return;
                sourceText=next;wheelRemainder=0;layoutDirty=true;
                if(plain!=null)
                {
                    assigningText=true;
                    try{plain.Text=next;}finally{assigningText=false;}
                }
                OnTextChanged(EventArgs.Empty);SynchronizeScrollBar();
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if(plain==null)return;
            plain.Font=Font;layoutDirty=true;SynchronizeScrollBar();
        }
        protected override void OnForeColorChanged(EventArgs e)
        {
            base.OnForeColorChanged(e);if(plain!=null)plain.ForeColor=ForeColor;
        }
        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            if(plain!=null)plain.BackColor=BackColor;
            if(scrollBar!=null)scrollBar.BackColor=BackColor;
        }
        protected override void OnTabStopChanged(EventArgs e){base.OnTabStopChanged(e);if(plain!=null)plain.TabStop=TabStop;}
        protected override void OnResize(EventArgs e){base.OnResize(e);layoutDirty=true;SynchronizeScrollBar();}
        protected override void OnPaddingChanged(EventArgs e){base.OnPaddingChanged(e);layoutDirty=true;SynchronizeScrollBar();}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible)SynchronizeScrollBar();}

        void PlainTextChanged(object sender,EventArgs e)
        {
            if(!assigningText){sourceText=plain.Text;OnTextChanged(EventArgs.Empty);}
            layoutDirty=true;SynchronizeScrollBar();
        }
        void PlainGeometryChanged(object sender,EventArgs e){if(!synchronizing)layoutDirty=true;SynchronizeScrollBar();}
        void PlainViewChanged(object sender,EventArgs e){SynchronizeScrollBar();}
        void PlainContentsResized(object sender,ContentsResizedEventArgs e){if(!synchronizing)layoutDirty=true;SynchronizeScrollBar();}

        void LayoutBody(bool showBar)
        {
            int width=Math.Max(0,ClientSize.Width-Padding.Horizontal),height=Math.Max(0,ClientSize.Height-Padding.Vertical);
            int barWidth=showBar?Math.Min(width,Math.Max(1,(int)Math.Round(16F*DeviceDpi/96F))):0;
            var textBounds=new Rectangle(Padding.Left,Padding.Top,width-barWidth,height);
            if(plain.Bounds!=textBounds)plain.Bounds=textBounds;
            var barBounds=new Rectangle(Padding.Left+width-barWidth,Padding.Top,barWidth,height);
            if(scrollBar.Bounds!=barBounds)scrollBar.Bounds=barBounds;
            if(barShown!=showBar){barShown=showBar;scrollBar.Visible=showBar;}
        }
        int VisibleLineCount()
        {
            int lineHeight=Math.Max(1,plain.Font.Height),topInset=1;
            if(plain.IsHandleCreated)
            {
                int first=plain.FirstVisibleLine;
                int start=plain.GetFirstCharIndexFromLine(first),next=plain.GetFirstCharIndexFromLine(first+1);
                if(start>=0)
                {
                    var position=plain.GetPositionFromCharIndex(start);
                    topInset=Math.Max(0,position.Y);
                    if(next>start)
                    {
                        int measured=plain.GetPositionFromCharIndex(next).Y-position.Y;
                        if(measured>0)lineHeight=measured;
                    }
                }
            }
            return Math.Max(1,Math.Max(0,plain.ClientSize.Height-topInset-1)/lineHeight);
        }

        // This is also available to component fixtures; normal updates are event driven.
        internal void SynchronizeScrollBar()
        {
            if(plain==null||scrollBar==null||disposed||Disposing||synchronizing)return;
            synchronizing=true;
            try
            {
                if(!plain.IsHandleCreated)
                {
                    LayoutBody(false);scrollBar.SetRange(0,1,0);return;
                }
                int desiredLine=plain.FirstVisibleLine;
                if(layoutDirty)
                {
                    int topCharacter=plain.GetFirstCharIndexFromLine(desiredLine);
                    // Probe the full width so a no-longer-needed bar cannot remain stuck.
                    LayoutBody(false);
                    bool needed=plain.VisualLineCount>VisibleLineCount();
                    if(needed)LayoutBody(true);
                    desiredLine=topCharacter<0?0:plain.GetLineFromCharIndex(Math.Min(plain.TextLength,topCharacter));
                    layoutDirty=false;
                }
                int visible=VisibleLineCount(),maximum=Math.Max(0,plain.VisualLineCount-visible);
                if(maximum==0)
                {
                    if(barShown)LayoutBody(false);
                    visible=VisibleLineCount();
                    desiredLine=0;
                }
                else if(!barShown)
                {
                    LayoutBody(true);visible=VisibleLineCount();maximum=Math.Max(0,plain.VisualLineCount-visible);
                }
                desiredLine=Math.Max(0,Math.Min(maximum,desiredLine));
                // A range clamp must move the native viewport too, not only the thumb.
                plain.NativeLineScroll(desiredLine-plain.FirstVisibleLine);
                scrollBar.LargeChange=Math.Max(1,visible-1);
                scrollBar.SetRange(maximum,visible,plain.FirstVisibleLine);
            }
            finally{synchronizing=false;}
        }
        void ScrollBarValueChanged(object sender,EventArgs e)
        {
            if(synchronizing||disposed||!plain.IsHandleCreated)return;
            synchronizing=true;
            try{plain.NativeLineScroll(scrollBar.Value-plain.FirstVisibleLine);}
            finally{synchronizing=false;}
            SynchronizeScrollBar();
        }
        void PlainMouseWheel(object sender,MouseEventArgs e)
        {
            var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;
            if(disposed||!plain.Enabled||!plain.IsHandleCreated)return;
            int lines=SystemInformation.MouseWheelScrollLines;
            if(lines==0){wheelRemainder=0;return;}
            wheelRemainder+=e.Delta;
            long notches=wheelRemainder/120;wheelRemainder%=120;
            if(notches==0)return;
            int visible=VisibleLineCount(),maximum=Math.Max(0,plain.VisualLineCount-visible);
            long step=lines<0?Math.Max(1,visible-1):lines;
            int target=(int)Math.Max(0L,Math.Min((long)maximum,plain.FirstVisibleLine-notches*step));
            synchronizing=true;
            try{plain.NativeLineScroll(target-plain.FirstVisibleLine);}
            finally{synchronizing=false;}
            SynchronizeScrollBar();
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing&&!disposed)
            {
                disposed=true;
                if(plain!=null)
                {
                    plain.TextChanged-=PlainTextChanged;plain.Resize-=PlainGeometryChanged;
                    plain.FontChanged-=PlainGeometryChanged;plain.HandleCreated-=PlainGeometryChanged;
                    plain.VScroll-=PlainViewChanged;plain.SelectionChanged-=PlainViewChanged;
                    plain.ContentsResized-=PlainContentsResized;plain.MouseWheel-=PlainMouseWheel;
                }
                if(scrollBar!=null)scrollBar.ValueChanged-=ScrollBarValueChanged;
            }
            base.Dispose(disposing);
        }
    }
}
