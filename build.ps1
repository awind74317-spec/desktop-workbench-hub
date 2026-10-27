$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /utf8output /win32manifest:app.manifest /out:WorkbenchHub.exe /r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:System.Web.Extensions.dll "/r:$wpf\PresentationFramework.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\WindowsBase.dll" Hub.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
