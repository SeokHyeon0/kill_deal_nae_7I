using System;
using System.Runtime.InteropServices;
using System.Text;
namespace PubgScore
{
    internal static class CredentialVault
    {
#if VIEWER
        internal static string Read(string target){throw new NotSupportedException("시청자용에서는 BJ 인증 정보를 사용할 수 없습니다.");}
        internal static void Save(string target,string uid,string secret){throw new NotSupportedException("시청자용에서는 BJ 인증 정보를 사용할 수 없습니다.");}
        internal static void Delete(string target){throw new NotSupportedException("시청자용에서는 BJ 인증 정보를 사용할 수 없습니다.");}
#else
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct Credential
        {
            public uint Flags,Type;public string TargetName,Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;public IntPtr CredentialBlob;
            public uint Persist,AttributeCount;public IntPtr Attributes;
            public string TargetAlias,UserName;
        }
        [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool WriteNative(ref Credential credential,uint flags);
        [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool ReadNative(string target,uint type,uint flags,out IntPtr credential);
        [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool DeleteNative(string target,uint type,uint flags);
        [DllImport("advapi32.dll")]static extern void CredFree(IntPtr buffer);
        internal static string Read(string target)
        {
            IntPtr pointer;
            if(!ReadNative(target,1,0,out pointer)){if(Marshal.GetLastWin32Error()==1168)return "";throw new InvalidOperationException("Windows 자격 증명 저장소를 읽을 수 없습니다.");}
            try{var item=(Credential)Marshal.PtrToStructure(pointer,typeof(Credential));var bytes=new byte[item.CredentialBlobSize];Marshal.Copy(item.CredentialBlob,bytes,0,bytes.Length);try{return Encoding.UTF8.GetString(bytes);}finally{Array.Clear(bytes,0,bytes.Length);}}
            finally{CredFree(pointer);}
        }
        internal static void Save(string target,string uid,string secret)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(secret);IntPtr pointer=Marshal.AllocHGlobal(bytes.Length);
            try{Marshal.Copy(bytes,0,pointer,bytes.Length);var item=new Credential {Type=1,TargetName=target,UserName=uid,CredentialBlobSize=(uint)bytes.Length,CredentialBlob=pointer,Persist=2};
                if(!WriteNative(ref item,0))throw new InvalidOperationException("Windows 자격 증명 저장소에 자동 로그인 정보를 저장하지 못했습니다. Windows 오류: "+Marshal.GetLastWin32Error());}
            finally{for(int i=0;i<bytes.Length;i++)Marshal.WriteByte(pointer,i,0);Marshal.FreeHGlobal(pointer);Array.Clear(bytes,0,bytes.Length);}
        }
        internal static void Delete(string target)
        {if(!DeleteNative(target,1,0)&&Marshal.GetLastWin32Error()!=1168)throw new InvalidOperationException("Windows 자격 증명 삭제에 실패했습니다.");}
#endif
    }
}
