using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace PubgScore
{
    internal sealed class UpdateSignatureException : CryptographicException
    {
        internal UpdateSignatureException(string message):base(message){}
    }

    internal sealed class SignedExecutableInfo
    {
        internal string Role, PdfName, PdfSha256;
        internal Version Version;
        internal long OriginalLength;
    }

    // Application-specific update signature, not Windows Authenticode trust.
    // Layout: original PE | canonical metadata | RSA signature | fixed footer.
    // SHA256 authenticates every original byte, metadata and the entire footer.
    // The only bytes omitted from hashing are the RSA signature itself.
    internal static class UpdateSignature
    {
        internal const int SignatureLength=384;
        internal const int FooterLength=32;
        internal const int MaxMetadataLength=4096;
        internal const long MaxExecutableLength=67108864;
        const string Domain="killdeal-update-signature-v1";
        static readonly byte[] FooterMagic=Encoding.ASCII.GetBytes("KD-SIGNED-EXE-V1");
        static readonly UTF8Encoding StrictUtf8=new UTF8Encoding(false,true);

        internal static SignedExecutableInfo Verify(string executable,string expectedRole,Version expectedVersion,string optionalPdfPath=null)
        {
            return VerifyWithPublicKey(executable,expectedRole,expectedVersion,optionalPdfPath,UpdateTrustAnchor.PublicKeyXml);
        }

        // Explicit trust input is shared only with the local signing utility/tests.
        // Production update entry points must use Verify and the compiled-in key.
        internal static SignedExecutableInfo VerifyWithPublicKey(string executable,string expectedRole,Version expectedVersion,string optionalPdfPath,string publicKeyXml)
        {
            RequireRole(expectedRole);RequireVersion(expectedVersion);
            SignedExecutableInfo info;
            using(var input=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(input.Length<64+SignatureLength+FooterLength||input.Length>MaxExecutableLength)throw Invalid("서명된 실행 파일 크기가 올바르지 않습니다.");
                input.Position=input.Length-FooterLength;
                byte[] footer=ReadExact(input,FooterLength);
                if(!HasMagic(footer))throw Invalid("제작자 서명이 없는 업데이트는 설치할 수 없습니다.");
                int metadataLength=ReadInt32(footer,16),signatureLength=ReadInt32(footer,20);
                long originalLength=ReadInt64(footer,24);
                if(metadataLength<=0||metadataLength>MaxMetadataLength||signatureLength!=SignatureLength||originalLength<64||
                    originalLength>MaxExecutableLength-FooterLength-SignatureLength-metadataLength||
                    originalLength+metadataLength+SignatureLength+FooterLength!=input.Length)
                    throw Invalid("제작자 서명 구조가 올바르지 않습니다.");
                RequirePePrefix(input,originalLength);
                if(HasEmbeddedFooter(input,originalLength))throw Invalid("중복 서명이 포함된 업데이트입니다.");
                input.Position=originalLength;
                byte[] metadata=ReadExact(input,metadataLength),signature=ReadExact(input,SignatureLength);
                byte[] hash=ComputeEnvelopeHash(input,originalLength,metadata,footer);
                try
                {
                    using(var rsa=CreatePublicRsa(publicKeyXml))
                    {
                        var verifier=new RSAPKCS1SignatureDeformatter(rsa);verifier.SetHashAlgorithm("SHA256");
                        if(!verifier.VerifySignature(hash,signature))throw Invalid("제작자 서명이 일치하지 않아 업데이트를 차단했습니다.");
                    }
                }
                catch(UpdateSignatureException){throw;}
                catch(CryptographicException){throw Invalid("제작자 서명을 검증할 수 없어 업데이트를 차단했습니다.");}
                info=ParseMetadata(metadata,originalLength);
                if(info.Role!=expectedRole||info.Version!=expectedVersion)throw Invalid("제작자가 서명한 프로그램 종류 또는 버전이 업데이트 정보와 다릅니다.");
            }
            if(!String.IsNullOrEmpty(optionalPdfPath))
            {
                if(info.PdfName.Length==0||Path.GetFileName(optionalPdfPath)!=info.PdfName)throw Invalid("제작자가 승인하지 않은 사용방법 PDF입니다.");
                using(var pdf=new FileStream(optionalPdfPath,FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var sha=SHA256.Create())
                {
                    if(pdf.Length<=0||pdf.Length>MaxExecutableLength||Hex(sha.ComputeHash(pdf))!=info.PdfSha256)
                        throw Invalid("사용방법 PDF의 제작자 서명 정보가 일치하지 않습니다.");
                }
            }
            return info;
        }

        internal static byte[] CreateMetadata(string role,Version version,long originalLength,string pdfName,string pdfSha256)
        {
            RequireRole(role);RequireVersion(version);
            if(originalLength<64||originalLength>MaxExecutableLength-SignatureLength-FooterLength)throw Invalid("서명할 실행 파일 크기가 올바르지 않습니다.");
            pdfName=pdfName??String.Empty;pdfSha256=pdfSha256??String.Empty;
            if((pdfName.Length==0)!=(pdfSha256.Length==0))throw Invalid("PDF 이름과 SHA256은 함께 지정해야 합니다.");
            if(pdfName.Length>0&&(pdfName!=CanonicalPdfName(role,version)||!IsLowerHash(pdfSha256)))throw Invalid("서명할 PDF 이름 또는 SHA256이 올바르지 않습니다.");
            string encodedName=pdfName.Length==0?String.Empty:Convert.ToBase64String(StrictUtf8.GetBytes(pdfName));
            byte[] bytes=Encoding.ASCII.GetBytes(Domain+"\n"+role+"\n"+version.ToString(4)+"\n"+originalLength.ToString(CultureInfo.InvariantCulture)+"\n"+encodedName+"\n"+pdfSha256+"\n");
            if(bytes.Length>MaxMetadataLength)throw Invalid("서명 정보가 너무 깁니다.");
            return bytes;
        }

        internal static byte[] CreateFooter(long originalLength,int metadataLength)
        {
            if(originalLength<64||metadataLength<=0||metadataLength>MaxMetadataLength||
                originalLength>MaxExecutableLength-SignatureLength-FooterLength-metadataLength)throw Invalid("서명할 파일 크기가 올바르지 않습니다.");
            byte[] footer=new byte[FooterLength];Buffer.BlockCopy(FooterMagic,0,footer,0,FooterMagic.Length);
            WriteInt32(footer,16,metadataLength);WriteInt32(footer,20,SignatureLength);WriteInt64(footer,24,originalLength);
            return footer;
        }

        internal static byte[] ComputeEnvelopeHash(Stream original,long originalLength,byte[] metadata,byte[] footer)
        {
            if(original==null||!original.CanRead||!original.CanSeek||originalLength<0||originalLength>original.Length||metadata==null||footer==null)throw Invalid("서명할 파일을 읽을 수 없습니다.");
            original.Position=0;
            using(var sha=SHA256.Create())
            {
                byte[] buffer=new byte[65536];long remaining=originalLength;
                while(remaining>0)
                {
                    int count=original.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));
                    if(count==0)throw Invalid("서명할 파일이 완전하지 않습니다.");
                    sha.TransformBlock(buffer,0,count,buffer,0);remaining-=count;
                }
                sha.TransformBlock(metadata,0,metadata.Length,metadata,0);
                sha.TransformFinalBlock(footer,0,footer.Length);return sha.Hash;
            }
        }

        internal static bool HasEmbeddedFooter(Stream input,long prefixLength)
        {
            if(prefixLength<FooterLength||prefixLength>input.Length)return false;
            input.Position=prefixLength-FooterLength;return HasMagic(ReadExact(input,FooterLength));
        }

        internal static string CanonicalPdfName(string role,Version version)
        {RequireRole(role);RequireVersion(version);return "킬딜내기-"+(role=="BJ"?"BJ":"시청자")+"-사용방법-V"+version.ToString(3)+".pdf";}

        internal static string Hex(byte[] value){return BitConverter.ToString(value).Replace("-",String.Empty).ToLowerInvariant();}

        static SignedExecutableInfo ParseMetadata(byte[] metadata,long originalLength)
        {
            foreach(byte value in metadata)if(value>127)throw Invalid("서명 정보 인코딩이 올바르지 않습니다.");
            string[] fields=Encoding.ASCII.GetString(metadata).Split('\n');
            if(fields.Length!=7||fields[0]!=Domain||fields[6].Length!=0)throw Invalid("서명 정보 형식이 올바르지 않습니다.");
            Version version;long length;
            if(!Version.TryParse(fields[2],out version)||!Int64.TryParse(fields[3],NumberStyles.None,CultureInfo.InvariantCulture,out length)||length!=originalLength)throw Invalid("서명된 버전 또는 파일 길이가 올바르지 않습니다.");
            string name;
            try{name=fields[4].Length==0?String.Empty:StrictUtf8.GetString(Convert.FromBase64String(fields[4]));}
            catch(FormatException){throw Invalid("서명된 PDF 정보가 올바르지 않습니다.");}
            catch(DecoderFallbackException){throw Invalid("서명된 PDF 이름 인코딩이 올바르지 않습니다.");}
            byte[] canonical=CreateMetadata(fields[1],version,length,name,fields[5]);
            if(!EqualBytes(metadata,canonical))throw Invalid("서명 정보가 표준 형식이 아닙니다.");
            return new SignedExecutableInfo {Role=fields[1],Version=version,OriginalLength=length,PdfName=name,PdfSha256=fields[5]};
        }

        static RSACryptoServiceProvider CreatePublicRsa(string xml)
        {
            if(String.IsNullOrEmpty(xml)||xml.Length>8192)throw Invalid("내장된 제작자 공개키가 올바르지 않습니다.");
            var document=new XmlDocument {XmlResolver=null};
            using(var text=new StringReader(xml))
            using(var reader=XmlReader.Create(text,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8192}))document.Load(reader);
            var root=document.DocumentElement;
            if(root==null||root.Name!="RSAKeyValue"||root.NamespaceURI.Length!=0||root.Attributes.Count!=0||root.ChildNodes.Count!=2||root.ChildNodes[0].Name!="Modulus"||root.ChildNodes[1].Name!="Exponent")throw Invalid("내장된 제작자 공개키 형식이 올바르지 않습니다.");
            byte[] modulus,exponent;
            try{modulus=Convert.FromBase64String(root.ChildNodes[0].InnerText);exponent=Convert.FromBase64String(root.ChildNodes[1].InnerText);}
            catch(FormatException){throw Invalid("내장된 제작자 공개키 값이 올바르지 않습니다.");}
            if(modulus.Length!=SignatureLength||(modulus[0]&128)==0||exponent.Length!=3||exponent[0]!=1||exponent[1]!=0||exponent[2]!=1)throw Invalid("제작자 공개키는 RSA 3072비트여야 합니다.");
            var rsa=new RSACryptoServiceProvider(new CspParameters(24) {Flags=CspProviderFlags.CreateEphemeralKey});
            try{rsa.PersistKeyInCsp=false;rsa.ImportParameters(new RSAParameters {Modulus=modulus,Exponent=exponent});return rsa;}
            catch{rsa.Dispose();throw;}
        }

        static void RequirePePrefix(Stream input,long originalLength)
        {
            input.Position=0;byte[] header=ReadExact(input,64);
            int pe=ReadInt32(header,60);
            if(header[0]!=(byte)'M'||header[1]!=(byte)'Z'||pe<64||pe>originalLength-4)throw Invalid("서명된 파일은 실행 파일이어야 합니다.");
            input.Position=pe;byte[] signature=ReadExact(input,4);
            if(signature[0]!=(byte)'P'||signature[1]!=(byte)'E'||signature[2]!=0||signature[3]!=0)throw Invalid("서명된 실행 파일 헤더가 올바르지 않습니다.");
        }

        static void RequireRole(string role){if(role!="BJ"&&role!="Viewer")throw Invalid("서명된 프로그램 종류가 올바르지 않습니다.");}
        static void RequireVersion(Version version)
        {
            if(version==null||version.Major<0||version.Minor<0||version.Build<0||version.Revision<0||version.Major>65535||version.Minor>65535||version.Build>65535||version.Revision>65535)throw Invalid("서명된 버전은 네 자리 숫자여야 합니다.");
        }
        static bool IsLowerHash(string value)
        {
            if(value==null||value.Length!=64)return false;
            foreach(char c in value)if(!(c>='0'&&c<='9')&&!(c>='a'&&c<='f'))return false;
            return true;
        }
        static bool HasMagic(byte[] footer)
        {
            if(footer==null||footer.Length!=FooterLength||FooterMagic.Length!=16)return false;
            for(int i=0;i<FooterMagic.Length;i++)if(footer[i]!=FooterMagic[i])return false;
            return true;
        }
        static bool EqualBytes(byte[] left,byte[] right)
        {if(left.Length!=right.Length)return false;int difference=0;for(int i=0;i<left.Length;i++)difference|=left[i]^right[i];return difference==0;}
        static byte[] ReadExact(Stream input,int count)
        {
            byte[] result=new byte[count];int offset=0;
            while(offset<count){int read=input.Read(result,offset,count-offset);if(read==0)throw Invalid("서명된 파일이 완전하지 않습니다.");offset+=read;}
            return result;
        }
        static int ReadInt32(byte[] bytes,int offset)
        {return bytes[offset]|bytes[offset+1]<<8|bytes[offset+2]<<16|bytes[offset+3]<<24;}
        static long ReadInt64(byte[] bytes,int offset)
        {return (long)(uint)ReadInt32(bytes,offset)|((long)ReadInt32(bytes,offset+4)<<32);}
        static void WriteInt32(byte[] bytes,int offset,int value)
        {for(int i=0;i<4;i++)bytes[offset+i]=(byte)((uint)value>>(i*8));}
        static void WriteInt64(byte[] bytes,int offset,long value)
        {for(int i=0;i<8;i++)bytes[offset+i]=(byte)((ulong)value>>(i*8));}
        static UpdateSignatureException Invalid(string message){return new UpdateSignatureException(message);}
    }
}
