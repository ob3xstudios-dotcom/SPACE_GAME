param(
    [string]$Command
)

$ErrorActionPreference = 'Stop'
$script:ExitCode = 0

function Show-Help {
    Write-Output 'Usage: .\Tools\SpaceWorld.ps1 <status|verify|project-files|help>'
    Write-Output 'status        Show Git paths and counts without changing the repository.'
    Write-Output 'verify        Check the clean baseline and protected files.'
    Write-Output 'project-files Check generated project-file tracking and ignore rules.'
}

function Invoke-GitRead {
    param(
        [string]$WorkingDirectory,
        [string[]]$GitArguments
    )

    # All arguments passed here are fixed switches, never file names.
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = 'git'
    $start.WorkingDirectory = $WorkingDirectory
    $start.Arguments = ($GitArguments -join ' ')
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $start.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $output = $process.StandardOutput.ReadToEnd()
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Git failed ($($process.ExitCode)): $($errorText.Trim())"
        }
        return $output
    }
    finally {
        $process.Dispose()
    }
}

function Get-RepoRoot {
    $root = (Invoke-GitRead -WorkingDirectory $PSScriptRoot -GitArguments @(
        '--no-optional-locks', 'rev-parse', '--show-toplevel'
    )).Trim()
    if (-not $root -or -not (Test-Path -LiteralPath $root -PathType Container)) {
        throw 'Cannot locate the Git repository root.'
    }
    return $root
}

function Split-NulPaths {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return }
    foreach ($path in $Text.Split([char]0)) {
        if ($path.Length -gt 0) { Write-Output $path }
    }
}

function Get-Paths {
    param([string]$Root, [string[]]$GitArguments)
    $output = Invoke-GitRead -WorkingDirectory $Root -GitArguments $GitArguments
    return @(Split-NulPaths -Text $output)
}

function Get-Snapshot {
    param([string]$Root)
    $branch = (Invoke-GitRead -WorkingDirectory $Root -GitArguments @(
        '--no-optional-locks', 'branch', '--show-current'
    )).Trim()
    $head = (Invoke-GitRead -WorkingDirectory $Root -GitArguments @(
        '--no-optional-locks', 'rev-parse', 'HEAD'
    )).Trim()
    $staged = @(Get-Paths -Root $Root -GitArguments @(
        '--no-optional-locks', 'diff', '--cached', '--no-ext-diff', '--no-textconv', '--name-only', '-z'
    ))
    $modified = @(Get-Paths -Root $Root -GitArguments @(
        '--no-optional-locks', 'diff', '--no-ext-diff', '--no-textconv', '--name-only', '-z'
    ))
    $untracked = @(Get-Paths -Root $Root -GitArguments @(
        '--no-optional-locks', 'ls-files', '--others', '--exclude-standard', '-z'
    ))
    return [pscustomobject]@{
        Branch = $branch
        Head = $head
        Staged = $staged
        Modified = $modified
        Untracked = $untracked
    }
}

function Write-PathBlock {
    param([string]$Name, [string[]]$Paths)
    Write-Output "${Name}_BEGIN"
    foreach ($path in $Paths) { Write-Output $path }
    Write-Output "${Name}_END"
}

function Show-Status {
    param([string]$Root)
    $snapshot = Get-Snapshot -Root $Root
    Write-Output 'SPACEWORLD STATUS'
    Write-Output "BRANCH=$($snapshot.Branch)"
    Write-Output "HEAD=$($snapshot.Head)"
    Write-Output "STAGED_COUNT=$($snapshot.Staged.Count)"
    Write-Output "TRACKED_MODIFIED_COUNT=$($snapshot.Modified.Count)"
    Write-Output "UNTRACKED_COUNT=$($snapshot.Untracked.Count)"
    Write-PathBlock -Name 'STAGED' -Paths $snapshot.Staged
    Write-PathBlock -Name 'TRACKED_MODIFIED' -Paths $snapshot.Modified
    Write-PathBlock -Name 'UNTRACKED' -Paths $snapshot.Untracked
}

function Get-TrackedFiles {
    param([string]$Root)
    return @(Get-Paths -Root $Root -GitArguments @(
        '--no-optional-locks', 'ls-files', '-z'
    ))
}

function Get-ProjectFiles {
    param([string[]]$Paths)
    return [pscustomobject]@{
        Csproj = @($Paths | Where-Object { $_.EndsWith('.csproj', [System.StringComparison]::OrdinalIgnoreCase) })
        Sln = @($Paths | Where-Object { $_.EndsWith('.sln', [System.StringComparison]::OrdinalIgnoreCase) })
        Slnx = @($Paths | Where-Object { $_.EndsWith('.slnx', [System.StringComparison]::OrdinalIgnoreCase) })
    }
}

