using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PubgScore
{
    internal static class RosterInputPolicy
    {
        internal const int MaximumLines=4;
        internal static string NormalizeBreaks(string value)
        {return (value??"").Replace("\r\n","\n").Replace('\r','\n').Replace('\v','\n').Replace('\f','\n').Replace('\u0085','\n').Replace('\u2028','\n').Replace('\u2029','\n');}
        internal static char NormalizeInputCharacter(char value)
        {return value=='\r'||value=='\n'||value=='\v'||value=='\f'||value=='\u0085'||value=='\u2028'||value=='\u2029'?'\r':value;}
        internal static bool IsFormattingShortcut(Keys keyData)
        {
            Keys key=keyData&Keys.KeyCode,modifiers=keyData&Keys.Modifiers;
            if((modifiers&Keys.Control)==0||(modifiers&Keys.Alt)!=0)return false;
            return key==Keys.B||key==Keys.I||key==Keys.U||key==Keys.E||key==Keys.J||key==Keys.L||key==Keys.R||
                key==Keys.Add||key==Keys.Subtract||key==Keys.Oemplus||key==Keys.OemMinus||key==Keys.D0||key==Keys.D1||key==Keys.D2||key==Keys.D5||
                key==Keys.OemOpenBrackets||key==Keys.OemCloseBrackets||
                ((modifiers&Keys.Shift)!=0&&(key==Keys.OemPeriod||key==Keys.Oemcomma||key==Keys.N||key==Keys.Space));
        }
        internal static int LineCount(string value)
        {
            string normalized=NormalizeBreaks(value);int count=1;
            foreach(char character in normalized)if(character=='\n')count++;
            return count;
        }
        internal static bool HasAtMostFourLines(string value){return LineCount(value)<=MaximumLines;}
        internal static bool TryReplace(string current,int start,int length,string insertion,bool paste,out string result)
        {string prepared;return TryPrepareInsertion(current,start,length,insertion,paste,out prepared,out result);}
        internal static bool TryPrepareInsertion(string current,int start,int length,string insertion,bool paste,out string prepared,out string result)
        {
            current=current??"";start=Math.Max(0,Math.Min(current.Length,start));length=Math.Max(0,Math.Min(current.Length-start,length));
            prepared=NormalizeBreaks(insertion);
            result=NormalizeBreaks(current.Substring(0,start)+prepared+current.Substring(start+length));
            if(HasAtMostFourLines(result))return true;
            // Spreadsheet exports append one terminator after their last row. Never trim team tabs or empty rows generally.
            if(paste&&prepared.EndsWith("\n",StringComparison.Ordinal))
            {
                string withoutTerminator=prepared.Substring(0,prepared.Length-1);
                string candidate=NormalizeBreaks(current.Substring(0,start)+withoutTerminator+current.Substring(start+length));
                if(HasAtMostFourLines(candidate)){prepared=withoutTerminator;result=candidate;return true;}
            }
            result=current;return false;
        }
        internal static bool TryDistribute(string[] rows,int tier,int start,int length,string text,bool all,out string[] result,out int caretTier,out int caretPosition)
        {
            result=new string[4];for(int i=0;i<4;i++)result[i]=rows!=null&&i<rows.Length?(rows[i]??""):"";
            caretTier=Math.Max(0,Math.Min(3,tier));caretPosition=0;if(tier<0||tier>3)return false;
            string insertion=NormalizeBreaks(text);int first=all?0:tier;string[] lines=insertion.Split('\n');
            if(lines.Length>4-first&&insertion.EndsWith("\n",StringComparison.Ordinal))lines=insertion.Substring(0,insertion.Length-1).Split('\n');
            if(lines.Length>4-first)return false;
            if(all){for(int i=0;i<4;i++)result[i]=i<lines.Length?lines[i]:"";caretTier=lines.Length-1;caretPosition=lines[lines.Length-1].Length;return true;}
            string original=result[tier];start=Math.Max(0,Math.Min(original.Length,start));length=Math.Max(0,Math.Min(original.Length-start,length));
            string before=original.Substring(0,start),after=original.Substring(start+length);
            if(lines.Length==1){result[tier]=before+lines[0]+after;caretTier=tier;caretPosition=start+lines[0].Length;return true;}
            result[tier]=before+lines[0];for(int i=1;i<lines.Length;i++)result[tier+i]=lines[i];
            caretTier=tier+lines.Length-1;caretPosition=lines[lines.Length-1].Length;result[caretTier]+=after;return true;
        }
        internal static string AcceptOrRestore(string accepted,string candidate,out bool rejected)
        {rejected=!HasAtMostFourLines(candidate);return rejected?(accepted??""):(candidate??"");}
    }

    internal sealed class RosterEditorSnapshot
    {
        internal readonly string[] Rows=new string[4];
        internal readonly int[] Starts=new int[4],Lengths=new int[4];
        internal int Active;internal bool All;
        internal bool SameText(RosterEditorSnapshot other)
        {if(other==null)return false;for(int i=0;i<4;i++)if(Rows[i]!=other.Rows[i])return false;return true;}
    }

    internal sealed class RosterEditorBox : UserControl
    {
        internal const float FixedFontSize=42F;
        internal const int FixedRowHeight=70;
        readonly RosterTierInput[] inputs=new RosterTierInput[4];
        readonly Label[] labels=new Label[4];
        readonly Panel[] hosts=new Panel[4];
        readonly string[] lastAccepted={"","","",""};
        readonly List<RosterEditorSnapshot> undo=new List<RosterEditorSnapshot>(),redo=new List<RosterEditorSnapshot>();
        readonly Font inputFont=new Font("Consolas",42F,FontStyle.Regular,GraphicsUnit.Point),labelFont=new Font("Malgun Gothic",9F);
        readonly Rectangle[] reportedRows=new Rectangle[4];
        TableLayoutPanel layout;
        RosterEditorSnapshot current,compositionBefore;
        bool ready,applying,placing,allSelected,selecting,enterDown,movingArrow;
        int? preferredArrowX;
        int activeTier,compositionTier=-1;
        string pendingText;
        internal event EventHandler InputRejected;
        internal event EventHandler DisplayMetricsChanged;
        internal int ActiveTier {get{return activeTier;}}
        internal int MinimumRowHeightPixels {get{return (int)Math.Ceiling(inputFont.GetHeight(DeviceDpi))+Scale(26);}}
        internal Font FixedInputFont {get{return inputFont;}}
        internal RosterEditorBox()
        {
            SuspendLayout();TabStop=false;BackColor=DialogPalette.Field;
            layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Margin=Padding.Empty,Padding=Padding.Empty,BackColor=DialogPalette.Field};
            layout.SuspendLayout();layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,62));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for(int i=0;i<4;i++)layout.RowStyles.Add(new RowStyle(SizeType.Percent,25));
            for(int i=0;i<4;i++)
            {
                int tier=i;
                var label=new Label {Text=(i+1)+"티어",Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleCenter,Font=labelFont,ForeColor=DialogPalette.Muted,BackColor=DialogPalette.Field};
                label.Paint+=delegate(object sender,PaintEventArgs e){int gap=Scale(12);using(var pen=new Pen(DialogPalette.Border))e.Graphics.DrawLine(pen,label.Width-1,gap,label.Width-1,Math.Max(gap,label.Height-gap));};
                var host=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(16,0,6,0),BackColor=DialogPalette.Field,TabIndex=i};
                var input=new RosterTierInput(this,i) {Font=inputFont,BorderStyle=BorderStyle.None,Multiline=true,WordWrap=false,AcceptsTab=true,AcceptsReturn=false,ScrollBars=ScrollBars.None,BackColor=DialogPalette.Field,ForeColor=DialogPalette.Text,HideSelection=false,TabIndex=0,AccessibleName=(i+1)+"티어 선수 이름"};
                inputs[i]=input;hosts[i]=host;labels[i]=label;
                input.TextChanged+=delegate {TierTextChanged(tier);};host.Layout+=delegate {PlaceTier(tier);};
                host.Controls.Add(input);layout.Controls.Add(label,0,i);layout.Controls.Add(host,1,i);
            }
            Controls.Add(layout);ready=true;current=CaptureSnapshot();layout.ResumeLayout(true);ResumeLayout(true);
            if(!String.IsNullOrEmpty(pendingText)){string initial=pendingText;pendingText=null;Text=initial;undo.Clear();redo.Clear();}
        }
        public override string Text
        {
            get{return !ready?(pendingText??""):String.Join("\n",RowValues());}
            set
            {
                if(!ready){pendingText=value??"";return;}
                string[] result;int tier,position;
                if(!RosterInputPolicy.TryDistribute(RowValues(),0,0,0,value,true,out result,out tier,out position)){Reject();return;}
                ApplyRows(result,tier,position,true);
            }
        }
        internal TextBox GetTierInput(int index){if(index<0||index>3)throw new ArgumentOutOfRangeException("index");return inputs[index];}
        internal Rectangle TierBounds(int index)
        {
            if(index<0||index>3)throw new ArgumentOutOfRangeException("index");
            if(!ready||layout==null||labels[index]==null)return Rectangle.Empty;
            return new Rectangle(layout.Left,layout.Top+labels[index].Top,layout.ClientSize.Width,labels[index].Height);
        }
        int Scale(int value){return Math.Max(1,(int)Math.Round(value*DeviceDpi/96F));}
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);if(!ready||layout==null)return;
            layout.ColumnStyles[0].Width=Scale(62);
            for(int i=0;i<4;i++){hosts[i].Padding=new Padding(Scale(16),0,Scale(6),0);PlaceTier(i);}
            bool changed=false;for(int i=0;i<4;i++){Rectangle row=TierBounds(i);if(row!=reportedRows[i]){reportedRows[i]=row;changed=true;}}
            if(changed){var handler=DisplayMetricsChanged;if(handler!=null)handler(this,EventArgs.Empty);}
        }
        void PlaceTier(int index)
        {
            if(!ready||placing||hosts[index]==null||inputs[index]==null)return;placing=true;
            try
            {
                var host=hosts[index];int height=(int)Math.Ceiling(inputFont.GetHeight(DeviceDpi))+Scale(2);
                inputs[index].SetBounds(host.Padding.Left,Math.Max(0,(host.ClientSize.Height-height)/2),Math.Max(1,host.ClientSize.Width-host.Padding.Horizontal),height);
            }
            finally{placing=false;}
        }
        string[] RowValues(){return new[]{inputs[0].Text,inputs[1].Text,inputs[2].Text,inputs[3].Text};}
        RosterEditorSnapshot CaptureSnapshot()
        {
            var snapshot=new RosterEditorSnapshot {Active=activeTier,All=allSelected};
            for(int i=0;i<4;i++){snapshot.Rows[i]=inputs[i].Text;snapshot.Starts[i]=inputs[i].SelectionStart;snapshot.Lengths[i]=inputs[i].SelectionLength;}
            return snapshot;
        }
        void RememberSelection(){if(ready&&!applying&&compositionTier<0)current=CaptureSnapshot();}
        void PushUndo(RosterEditorSnapshot snapshot){undo.Add(snapshot);if(undo.Count>100)undo.RemoveAt(0);redo.Clear();}
        void RecordChange(RosterEditorSnapshot before)
        {var next=CaptureSnapshot();if(!next.SameText(before)){PushUndo(before);current=next;base.OnTextChanged(EventArgs.Empty);}else current=next;}
        bool Editable(int index){return ready&&Enabled&&inputs[index].Enabled&&!inputs[index].ReadOnly;}
        bool AllEditable(){for(int i=0;i<4;i++)if(!Editable(i))return false;return true;}
        void TierTextChanged(int index)
        {
            if(!ready||applying)return;
            preferredArrowX=null;
            if(RosterInputPolicy.NormalizeBreaks(inputs[index].Text).IndexOf('\n')>=0)
            {
                applying=true;
                try{int position=Math.Min(inputs[index].SelectionStart,lastAccepted[index].Length);inputs[index].Text=lastAccepted[index];inputs[index].Select(position,0);inputs[index].ClearUndo();}
                finally{applying=false;}
                Reject();return;
            }
            if(allSelected){applying=true;try{for(int i=0;i<4;i++)if(i!=index){inputs[i].Clear();lastAccepted[i]="";}allSelected=false;}finally{applying=false;}}
            lastAccepted[index]=inputs[index].Text;
            activeTier=index;
            if(compositionTier>=0){base.OnTextChanged(EventArgs.Empty);return;}
            RecordChange(current);
        }
        void ApplyRows(string[] rows,int tier,int position,bool record)
        {
            if(!ready)return;CommitComposition();var before=CaptureSnapshot();bool focus=ContainsFocus;applying=true;
            preferredArrowX=null;
            try
            {
                for(int i=0;i<4;i++){if(inputs[i].Text!=rows[i])inputs[i].Text=rows[i];lastAccepted[i]=rows[i];inputs[i].Select(0,0);inputs[i].ClearUndo();}
                activeTier=Math.Max(0,Math.Min(3,tier));allSelected=false;inputs[activeTier].Select(Math.Max(0,Math.Min(position,inputs[activeTier].TextLength)),0);
                if(focus)inputs[activeTier].Focus();
            }
            finally{applying=false;}
            if(record)RecordChange(before);else current=CaptureSnapshot();
        }
        internal bool ApplyPasteText(int index,string text)
        {
            if(index<0||index>3||!Editable(index))return false;
            string[] result;int tier,position;
            if(!RosterInputPolicy.TryDistribute(RowValues(),index,inputs[index].SelectionStart,inputs[index].SelectionLength,text,allSelected,out result,out tier,out position)){Reject();return false;}
            for(int i=allSelected?0:index;i<=(allSelected?3:tier);i++)if(!Editable(i))return false;
            ApplyRows(result,tier,position,true);return true;
        }
        void PasteClipboard(int index)
        {
            if(!Editable(index))return;
            try{string value=Clipboard.GetText(TextDataFormat.UnicodeText);if(value.Length>0||Clipboard.ContainsText(TextDataFormat.UnicodeText))ApplyPasteText(index,value);}
            catch(ExternalException){}
        }
        internal void SelectAllTiers()
        {
            if(!ready)return;CommitComposition();selecting=true;
            try{allSelected=true;activeTier=0;if(ContainsFocus)inputs[0].Focus();for(int i=0;i<4;i++)inputs[i].SelectAll();}
            finally{selecting=false;}
            current=CaptureSnapshot();
        }
        void CancelWholeSelection(int index)
        {if(!allSelected||selecting||applying)return;allSelected=false;for(int i=0;i<4;i++)if(i!=index)inputs[i].Select(inputs[i].SelectionStart,0);}
        bool CopyWhole(bool cut)
        {
            if(!allSelected)return false;if(cut&&!AllEditable())return true;
            try{Clipboard.SetText(Text);if(cut)ApplyRows(new[]{"","","",""},0,0,true);}catch(ExternalException){}
            return true;
        }
        internal void DeleteSelection()
        {
            if(allSelected){if(AllEditable())ApplyRows(new[]{"","","",""},0,0,true);}
            else if(Editable(activeTier)){RememberSelection();inputs[activeTier].SelectedText="";}
        }
        void Restore(RosterEditorSnapshot snapshot)
        {
            preferredArrowX=null;
            bool focus=ContainsFocus;applying=true;
            try
            {
                for(int i=0;i<4;i++){inputs[i].Text=snapshot.Rows[i];lastAccepted[i]=snapshot.Rows[i];inputs[i].Select(snapshot.Starts[i],snapshot.Lengths[i]);inputs[i].ClearUndo();}
                activeTier=snapshot.Active;allSelected=snapshot.All;if(focus)inputs[activeTier].Focus();
            }
            finally{applying=false;}
            current=CaptureSnapshot();base.OnTextChanged(EventArgs.Empty);
        }
        internal bool UndoEdit()
        {
            if(!ready||!AllEditable()||compositionTier>=0||undo.Count==0)return false;
            var target=undo[undo.Count-1];undo.RemoveAt(undo.Count-1);redo.Add(CaptureSnapshot());Restore(target);return true;
        }
        internal bool RedoEdit()
        {
            if(!ready||!AllEditable()||compositionTier>=0||redo.Count==0)return false;
            var target=redo[redo.Count-1];redo.RemoveAt(redo.Count-1);undo.Add(CaptureSnapshot());if(undo.Count>100)undo.RemoveAt(0);Restore(target);return true;
        }
        internal void TierEntered(int index){if(!ready||applying)return;if(!movingArrow)preferredArrowX=null;activeTier=index;if(!selecting)CancelWholeSelection(index);RememberSelection();}
        internal void TierPointer(int index){if(!ready||applying)return;preferredArrowX=null;ReleaseEnter();CancelWholeSelection(index);activeTier=index;RememberSelection();}
        internal void TierAfterInput(){RememberSelection();}
        internal void CompositionStarted(int index){if(!ready)return;RememberSelection();compositionBefore=CaptureSnapshot();compositionTier=index;}
        internal void CompositionEnded(int index){if(compositionTier==index)CommitComposition();}
        void CommitComposition()
        {
            if(compositionTier<0)return;var before=compositionBefore;compositionTier=-1;compositionBefore=null;
            var next=CaptureSnapshot();if(before!=null&&!next.SameText(before))PushUndo(before);current=next;
        }
        internal bool HandleEnter(int index,bool composing)
        {
            preferredArrowX=null;
            if(!Editable(index))return true;if(composing){enterDown=true;return false;}if(enterDown)return true;
            enterDown=true;CommitComposition();CancelWholeSelection(index);activeTier=Math.Min(3,index+1);
            if(ContainsFocus)inputs[activeTier].Focus();RememberSelection();return true;
        }
        internal void ReleaseEnter(){enterDown=false;}
        protected override void OnLeave(EventArgs e){base.OnLeave(e);if(!ContainsFocus){ReleaseEnter();preferredArrowX=null;}}
        static bool IsArrow(Keys key){return key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down;}
        int LogicalCaretX(TextBox input,int position)
        {return LogicalCaretX(input,position,input.TextLength==0?0:input.GetPositionFromCharIndex(0).X);}
        int LogicalCaretX(TextBox input,int position,int originX)
        {
            if(position<=0||input.TextLength==0)return 0;
            position=Math.Min(position,input.TextLength);
            if(position<input.TextLength)return input.GetPositionFromCharIndex(position).X-originX;
            // Standard Edit does not expose a position for the insertion point after the final character.
            using(var graphics=input.CreateGraphics())
                return TextRenderer.MeasureText(graphics,input.Text,input.Font,new Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.ExpandTabs).Width;
        }
        int NearestCaret(TextBox input,int x)
        {
            if(input.TextLength==0)return 0;
            string text=input.Text;int[] starts=StringInfo.ParseCombiningCharacters(text);int best=0,originX=input.GetPositionFromCharIndex(0).X;long distance=Int64.MaxValue;
            for(int i=0;i<=starts.Length;i++)
            {
                int position=i==starts.Length?text.Length:starts[i];if(!SafeCaretBoundary(text,position))continue;
                long delta=Math.Abs((long)LogicalCaretX(input,position,originX)-x);
                if(delta<distance){distance=delta;best=position;}
            }
            return best;
        }
        static int CodePointAt(string text,int position)
        {
            char value=text[position];
            return Char.IsHighSurrogate(value)&&position+1<text.Length&&Char.IsLowSurrogate(text[position+1])?Char.ConvertToUtf32(value,text[position+1]):value;
        }
        static int PreviousCodePointStart(string text,int position)
        {int index=position-1;return index>0&&Char.IsLowSurrogate(text[index])&&Char.IsHighSurrogate(text[index-1])?index-1:index;}
        internal static bool SafeCaretBoundary(string text,int position)
        {
            if(position<=0||position>=text.Length)return true;
            if(Char.IsLowSurrogate(text[position])&&Char.IsHighSurrogate(text[position-1]))return false;
            int next=CodePointAt(text,position),previous=CodePointAt(text,PreviousCodePointStart(text,position));
            // Framework StringInfo predates extended emoji clusters; keep their joining units together.
            if(next==0x200d||previous==0x200d||next>=0x1f3fb&&next<=0x1f3ff||next>=0xfe00&&next<=0xfe0f||next>=0xe0020&&next<=0xe007f||next>=0xe0100&&next<=0xe01ef)return false;
            var category=Char.GetUnicodeCategory(text,position);
            if(category==UnicodeCategory.NonSpacingMark||category==UnicodeCategory.SpacingCombiningMark||category==UnicodeCategory.EnclosingMark)return false;
            if(next>=0x1f1e6&&next<=0x1f1ff&&previous>=0x1f1e6&&previous<=0x1f1ff)
            {
                int count=0,index=position;
                while(index>0){index=PreviousCodePointStart(text,index);int value=CodePointAt(text,index);if(value<0x1f1e6||value>0x1f1ff)break;count++;}
                if(count%2==1)return false;
            }
            return true;
        }
        void MoveArrowCaret(int source,int target,int position)
        {
            movingArrow=true;
            try
            {
                CancelWholeSelection(source);activeTier=target;
                if(ContainsFocus)inputs[target].Focus();
                inputs[target].Select(Math.Max(0,Math.Min(position,inputs[target].TextLength)),0);inputs[target].ScrollToCaret();RememberSelection();
            }
            finally{movingArrow=false;}
        }
        internal bool HandleArrow(int index,Keys key)
        {
            if(!ready||index<0||index>3||!IsArrow(key))return false;
            if(!Enabled||!inputs[index].Enabled)return true;
            if(inputs[index].Composing){preferredArrowX=null;return false;}
            var input=inputs[index];
            if(key==Keys.Up||key==Keys.Down)
            {
                int position=input.SelectionStart+(key==Keys.Down?input.SelectionLength:0);
                if(!preferredArrowX.HasValue)preferredArrowX=LogicalCaretX(input,position);
                if(input.SelectionLength>0)input.Select(position,0);
                int target=Math.Max(0,Math.Min(3,index+(key==Keys.Up?-1:1)));
                MoveArrowCaret(index,target,NearestCaret(inputs[target],preferredArrowX.Value));return true;
            }
            preferredArrowX=null;
            if(input.SelectionLength>0)return false;
            if(key==Keys.Left&&input.SelectionStart==0&&index>0){MoveArrowCaret(index,index-1,inputs[index-1].TextLength);return true;}
            if(key==Keys.Right&&input.SelectionStart==input.TextLength&&index<3){MoveArrowCaret(index,index+1,0);return true;}
            return false;
        }
        internal bool HandleCommand(int index,ref Message message,Keys keyData)
        {
            Keys key=keyData&Keys.KeyCode,modifiers=keyData&Keys.Modifiers;bool control=(modifiers&Keys.Control)!=0,alt=(modifiers&Keys.Alt)!=0;
            if(IsArrow(key)&&modifiers==Keys.None&&HandleArrow(index,key))return true;
            if(key!=Keys.Up&&key!=Keys.Down||modifiers!=Keys.None)preferredArrowX=null;
            if(key==Keys.Enter&&!alt)return HandleEnter(index,inputs[index].Composing);
            if(control&&!alt&&key==Keys.A){SelectAllTiers();return true;}
            if((control&&!alt&&key==Keys.V)||(modifiers==Keys.Shift&&key==Keys.Insert)){PasteClipboard(index);return true;}
            if(control&&!alt&&key==Keys.C&&allSelected){CopyWhole(false);return true;}
            if(control&&!alt&&key==Keys.X&&allSelected){CopyWhole(true);return true;}
            if(modifiers==Keys.Shift&&key==Keys.Delete&&allSelected){CopyWhole(true);return true;}
            if(control&&!alt&&(key==Keys.Z||key==Keys.Y))
            {if(inputs[index].Composing)return false;if(key==Keys.Y||(modifiers&Keys.Shift)!=0)RedoEdit();else UndoEdit();return true;}
            if(key==Keys.Tab&&modifiers==Keys.None){ApplyPasteText(index,"\t");return true;}
            if(allSelected&&(key==Keys.Delete||key==Keys.Back)){DeleteSelection();return true;}
            if(RosterInputPolicy.IsFormattingShortcut(keyData))return true;
            if(key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||key==Keys.Home||key==Keys.End||key==Keys.PageUp||key==Keys.PageDown||key==Keys.Escape)CancelWholeSelection(index);
            RememberSelection();return false;
        }
        internal bool HandleWindowMessage(int index,ref Message message)
        {
            const int KeyDown=0x100,KeyUp=0x101,Character=0x102,Cut=0x300,Copy=0x301,Paste=0x302,Clear=0x303,Undo=0x304,Redo=0x400+84,MouseWheel=0x20A;
            if(message.Msg==Character)
            {
                char value=(char)message.WParam.ToInt32();if(RosterInputPolicy.NormalizeInputCharacter(value)=='\r'||value=='\t')return true;
                RememberSelection();return false;
            }
            if(message.Msg==KeyDown)
            {
                Keys key=(Keys)message.WParam.ToInt32();
                if(IsArrow(key)&&Control.ModifierKeys==Keys.None&&HandleArrow(index,key))return true;
                if(key!=Keys.Up&&key!=Keys.Down||Control.ModifierKeys!=Keys.None)preferredArrowX=null;
                if(key==Keys.Enter)return HandleEnter(index,inputs[index].Composing);
                if(key==Keys.Tab&&Control.ModifierKeys==Keys.None){ApplyPasteText(index,"\t");return true;}
                if(allSelected&&(key==Keys.Delete||key==Keys.Back)){DeleteSelection();return true;}RememberSelection();
            }
            if(message.Msg==KeyUp&&(Keys)message.WParam.ToInt32()==Keys.Enter){ReleaseEnter();return true;}
            if(message.Msg==Paste){PasteClipboard(index);return true;}
            if(message.Msg==Copy&&allSelected)return CopyWhole(false);
            if(message.Msg==Cut){if(allSelected)return CopyWhole(true);RememberSelection();}
            if(message.Msg==Clear&&allSelected){DeleteSelection();return true;}
            if(message.Msg==Undo||message.Msg==Redo){if(inputs[index].Composing)return false;if(message.Msg==Undo)UndoEdit();else RedoEdit();return true;}
            if(message.Msg==MouseWheel&&(Control.ModifierKeys&Keys.Control)!=0)return true;
            return false;
        }
        void Reject(){var rejected=InputRejected;if(rejected!=null)rejected(this,EventArgs.Empty);}
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){inputFont.Dispose();labelFont.Dispose();}}
    }

    internal sealed class RosterTierInput : TextBox
    {
        readonly RosterEditorBox owner;readonly int tier;bool fixingFont;
        internal bool Composing {get;private set;}
        internal RosterTierInput(RosterEditorBox controller,int index){owner=controller;tier=index;}
        protected override bool ProcessCmdKey(ref Message message,Keys keyData)
        {if(owner!=null&&owner.HandleCommand(tier,ref message,keyData))return true;return base.ProcessCmdKey(ref message,keyData);}
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x10D){Composing=true;if(owner!=null)owner.CompositionStarted(tier);}
            if(owner!=null&&owner.HandleWindowMessage(tier,ref message)){message.Result=IntPtr.Zero;return;}
            base.WndProc(ref message);
            if(message.Msg==0x10E){Composing=false;if(owner!=null)owner.CompositionEnded(tier);}
        }
        protected override void OnEnter(EventArgs e){base.OnEnter(e);if(owner!=null)owner.TierEntered(tier);}
        protected override void OnMouseDown(MouseEventArgs e){if(owner!=null)owner.TierPointer(tier);base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(owner!=null)owner.TierAfterInput();}
        protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);if(owner!=null)owner.TierAfterInput();}
        protected override void OnFontChanged(EventArgs e)
        {
            if(owner!=null&&!fixingFont&&owner.FixedInputFont!=null&&!Font.Equals(owner.FixedInputFont))
            {fixingFont=true;try{Font=owner.FixedInputFont;}finally{fixingFont=false;}}
            base.OnFontChanged(e);
        }
    }
}

