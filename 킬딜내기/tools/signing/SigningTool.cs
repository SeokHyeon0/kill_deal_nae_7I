using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace PubgScore
{
    // Local publisher tool only. This source is never compiled into the BJ or viewer application.
    internal static class SigningTool
    {
        static readonly byte[] KeyMagic=Encoding.ASCII.GetBytes("KDSKEY-DPAPI-V1\n");
        static readonly byte[] Entropy=Encoding.UTF8.GetBytes("KillDeal.UpdateSigning.PrivateKey.v1");
        static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        static int Main(string[] args)
        {
            try
            {
                if(args.Length==0)throw new ArgumentException("명령: init, public, sign, verify, backup, restore");
                string command=args[0];var options=Options(args);
                switch(command)
                {
                    case "init": Init(options);break;
                    case "public": ExportPublic(options);break;
                    case "sign": Sign(options);break;
                    case "verify": Verify(options);break;
                    case "backup": Backup(options);break;
                    case "restore": Restore(options);break;
                    default:throw new ArgumentException("지원하지 않는 명령입니다.");
                }
                return 0;
            }
            catch(Exception error)
            {
                // No exception stack, key material, XML or secret input is printed.
                Console.Error.WriteLine("실패: "+SafeError(error));return 1;
            }
        }
        static string SafeError(Exception error)
        {
            if(error is CryptographicException)return "암호화 키 또는 서명 검증에 실패했습니다. 현재 Windows 사용자, 암호 및 공개키를 확인하세요.";
            if(error is ArgumentException||error is InvalidDataException||error is IOException||error is InvalidOperationException||error is UnauthorizedAccessException)return error.Message;
            return "작업을 완료하지 못했습니다 ("+error.GetType().Name+").";
        }
        static Dictionary<string,string> Options(string[] args)
        {
            if(args.Length>17||(args.Length-1)%2!=0)throw new ArgumentException("옵션은 --이름 값 형식이어야 합니다.");
            var options=new Dictionary<string,string>(StringComparer.Ordinal);
            for(int i=1;i<args.Length;i+=2)
            {
                if(!args[i].StartsWith("--",StringComparison.Ordinal)||String.IsNullOrWhiteSpace(args[i+1])||options.ContainsKey(args[i]))throw new ArgumentException("잘못되거나 중복된 옵션입니다.");
                options.Add(args[i],args[i+1]);
            }
            return options;
        }
        static void Allow(Dictionary<string,string> options,params string[] names)
        {foreach(string name in options.Keys)if(Array.IndexOf(names,name)<0)throw new ArgumentException("지원하지 않는 옵션: "+name);}
        static string Need(Dictionary<string,string> options,string name)
        {string value;if(!options.TryGetValue(name,out value))throw new ArgumentException("필수 옵션: "+name);return value;}
        static string Optional(Dictionary<string,string> options,string name)
        {string value;return options.TryGetValue(name,out value)?value:null;}
        static string Full(string path){return Path.GetFullPath(path);}
        static bool Same(string a,string b){return String.Equals(Full(a),Full(b),StringComparison.OrdinalIgnoreCase);}
        static void NewPath(string path)
        {
            if(File.Exists(path)||Directory.Exists(path))throw new IOException("기존 파일은 덮어쓰지 않습니다: "+Path.GetFileName(path));
            string parent=Path.GetDirectoryName(Full(path));if(!Directory.Exists(parent))Directory.CreateDirectory(parent);
            RejectReparseParents(parent);
        }
        static void RejectReparseParents(string parent)
        {
            for(var current=new DirectoryInfo(parent);current!=null;current=current.Parent)
                if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("서명 키와 출력 경로에는 링크 폴더를 사용할 수 없습니다.");
        }
        static void Regular(string path)
        {
            if(!File.Exists(path))throw new FileNotFoundException("필요한 파일이 없습니다: "+Path.GetFileName(path));
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("링크 파일을 사용할 수 없습니다.");
            RejectReparseParents(Path.GetDirectoryName(Full(path)));
        }
        static void WriteNew(string path,byte[] bytes)
        {NewPath(path);using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(bytes,0,bytes.Length);}
        static RSACryptoServiceProvider NewRsa()
        {
            var parameters=new CspParameters(24);parameters.Flags=CspProviderFlags.CreateEphemeralKey;
            return new RSACryptoServiceProvider(3072,parameters){PersistKeyInCsp=false};
        }
        static void CheckKey(RSA rsa)
        {if(rsa.KeySize!=3072)throw new InvalidDataException("RSA 3072비트 서명 키만 사용할 수 있습니다.");}
        static string Public(RSA rsa){CheckKey(rsa);return rsa.ToXmlString(false);}
        static string Fingerprint(RSA rsa)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(Public(rsa)))).Replace("-","").ToLowerInvariant();}
        static void PrintPublicIdentity(RSA rsa)
        {Console.WriteLine("공개키 SHA256: "+Fingerprint(rsa));}
        static void StorePrivate(string file,RSA rsa)
        {
            CheckKey(rsa);byte[] plain=null;
            try
            {
                plain=Utf8.GetBytes(rsa.ToXmlString(true));StoreProtected(file,plain);
            }
            finally{if(plain!=null)Array.Clear(plain,0,plain.Length);}
        }
        static void StoreProtected(string file,byte[] plain)
        {
            byte[] encrypted=ProtectedData.Protect(plain,Entropy,DataProtectionScope.CurrentUser);
            try{NewPath(file);using(var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(KeyMagic,0,KeyMagic.Length);output.Write(encrypted,0,encrypted.Length);}}
            finally{Array.Clear(encrypted,0,encrypted.Length);}
        }
        sealed class PrivateKeyLease:IDisposable
        {
            internal RSA Rsa;internal X509Certificate2 Certificate;
            public void Dispose(){if(Rsa!=null)Rsa.Dispose();if(Certificate!=null)Certificate.Dispose();}
        }
        static PrivateKeyLease LoadPrivate(string file)
        {
            Regular(file);var info=new FileInfo(file);if(info.Length<=KeyMagic.Length||info.Length>131072)throw new InvalidDataException("개인키 보관 파일의 크기가 잘못되었습니다.");
            byte[] stored=File.ReadAllBytes(file),encrypted=null,plain=null;PrivateKeyLease lease=null;
            try
            {
                for(int i=0;i<KeyMagic.Length;i++)if(stored[i]!=KeyMagic[i])throw new InvalidDataException("개인키 보관 파일 형식이 잘못되었습니다.");
                encrypted=new byte[stored.Length-KeyMagic.Length];Buffer.BlockCopy(stored,KeyMagic.Length,encrypted,0,encrypted.Length);
                plain=ProtectedData.Unprotect(encrypted,Entropy,DataProtectionScope.CurrentUser);
                string text=Utf8.GetString(plain);lease=new PrivateKeyLease();
                if(text.StartsWith("PFX1\n",StringComparison.Ordinal))
                {
                    string[] parts=text.Split('\n');if(parts.Length!=3)throw new InvalidDataException("복원된 키 보관 형식이 잘못되었습니다.");
                    string password=Utf8.GetString(Convert.FromBase64String(parts[1]));byte[] pfx=Convert.FromBase64String(parts[2]);
                    try{lease.Certificate=new X509Certificate2(pfx,password,X509KeyStorageFlags.Exportable|X509KeyStorageFlags.EphemeralKeySet);lease.Rsa=lease.Certificate.GetRSAPrivateKey();}
                    finally{Array.Clear(pfx,0,pfx.Length);}
                    if(lease.Rsa==null)throw new InvalidDataException("서명용 개인키가 없습니다.");
                }
                else
                {
                    var rsa=NewRsa();lease.Rsa=rsa;rsa.FromXmlString(text);if(rsa.PublicOnly)throw new InvalidDataException("서명용 개인키가 없습니다.");
                }
                CheckKey(lease.Rsa);var result=lease;lease=null;return result;
            }
            finally{if(lease!=null)lease.Dispose();Array.Clear(stored,0,stored.Length);if(encrypted!=null)Array.Clear(encrypted,0,encrypted.Length);if(plain!=null)Array.Clear(plain,0,plain.Length);}
        }
        static string ReadPublic(string file)
        {
            Regular(file);if(new FileInfo(file).Length>8192)throw new InvalidDataException("공개키 파일이 너무 큽니다.");
            string text=File.ReadAllText(file,Utf8).Trim();
            // The public file must never carry private RSA components.
            if(text.IndexOf("<D>",StringComparison.Ordinal)>=0||text.IndexOf("<P>",StringComparison.Ordinal)>=0||text.IndexOf("<Q>",StringComparison.Ordinal)>=0)throw new InvalidDataException("공개키 파일에 개인키를 넣을 수 없습니다.");
            using(var rsa=NewRsa()){rsa.FromXmlString(text);if(!rsa.PublicOnly)throw new InvalidDataException("공개키 파일에 개인키를 넣을 수 없습니다.");return Public(rsa);}
        }
        static void MatchPublic(RSA rsa,string expected)
        {if(!String.Equals(Public(rsa),expected,StringComparison.Ordinal))throw new InvalidDataException("개인키가 프로그램에 사용하는 공개키와 일치하지 않습니다. 다른 키로 서명하지 않았습니다.");}
        static string Role(Dictionary<string,string> options)
        {string role=Need(options,"--role");if(role!="BJ"&&role!="Viewer")throw new ArgumentException("--role 값은 BJ 또는 Viewer여야 합니다.");return role;}
        static Version VersionOf(Dictionary<string,string> options)
        {
            string text=Need(options,"--version");Version version;
            if(!Version.TryParse(text,out version)||version.Build<0||version.Revision<0||version.ToString(4)!=text)throw new ArgumentException("--version 값은 2.5.1.0처럼 네 자리 숫자여야 합니다.");
            return version;
        }
        static string HashFile(string file)
        {using(var stream=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static void Init(Dictionary<string,string> options)
        {
            Allow(options,"--key","--public");string key=Full(Need(options,"--key")),pub=Full(Need(options,"--public"));
            if(Same(key,pub))throw new ArgumentException("개인키와 공개키 파일 경로는 달라야 합니다.");
            NewPath(key);NewPath(pub);
            using(var rsa=NewRsa()){StorePrivate(key,rsa);WriteNew(pub,Utf8.GetBytes(Public(rsa)+Environment.NewLine));PrintPublicIdentity(rsa);}
            Console.WriteLine("키 생성 완료. 개인키는 이 Windows 사용자용 DPAPI로 암호화되었습니다. 배포 파일에 포함하지 마세요.");
        }
        static void ExportPublic(Dictionary<string,string> options)
        {
            Allow(options,"--key","--output");string output=Full(Need(options,"--output"));NewPath(output);
            using(var lease=LoadPrivate(Need(options,"--key"))){WriteNew(output,Utf8.GetBytes(Public(lease.Rsa)+Environment.NewLine));PrintPublicIdentity(lease.Rsa);}
            Console.WriteLine("공개키 저장 완료.");
        }
        static void Sign(Dictionary<string,string> options)
        {
            Allow(options,"--key","--public","--input","--output","--role","--version","--pdf");
            string input=Full(Need(options,"--input")),output=Full(Need(options,"--output")),role=Role(options),pdf=Optional(options,"--pdf");Version version=VersionOf(options);
            if(Same(input,output))throw new ArgumentException("원본 EXE와 서명된 출력 EXE의 경로는 달라야 합니다.");
            Regular(input);NewPath(output);string publicXml=ReadPublic(Need(options,"--public"));
            var assembly=AssemblyName.GetAssemblyName(input);string expectedName=role=="BJ"?"킬딜내기_BJ":"킬딜내기_시청자";
            if(assembly.Name!=expectedName||assembly.Version!=version)throw new InvalidDataException("원본 EXE의 종류나 버전이 서명 요청과 다릅니다.");
            string pdfName="",pdfHash="";if(pdf!=null){pdf=Full(pdf);Regular(pdf);pdfName=Path.GetFileName(pdf);pdfHash=HashFile(pdf);}
            using(var lease=LoadPrivate(Need(options,"--key")))
            using(var source=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                RSA rsa=lease.Rsa;
                MatchPublic(rsa,publicXml);long length=source.Length;
                if(length<64||length>UpdateSignature.MaxExecutableLength-UpdateSignature.MaxMetadataLength-UpdateSignature.SignatureLength-UpdateSignature.FooterLength)throw new InvalidDataException("서명할 EXE 크기가 허용 범위를 벗어났습니다.");
                // Refuse existing app envelopes even if their signature is damaged.
                if(length>=UpdateSignature.FooterLength)
                {
                    source.Position=length-UpdateSignature.FooterLength;byte[] footerProbe=new byte[16];source.Read(footerProbe,0,footerProbe.Length);
                    if(Encoding.ASCII.GetString(footerProbe).TrimEnd('\0')=="KD-SIGNED-EXE-V1")throw new InvalidDataException("이미 서명된 EXE입니다. 새로 빌드한 서명 전 EXE를 사용하세요.");
                }
                source.Position=0;byte[] metadata=UpdateSignature.CreateMetadata(role,version,length,pdfName,pdfHash),footer=UpdateSignature.CreateFooter(length,metadata.Length);
                byte[] hash=UpdateSignature.ComputeEnvelopeHash(source,length,metadata,footer);var formatter=new RSAPKCS1SignatureFormatter(rsa);formatter.SetHashAlgorithm("SHA256");byte[] signature=formatter.CreateSignature(hash);
                if(signature.Length!=UpdateSignature.SignatureLength)throw new InvalidDataException("서명 길이가 올바르지 않습니다.");
                // Output only appears at its final name after verification succeeds.
                string temporary=output+".signing-"+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    using(var target=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {source.Position=0;source.CopyTo(target);target.Write(metadata,0,metadata.Length);target.Write(signature,0,signature.Length);target.Write(footer,0,footer.Length);}
                    UpdateSignature.VerifyWithPublicKey(temporary,role,version,pdf,publicXml);
                    File.Move(temporary,output);
                }
                finally{if(File.Exists(temporary))File.Delete(temporary);}
                PrintPublicIdentity(rsa);
            }
            Console.WriteLine("서명 및 검증 완료: "+Path.GetFileName(output));Console.WriteLine("EXE SHA256: "+HashFile(output));
        }
        static void Verify(Dictionary<string,string> options)
        {
            Allow(options,"--public","--input","--role","--version","--pdf");
            UpdateSignature.VerifyWithPublicKey(Full(Need(options,"--input")),Role(options),VersionOf(options),Optional(options,"--pdf"),ReadPublic(Need(options,"--public")));
            Console.WriteLine("제작자 서명 검증 성공.");
        }
        static string Password(string prompt,bool confirm)
        {
            if(Console.IsInputRedirected)throw new InvalidOperationException("백업 암호는 대화형 콘솔에서 직접 입력해야 합니다. 암호를 명령행이나 파일로 넘기지 마세요.");
            string first=ReadPassword(prompt);
            if(first.Length<12)throw new ArgumentException("백업 암호는 12자 이상으로 입력하세요.");
            if(confirm&&!String.Equals(first,ReadPassword("암호 다시 입력: "),StringComparison.Ordinal))throw new ArgumentException("입력한 암호가 서로 다릅니다.");
            return first;
        }
        static string ReadPassword(string prompt)
        {
            Console.Write(prompt);var buffer=new StringBuilder();
            while(true)
            {
                var key=Console.ReadKey(true);if(key.Key==ConsoleKey.Enter){Console.WriteLine();return buffer.ToString();}
                if(key.Key==ConsoleKey.Escape)throw new OperationCanceledException();
                if(key.Key==ConsoleKey.Backspace){if(buffer.Length>0)buffer.Length--;continue;}
                if(!Char.IsControl(key.KeyChar)&&buffer.Length<1024)buffer.Append(key.KeyChar);
            }
        }
        static void Backup(Dictionary<string,string> options)
        {
            Allow(options,"--key","--public","--output");string output=Full(Need(options,"--output"));NewPath(output);string publicXml=ReadPublic(Need(options,"--public"));
            using(var lease=LoadPrivate(Need(options,"--key")))
            {
                RSA rsa=lease.Rsa;
                MatchPublic(rsa,publicXml);string password=Password("암호화 백업의 새 암호 (12자 이상): ",true);
                byte[] pfx=null;
                try
                {
                    if(lease.Certificate!=null)pfx=lease.Certificate.Export(X509ContentType.Pfx,password);
                    else
                    {
                        var request=new CertificateRequest(new X500DistinguishedName("CN=KillDeal Update Key Backup"),rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
                        using(var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(20)))pfx=certificate.Export(X509ContentType.Pfx,password);
                    }
                    WriteNew(output,pfx);
                }
                finally{if(pfx!=null)Array.Clear(pfx,0,pfx.Length);}
                PrintPublicIdentity(rsa);
            }
            Console.WriteLine("암호화 PFX 백업 완료. 백업 파일과 암호는 별도로 보관하세요. 이 인증서를 Windows 신뢰 루트에 설치하지 마세요.");
        }
        static void Restore(Dictionary<string,string> options)
        {
            Allow(options,"--backup","--public","--key");string backup=Full(Need(options,"--backup")),key=Full(Need(options,"--key"));Regular(backup);NewPath(key);
            if(new FileInfo(backup).Length>65536)throw new InvalidDataException("백업 파일이 너무 큽니다.");string publicXml=ReadPublic(Need(options,"--public"));string password=Password("암호화 백업의 암호: ",false);
            byte[] pfx=File.ReadAllBytes(backup),plain=null;
            try
            {
                using(var certificate=new X509Certificate2(pfx,password,X509KeyStorageFlags.Exportable|X509KeyStorageFlags.EphemeralKeySet))
                using(var rsa=certificate.GetRSAPrivateKey())
                {
                    if(rsa==null)throw new InvalidDataException("백업에 RSA 개인키가 없습니다.");MatchPublic(rsa,publicXml);
                    // Imported CNG keys may allow only encrypted export. Preserve the standard PFX
                    // and its password under CurrentUser DPAPI instead of requesting plaintext export.
                    plain=Utf8.GetBytes("PFX1\n"+Convert.ToBase64String(Utf8.GetBytes(password))+"\n"+Convert.ToBase64String(pfx));StoreProtected(key,plain);PrintPublicIdentity(rsa);
                }
            }
            finally{Array.Clear(pfx,0,pfx.Length);if(plain!=null)Array.Clear(plain,0,plain.Length);}
            Console.WriteLine("복원 완료. 현재 Windows 사용자용 DPAPI로 다시 암호화했습니다.");
        }
    }
}
