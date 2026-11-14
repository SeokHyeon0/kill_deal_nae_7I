using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PubgScore
{
    // A passive child overlay; it never becomes a window, editor, or mouse target.
    internal sealed class ResultCopyToast : Control
    {
        readonly Font heading,detail;
        internal ResultCopyToast(Font headingFont,Font detailFont)
        {
            heading=headingFont;detail=detailFont;TabStop=false;CausesValidation=false;
            AccessibleRole=AccessibleRole.StatusBar;AccessibleName="결과가 복사되었습니다";
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            SetStyle(ControlStyles.Selectable,false);
        }
        protected override void WndProc(ref Message message)
        {if(message.Msg==0x84){message.Result=new IntPtr(-1);return;}base.WndProc(ref message);}
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);if(Width<2||Height<2)return;
            using(var shape=DesignTheme.Round(new RectangleF(0,0,Width,Height),14*DeviceDpi/96F))
            {var previous=Region;Region=new Region(shape);if(previous!=null)previous.Dispose();}
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if(Width<2||Height<2)return;float scale=DeviceDpi/96F;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=DesignTheme.Round(new RectangleF(.5F,.5F,Width-1,Height-1),14*scale))
            {using(var fill=new SolidBrush(DialogPalette.Surface))e.Graphics.FillPath(fill,shape);using(var border=new Pen(Color.FromArgb(205,230,217),scale))e.Graphics.DrawPath(border,shape);}
            var badge=new RectangleF(16*scale,22*scale,40*scale,40*scale);
            using(var shape=DesignTheme.Round(badge,12*scale))using(var fill=new SolidBrush(Color.FromArgb(227,245,236)))e.Graphics.FillPath(fill,shape);
            using(var check=new Pen(Color.FromArgb(35,136,91),2.2F*scale){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
                e.Graphics.DrawLines(check,new[]{new PointF(27*scale,42*scale),new PointF(34*scale,49*scale),new PointF(46*scale,35*scale)});
            var flags=TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;
            int left=(int)Math.Round(72*scale),width=Math.Max(0,Width-left-(int)Math.Round(16*scale));
            TextRenderer.DrawText(e.Graphics,"결과가 복사되었습니다",heading,new Rectangle(left,(int)(16*scale),width,(int)(26*scale)),DialogPalette.Text,flags);
            TextRenderer.DrawText(e.Graphics,"그림판·엑셀에서 Ctrl+V로 붙여넣기",detail,new Rectangle(left,(int)(43*scale),width,(int)(23*scale)),DialogPalette.Muted,flags);
        }
        protected override void Dispose(bool disposing)
        {if(disposing){var previous=Region;Region=null;if(previous!=null)previous.Dispose();}base.Dispose(disposing);}
    }

    public sealed partial class MainForm
    {
        ResultCopyToast resultCopyToast;
        Timer resultCopyToastTimer;
        bool resultCopyToastEventsBound;
        void ShowResultCopiedToast()
        {
            if(closing||IsDisposed||Disposing)return;
            if(resultCopyToast==null||resultCopyToast.IsDisposed)
            {resultCopyToast=new ResultCopyToast(UiFont(10F,FontStyle.Bold),UiFont(9F)){Visible=false};Controls.Add(resultCopyToast);}
            if(!resultCopyToastEventsBound)
            {
                resultCopyToastEventsBound=true;ClientSizeChanged+=PositionResultCopiedToast;
                FormClosing+=CloseResultCopiedToast;FormClosed+=DisposeResultCopiedToast;Disposed+=DisposeResultCopiedToast;
            }
            PositionResultCopiedToast(this,EventArgs.Empty);resultCopyToast.Visible=true;resultCopyToast.BringToFront();
            if(resultCopyToastTimer==null){resultCopyToastTimer=new Timer {Interval=3000};resultCopyToastTimer.Tick+=ExpireResultCopiedToast;}
            resultCopyToastTimer.Stop();resultCopyToastTimer.Start();
        }
        void PositionResultCopiedToast(object sender,EventArgs e)
        {
            if(resultCopyToast==null||resultCopyToast.IsDisposed)return;float scale=DeviceDpi/96F;
            int inset=(int)Math.Round(16*scale),footer=(int)Math.Round(55*scale);
            int width=Math.Max(1,Math.Min((int)Math.Round(380*scale),ClientSize.Width-inset*2));
            int height=Math.Max(1,Math.Min((int)Math.Round(84*scale),ClientSize.Height-footer-inset*2));
            resultCopyToast.SetBounds(Math.Max(0,ClientSize.Width-inset-width),Math.Max(0,ClientSize.Height-footer-inset-height),width,height);
        }
        void ExpireResultCopiedToast(object sender,EventArgs e){DismissResultCopiedToast();}
        void DismissResultCopiedToast()
        {
            if(resultCopyToastTimer!=null){resultCopyToastTimer.Stop();resultCopyToastTimer.Tick-=ExpireResultCopiedToast;resultCopyToastTimer.Dispose();resultCopyToastTimer=null;}
            if(resultCopyToast!=null&&!resultCopyToast.IsDisposed)resultCopyToast.Visible=false;
        }
        void CloseResultCopiedToast(object sender,FormClosingEventArgs e){if(!e.Cancel)DismissResultCopiedToast();}
        void DisposeResultCopiedToast(object sender,EventArgs e)
        {
            DismissResultCopiedToast();if(resultCopyToast!=null){resultCopyToast.Dispose();resultCopyToast=null;}
            if(!resultCopyToastEventsBound)return;resultCopyToastEventsBound=false;
            ClientSizeChanged-=PositionResultCopiedToast;FormClosing-=CloseResultCopiedToast;
            FormClosed-=DisposeResultCopiedToast;Disposed-=DisposeResultCopiedToast;
        }
    }
}
