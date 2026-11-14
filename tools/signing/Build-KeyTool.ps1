param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $PSScriptRoot 'bin' }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
# The publisher CLI always takes an explicit build public key and calls VerifyWithPublicKey.
# This empty anchor only permits the same verifier source to compile before the first key is created.
$stub = Join-Path $outputPath 'ToolOnlyTrustAnchor.cs'
[IO.File]::WriteAllText($stub, 'namespace PubgScore { internal static class UpdateTrustAnchor { internal const string PublicKeyXml=""; } }', [Text.Encoding]::ASCII)
$executable = Join-Path $outputPath 'KillDeal.SigningTool.exe'
& $compiler /nologo /target:exe /optimize+ /codepage:65001 /platform:anycpu /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Xml.dll ("/out:"+$executable) (Join-Path $PSScriptRoot 'SigningTool.cs') (Join-Path $sourceRoot 'src\UpdateSignature.cs') $stub
if ($LASTEXITCODE -ne 0) { throw '서명 도구 빌드에 실패했습니다.' }
Write-Output $executable
