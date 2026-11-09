# Stores the SQL login of SQL2025CLN2 (read-only use) WITHOUT putting it in any file of the repo, in a command line or in a chat.
# Run it yourself in PowerShell:  .\scripts\set-cln2-login.ps1
#   1. the website:  dotnet user-secrets of the API project (Agent:ServerLogins:SQL2025CLN2:User / :Password)
#   2. optional, for Claude Code's MCP server "manti-sql-cln2": your own user environment variables
#      MANTI_SQL_CLN2_USER / MANTI_SQL_CLN2_PASSWORD (restart Claude Code afterwards)
# Use a login that can only read (db_datareader), not a login that can write.
param([string]$Server = 'SQL2025CLN2')

$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root 'src\Manti3.Backend\Manti3.Backend.Api'

$user = Read-Host "SQL login name for $Server"
$secure = Read-Host 'Password (not shown)' -AsSecureString
$password = [System.Net.NetworkCredential]::new('', $secure).Password
if ([string]::IsNullOrWhiteSpace($user) -or [string]::IsNullOrEmpty($password)) { Write-Host 'Empty name or password: nothing saved.' -ForegroundColor Yellow; return }

# The values travel through standard input, never as command-line arguments.
@{ "Agent:ServerLogins:${Server}:User" = $user; "Agent:ServerLogins:${Server}:Password" = $password } |
    ConvertTo-Json -Compress | dotnet user-secrets set --project $api
Write-Host "Saved for the website (user-secrets). Restart the API: .\scripts\dev.ps1 restart" -ForegroundColor Green

if ((Read-Host 'Also save as your Windows user environment variables, for the MCP server? (y/N)') -match '^[yY]') {
    [Environment]::SetEnvironmentVariable('MANTI_SQL_CLN2_USER', $user, 'User')
    [Environment]::SetEnvironmentVariable('MANTI_SQL_CLN2_PASSWORD', $password, 'User')
    Write-Host 'Saved. Restart Claude Code / VS Code to pick them up.' -ForegroundColor Green
}

$password = $null
