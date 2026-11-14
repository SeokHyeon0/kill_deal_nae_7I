param(
 [Parameter(Mandatory=$true)][string]$PrivateKeyPath,
 [Parameter(Mandatory=$true)][string]$UnsignedDirectory,
 [Parameter(Mandatory=$true)][string]$SignedDirectory,
 [string]$PublicKeyPath,
 [string]$ManualDirectory
)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($PublicKeyPath)) { $PublicKeyPath = Join-Path $PSScriptRoot 'creator-public-key.xml' }
$keyTool = Join-Path $PSScriptRoot 'bin\KillDeal.SigningTool.exe'
if (-not (Test-Path -LiteralPath $keyTool)) { & (Join-Path $PSScriptRoot 'Build-KeyTool.ps1') | Out-Null }
$outputPath = [IO.Path]::GetFullPath($SignedDirectory)
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
foreach ($item in @(@{Role='BJ';Name='킬딜내기_BJ.exe';ManualRole='BJ'}, @{Role='Viewer';Name='킬딜내기_시청자.exe';ManualRole='시청자'})) {
 $inputPath = Join-Path $UnsignedDirectory $item.Name
 if (-not (Test-Path -LiteralPath $inputPath)) { throw ('서명 전 EXE가 없습니다: '+$item.Name) }
 $version = [Diagnostics.FileVersionInfo]::GetVersionInfo([IO.Path]::GetFullPath($inputPath)).FileVersion
 $parsedVersion = [Version]::Parse($version)
 $arguments = @('sign', '--key', [IO.Path]::GetFullPath($PrivateKeyPath), '--public', [IO.Path]::GetFullPath($PublicKeyPath), '--input', [IO.Path]::GetFullPath($inputPath), '--output', (Join-Path $outputPath $item.Name), '--role', $item.Role, '--version', $parsedVersion.ToString(4))
 if (-not [string]::IsNullOrWhiteSpace($ManualDirectory)) {
  $manualName = '킬딜내기-'+$item.ManualRole+'-사용방법-V'+$parsedVersion.ToString(3)+'.pdf'
  $manualPath = Join-Path $ManualDirectory $manualName
  if (-not (Test-Path -LiteralPath $manualPath)) { throw ('새 사용방법 PDF가 없습니다: '+$manualName) }
  $arguments += @('--pdf', [IO.Path]::GetFullPath($manualPath))
 }
 & $keyTool @arguments
 if ($LASTEXITCODE -ne 0) { throw ('제작자 서명에 실패했습니다: '+$item.Name) }
}
Write-Output 'BJ·시청자 EXE 서명 및 검증을 완료했습니다. signed 출력만 배포하세요.'
