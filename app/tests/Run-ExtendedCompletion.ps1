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
$taskResults.ranked=PublicSuite 'accept_ranked_class'
RuntimeSuite 'RunComplexClass' $taskResults.ranked
Write-Output 'Ranked class passed.'
$taskResults.nested=PublicSuite 'accept_nested_methods'
RuntimeSuite 'RunCustomMethods' $taskResults.nested
RuntimeSuite 'RunTypedMethods' $taskResults.nested
Write-Output 'Nested method closure passed.'
$taskResults.interfaces=PublicSuite 'accept_interfaces'
RuntimeSuite 'RunInterfaceMethods' $taskResults.interfaces
Write-Output 'Interface dispatch passed.'
$taskResults.libraries=PublicSuite 'accept_libraries'
RuntimeSuite 'ReopenLibraries' $taskResults.libraries
Write-Output 'Libraries passed.'
$taskResults.build_specs=PublicSuite 'accept_build_specs'
$taskExe=Join-Path $taskDev 'RunBuiltDll.exe'
& $taskCompiler /nologo /platform:x86 /main:RunBuiltDll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ('/out:'+$taskExe) $taskSources (Join-Path $taskDev 'RunBuiltDll.cs')
if($LASTEXITCODE -ne 0){throw 'DLL runner compile failed'}
$taskBuild=Get-Content (Join-Path $taskResults.build_specs 'first.json') -Raw | ConvertFrom-Json
$taskDll=Join-Path $taskBuild.build_results[1].destination 'FiniteDLL.dll'
& $taskExe $taskDll (Join-Path $taskResults.build_specs 'dll-runtime.json')
if($LASTEXITCODE -ne 0){throw 'DLL runtime failed'}
$taskRecord=[ordered]@{passed=$true;completed=(Get-Date).ToString('o');suites=$taskResults;executable_sha256=(Get-FileHash (Join-Path $taskApp 'bin\OwnLabViewMcp.exe') -Algorithm SHA256).Hash.ToLowerInvariant();window_operations_used=$false}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'completion-extended-latest.json'),($taskRecord | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$taskRecord | ConvertTo-Json -Depth 6
