$ErrorActionPreference='Stop'
$taskApp=Split-Path -Parent $PSScriptRoot
$taskDev=Join-Path $taskApp 'development'
$taskPython=if($env:OWN_PYTHON){$env:OWN_PYTHON}elseif(Test-Path 'C:\Program Files (x86)\vis-env-32bit\python.exe'){'C:\Program Files (x86)\vis-env-32bit\python.exe'}else{'python'}
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskSources=@(Get-ChildItem (Join-Path $taskApp 'src') -Filter '*.cs' | ForEach-Object FullName)
$taskResults=[ordered]@{}
function PublicSuite([string]$taskModule) {
    $taskOutput=& $taskPython -X utf8 -c 'import runpy,sys;sys.path.insert(0,sys.argv[1]);runpy.run_module(sys.argv[2],run_name="__main__")' $taskDev $taskModule
    if($LASTEXITCODE -ne 0){throw ($taskModule+': '+($taskOutput -join "`n"))}
    $taskResult=($taskOutput -join "`n") | ConvertFrom-Json
    if(-not $taskResult.passed){throw ($taskModule+' failed')}
    return $taskResult.folder
}
function RuntimeSuite([string]$taskHarness,[string]$taskFolder) {
    $taskExe=Join-Path $taskDev ($taskHarness+'.exe')
    & $taskCompiler /nologo /platform:x86 ('/main:'+$taskHarness) /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ('/out:'+$taskExe) $taskSources (Join-Path $taskDev 'ClassGraph.cs') (Join-Path $taskDev ($taskHarness+'.cs'))
    if($LASTEXITCODE -ne 0){throw ($taskHarness+' compile failed')}
    foreach($taskMode in @('create','reopen')){
        & $taskExe $taskApp $taskFolder $taskMode
        if($LASTEXITCODE -ne 0){throw ($taskHarness+' '+$taskMode+' failed: '+$taskFolder)}
    }
}
$taskResults.methods=PublicSuite 'accept_custom_methods'
RuntimeSuite 'RunCustomMethods' $taskResults.methods
RuntimeSuite 'RunTypedMethods' $taskResults.methods
Write-Output 'Custom method public, dynamic runtime and typed runtime passed.'
$taskResults.scalar=PublicSuite 'accept_oop_suite'
RuntimeSuite 'RunClassSuite' $taskResults.scalar
Write-Output 'Scalar class regression passed.'
$taskResults.complex=PublicSuite 'accept_complex_class'
RuntimeSuite 'RunComplexClass' $taskResults.complex
Write-Output 'Complex class regression passed.'
$taskPrevious=$env:OWN_METHOD_SUITE
try{$env:OWN_METHOD_SUITE=$taskResults.methods;$taskResults.project=PublicSuite 'accept_mixed_project'}finally{$env:OWN_METHOD_SUITE=$taskPrevious}
$taskExe=Join-Path $taskDev 'ReadMixedProject.exe'
& $taskCompiler /nologo /platform:x86 /main:ReadMixedProject /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ('/out:'+$taskExe) $taskSources (Join-Path $taskDev 'ReadMixedProject.cs')
if($LASTEXITCODE -ne 0){throw 'Project reader compile failed'}
foreach($taskKind in @('project','copy')){
    & $taskExe $taskApp (Join-Path $taskResults.project ($taskKind+'.json')) (Join-Path $taskResults.project ($taskKind+'-reopen.json'))
    if($LASTEXITCODE -ne 0){throw 'Mixed project reopen failed'}
}
$taskRecord=[ordered]@{passed=$true;completed=(Get-Date).ToString('o');suites=$taskResults;executable_sha256=(Get-FileHash (Join-Path $taskApp 'bin\OwnLabViewMcp.exe') -Algorithm SHA256).Hash.ToLowerInvariant();window_operations_used=$false}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'final-class-batch-latest.json'),($taskRecord | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$taskRecord | ConvertTo-Json -Depth 6
