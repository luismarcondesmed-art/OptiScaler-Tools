$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler not found.' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | ForEach-Object FullName)
$output = Join-Path $PSScriptRoot 'OptiScaler-Tools.exe'
$plugin = Join-Path $PSScriptRoot 'source\dlssenabler\version.dll'
if (-not (Test-Path -LiteralPath $plugin)) { $plugin = Join-Path $PSScriptRoot 'plugins\version.dll' }
$references = @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
$wpfRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references += @("/r:$wpfRoot\PresentationFramework.dll", "/r:$wpfRoot\PresentationCore.dll", "/r:$wpfRoot\WindowsBase.dll", '/r:System.Xaml.dll')
$theme = Join-Path $PSScriptRoot 'src\Theme.xaml'
& $compiler /nologo /optimize+ /target:winexe /main:Program /platform:anycpu $references "/resource:$plugin,version.dll" "/resource:$theme,Theme.xaml" "/out:$output" $sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
$setup = Join-Path $PSScriptRoot 'setup_optiscaler.exe'
& $compiler /nologo /optimize+ /target:exe /main:SetupEntry /platform:anycpu $references "/resource:$theme,Theme.xaml" "/out:$setup" $sources
if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
$testLogDir = Join-Path $PSScriptRoot 'output\.logs'
New-Item -ItemType Directory -Path $testLogDir -Force | Out-Null
$testOutput = Join-Path $testLogDir 'test-results.txt'
$testError = Join-Path $testLogDir 'test-errors.txt'
$testProcess = Start-Process -FilePath $output -ArgumentList '--self-test','--no-pause' -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $testOutput -RedirectStandardError $testError
Get-Content -LiteralPath $testOutput
if ($testProcess.ExitCode -ne 0) { throw 'Tests failed.' }
Write-Host "Built: $output"
