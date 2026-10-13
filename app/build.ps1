$ErrorActionPreference = 'Stop'
$appRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework C# compiler unavailable.' }
$bin = Join-Path $appRoot 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$source = @(Get-ChildItem -LiteralPath (Join-Path $appRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$reference = @('/r:System.Core.dll','/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
& $compiler /nologo /utf8output /platform:x86 /optimize+ /target:exe (('/out:' + (Join-Path $bin 'OwnLabViewMcp.exe'))) $reference $source
if ($LASTEXITCODE -ne 0) { throw 'MCP build failed.' }
& $compiler /nologo /utf8output /platform:x86 /optimize+ /target:winexe (('/out:' + (Join-Path $bin 'OwnLabViewStudio.exe'))) $reference $source
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
$testResult = & (Join-Path $bin 'OwnLabViewMcp.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Self-tests failed.' }
[IO.File]::WriteAllText((Join-Path $appRoot 'tests\unit-acceptance.json'),($testResult -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
$testResult
