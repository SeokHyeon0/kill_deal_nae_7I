using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace PubgScore
{
    public sealed class FirebaseConfig
    {
        public string DatabaseUrl {get;set;}
        public string ApiKey {get;set;}
        public string ProjectId {get;set;}
        public string AuthDomain {get;set;}
        internal string TestIdentityDirectory {get;set;}
        public bool EmulatorMode {get;set;}
        public string AuthEmulatorUrl {get;set;}
        public static FirebaseConfig Load()
        {
#if SINGLEFILE || VIEWER_SINGLEFILE
            string json=BundledFiles.ReadText(BundledFiles.FirebaseResource);
#else
            string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"firebase.config.json");
            if(!File.Exists(file))throw new InvalidOperationException("firebase.config.example.json을 firebase.config.json으로 복사하고 API Key를 입력하세요.");
            string json=File.ReadAllText(file);
#endif
            var config=new JavaScriptSerializer().Deserialize<FirebaseConfig>(json);config.Validate();return config;
        }
        public void Validate()
        {
            Uri uri;
            if(!Uri.TryCreate(DatabaseUrl,UriKind.Absolute,out uri)||uri.Query.Length>0||uri.UserInfo.Length>0||uri.Fragment.Length>0)throw new ArgumentException("DatabaseUrl을 확인하세요.");
            if(EmulatorMode)
            {
                Uri auth;if(!uri.IsLoopback||!Uri.TryCreate(AuthEmulatorUrl,UriKind.Absolute,out auth)||!auth.IsLoopback||ProjectId==null||!ProjectId.StartsWith("demo-"))throw new ArgumentException("에뮬레이터는 loopback과 demo- 프로젝트만 허용합니다.");
            }
            else if(uri.Scheme!="https"||!(uri.Host.EndsWith(".firebasedatabase.app")||uri.Host.EndsWith(".firebaseio.com")))throw new ArgumentException("Firebase HTTPS Database URL이 필요합니다.");
            if(String.IsNullOrWhiteSpace(ApiKey)||ApiKey.Contains("YOUR_")||String.IsNullOrWhiteSpace(ProjectId))throw new ArgumentException("firebase.config.json의 API Key와 ProjectId를 설정하세요.");
        }
        internal string AuthBase {get{return EmulatorMode?AuthEmulatorUrl.TrimEnd('/')+"/identitytoolkit.googleapis.com/v1/":"https://identitytoolkit.googleapis.com/v1/";}}
        internal string TokenBase {get{return EmulatorMode?AuthEmulatorUrl.TrimEnd('/')+"/securetoken.googleapis.com/v1/token":"https://securetoken.googleapis.com/v1/token";}}
        internal string DatabaseEndpoint(string location,string token)
        {
            return DatabaseUrl.TrimEnd('/')+"/"+location+".json?auth="+Uri.EscapeDataString(token)+(EmulatorMode?"&ns="+Uri.EscapeDataString(ProjectId):"");
        }
        internal string CredentialTarget {get{using(var sha=SHA256.Create())return "PubgScore/Admin/"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(DatabaseUrl+"|"+(EmulatorMode?TestIdentityDirectory:"")))).Replace("-","");}}
        internal string IdentityFile()
        {using(var sha=SHA256.Create()){string id=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(DatabaseUrl))).Replace("-","").Substring(0,16);string folder=EmulatorMode?(TestIdentityDirectory??Path.Combine(Path.GetTempPath(),"PubgScoreFirebase-emulator",Guid.NewGuid().ToString("N"))):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PubgScoreFirebase");return Path.Combine(folder,"anonymous-"+id+".bin");}
        }
    }
}
