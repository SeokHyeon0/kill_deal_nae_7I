using System;
using System.Threading;
using System.Windows.Forms;
namespace PubgScore
{
    internal static class Program
    {
        [STAThread] public static void Main(string[] args)
        {
#if !SIGNPATH_REVIEW
            EmbeddedUpdater.Register();
            if(UpdateInstaller.TryHandle(args))return;
            string cleanupPlan;
            args=UpdateInstaller.ConsumeCleanupArguments(args,out cleanupPlan);
#endif
#if ADMIN
            const bool administrator=true;
#else
            const bool administrator=false;
#endif
#if SINGLEFILE || VIEWER_SINGLEFILE
            if(!BundledFiles.HasSupportedFramework())
            {MessageBox.Show("이 프로그램에는 .NET Framework 4.7.2 이상이 필요합니다. .NET Framework를 설치한 뒤 다시 실행하세요.","실행 환경 확인");return;}
#endif
            string mutexName="Local\\PubgScoreFirebase_"+(administrator?"Admin":"Participant");
#if UPDATE_TESTING
            mutexName+="_UpdateTest";
#elif SIGNPATH_REVIEW
            mutexName+="_SignPathReview";
#endif
            bool owner;using(var mutex=new Mutex(true,mutexName,out owner))
            {
                if(!owner){MessageBox.Show("같은 종류의 프로그램이 이미 실행 중입니다.");return;}
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e){MessageBox.Show(e.Exception.Message,"작업 확인");};
#if !SIGNPATH_REVIEW
                if(!StartupUpdate.Run(administrator,args))return;
#endif
#if UPDATE_TESTING
                for(int i=0;i+1<args.Length;i++)if(args[i]=="--after-update-probe")
                {UpdateInstaller.CompletePrevious(cleanupPlan);System.IO.File.WriteAllText(args[i+1],typeof(Program).Assembly.GetName().Version.ToString()+"\n"+Application.ExecutablePath);return;}
#endif
                try{using(var icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    using(var form=new MainForm(administrator))
                    {
                        form.Icon=icon;
#if !SIGNPATH_REVIEW
                        form.Shown+=delegate {UpdateInstaller.CompletePrevious(cleanupPlan);};
#endif
                        Application.Run(form);
                    }
                }
                catch(Exception ex){MessageBox.Show(ex.Message,"실행 확인");}
            }
        }
    }
}
