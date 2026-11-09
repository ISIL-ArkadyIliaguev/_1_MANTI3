# PreToolUse guard for Bash / PowerShell tool calls.
# Reads the tool call JSON from stdin. Exit 0 = allow, exit 2 = block (message on stderr is
# returned to Claude). This is one protection layer; the real one is a read-only SQL account.

$raw = [Console]::In.ReadToEnd()
try { $call = $raw | ConvertFrom-Json } catch { exit 0 }

$cmd = [string]$call.tool_input.command
if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }

function Block([string]$reason) {
    [Console]::Error.WriteLine("Blocked: $reason")
    exit 2
}

# 1. Secrets typed into a command line.
if ($cmd -match '(?i)(password|passwd|pwd|token|api[_-]?key|secret)\s*[:=]\s*\S') {
    Block 'possible secret in command. Use user-secrets or environment variables.'
}
if ($cmd -match '(?i)BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY') {
    Block 'private key in command.'
}

# 1b. The SQL login password of a server is passed to the agent only as an environment variable
# (read by sqlcmd itself). The agent must not read or print it.
if ($cmd -match '(?i)SQLCMDPASSWORD|MANTI_SQL_PASSWORD|\bprintenv\b|\$env:|\bGet-ChildItem\s+env:') {
    Block 'do not read or print environment variables (they can hold database logins).'
}

# 2. SQL through sqlcmd / Invoke-Sqlcmd: SELECT only.
if ($cmd -match '(?i)\b(sqlcmd|Invoke-Sqlcmd)\b') {
    # Only the named servers (SELECT only, see db-readonly.md); the first is the default and the only one a script may run on.
    # Keep this list the same as Agent:Servers in appsettings.Development.json.
    # sqlcmd flags are case-sensitive (-s is the column separator), hence -cmatch for -S.
    $allowedServers = @('SQL25DEV', 'SQL2025CLN2.iscar.com')
    $allowedServer = $allowedServers[0]
    $server = $null
    # -ServerInstance first: it also starts with -S.
    if ($cmd -match '(?i)(^|\s)-ServerInstance\s+["'']?([^\s"'']+)') { $server = $Matches[2] }
    elseif ($cmd -cmatch '(^|\s)-S\s*["'']?([^\s"'']+)') { $server = $Matches[2] }
    if (-not $server) {
        Block "name the server explicitly with -S $allowedServer."
    }
    $serverName = (($server -replace '^(?i)(tcp|np|lpc):', '') -split '[,\\]')[0]
    if ($allowedServers -notcontains $serverName) {
        Block "only these servers are allowed: $($allowedServers -join ', ') (got '$server')."
    }
    if ($cmd -match '(?i)(^|\s)-P\s+\S') {
        Block 'do not pass a password with -P. Use Windows authentication (-E) or a secret from the environment.'
    }
    if ($cmd -match '(?i)(^|\s)-i\s|-InputFile') {
        Block 'sqlcmd with an input file is not allowed. Use -Q with a single SELECT.'
    }
    if ($cmd -notmatch '(?i)(^|\s)(-Q|-Query)\s+["'']?\s*(SELECT|WITH)\b') {
        Block 'sqlcmd must run a single query that starts with SELECT (-Q "SELECT ...").'
    }
    $writeWords = 'INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|TRUNCATE|CREATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|BACKUP|RESTORE|SHUTDOWN|INTO|xp_cmdshell|sp_executesql'
    if ($cmd -match "(?i)\b($writeWords)\b") {
        Block 'the database is read-only. Write SQL may only be saved as a script file for a human to run.'
    }
}

exit 0
