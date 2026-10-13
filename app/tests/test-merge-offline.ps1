$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $PSScriptRoot ('vi-merge-acceptance-' + [guid]::NewGuid().ToString('N'))
$testBin = Join-Path $testRoot 'bin'
New-Item -ItemType Directory -Path $testBin | Out-Null
$mergeRecipeFolder = Join-Path $testRoot 'recipes'
New-Item -ItemType Directory -Path $mergeRecipeFolder | Out-Null
Copy-Item -LiteralPath (Join-Path $appRoot 'recipes\operations.json') -Destination (Join-Path $mergeRecipeFolder 'operations.json')
$sources = @(Get-ChildItem -LiteralPath (Join-Path $appRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$sources += Join-Path $PSScriptRoot 'ViMergeAcceptance.cs'
& "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" /nologo /utf8output /platform:x86 /target:exe /main:ViMergeAcceptance ('/out:' + (Join-Path $testBin 'ViMergeAcceptance.exe')) /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Merge offline acceptance compilation failed' }
& (Join-Path $testBin 'ViMergeAcceptance.exe')
if ($LASTEXITCODE -ne 0) { throw 'Merge offline acceptance failed' }
Write-Output (Join-Path $testRoot 'acceptance.json')
