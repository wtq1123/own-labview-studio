param(
    [Parameter(Mandatory=$true)][string]$Python,
    [string]$ToolsRoot='D:\LabVIEWTools',
    [string]$IndexUrl='https://pypi.org/simple'
)
$ErrorActionPreference='Stop'
$ToolsRoot=[IO.Path]::GetFullPath($ToolsRoot)
if(-not (Test-Path -LiteralPath $Python -PathType Leaf)){throw 'Select an installed Python executable.'}
& $Python -c "import sys,struct; assert sys.version_info[:2]==(3,12) and struct.calcsize('P')==8, 'Python 3.12 x64 required for pinned environment'"
if($LASTEXITCODE -ne 0){throw 'Python version/architecture check failed'}
New-Item -ItemType Directory -Path $ToolsRoot -Force|Out-Null
function Checkout($name,$url,$commit) {
    $path=Join-Path $ToolsRoot $name
    if(-not(Test-Path -LiteralPath $path)){& git clone $url $path | Out-Host;if($LASTEXITCODE -ne 0){throw "Clone failed: $name"}}
    $remote=& git -C $path remote get-url origin
    if($LASTEXITCODE -ne 0 -or $remote -ne $url){throw "Unexpected repository at $path"}
    $dirty=& git -C $path status --porcelain
    if($LASTEXITCODE -ne 0 -or $dirty){throw "Preserve existing changes at $path before deployment"}
    & git -C $path fetch origin $commit | Out-Host
    if($LASTEXITCODE -ne 0){throw "Fetch failed: $name"}
    & git -C $path checkout --detach $commit | Out-Host
    if($LASTEXITCODE -ne 0){throw "Checkout failed: $name"}
    return $path
}
$lvkit=Checkout 'lvkit' 'https://github.com/pragmatest-dev/lvkit.git' '77f1a3d8e24fe92cc99fe920b98999fe5be9596b'
[void](Checkout 'lv-scripting' 'https://github.com/erdosmiller/lv-scripting.git' 'f7a14bd02f2c7c1fb062201657568a2ea6e0b7f6')
[void](Checkout 'labview-mcp-reference' 'https://github.com/Zuehlke/labview-mcp.git' '7702e6d42bcd1218ed4227657b3634547d82b636')
$venv=Join-Path $lvkit '.venv'
if(-not(Test-Path -LiteralPath $venv)){& $Python -m venv $venv;if($LASTEXITCODE -ne 0){throw 'Virtual environment creation failed'}}
$venvPython=Join-Path $venv 'Scripts\python.exe'
& $venvPython -m pip install --index-url $IndexUrl -c (Join-Path $PSScriptRoot 'deployment\lvkit-constraints.txt') $lvkit
if($LASTEXITCODE -ne 0){throw 'lvkit installation failed'}
& $venvPython -m pip check
if($LASTEXITCODE -ne 0){throw 'Dependency validation failed'}
& (Join-Path $venv 'Scripts\lvkit.exe') --version
if($LASTEXITCODE -ne 0){throw 'CLI check failed'}
Write-Output ('lvkit MCP: '+(Join-Path $venv 'Scripts\lvkit-mcp.exe'))
