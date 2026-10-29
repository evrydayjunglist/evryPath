# A second stack beside a running one (see Agents/GettingStarted.md): the same steps as live.ps1 on the alternative ports
# below, from this checkout's Client/ and data/, so another checkout's Auth/Patch/Gateway on the default ports keep
# running. It stops only its own servers (the ones on these ports) and never the default-port ones.
#   -NoClient   keep a running client of this checkout
#   -NoBuild    skip the builds
#   -Stop       stop this stack's servers and client, nothing else
param([switch]$NoClient, [switch]$NoBuild, [switch]$Stop)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$out = Join-Path $repo 'logs'
New-Item -ItemType Directory -Force $out | Out-Null

# The alternative ports (the defaults are 20493 / 20495 / 6112 / 12995 / 8080).
$env:Game_LOGIN_PORT = '20593'
$env:Game_AUTH_CONTROL_PORT = '20595'
$env:Game_GATEWAY_PORT = '6212'
$env:Game_PATCH_TCP_PORT = '13995'
$env:Game_PATCH_URL = 'http://127.0.0.1:8180/'
if (-not $env:Game_GATEWAY_ENTER) { $env:Game_GATEWAY_ENTER = '1' }
$ports = @(20593, 20595, 6212, 13995, 8180)
$client = Join-Path $repo 'Client'

function Need($ok, $message) { if (-not $ok) { throw "setup incomplete: $message (see Agents/GettingStarted.md)" } }

function Stop-Own {
    # This checkout's client, then the dotnet servers on this stack's ports.
    Get-Process -Name 'Game.patched' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq (Join-Path $client 'Game.patched.exe') } | Stop-Process -Force -ErrorAction SilentlyContinue
    foreach ($port in $ports) {
        foreach ($c in Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
            $proc = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue
            if ($proc -and $proc.ProcessName -eq 'dotnet') { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
            elseif ($proc) { throw "port $port is used by $($proc.ProcessName) (pid $($proc.Id))" }
        }
    }
}

if ($Stop) { Stop-Own; Write-Output 'parallel stack stopped'; return }

Need (Get-Command dotnet -ErrorAction SilentlyContinue) '.NET SDK not found (dotnet)'
Need (Test-Path (Join-Path $repo 'data\login-keys.json')) 'data\login-keys.json missing'
Need (Test-Path (Join-Path $client 'Game.patched.exe')) 'Client\Game.patched.exe missing'
Need (Test-Path (Join-Path $client 'Content.ggpk')) 'Client\Content.ggpk missing'
Need (Test-Path (Join-Path $repo 'data\tables')) 'data\tables missing: run scripts\dev\extract-data.ps1'

if (-not $NoClient) {
    Get-Process -Name 'Game.patched' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq (Join-Path $client 'Game.patched.exe') } | Stop-Process -Force -ErrorAction SilentlyContinue
}
Stop-Own
Start-Sleep -Milliseconds 500
foreach ($s in 'auth', 'patch', 'gateway') {
    Remove-Item (Join-Path $out "live-$s.log"), (Join-Path $out "live-$s.err") -ErrorAction SilentlyContinue
}

if (-not $NoBuild) {
    foreach ($s in 'Auth', 'Patch', 'Gateway') {
        dotnet build (Join-Path $repo "src\GameServer.$s") -v q -nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "build failed: GameServer.$s" }
    }
}

function Start-Detached([string]$exe, [string[]]$arguments, [string]$log, [string]$err) {
    $line = (@($exe) + $arguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
    Start-Process cmd.exe -ArgumentList "/c $line 1>`"$log`" 2>`"$err`"" -WorkingDirectory $repo -WindowStyle Hidden
}

$bin = 'bin\Debug\net10.0'
# The client's gateway list in this checkout's GGPK: this stack's login server (the client must be closed).
if (-not $NoClient) {
    & dotnet (Join-Path $repo "src\GameServer.Patch\$bin\GameServer.Patch.dll") gateway (Join-Path $client 'Content.ggpk') --login "127.0.0.1:$($env:Game_LOGIN_PORT)" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'could not select the login server in Client\Content.ggpk; close this checkout''s client and retry' }
}

$serverArgs = @{ Auth = @('serve'); Patch = @(); Gateway = @() }
foreach ($s in 'Patch', 'Gateway', 'Auth') {
    Start-Detached 'dotnet' (@("src\GameServer.$s\$bin\GameServer.$s.dll") + $serverArgs[$s]) `
        (Join-Path $out "live-$($s.ToLower()).log") (Join-Path $out "live-$($s.ToLower()).err")
}

$deadline = (Get-Date).AddSeconds(30)
$patchLog = Join-Path $out 'live-patch.log'
while (-not ((Test-Path $patchLog) -and (Select-String -Path $patchLog -Pattern 'patching protocol on' -Quiet))) {
    if ((Get-Date) -gt $deadline) {
        $err = Get-Content (Join-Path $out 'live-patch.err') -Raw -ErrorAction SilentlyContinue
        throw "patch server did not start (logs\live-patch.err):`n$err"
    }
    Start-Sleep -Milliseconds 300
}
Start-Sleep -Seconds 2
foreach ($name in 'auth', 'gateway') {
    $err = Get-Content (Join-Path $out "live-$name.err") -Raw -ErrorAction SilentlyContinue
    if ($err) { throw "$name failed to start (logs\live-$name.err):`n$err" }
}
Write-Output "servers up (login $($env:Game_LOGIN_PORT), gateway $($env:Game_GATEWAY_PORT), patch $($env:Game_PATCH_TCP_PORT))"

if (-not $NoClient) {
    $p = Start-Process (Join-Path $client 'Game.patched.exe') -ArgumentList '--patching-server', "127.0.0.1:$($env:Game_PATCH_TCP_PORT)" -WorkingDirectory $client -PassThru
    Write-Output "client pid $($p.Id)"
}
