param(
 [string[]]$Suite=@('CoreTests','EmptyTeamRankTests','BjTests','InteractionTests','ResultExportTests','PlacementTests','NumericRoundTests','CreatedRoomRosterUiTests','IncrementalTests','ResourceCoreTests','ResourceNetworkTests','ResourceUiTests','RosterResetUiTests'),
 [string]$OutputDirectory=(Join-Path $PSScriptRoot 'test-output'),
 [switch]$BuildOnly,
 [string[]]$TestArguments=@()
)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@(Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Where-Object Name -ne 'CredentialVault.cs' | ForEach-Object FullName)
$updater=Join-Path $PSScriptRoot 'vendor\AutoUpdater.NET.1.9.3\AutoUpdater.NET.dll'
$refs=@('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll','/r:System.Xml.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',("/r:"+$updater))
Copy-Item -LiteralPath $updater -Destination (Join-Path $out 'AutoUpdater.NET.dll') -Force
Push-Location $PSScriptRoot
try {
 foreach($name in $Suite){
  if($name -notmatch '^[A-Za-z][A-Za-z0-9]*Tests$'){throw 'Unexpected suite name'}
  $test=Join-Path $PSScriptRoot "tests\$name.cs"
  $resources=@()
  if($name -eq 'ResultExportTests'){$resources+="/resource:$PSScriptRoot\bundled\fonts\DoHyeon-Regular.ttf,PubgScore.Bundled.DoHyeon-Regular.ttf"}
  & $compiler /nologo /optimize+ /target:exe /codepage:65001 "/out:$out\$name.exe" "/main:$name" @refs @resources @sources .\tests\TestCredentialVault.cs .\tests\TestGameClient.cs $test
  if($LASTEXITCODE -ne 0){throw "Compile failed: $name"}
  if($BuildOnly){continue}
  $testArgs=$TestArguments
  if($name -in @('ResultExportTests','PlacementTests','KillDealScoreTests')){$testArgs=@($out)}
  if($name -eq 'BjTests'){$testArgs=@((Join-Path $out 'bj.png'))}
  & "$out\$name.exe" @testArgs *> "$out\$name.log"
  $code=$LASTEXITCODE
  Get-Content "$out\$name.log" -Tail 2
  if($code -ne 0){throw "$name failed ($code); inspect $out\$name.log"}
 }
}finally{Pop-Location}
