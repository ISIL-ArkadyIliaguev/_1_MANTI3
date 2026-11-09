# One-time setup of a new PC for MANTI3 (run from the repo root):  .\scripts\setup-new-pc.ps1
# It checks the prerequisites, trusts the dev certificate and builds the SQL helper. It changes nothing in the database.
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ok = $true

function Check($name, $found, $hint) {
    if ($found) { Write-Host "[OK]      $name" -ForegroundColor Green }
    else { Write-Host "[MISSING] $name  ->  $hint" -ForegroundColor Red; $script:ok = $false }
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ((& dotnet --list-sdks) -match '^10\.')
Check '.NET 10 SDK' $dotnet 'install the .NET 10 SDK'
Check 'Claude Code (claude.cmd)' ([bool](Get-Command claude.cmd -ErrorAction SilentlyContinue)) 'install Claude Code, then run "claude" once and log in'
Check 'sqlcmd' ([bool](Get-Command sqlcmd -ErrorAction SilentlyContinue)) 'install sqlcmd (SQL Server command line tools)'
$bash = @("$env:ProgramFiles\Git\usr\bin\bash.exe", "$env:LOCALAPPDATA\Programs\Git\usr\bin\bash.exe") +
    @(Get-ChildItem "$env:USERPROFILE\scoop\apps\git\*\usr\bin\bash.exe" -ErrorAction SilentlyContinue | ForEach-Object FullName)
Check 'Git for Windows (bash, pdftotext)' ([bool]($bash | Where-Object { Test-Path $_ })) 'install Git for Windows'

# Windows access to the test server (read-only check)
if (Get-Command sqlcmd -ErrorAction SilentlyContinue) {
    $out = & sqlcmd -S SQL25DEV -d Manti_IscarData -E -C -Q 'SELECT 1' -h -1 2>&1 | Out-String
    Check 'Windows login to SQL25DEV' ($out -match '^\s*1\s*$' -or $out -match '\b1\b' -and $out -notmatch 'Login failed|Error') 'ask the DBA for access to SQL25DEV (Manti_IscarData) for your Windows user'
}

if ($dotnet) {
    Write-Host 'Trusting the HTTPS dev certificate (a Windows prompt may appear)...'
    & dotnet dev-certs https --trust
    Write-Host 'Building the SQL helper for the agent...'
    & dotnet publish tools/sql-mcp/SqlMcp.cs -p:PublishAot=false -p:NuGetAudit=false -o tools/sql-mcp/bin | Out-Null
    Check 'SQL helper built' (Test-Path tools/sql-mcp/bin/SqlMcp.dll) 'see the error above'
}

if ($ok) {
    Write-Host "`nReady. Next: .\scripts\dev.ps1 restart, then add the Outlook add-in (docs\how-to\outlook-addin.md)." -ForegroundColor Green
} else {
    Write-Host "`nFix the [MISSING] items and run this script again." -ForegroundColor Yellow
}
