param([switch]$Offline,[string]$ReportPath)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$process = New-Object System.Diagnostics.Process
$process.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
$process.StartInfo.FileName = Join-Path $appRoot 'bin\OwnLabViewMcp.exe'
$process.StartInfo.Arguments = '--mcp'
$process.StartInfo.UseShellExecute = $false
$process.StartInfo.CreateNoWindow = $true
$process.StartInfo.RedirectStandardInput = $true
$process.StartInfo.RedirectStandardOutput = $true
$process.StartInfo.RedirectStandardError = $true
$process.StartInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$process.StartInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
$process.StartInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
$processStarted = $false
$stderrRead = $null
$runToken = [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss') + '_' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$script:requestId = 0
$script:checks = @()
function Request($method, $parameters) {
    $script:requestId++
    $request = @{jsonrpc='2.0';id=$script:requestId;method=$method;params=$parameters}
    $process.StandardInput.WriteLine(($request | ConvertTo-Json -Depth 30 -Compress))
    $process.StandardInput.Flush()
    $pending = $process.StandardOutput.ReadLineAsync()
    if (-not $pending.Wait(200000)) { throw "MCP timed out for $method" }
    if ([string]::IsNullOrWhiteSpace($pending.Result)) { throw "MCP closed its output before replying to $method" }
    $response = $pending.Result | ConvertFrom-Json
    if ($response.jsonrpc -ne '2.0') { throw 'MCP response has an invalid JSON-RPC version' }
    if ($response.id -ne $script:requestId) { throw 'MCP response id mismatch' }
    if ($response.error) { throw ($response.error | ConvertTo-Json -Compress) }
    return $response.result
}
function CallTool($name, $arguments) {
    $result = Request 'tools/call' @{name=$name;arguments=$arguments}
    if (@($result.content).Count -eq 0 -or $result.content[0].type -ne 'text') { throw "MCP tool $name returned no text result" }
    if ($result.isError) { throw $result.content[0].text }
    return $result.content[0].text | ConvertFrom-Json
}
function Check($name, $value) {
    if (-not $value) { throw "FAILED: $name" }
    $script:checks += @{name=$name;passed=$true}
}
function HelpersLoadedFromApp($report) {
    if ($null -eq $report.loaded_helper_paths) { return $false }
    $allowedFolders = @([IO.Path]::GetFullPath((Join-Path $appRoot 'helpers')),[IO.Path]::GetFullPath((Join-Path $appRoot 'recipes\helpers')),[IO.Path]::GetFullPath((Join-Path $appRoot 'type-helpers')))
    $loaded = @($report.loaded_helper_paths.PSObject.Properties)
    if ($loaded.Count -eq 0) { return $false }
    foreach ($entry in $loaded) {
        if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($entry.Value)) -notin $allowedFolders) { return $false }
    }
    return $true
}
try {
    $processStarted = $process.Start()
    if (-not $processStarted) { throw 'MCP process did not start' }
    # Drain stderr while tools run so a full redirected pipe cannot deadlock the test.
    $stderrRead = $process.StandardError.ReadToEndAsync()
    $initialize = Request 'initialize' @{protocolVersion='2025-06-18';capabilities=@{};clientInfo=@{name='OwnStudio acceptance';version='1'}}
    Check 'MCP initialized' ($initialize.serverInfo.name -eq 'own-labview-studio')
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $process.StandardInput.Flush()
    $toolList = Request 'tools/list' @{}
    $expectedTools = @('studio_capabilities','export_vi_html','auto_wire_copy','import_vi','merge_vi','place_ni_file','prepare_file','create_type_artifact','create_library','create_project','search_styles','find_ni_examples','validate_graph','evaluate_graph','save_graph','labview_status','inspect_vi','build_vi','recipe_catalog','validate_recipe','build_recipe')
    $actualTools = @($toolList.tools | ForEach-Object { $_.name })
    Check 'All expected MCP tools advertised exactly once' (($actualTools.Count -eq $expectedTools.Count) -and (@(Compare-Object -ReferenceObject $expectedTools -DifferenceObject $actualTools).Count -eq 0))
    $styles = CallTool 'search_styles' @{query='Add';limit=5}
    Check 'MCP style discovery returns exact Add candidate with explicit metadata boundary' (($styles.matches[0].name -eq 'Add') -and ($styles.matches[0].style -eq 1050) -and $styles.metadata_only -and (-not $styles.individually_tested))
    $examples = CallTool 'find_ni_examples' @{query='VI Scripting';limit=5}
    Check 'MCP finds installed NI scripting examples without opening them' ((@($examples.matches).Count -gt 0) -and (-not $examples.llb_contents_enumerated))
    $browse = CallTool 'find_ni_examples' @{limit=2}
    Check 'MCP browses direct example entries without a query' (($browse.mode -eq 'browse') -and ($browse.matches.Count -eq 2) -and $browse.listing_complete)
    $browseNext = CallTool 'find_ni_examples' $browse.next_page_args
    Check 'MCP directory pagination preserves snapshot without duplicate entries' (($browse.snapshot_id -eq $browseNext.snapshot_id) -and (@($browse.matches.path | Where-Object { $_ -in $browseNext.matches.path }).Count -eq 0))
    $graph = Get-Content -LiteralPath (Join-Path $appRoot 'examples\arithmetic.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $validation = CallTool 'validate_graph' @{graph=$graph}
    Check 'Real MCP graph validation' $validation.valid
    $evaluation = CallTool 'evaluate_graph' @{graph=$graph;inputs=@{a=9.5;b=3}}
    Check 'Offline outputs distinguished from NI runtime' (($evaluation.outputs.result -eq 9.5) -and (-not $evaluation.labview_executed))
    $negative = Request 'tools/call' @{name='labview_status';arguments=@{run=$true}}
    Check 'Unknown mutation parameter refused' $negative.isError
    $capabilities = CallTool 'studio_capabilities' @{}
    $acceptanceValid=$false
    if(Test-Path -LiteralPath $capabilities.acceptance_report){
        $acceptance=Get-Content -LiteralPath $capabilities.acceptance_report -Raw -Encoding UTF8 | ConvertFrom-Json
        $acceptanceValid=$acceptance.agreed_scope_acceptance_passed -and @($acceptance.production_snapshot | Where-Object {$_.path -eq (Join-Path $appRoot 'bin\OwnLabViewMcp.exe')}).Count -eq 1 -and @($acceptance.production_snapshot | Where-Object {(-not (Test-Path -LiteralPath $_.path)) -or ((Get-FileHash -LiteralPath $_.path).Hash -ne $_.sha256)}).Count -eq 0
    }
    Check 'Complete-replacement claim agrees with current production receipt' ($capabilities.complete_replacement -eq $acceptanceValid)
    if ($Offline) {
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(10000)) { throw 'MCP did not close on EOF' }
        Check 'Clean offline EOF shutdown' ($process.ExitCode -eq 0)
        $offlineReport = @{passed=$true;run_token=$runToken;tests=$script:checks;tool_names=$actualTools;labview_called=$false;scope='Protocol, discovery, graph validation and evaluation only';stderr=$stderrRead.Result;complete_replacement=$capabilities.complete_replacement}
        $offlineJson = $offlineReport | ConvertTo-Json -Depth 20
        [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'mcp-offline-acceptance.json'),$offlineJson,[Text.UTF8Encoding]::new($false))
        $offlineJson
        return
    }
    $graph.name = 'McpArithmetic_' + $runToken
    $build = CallTool 'build_vi' @{graph=$graph;verify=$true}
    Check 'MCP built and ran new real VI' ($build.created -and $build.execution_verified -and ($build.actual.result -eq 8.5))
    Check 'Arithmetic helpers actually loaded from this app' (HelpersLoadedFromApp $build)
    $buildReportPath = Join-Path (Split-Path -Parent $build.vi_path) 'build-report.json'
    Check 'Arithmetic artifact and report exist' ((Test-Path -LiteralPath $build.vi_path -PathType Leaf) -and (Test-Path -LiteralPath $buildReportPath -PathType Leaf))
    $buildDisk = Get-Content -LiteralPath $buildReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Check 'Arithmetic disk report matches real verification' (($buildDisk.vi_path -eq $build.vi_path) -and $buildDisk.execution_verified -and ($buildDisk.actual.result -eq 8.5) -and ($buildDisk.vi_sha256 -eq (Get-FileHash -LiteralPath $build.vi_path -Algorithm SHA256).Hash))
    $inspection = CallTool 'inspect_vi' @{path=$build.vi_path}
    Check 'VI metadata inspection left bytes unchanged' ($inspection.file_unchanged -and ($inspection.sha256 -eq $build.vi_sha256) -and (-not $inspection.ran_vi))

    $recipe = Get-Content -LiteralPath (Join-Path $appRoot 'recipes\multiply.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $recipe.name = 'McpMultiply_' + $runToken
    $catalog = CallTool 'recipe_catalog' @{}
    $catalogNames = @($catalog.operations.PSObject.Properties | ForEach-Object { $_.Name })
    $neededOperations = @($recipe.steps | ForEach-Object { $_.op } | Sort-Object -Unique)
    $missingOperations = @($neededOperations | Where-Object { $_ -notin $catalogNames })
    Check 'Recipe catalog contains multiply operation contracts' (($catalogNames.Count -gt 0) -and ($missingOperations.Count -eq 0) -and ($catalog.complete_replacement -eq $false))
    foreach ($operation in $neededOperations) {
        $contract = $catalog.operations.PSObject.Properties[$operation].Value
        Check ("Recipe contract includes helper and outputs: " + $operation) ((-not [string]::IsNullOrWhiteSpace($contract.helper)) -and ($null -ne $contract.outputs))
    }
    $recipeValidation = CallTool 'validate_recipe' @{recipe=$recipe}
    Check 'MCP validates recipe without NI execution' (($recipeValidation.valid -eq $true) -and ($recipeValidation.labview_executed -eq $false) -and ($recipeValidation.step_count -eq @($recipe.steps).Count))
    # This endpoint builds and inspects only; never call Run on the generic recipe VI.
    $recipeBuild = CallTool 'build_recipe' @{recipe=$recipe}
    Check 'MCP builds multiply recipe with static acceptance only' (($recipeBuild.created -eq $true) -and ($recipeBuild.static_checks_passed -eq $true) -and ($recipeBuild.execution_state -eq 1) -and ($recipeBuild.generated_vi_executed -eq $false) -and (@($recipeBuild.broken_wire_ids).Count -eq 0) -and ($recipeBuild.step_count -eq @($recipe.steps).Count))
    Check 'Recipe helpers actually loaded from this app' (HelpersLoadedFromApp $recipeBuild)
    $recipeReportPath = Join-Path (Split-Path -Parent $recipeBuild.vi_path) 'recipe-report.json'
    Check 'Recipe artifact and report exist' ((Test-Path -LiteralPath $recipeBuild.vi_path -PathType Leaf) -and (Test-Path -LiteralPath $recipeReportPath -PathType Leaf))
    $recipeDisk = Get-Content -LiteralPath $recipeReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Check 'Recipe disk evidence retains static-only boundary' (($recipeDisk.vi_path -eq $recipeBuild.vi_path) -and ($recipeDisk.static_checks_passed -eq $true) -and ($recipeDisk.generated_vi_executed -eq $false) -and ($recipeDisk.complete_replacement -eq $false) -and ($recipeDisk.vi_sha256 -eq $recipeBuild.vi_sha256) -and ($recipeDisk.vi_sha256 -eq (Get-FileHash -LiteralPath $recipeBuild.vi_path -Algorithm SHA256).Hash))

    $imported=CallTool 'import_vi' @{source_path=$recipeBuild.vi_path;name=('McpImport_'+$runToken)}
    Check 'MCP imports a compiled VI into a unique saved copy' ($imported.created -and ($imported.artifact_kind -eq 'imported_vi') -and ($imported.vi_path -ne $recipeBuild.vi_path) -and ($imported.execution_state -eq 1) -and $imported.static_checks_passed)
    Check 'Import preserves source bytes and never explicitly opens saves or runs source' ($imported.source_file_unchanged -and (-not $imported.import_source.explicitly_opened_in_labview) -and (-not $imported.import_source.saved_in_labview) -and (-not $imported.import_source.executed) -and ((Get-FileHash -LiteralPath $recipeBuild.vi_path).Hash -eq $recipeBuild.vi_sha256))
    Check 'Import audits static callees without claiming runtime or dynamic validation' ($imported.static_vi_callee_audit_complete -and (@($imported.saved_callee_closure).Count -eq 0) -and (-not $imported.generated_vi_executed) -and (-not $imported.runtime_tested) -and (-not $imported.dynamic_vi_and_dll_audit_performed))
    $importCopyRecipe=@{schema_version=1;name=('McpImportEdit_'+$runToken);source_vi=$imported.vi_path;steps=@(@{id='output';op='find_control';args=@{label='Result'}},@{id='rename';op='label';args=@{control_id='$output.control_id';label='ImportedResult'}})}
    $importEdited=CallTool 'build_recipe' @{recipe=$importCopyRecipe}
    Check 'Imported VI can be edited through the same recipe interface' ($importEdited.created -and $importEdited.static_checks_passed -and (-not $importEdited.generated_vi_executed) -and (HelpersLoadedFromApp $importEdited))
    Check 'Editing an import preserves the imported file and the original file' (((Get-FileHash -LiteralPath $imported.vi_path).Hash -eq $imported.vi_sha256) -and ((Get-FileHash -LiteralPath $recipeBuild.vi_path).Hash -eq $recipeBuild.vi_sha256))

    $generic = Get-Content -Raw -LiteralPath (Join-Path $appRoot 'recipes\bound-control-read-reset.json') -Encoding UTF8 | ConvertFrom-Json
    $generic.name = 'McpBound_' + $runToken
    $genericBuild = CallTool 'build_recipe' @{recipe=$generic}
    Check 'MCP builds bound Property-Invoke graph without running it' ($genericBuild.created -and $genericBuild.static_checks_passed -and (-not $genericBuild.generated_vi_executed))
    Check 'Generic helpers actually loaded from this app' (HelpersLoadedFromApp $genericBuild)
    $methodMetadata = @($genericBuild.steps | Where-Object id -eq 'methodMetadata')[0].outputs
    Check 'Modern method metadata preserves exact NI method UID' ($methodMetadata.method[0] -eq '6331c00')
    $rawTerms = @($genericBuild.steps | Where-Object id -eq 'resetAfterWire')[0].outputs
    Check 'Duplicate Invoke terminal labels retained as raw sequence only' ((@($rawTerms.terminal_names | Where-Object { $_ -eq 'Reinit To Dflt' }).Count -eq 2) -and ('Reinit To Dflt' -in $rawTerms.ambiguous_pin_names) -and ($null -eq $rawTerms.pins.PSObject.Properties['Reinit To Dflt']))
    $copy = @{schema_version=1;name=('McpCopy_'+$runToken);source_vi=$recipeBuild.vi_path;steps=@(@{id='output';op='find_control';args=@{label='Result'}},@{id='rename';op='label';args=@{control_id='$output.control_id';label='CopiedResult'}})}
    $copyBuild = CallTool 'build_recipe' @{recipe=$copy}
    Check 'MCP copies and edits generated VI without execution' ($copyBuild.created -and $copyBuild.static_checks_passed -and (-not $copyBuild.generated_vi_executed))
    $copyEvidence = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $copyBuild.vi_path) 'source-copy-report.json') | ConvertFrom-Json
    Check 'MCP copy retains exact source bytes without source Save or Run' ($copyEvidence.source_file_unchanged -and (-not $copyEvidence.source_saved) -and (-not $copyEvidence.source_executed) -and ((Get-FileHash -LiteralPath $recipeBuild.vi_path).Hash -eq $recipeBuild.vi_sha256))
    $formula = Get-Content -Raw -LiteralPath (Join-Path $appRoot 'recipes\formula-square.json') | ConvertFrom-Json
    $formula.name = 'McpFormula_' + $runToken
    $formulaBuild = CallTool 'build_recipe' @{recipe=$formula}
    Check 'MCP Formula parameter editing and creation compile without execution' ($formulaBuild.created -and $formulaBuild.static_checks_passed -and (-not $formulaBuild.generated_vi_executed))
    Check 'Formula helper paths belong to this app' (HelpersLoadedFromApp $formulaBuild)
    Check 'Formula parameter readback preserves name and direction' ((@($formulaBuild.steps | Where-Object id -eq 'xInfo')[0].outputs.name -eq 'x') -and @($formulaBuild.steps | Where-Object id -eq 'xInfo')[0].outputs.is_input -and (@($formulaBuild.steps | Where-Object id -eq 'yInfo')[0].outputs.name -eq 'y') -and (-not @($formulaBuild.steps | Where-Object id -eq 'yInfo')[0].outputs.is_input))
    $formula.name = 'McpBadFormula_' + $runToken
    @($formula.steps | Where-Object op -eq 'formula_expression')[0].args.expression = 'y = x * ;'
    $badFormulaBuild = CallTool 'build_recipe' @{recipe=$formula}
    Check 'Invalid Formula syntax is saved but not claimed executable or run' ($badFormulaBuild.created -and (-not $badFormulaBuild.static_checks_passed) -and ($badFormulaBuild.execution_state -eq 0) -and (-not $badFormulaBuild.generated_vi_executed))

    $namedRecipe = Get-Content -LiteralPath (Join-Path $appRoot 'recipes\named-nested-cluster.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $namedRecipe.name = 'McpNamed_' + $runToken
    $namedBuild = CallTool 'build_recipe' @{recipe=$namedRecipe}
    Check 'MCP builds nested Cluster and checked named Bundle/Unbundle selections' ($namedBuild.created -and $namedBuild.static_checks_passed -and ($namedBuild.execution_state -eq 1) -and (-not $namedBuild.generated_vi_executed))
    $namedHelpers=@($namedBuild.loaded_helper_paths.PSObject.Properties | Where-Object Name -like 'Own20261012_*Named*')
    Check 'Named selectors load from this application' (($namedHelpers.Count -eq 4) -and (@($namedHelpers | Where-Object { -not $_.Value.StartsWith((Join-Path $appRoot 'recipes\helpers\'),[StringComparison]::OrdinalIgnoreCase) }).Count -eq 0))
    $valuesRecipe=Get-Content -LiteralPath (Join-Path $appRoot 'recipes\constants-defaults.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $valuesRecipe.name='McpValues_' + $runToken
    $valuesBuild=CallTool 'build_recipe' @{recipe=$valuesRecipe}
    Check 'MCP creates DBL constants and checked saved control defaults' ($valuesBuild.created -and $valuesBuild.static_checks_passed -and ($valuesBuild.execution_state -eq 1) -and (-not $valuesBuild.generated_vi_executed))
    Check 'Constant and default helper paths belong to this app' (HelpersLoadedFromApp $valuesBuild)
    $arrayRecipe=Get-Content -LiteralPath (Join-Path $appRoot 'recipes\array-defaults.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $arrayRecipe.name='McpArrays_'+$runToken
    $arrayBuild=CallTool 'build_recipe' @{recipe=$arrayRecipe}
    Check 'MCP builds typed 1D and 2D Array defaults without running the graph' ($arrayBuild.created -and $arrayBuild.static_checks_passed -and (-not $arrayBuild.generated_vi_executed))
    Check 'Array defaults use migrated helpers from this application' (HelpersLoadedFromApp $arrayBuild)

    $typeBuild=CallTool 'create_type_artifact' @{name=('McpType_'+$runToken);kind='control';control_vi_type=2;controls=@(@{kind='cluster';label='Record';children=@(@{kind='numeric';label='N'},@{kind='string';label='S'},@{kind='boolean';label='B'});value=@(-2.5,'MCP type',$true)})}
    Check 'MCP creates a self-contained CTL with exact VI and control types' ($typeBuild.created -and $typeBuild.static_checks_passed -and ($typeBuild.vi_type -eq 2) -and ($typeBuild.control_vi_type -eq 2) -and (-not $typeBuild.generated_vi_executed) -and (@($typeBuild.saved_direct_callees).Count -eq 0))
    Check 'CTL helper paths belong to this app and saved defaults match' ((HelpersLoadedFromApp $typeBuild) -and ($typeBuild.defaults.Record[0] -eq -2.5) -and ($typeBuild.defaults.Record[1] -eq 'MCP type') -and ($typeBuild.defaults.Record[2] -eq $true) -and ((Get-FileHash -LiteralPath $typeBuild.vi_path -Algorithm SHA256).Hash -eq $typeBuild.vi_sha256))
    $globalBuild=CallTool 'create_type_artifact' @{name=('McpGlobal_'+$runToken);kind='global';controls=@(@{kind='numeric';label='N';value=-1.5},@{kind='boolean';label='B';value=$true})}
    Check 'MCP creates a global file without running it' ($globalBuild.created -and $globalBuild.static_checks_passed -and ($globalBuild.vi_type -eq 3) -and ($globalBuild.artifact_kind -eq 'global') -and (-not $globalBuild.generated_vi_executed))
    Check 'Global defaults, file hash and helper paths match' (($globalBuild.defaults.N -eq -1.5) -and ($globalBuild.defaults.B -eq $true) -and (HelpersLoadedFromApp $globalBuild) -and ((Get-FileHash -LiteralPath $globalBuild.vi_path -Algorithm SHA256).Hash -eq $globalBuild.vi_sha256))

    $memberPaths = @($build.vi_path,$recipeBuild.vi_path)
    $project = CallTool 'create_project' @{name=('McpProject_' + $runToken);vi_paths=$memberPaths}
    Check 'MCP creates project with both new VIs without running them' (($project.created -eq $true) -and ($project.member_count -eq 2) -and ($project.source_vis_unchanged -eq $true) -and ($project.generated_vis_executed -eq $false) -and ($project.source_vis_saved -eq $false) -and ($project.complete_replacement -eq $false))
    $projectReportPath = Join-Path (Split-Path -Parent $project.project_path) 'project-report.json'
    Check 'Project and disk report exist' (([IO.Path]::GetExtension($project.project_path) -eq '.lvproj') -and (Test-Path -LiteralPath $project.project_path -PathType Leaf) -and (Test-Path -LiteralPath $projectReportPath -PathType Leaf))
    $returnedMemberPaths = @($project.xml_member_references | ForEach-Object { $_.path })
    Check 'Project XML readback confirms exact requested members' (($returnedMemberPaths.Count -eq 2) -and (@(Compare-Object -ReferenceObject $memberPaths -DifferenceObject $returnedMemberPaths).Count -eq 0) -and (@($project.xml_member_references | Where-Object { $_.matched -ne $true }).Count -eq 0))
    Check 'Project source checks prove both disk hashes unchanged' ((@($project.source_checks).Count -eq 2) -and (@($project.source_checks | Where-Object { ($_.unchanged -ne $true) -or ($_.before_sha256 -ne $_.after_sha256) -or (-not [string]::IsNullOrEmpty($_.error)) }).Count -eq 0))
    Check 'Actual VI files still match their original build hashes' (((Get-FileHash -LiteralPath $build.vi_path -Algorithm SHA256).Hash -eq $build.vi_sha256) -and ((Get-FileHash -LiteralPath $recipeBuild.vi_path -Algorithm SHA256).Hash -eq $recipeBuild.vi_sha256))
    $projectDisk = Get-Content -LiteralPath $projectReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $diskMemberPaths = @($projectDisk.xml_member_references | ForEach-Object { $_.path })
    Check 'Project disk report agrees with returned acceptance evidence' (($projectDisk.created -eq $true) -and ($projectDisk.project_path -eq $project.project_path) -and ($projectDisk.project_sha256 -eq $project.project_sha256) -and ($projectDisk.project_sha256 -eq (Get-FileHash -LiteralPath $project.project_path -Algorithm SHA256).Hash) -and ($projectDisk.source_vis_unchanged -eq $true) -and ($projectDisk.generated_vis_executed -eq $false) -and ($diskMemberPaths.Count -eq 2) -and (@(Compare-Object -ReferenceObject $memberPaths -DifferenceObject $diskMemberPaths).Count -eq 0))
    $sourceProjectHash=(Get-FileHash -LiteralPath $project.project_path -Algorithm SHA256).Hash
    $sourceProjectReportHash=(Get-FileHash -LiteralPath $projectReportPath -Algorithm SHA256).Hash
    $editedMembers=@($recipeBuild.vi_path,$formulaBuild.vi_path)
    $editedProject=CallTool 'create_project' @{name=('McpEdited_' + $runToken);source_project=$project.project_path;vi_paths=$editedMembers}
    Check 'MCP edits project members in a new saved copy' ($editedProject.created -and ($editedProject.project_operation -eq 'edit_members_in_new_copy') -and ($editedProject.project_path -ne $project.project_path))
    $editedMemberPaths=@($editedProject.xml_member_references | ForEach-Object path)
    Check 'Edited project removes old member and adds Formula member' (($editedMemberPaths.Count -eq 2) -and (@(Compare-Object -ReferenceObject $editedMembers -DifferenceObject $editedMemberPaths).Count -eq 0))
    Check 'Source project and report remain unchanged and are never opened for NI editing' ($editedProject.source_project.unchanged -and (-not $editedProject.source_project.opened_in_labview) -and (-not $editedProject.source_project.saved_in_labview) -and ((Get-FileHash -LiteralPath $project.project_path -Algorithm SHA256).Hash -eq $sourceProjectHash) -and ((Get-FileHash -LiteralPath $projectReportPath -Algorithm SHA256).Hash -eq $sourceProjectReportHash))
    Check 'Project copy edit does not run or save source VIs' ($editedProject.source_vis_unchanged -and (-not $editedProject.generated_vis_executed) -and (-not $editedProject.source_vis_saved))
    $connection = CallTool 'labview_status' @{}
    Check 'Read-only attaches without startup or execution' ($connection.connected -and (-not $connection.started_labview) -and (-not $connection.ran_vi))
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(10000)) { throw 'MCP did not close on EOF' }
    Check 'Clean EOF shutdown' ($process.ExitCode -eq 0)
    $report = @{passed=$true;run_token=$runToken;tests=$script:checks;generated_vi=$build.vi_path;build_report=$buildReportPath;recipe_vi=$recipeBuild.vi_path;recipe_report=$recipeReportPath;generic_vi=$genericBuild.vi_path;copy_vi=$copyBuild.vi_path;formula_vi=$formulaBuild.vi_path;named_vi=$namedBuild.vi_path;values_vi=$valuesBuild.vi_path;array_vi=$arrayBuild.vi_path;control_artifact=$typeBuild.vi_path;global_artifact=$globalBuild.vi_path;imported_vi=$imported.vi_path;imported_edited_vi=$importEdited.vi_path;recipe_runtime_verified=$false;project_path=$project.project_path;project_report=$projectReportPath;edited_project_path=$editedProject.project_path;project_reopen_verified=$false;tool_names=$actualTools;connection=$connection;stderr=$stderrRead.Result;complete_replacement=$capabilities.complete_replacement}
    $json = $report | ConvertTo-Json -Depth 20
    if(-not $ReportPath){$ReportPath=Join-Path $PSScriptRoot 'mcp-acceptance.json'}
    [IO.File]::WriteAllText($ReportPath,$json,[Text.UTF8Encoding]::new($false))
    $json
} finally {
    # HasExited itself throws when Start failed; cleanup must preserve that original error.
    if ($processStarted) {
        try {
            if (-not $process.HasExited) {
                $process.Kill()
                [void]$process.WaitForExit(5000)
            }
        } catch { Write-Warning ("MCP process cleanup: " + $_.Exception.Message) }
    }
    try { $process.Dispose() } catch { Write-Warning ("MCP process disposal: " + $_.Exception.Message) }
}
