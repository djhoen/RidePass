#requires -version 5.1
<#
.SYNOPSIS
    Generate SQL to add a RidePass super admin.

.DESCRIPTION
    A super admin is a row in `users` with role='super_admin', tenant_id NULL. The password hash must
    match ASP.NET Core Identity's PasswordHasher<User> (the app's verifier), so this produces an
    Identity v3 hash: PBKDF2-HMACSHA256, 100k iterations, 128-bit salt, 256-bit subkey, in the
    self-describing v3 binary format (the app reads the iteration count etc. back out of the hash).

    It prints an IDEMPOTENT INSERT (no-op if a tenant-less user with that email already exists, which
    is what the unique index idx_users_email_super_admin enforces). Run the SQL against the target DB
    yourself — e.g. stage: psql "<stage-conn-string>" -f the-output.sql

.EXAMPLE
    ./add-super-admin.ps1 -Email julien@ridepass.io -FirstName Julien -LastName Markewitz
    # prompts for a password (or generates one) and prints the INSERT to stdout.

.EXAMPLE
    ./add-super-admin.ps1 -Email julien@ridepass.io -FirstName Julien -LastName Markewitz -OutFile add-julien.sql
#>
param(
    [Parameter(Mandatory = $true)][string]$Email,
    [Parameter(Mandatory = $true)][string]$FirstName,
    [Parameter(Mandatory = $true)][string]$LastName,
    [string]$Password,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'

if (-not $Password) {
    # No password supplied: generate a strong one and show it (only chance to see it).
    $rb = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($rb)
    $Password = ([Convert]::ToBase64String($rb) -replace '[+/=]', '').Substring(0, 20)
    Write-Host "Generated password (save it now): $Password" -ForegroundColor Yellow
}

# --- ASP.NET Core Identity v3 password hash (matches PasswordHasher<User>) ---
$prf       = 1        # KeyDerivationPrf.HMACSHA256
$iter      = 100000
$saltSize  = 16       # 128 bits
$subkeyLen = 32       # 256 bits

$salt = New-Object byte[] $saltSize
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($salt)

$pbkdf2 = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
    $Password, $salt, $iter, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
try { $subkey = $pbkdf2.GetBytes($subkeyLen) } finally { $pbkdf2.Dispose() }

function Write-BE([byte[]]$b, [int]$o, [uint32]$v) {
    $b[$o]     = [byte](($v -shr 24) -band 0xFF)
    $b[$o + 1] = [byte](($v -shr 16) -band 0xFF)
    $b[$o + 2] = [byte](($v -shr 8)  -band 0xFF)
    $b[$o + 3] = [byte]( $v          -band 0xFF)
}

$out = New-Object byte[] (13 + $saltSize + $subkeyLen)
$out[0] = 1                                   # format marker 0x01 (v3)
Write-BE $out 1 ([uint32]$prf)                # PRF
Write-BE $out 5 ([uint32]$iter)               # iteration count
Write-BE $out 9 ([uint32]$saltSize)           # salt length
[Array]::Copy($salt,   0, $out, 13,             $saltSize)
[Array]::Copy($subkey, 0, $out, 13 + $saltSize, $subkeyLen)
$hash = [Convert]::ToBase64String($out)

function SqlLit([string]$s) { "'" + ($s -replace "'", "''") + "'" }

$sql = @"
-- RidePass super admin: $Email ($FirstName $LastName)
-- Idempotent: no-op if a tenant-less user with this email already exists
-- (unique index idx_users_email_super_admin covers all tenant_id-NULL rows, incl. global riders).
INSERT INTO users (tenant_id, email, password_hash, first_name, last_name, role, roles, status, email_verified)
SELECT NULL, $(SqlLit $Email), $(SqlLit $hash), $(SqlLit $FirstName), $(SqlLit $LastName),
       'super_admin', ARRAY['super_admin'], 'active', true
WHERE NOT EXISTS (
    SELECT 1 FROM users WHERE tenant_id IS NULL AND lower(email) = lower($(SqlLit $Email))
);
"@

if ($OutFile) {
    $sql | Out-File -FilePath $OutFile -Encoding utf8
    Write-Host "Wrote $OutFile" -ForegroundColor Green
} else {
    $sql
}
