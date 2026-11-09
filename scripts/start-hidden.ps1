# Starts the API and the frontend as hidden processes (logs in the temp folder).
# Loads the user-level SQL login variables (Agent__ServerLogins__*) so the servers see them even when this
# shell was opened before they were saved.
$root = Split-Path -Parent $PSScriptRoot
$log = [IO.Path]::GetTempPath()

$reg = Get-ItemProperty 'HKCU:\Environment'
foreach ($prop in $reg.PSObject.Properties | Where-Object { $_.Name -like 'Agent__*' }) {
    [Environment]::SetEnvironmentVariable($prop.Name, [string]$prop.Value, 'Process')
}
[Environment]::SetEnvironmentVariable('Agent__WorkingDirectory', $root, 'Process')
$bash = "$([Environment]::GetFolderPath('ProgramFiles'))\Git\usr\bin\bash.exe"
if (Test-Path $bash) { [Environment]::SetEnvironmentVariable('Agent__GitBashPath', $bash, 'Process') }

$api = Join-Path $root 'src\Manti3.Backend\Manti3.Backend.Api'
$web = Join-Path $root 'src\Manti3.Frontend'
Start-Process dotnet -ArgumentList 'run', '--project', $api, '--launch-profile', 'https' -WindowStyle Hidden `
    -RedirectStandardOutput "$log\manti-api-ps.log" -RedirectStandardError "$log\manti-api-ps.err"
Start-Process dotnet -ArgumentList 'run', '--project', $web, '--launch-profile', 'https' -WindowStyle Hidden `
    -RedirectStandardOutput "$log\manti-fe-ps.log" -RedirectStandardError "$log\manti-fe-ps.err"
Write-Host 'Started hidden. API https://localhost:7127, frontend https://localhost:7296'
