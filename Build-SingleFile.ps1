param(
 [Parameter(Mandatory=$true)][string]$OutputDirectory,
 [ValidateSet('Both','BJ','Viewer')][string]$Role='Both',
 [string]$FirebaseConfiguration,
 [switch]$UpdateTest,
 [switch]$ReviewBuild
)
$ErrorActionPreference='Stop'
if($ReviewBuild -and $UpdateTest){throw 'ReviewBuild and UpdateTest cannot be combined.'}
if([string]::IsNullOrWhiteSpace($FirebaseConfiguration)){$FirebaseConfiguration=Join-Path $PSScriptRoot 'bundled\firebase.config.json'}
$outputPath=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references=@('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll','/r:System.Xml.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',("/r:"+(Join-Path $PSScriptRoot 'vendor\AutoUpdater.NET.1.9.3\AutoUpdater.NET.dll')))
$configurationPath=(Resolve-Path -LiteralPath $FirebaseConfiguration).Path
$releases=@(@{Role='Viewer';Name='킬딜내기_시청자.exe';Symbol='VIEWER';Icon='app-viewer.ico';Hashes='original-Viewer-V1.4.2-SHA256.txt'},@{Role='BJ';Name='킬딜내기_BJ.exe';Symbol='ADMIN';Icon='app-bj.ico';Hashes='original-BJ-V1.4.2-SHA256.txt'})
Push-Location $PSScriptRoot
try {
 foreach($release in $releases) {
  if($Role -ne 'Both' -and $Role -ne $release.Role){continue}
  $resources=@("/resource:$configurationPath,PubgScore.Bundled.firebase.config.json", "/resource:$PSScriptRoot\bundled\ICON-LICENSE.txt,PubgScore.Bundled.ICON-LICENSE.txt", "/resource:$PSScriptRoot\bundled\$($release.Hashes),PubgScore.Bundled.original-V1.4.2-SHA256.txt", "/resource:$PSScriptRoot\bundled\AutoUpdater.NET-LICENSE.txt,PubgScore.Bundled.AutoUpdater.NET-LICENSE.txt", "/resource:$PSScriptRoot\vendor\AutoUpdater.NET.1.9.3\AutoUpdater.NET.dll,PubgScore.Updater.AutoUpdater.NET.dll", "/resource:$PSScriptRoot\vendor\AutoUpdater.NET.1.9.3\ko\AutoUpdater.NET.resources.dll,PubgScore.Updater.ko.AutoUpdater.NET.resources.dll")
  $resources+="/resource:$PSScriptRoot\bundled\fonts\NanumSquareR.ttf,PubgScore.Bundled.NanumSquareR.ttf"
  $resources+="/resource:$PSScriptRoot\bundled\fonts\NanumSquare-OFL.txt,PubgScore.Bundled.NanumSquare-OFL.txt"
  if($release.Role -eq 'BJ') {
   $resources+="/resource:$PSScriptRoot\bundled\fonts\DoHyeon-Regular.ttf,PubgScore.Bundled.DoHyeon-Regular.ttf"
   $resources+="/resource:$PSScriptRoot\bundled\fonts\DoHyeon-OFL.txt,PubgScore.Bundled.DoHyeon-OFL.txt"
  }
  $symbols=$release.Symbol+',SINGLEFILE';if($UpdateTest){$symbols+=',UPDATE_TESTING'}
  if($ReviewBuild){$symbols+=',SIGNPATH_REVIEW'}
  & $compiler /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu "/win32icon:$($release.Icon)" /win32manifest:app.manifest "/define:$symbols" "/out:$outputPath\$($release.Name)" @references @resources src\*.cs
  if($LASTEXITCODE -ne 0){throw "Single-file build failed: $($release.Role)"}
 }
} finally {Pop-Location}