function Show-Verify {
    param([string]$Root)
    $snapshot = Get-Snapshot -Root $Root
    $tracked = @(Get-TrackedFiles -Root $Root)
    $projects = Get-ProjectFiles -Paths $tracked
    $failures = New-Object System.Collections.Generic.List[string]

    Write-Output 'SPACEWORLD VERIFY'
    Write-Output "BRANCH=$($snapshot.Branch)"
    Write-Output "HEAD=$($snapshot.Head)"
    Write-Output "STAGED_COUNT=$($snapshot.Staged.Count)"
    Write-Output "TRACKED_MODIFIED_COUNT=$($snapshot.Modified.Count)"
    Write-Output "UNTRACKED_COUNT=$($snapshot.Untracked.Count)"
    Write-Output "TRACKED_CSPROJ_COUNT=$($projects.Csproj.Count)"
    Write-Output "TRACKED_SLN_COUNT=$($projects.Sln.Count)"
    Write-Output "TRACKED_SLNX_COUNT=$($projects.Slnx.Count)"

    if (-not $snapshot.Branch) { $failures.Add('DETACHED_HEAD') }
    if ($snapshot.Staged.Count -ne 0) { $failures.Add('STAGED_NOT_EMPTY') }
    if ($snapshot.Modified.Count -ne 0) { $failures.Add('TRACKED_MODIFIED_NOT_EMPTY') }
    if ($projects.Csproj.Count + $projects.Sln.Count + $projects.Slnx.Count -ne 0) {
        $failures.Add('GENERATED_PROJECT_FILE_TRACKED')
    }

    $protected = [ordered]@{
        MUELLE = 'Assets/Scenes/Sandbox/MUELLE.unity'
        ADUANAS = 'Assets/Scenes/Sandbox/ADUANAS.unity'
        SCENEFALLRESPAWN = 'Assets/Scripts/Systems/SceneFallRespawn.cs'
        PLAYER_PREFAB = 'Assets/Prefabs/Player/Player.prefab'
        PLAYERCONTROLLER = 'Assets/Scripts/Player/PlayerController.cs'
    }
    foreach ($name in $protected.Keys) {
        $path = $protected[$name]
        $exists = Test-Path -LiteralPath (Join-Path $Root $path) -PathType Leaf
        $isTracked = $tracked -contains $path
        $isClean = ($snapshot.Staged -notcontains $path) -and ($snapshot.Modified -notcontains $path)
        $result = if ($exists -and $isTracked -and $isClean) { 'PASS' } else { 'FAIL' }
        Write-Output "PROTECTED_${name}=$result"
        if ($result -eq 'FAIL') { $failures.Add("PROTECTED_${name}_MISSING_OR_DIRTY") }
    }

    foreach ($failure in $failures) { Write-Output "FAIL_REASON=$failure" }
    if ($failures.Count -eq 0) {
        Write-Output 'VERIFY_RESULT=PASS'
    }
    else {
        Write-Output 'VERIFY_RESULT=FAIL'
        $script:ExitCode = 1
    }
}

function Test-IgnoredLocalFile {
    param([string]$Root, [string]$FileName)
    & git -C $Root --no-optional-locks check-ignore --no-index -q -- $FileName 2>$null | Out-Null
    $code = $LASTEXITCODE
    if ($code -ne 0 -and $code -ne 1) {
        throw "Git check-ignore failed ($code) for $FileName."
    }
    return ($code -eq 0)
}

function Show-ProjectFiles {
    param([string]$Root)
    $tracked = Get-ProjectFiles -Paths @(Get-TrackedFiles -Root $Root)
    $localNames = @(Get-ChildItem -LiteralPath $Root -File | ForEach-Object { $_.Name })
    $local = Get-ProjectFiles -Paths $localNames
    $ignoredCsproj = @($local.Csproj | Where-Object { Test-IgnoredLocalFile -Root $Root -FileName $_ })
    $ignoredSln = @($local.Sln | Where-Object { Test-IgnoredLocalFile -Root $Root -FileName $_ })
    $ignoredSlnx = @($local.Slnx | Where-Object { Test-IgnoredLocalFile -Root $Root -FileName $_ })

    Write-Output 'SPACEWORLD PROJECT FILES'
    Write-Output "TRACKED_CSPROJ_COUNT=$($tracked.Csproj.Count)"
    Write-Output "TRACKED_SLN_COUNT=$($tracked.Sln.Count)"
    Write-Output "TRACKED_SLNX_COUNT=$($tracked.Slnx.Count)"
    Write-Output "LOCAL_CSPROJ_COUNT=$($local.Csproj.Count)"
    Write-Output "LOCAL_SLN_COUNT=$($local.Sln.Count)"
    Write-Output "LOCAL_SLNX_COUNT=$($local.Slnx.Count)"
    Write-Output "IGNORED_LOCAL_CSPROJ_COUNT=$($ignoredCsproj.Count)"
    Write-Output "IGNORED_LOCAL_SLN_COUNT=$($ignoredSln.Count)"
    Write-Output "IGNORED_LOCAL_SLNX_COUNT=$($ignoredSlnx.Count)"

    foreach ($path in $local.Slnx) {
        if ($ignoredSlnx -notcontains $path) { Write-Output "WARNING=LOCAL_SLNX_NOT_IGNORED:$path" }
    }
    $trackedCount = $tracked.Csproj.Count + $tracked.Sln.Count + $tracked.Slnx.Count
    if ($trackedCount -eq 0 -and
        $ignoredCsproj.Count -eq $local.Csproj.Count -and
        $ignoredSln.Count -eq $local.Sln.Count) {
        Write-Output 'PROJECT_FILES_RESULT=PASS'
    }
    else {
        Write-Output 'PROJECT_FILES_RESULT=FAIL'
        $script:ExitCode = 1
    }
}

if (-not $Command -or $Command -eq 'help') {
    Show-Help
    exit 2
}
if ($Command -notin @('status', 'verify', 'project-files')) {
    Write-Output "ERROR=Unknown command: $Command"
    Show-Help
    exit 2
}

try {
    $root = Get-RepoRoot
    switch ($Command) {
        'status' { Show-Status -Root $root }
        'verify' { Show-Verify -Root $root }
        'project-files' { Show-ProjectFiles -Root $root }
    }
}
catch {
    Write-Output "ERROR=$($_.Exception.Message)"
    if ($Command -eq 'verify') { Write-Output 'VERIFY_RESULT=FAIL' }
    if ($Command -eq 'project-files') { Write-Output 'PROJECT_FILES_RESULT=FAIL' }
    $script:ExitCode = 2
}
exit $script:ExitCode
