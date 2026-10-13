$ErrorActionPreference = 'Stop'
$taskApp = Split-Path -Parent $PSScriptRoot
$taskExe = Join-Path $taskApp 'bin\OwnLabViewMcp.exe'
$taskRun = Join-Path $taskApp ('projects\ClassDemo_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
function Invoke-DemoTool([string]$taskName, [string]$taskTool, $taskArguments) {
    $taskInput = Join-Path $taskRun ($taskName + '-request.json')
    [IO.File]::WriteAllText($taskInput, ($taskArguments | ConvertTo-Json -Depth 60), [Text.UTF8Encoding]::new($false))
    $taskOutput = & $taskExe --call $taskTool $taskInput
    if ($LASTEXITCODE -ne 0) { throw ($taskTool + ' failed: ' + ($taskOutput -join "`n")) }
    $taskText = $taskOutput -join "`n"
    [IO.File]::WriteAllText((Join-Path $taskRun ($taskName + '.json')), $taskText, [Text.UTF8Encoding]::new($false))
    return ($taskText | ConvertFrom-Json)
}
# Each operation creates a unique copy. This example never runs generated VIs.
$taskRecipe = Get-Content (Join-Path $taskApp 'recipes\connector-auto-sum.json') -Raw | ConvertFrom-Json
$taskSum = Invoke-DemoTool 'sum' 'build_recipe' @{recipe=$taskRecipe}
$taskRecipe.name = 'DemoMultiplyBody'
foreach ($taskStep in $taskRecipe.steps) {
    if ($taskStep.id -eq 'add') { $taskStep.args.style = 'Multiply' }
    if ($taskStep.id -eq 'wo') { $taskStep.args.source_terminal = '$add.pins.x*y' }
}
$taskMultiply = Invoke-DemoTool 'multiply' 'build_recipe' @{recipe=$taskRecipe}
$taskBase = Invoke-DemoTool 'base' 'create_type_artifact' @{
    name='DemoCalculator'; kind='class'; controls=@()
    methods=@(@{name='Compute'; source_path=$taskSum.vi_path; dispatch='dynamic'})
}
$taskChild = Invoke-DemoTool 'child' 'create_type_artifact' @{
    name='DemoMultiplier'; kind='class'; controls=@(); parent_class=$taskBase.vi_path
    overrides=@(@{method='Compute.vi'; source_path=$taskMultiply.vi_path})
}
# The child's class bundle includes its own independent parent snapshot.
$taskProject = Invoke-DemoTool 'project' 'create_project' @{
    name='DynamicClassDemo'; vi_paths=@($taskChild.vi_path)
}
$taskResult = [ordered]@{
    created=$true; project_path=$taskProject.project_path
    base_class=$taskBase.vi_path; child_class=$taskChild.vi_path
    requests_and_reports=$taskRun; generated_vis_executed=$false
}
[IO.File]::WriteAllText((Join-Path $taskRun 'demo-result.json'), ($taskResult | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
$taskResult | ConvertTo-Json -Depth 6
