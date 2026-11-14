using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace PubgScore
{
    internal sealed class AdminLoginDialog : Form
    {
        readonly TextBox email=new TextBox(),password=new TextBox();
        readonly Button login=DialogPalette.Button("로그인",true);readonly Label status=new Label();bool busy;
        internal AdminLoginDialog(IGameClient client)
        {
            Text="BJ 로그인";ClientSize=new Size(490,360);Font=new Font("Malgun Gothic",10);BackColor=DialogPalette.Surface;ForeColor=DialogPalette.Text;
            StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
            // Match the supplied HTML #entry surface and spacing; authentication stays unchanged.
            var panel=new Panel {Dock=DockStyle.Fill,Padding=new Padding(24),BackColor=DialogPalette.Surface};Controls.Add(panel);
            panel.Controls.Add(new Label {Text="BJ 계정으로 로그인",Bounds=new Rectangle(24,24,442,30),Font=new Font(Font.FontFamily,13F,FontStyle.Bold),ForeColor=DialogPalette.Text});
            panel.Controls.Add(new Label {Text="아이디(등록한 이메일)",Bounds=new Rectangle(24,72,442,22),Font=new Font(Font.FontFamily,9.5F,FontStyle.Bold)});
            email.AccessibleName="아이디(등록한 이메일)";var emailField=Field(email);emailField.SetBounds(24,98,442,44);emailField.TabIndex=0;panel.Controls.Add(emailField);
            panel.Controls.Add(new Label {Text="비밀번호",Bounds=new Rectangle(24,154,442,22),Font=new Font(Font.FontFamily,9.5F,FontStyle.Bold)});
            password.UseSystemPasswordChar=true;password.AccessibleName="비밀번호";var passwordField=Field(password);passwordField.SetBounds(24,180,442,44);passwordField.TabIndex=1;panel.Controls.Add(passwordField);
            login.SetBounds(250,292,104,44);login.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;login.TabIndex=2;panel.Controls.Add(login);AcceptButton=login;
            var close=DialogPalette.Button("취소",false);close.SetBounds(362,292,104,44);close.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;close.TabIndex=3;close.Click+=delegate {Close();};panel.Controls.Add(close);
            status.AutoSize=false;status.MaximumSize=new Size(442,70);status.Bounds=new Rectangle(24,240,442,38);status.Font=new Font(Font.FontFamily,9F);status.ForeColor=DialogPalette.Muted;status.Text="로그인 정보는 이 Windows 계정의 보안 저장소에 보관됩니다.";panel.Controls.Add(status);
            status.TextChanged+=delegate {
                int height=Math.Max(38,Math.Min(70,status.GetPreferredSize(new Size(442,0)).Height));
                status.Height=height;ClientSize=new Size(490,360+height-38);
            };
            panel.MouseDown+=delegate {ActiveControl=null;Focus();};
            foreach(Control item in panel.Controls)if(item is Label)item.MouseDown+=delegate {ActiveControl=null;Focus();};
            login.Click+=async delegate {
                if(busy)return;busy=true;login.Enabled=email.Enabled=password.Enabled=false;status.Text="Firebase BJ 권한 확인 중…";
                string secret=password.Text;password.Clear();
                try{await client.LoginAdministrator(email.Text.Trim(),secret);busy=false;DialogResult=DialogResult.OK;Close();}
                catch(Exception ex){status.Text=ex.Message;}
                finally{secret=null;password.Clear();busy=false;login.Enabled=email.Enabled=password.Enabled=true;if(DialogResult==DialogResult.OK)Close();}
            };
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};
        }
        static DialogSurface Field(TextBox input)
        {
            var field=new DialogSurface {BackColor=DialogPalette.Field,BorderColor=DialogPalette.Border,CornerRadius=10,Padding=new Padding(14,10,14,10)};
            input.Dock=DockStyle.Fill;input.AutoSize=false;input.BorderStyle=BorderStyle.None;input.BackColor=DialogPalette.Field;input.ForeColor=DialogPalette.Text;
            field.Controls.Add(input);field.MouseDown+=delegate {input.Focus();};
            input.Enter+=delegate {field.BorderColor=DialogPalette.Accent;field.Invalidate();};
            input.Leave+=delegate {field.BorderColor=DialogPalette.Border;field.Invalidate();};
            return field;
        }
        protected override void Dispose(bool disposing){if(disposing)password.Clear();base.Dispose(disposing);}
    }
    internal sealed class ResetConfirmationDialog : Form
    {
        internal ResetConfirmationDialog()
        {
            Text="전체 초기화";ClientSize=new Size(530,204);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;Font=new Font("Malgun Gothic",10F);BackColor=DialogPalette.Surface;ForeColor=DialogPalette.Text;
            Controls.Add(new DialogIconBadge(true){Location=new Point(26,26)});
            Controls.Add(new Label {Text="초기화를 진행할까요?",AutoSize=false,Font=new Font(Font.FontFamily,13F,FontStyle.Bold),Location=new Point(86,26),Size=new Size(418,28)});
            Controls.Add(new Label {Text="모든 팀의 선수 이름, 차감 점수, 경기 기록이 지워집니다.\n이 작업은 되돌릴 수 없습니다.",ForeColor=DialogPalette.Muted,Location=new Point(86,62),Size=new Size(418,52)});
            var yes=new ConfirmationChoiceButton("예, 초기화");yes.DialogResult=DialogResult.Yes;yes.SetBounds(282,136,118,44);
            var no=new ConfirmationChoiceButton("아니요");no.DialogResult=DialogResult.No;no.SetBounds(408,136,96,44);
            Controls.Add(yes);Controls.Add(no);ConfirmationChoiceButton.Bind(this,yes,no);CancelButton=no;
        }
    }
}
