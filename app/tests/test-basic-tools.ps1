$ErrorActionPreference='Stop'
$appRoot=Split-Path -Parent $PSScriptRoot
$runToken=[guid]::NewGuid().ToString('N').Substring(0,8)
$process=[Diagnostics.Process]::new()
$process.StartInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $appRoot 'bin\OwnLabViewMcp.exe'),'--mcp')
$process.StartInfo.UseShellExecute=$false
$process.StartInfo.CreateNoWindow=$true
$process.StartInfo.RedirectStandardInput=$true
$process.StartInfo.RedirectStandardOutput=$true
$process.StartInfo.RedirectStandardError=$true
$process.StartInfo.StandardOutputEncoding=[Text.UTF8Encoding]::new($false)
$process.StartInfo.StandardInputEncoding=[Text.UTF8Encoding]::new($false)
$script:requestId=0
$checks=@()
function Request($method,$parameters) {
    $script:requestId++
    $process.StandardInput.WriteLine((@{jsonrpc='2.0';id=$script:requestId;method=$method;params=$parameters}|ConvertTo-Json -Depth 30 -Compress))
    $process.StandardInput.Flush()
    $pending=$process.StandardOutput.ReadLineAsync()
    if(-not $pending.Wait(150000)){throw "Timeout: $method"}
    $response=$pending.Result|ConvertFrom-Json
    if($response.id -ne $script:requestId -or $response.error){throw 'Invalid MCP response'}
    return $response.result
}
function Call($name,$arguments) {
    $result=Request 'tools/call' @{name=$name;arguments=$arguments}
    if($result.isError){throw $result.content[0].text}
    return $result.content[0].text|ConvertFrom-Json
}
function Check($name,$value) { if(-not $value){throw "FAILED: $name"}; $script:checks+=@{name=$name;passed=$true} }
function Rejected($name,$arguments) { return (Request 'tools/call' @{name=$name;arguments=$arguments}).isError }
function Fixture($name,$duplicate) {
    $steps=@(@{id='input';op='control';args=@{style='Numeric Control';x=40;y=40;label='Input'}},@{id='output';op='control';args=@{style='Numeric Indicator';x=220;y=40;label='Output'}})
    if($duplicate){$steps+=@{id='other';op='control';args=@{style='Numeric Control';x=40;y=160;label='Input'}}}
    return Call 'build_recipe' @{recipe=@{schema_version=1;name=$name;steps=$steps}}
}
try {
    [void]$process.Start();$stderr=$process.StandardError.ReadToEndAsync()
    [void](Request 'initialize' @{protocolVersion='2025-06-18';capabilities=@{};clientInfo=@{name='Basic VI acceptance';version='1'}})
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $process.StandardInput.Flush()
    $fixture=Fixture ('BasicWireSource_'+$runToken) $false
    $before=(Get-FileHash -LiteralPath $fixture.vi_path).Hash
    $arguments=@{source_vi=$fixture.vi_path;name=('BasicWireCopy_'+$runToken);source_object='Input';source_terminal=0;sink_object='Output';sink_terminal=0}
    $copy=Call 'auto_wire_copy' $arguments
    Check 'Real wire verified and copied VI compiled idle' ($copy.wire_verified -and $copy.execution_state -eq 1)
    Check 'Only new disk copy changed' ($copy.vi_path -ne $fixture.vi_path -and (Get-FileHash -LiteralPath $fixture.vi_path).Hash -eq $before)
    Check 'Source and generated VI never executed by wiring' (-not $copy.source_executed -and -not $copy.generated_vi_executed -and -not $copy.source_saved)
    $exportDir=Join-Path (Split-Path -Parent $copy.vi_path) 'export'
    $export=Call 'export_vi_html' @{path=$copy.vi_path;output_html=(Join-Path $exportDir 'VI.html')}
    Check 'NI exports real HTML and diagram/front-panel images' ((Test-Path -LiteralPath $export.output_html) -and @($export.files|Where-Object {$_ -match '\.png$'}).Count -ge 2)
    Check 'HTML export preserves source bytes' $export.source_unchanged
    Check 'Existing export folder rejected' (Rejected 'export_vi_html' @{path=$copy.vi_path;output_html=(Join-Path $exportDir 'Another.html')})
    $arguments.name='BasicWireOccupied_'+$runToken;$arguments.source_vi=$copy.vi_path
    Check 'Occupied sink rejected' (Rejected 'auto_wire_copy' $arguments)
    $arguments.name='BasicWireBadIndex_'+$runToken;$arguments.source_vi=$fixture.vi_path;$arguments.sink_terminal=255
    Check 'Out-of-range terminal rejected' (Rejected 'auto_wire_copy' $arguments)
    $arguments.name='BasicWireWrongDirection_'+$runToken;$arguments.sink_terminal=0;$arguments.source_object='Output';$arguments.sink_object='Input'
    Check 'Wrong terminal direction rejected' (Rejected 'auto_wire_copy' $arguments)
    $duplicate=Fixture ('BasicWireDuplicate_'+$runToken) $true
    $arguments.name='BasicWireAmbiguous_'+$runToken;$arguments.source_vi=$duplicate.vi_path;$arguments.source_object='Input';$arguments.sink_object='Output'
    Check 'Ambiguous object label rejected' (Rejected 'auto_wire_copy' $arguments)
    $mismatch=Call 'build_recipe' @{recipe=@{schema_version=1;name=('BasicWireTypes_'+$runToken);steps=@(@{id='input';op='control';args=@{style='Numeric Control';x=40;y=40;label='Input'}},@{id='output';op='control';args=@{style='String Indicator';x=220;y=40;label='Output'}})}}
    $arguments.name='BasicWireTypeMismatch_'+$runToken;$arguments.source_vi=$mismatch.vi_path
    Check 'Incompatible native terminal types rejected' (Rejected 'auto_wire_copy' $arguments)
    $topology=Call 'build_recipe' @{recipe=@{schema_version=1;name=('BasicTopology_'+$runToken);steps=@(@{id='input';op='control';args=@{style='Numeric Control';x=40;y=40;label='Input'}},@{id='output';op='control';args=@{style='Numeric Indicator';x=220;y=40;label='Output'}},@{id='connect';op='wire';args=@{source_terminal='$input.terminal_id';sink_terminal='$output.terminal_id'}},@{id='lookup';op='terminal_wire';args=@{terminal_id='$output.terminal_id'}},@{id='endpoints';op='wire_terminals';args=@{wire_id='$lookup.wire_id'}},@{id='integrity';op='wire_broken';args=@{wire_id='$lookup.wire_id'}})}}
    Check 'Migrated topology operations compile with scoped symbolic references' ($topology.static_checks_passed -and $topology.execution_state -eq 1)
    $functionRecipe=Get-Content -LiteralPath (Join-Path $appRoot 'recipes\subvi-function.json') -Raw|ConvertFrom-Json
    $functionRecipe.name='BasicSubviSource_'+$runToken
    $function=Call 'build_recipe' @{recipe=$functionRecipe}
    $callTarget=Call 'build_recipe' @{recipe=@{schema_version=1;name=('BasicSubviPath_'+$runToken);steps=@(@{id='node';op='subvi';args=@{owner_diagram_id='$root.diagram_id';subvi_path=$function.vi_path;x=100;y=100}},@{id='target';op='subvi_path';args=@{subvi_id='$node.object_id'}})}}
    Check 'Migrated SubVI target lookup accepts actual SubVI reference' $callTarget.created
    Check 'Negative tests preserve original source' ((Get-FileHash -LiteralPath $fixture.vi_path).Hash -eq $before)
    $process.StandardInput.Close()
    if(-not $process.WaitForExit(10000)){throw 'MCP did not stop after EOF'}
    Check 'Clean protocol shutdown' ($process.ExitCode -eq 0)
    $report=@{passed=$true;tests=$checks;fixture=$fixture.vi_path;copy=$copy;export=$export;hardware_executed=$false;generated_vi_executed=$false;stderr=$stderr.Result}
    $report|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'basic-tools-acceptance.json') -Encoding utf8
    $report|ConvertTo-Json -Depth 20
} finally {
    if(-not $process.HasExited){$process.Kill();[void]$process.WaitForExit(5000)}
    $process.Dispose()
}
