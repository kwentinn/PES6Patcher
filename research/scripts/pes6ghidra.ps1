# A persistent Ghidra project of the reference exe (imported once, without analysis): the functions created and named
# on the way are saved in it, so each question starts from what the previous ones left.
# Usage: pes6ghidra.ps1 -Import -Exe "C:\...\PES6.exe.orig"      (once: copies the exe, imports it, applies the labels)
#        pes6ghidra.ps1 -Labels                                  (after editing research/ghidra-labels.csv)
#        pes6ghidra.ps1 -Decompile 5E1BD0,642A90 -Out out.txt    (the function containing each address: C, then assembly)
# The project folder must have a short path (Ghidra's decompiler fails above 260 characters): default C:\Users\<you>\ghidra-pes6.
param([switch]$Import, [switch]$Labels, [string[]]$Decompile = @(), [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) 'pes6-decompiled.txt'),
      [string]$Exe, [string]$Project = (Join-Path $env:USERPROFILE 'ghidra-pes6'), [string]$Ghidra = (Join-Path $env:USERPROFILE 'ghidra12'))

$ErrorActionPreference = 'Stop'
$headless = Join-Path $Ghidra 'support\analyzeHeadless.bat'
if (-not (Test-Path $headless)) { throw "Ghidra not found in $Ghidra (use -Ghidra)" }
$labelsFile = Resolve-Path (Join-Path $PSScriptRoot '..\ghidra-labels.csv')
New-Item -ItemType Directory -Force $Project | Out-Null

function Invoke-Ghidra([string[]]$arguments) {
    $log = & $headless @arguments 2>&1
    $log | Where-Object { $_ -match 'ERROR|FAILED|labels applied|Import succeeded|Save succeeded|Exception' } | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "analyzeHeadless failed (exit code $LASTEXITCODE)" }
}

if ($Import) {
    if (-not $Exe -or -not (Test-Path $Exe)) { throw '-Import needs -Exe: the unpatched exe (PES6.exe.orig)' }
    $stage = Join-Path ([IO.Path]::GetTempPath()) 'pes6-ghidra-import'
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item $Exe (Join-Path $stage 'PES6.exe') -Force
    Invoke-Ghidra @($Project, 'pes6', '-import', (Join-Path $stage 'PES6.exe'), '-noanalysis', '-overwrite')
    $Labels = $true
}
if ($Labels) {
    Invoke-Ghidra @($Project, 'pes6', '-process', 'PES6.exe', '-noanalysis', '-scriptPath', $PSScriptRoot, '-postScript', 'ApplyLabels.java', "$labelsFile")
}
if ($Decompile.Count) {
    $addresses = $Decompile | ForEach-Object { $_ -replace '^0x', '' }
    Invoke-Ghidra (@($Project, 'pes6', '-process', 'PES6.exe', '-noanalysis', '-scriptPath', $PSScriptRoot, '-postScript', 'DecompIn.java', $Out) + $addresses)
    "decompiled -> $Out"
}
