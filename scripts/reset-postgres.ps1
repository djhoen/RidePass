# One-shot local dev: resets postgres superuser password, creates ridepass user/db.
# Run in an ELEVATED PowerShell (Run as Administrator).

$ErrorActionPreference = 'Stop'

$PgBin    = 'C:\Program Files\PostgreSQL\16\bin'
$PgData   = 'C:\Program Files\PostgreSQL\16\data'
$HbaPath  = Join-Path $PgData 'pg_hba.conf'
$Backup   = Join-Path $PgData 'pg_hba.conf.bak'
$Service  = 'postgresql-x64-16'

# New credentials
$PostgresPw = 'GFIy06h4gE3jVL3LbzshHK7t'
$RidepassPw = 'ZeDRZzteNApEI50imT15FuC9'

function Invoke-Psql([string]$sql) {
    $tmp = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $tmp -Value $sql -Encoding ASCII
    try {
        & "$PgBin\psql.exe" -U postgres -h 127.0.0.1 -v ON_ERROR_STOP=1 -f $tmp
        if ($LASTEXITCODE -ne 0) { throw "psql exited with code $LASTEXITCODE" }
    } finally {
        Remove-Item $tmp -ErrorAction SilentlyContinue
    }
}

Write-Host "==> Backing up pg_hba.conf to $Backup"
Copy-Item -Path $HbaPath -Destination $Backup -Force

try {
    Write-Host "==> Patching pg_hba.conf: scram-sha-256 to trust on localhost"
    $content = Get-Content $HbaPath -Raw
    $rx1 = [regex]::new('^(host\s+all\s+all\s+127\.0\.0\.1/32\s+)scram-sha-256', 'Multiline')
    $rx2 = [regex]::new('^(host\s+all\s+all\s+::1/128\s+)scram-sha-256',         'Multiline')
    $patched = $rx1.Replace($content, '$1trust')
    $patched = $rx2.Replace($patched, '$1trust')
    if ($patched -eq $content) { throw "pg_hba.conf had no matching scram-sha-256 lines. File layout unexpected." }
    Set-Content -Path $HbaPath -Value $patched -NoNewline -Encoding ASCII

    Write-Host "==> Restarting $Service"
    Restart-Service $Service
    Start-Sleep -Seconds 2

    Write-Host "==> Resetting postgres password and creating ridepass user/db"
    $sqlTemplate = @'
ALTER USER postgres WITH PASSWORD '__POSTGRES_PW__';
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ridepass') THEN
        CREATE ROLE ridepass WITH LOGIN PASSWORD '__RIDEPASS_PW__';
    ELSE
        ALTER ROLE ridepass WITH PASSWORD '__RIDEPASS_PW__';
    END IF;
END
$$;
SELECT 'CREATE DATABASE ridepass_dev OWNER ridepass'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'ridepass_dev')
\gexec
'@
    $sql = $sqlTemplate.Replace('__POSTGRES_PW__', $PostgresPw).Replace('__RIDEPASS_PW__', $RidepassPw)
    Invoke-Psql $sql
}
finally {
    Write-Host "==> Restoring original pg_hba.conf"
    Move-Item -Path $Backup -Destination $HbaPath -Force
    Write-Host "==> Restarting $Service (re-enabling scram-sha-256)"
    Restart-Service $Service
}

Write-Host ""
Write-Host "=============================================================="
Write-Host "DONE."
Write-Host "postgres superuser password: $PostgresPw"
Write-Host "ridepass app user password : $RidepassPw"
Write-Host "Database created           : ridepass_dev (owner: ridepass)"
Write-Host "=============================================================="
