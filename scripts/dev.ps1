# Stop / start the MANTI3 dev servers (API on https 7127 / http 5126, frontend on https 7296 / http 5203).
#   .\scripts\dev.ps1 stop       stop whatever listens on those ports
#   .\scripts\dev.ps1 restart    stop, then start both in new windows (default)
#   .\scripts\dev.ps1 start      start both in new windows
param([ValidateSet('stop', 'start', 'restart')][string]$Action = 'restart')

$root = Split-Path -Parent $PSScriptRoot
$ports = 5126, 5203, 7127, 7296

function Stop-Dev {
    foreach ($port in $ports) {
        $owners = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique
        foreach ($id in $owners) {
            $p = Get-Process -Id $id -ErrorAction SilentlyContinue
            if ($p) {
                Write-Host "Stopping $($p.ProcessName) (pid $id) on port $port"
                Stop-Process -Id $id -Force
            }
        }
    }
}

function Find-GitBash {
    $candidates = @(
        "$env:ProgramFiles\Git\usr\bin\bash.exe",
        "${env:ProgramFiles(x86)}\Git\usr\bin\bash.exe",
        "$env:LOCALAPPDATA\Programs\Git\usr\bin\bash.exe"
    ) + @(Get-ChildItem "$env:USERPROFILE\scoop\apps\git\*\usr\bin\bash.exe" -ErrorAction SilentlyContinue | ForEach-Object FullName)
    $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

function Start-Dev {
    # Machine-specific settings come from environment variables, so appsettings.Development.json needs no editing
    # on another PC (the new windows inherit these).
    $env:Agent__WorkingDirectory = $root
    $bash = Find-GitBash
    if ($bash) { $env:Agent__GitBashPath = $bash } else { Write-Warning 'Git bash.exe not found: install Git for Windows.' }
    $api =Join-Path $root 'src\Manti3.Backend\Manti3.Backend.Api'
    $web = Join-Path $root 'src\Manti3.Frontend'
    Start-Process powershell -WorkingDirectory $root -ArgumentList '-NoExit', '-Command', "dotnet run --project '$api' --launch-profile https"
    Start-Process powershell -WorkingDirectory $root -ArgumentList '-NoExit', '-Command', "dotnet run --project '$web' --launch-profile https"
    Write-Host 'Started. Open https://localhost:7296/access-requests (the Outlook add-in needs the https address)'
}

if ($Action -in 'stop', 'restart') { Stop-Dev; Start-Sleep -Seconds 2 }
if ($Action -in 'start', 'restart') { Start-Dev }
