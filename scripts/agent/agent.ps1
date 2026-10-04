#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Isolated worktree + local stack per agent. See docs/parallel-agents.md.

.DESCRIPTION
    agent.ps1 new <name> [-Base main] [-Slot n] [-Up] [-NoHotReload]
    agent.ps1 up     [<name>] [-NoHotReload]   start the stack (hot reload by default) and run tofu apply
    agent.ps1 down   [<name>] [-Volumes]       stop the stack; -Volumes wipes its data and tofu state
    agent.ps1 db     [<name>]                  re-publish the database schema (dacpac) into the running stack
    agent.ps1 list                             every agent: slot, branch, URL, running?
    agent.ps1 status [<name>]                  the compose services of one agent
    agent.ps1 env    [<name>]                  print how to load this agent's test environment
    agent.ps1 refresh [<name>]                 regenerate .env, tfvars, .agent/ and the dev container config (after the scripts change)
    agent.ps1 remove <name> [-Purge]           stop, wipe, delete the worktree and release the slot

    <name> may be omitted inside a worktree. The main checkout is slot 0 (default ports).
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory)]
    [ValidateSet('new', 'up', 'down', 'db', 'list', 'status', 'env', 'refresh', 'remove', 'ports')]
    [string]$Command,

    [Parameter(Position = 1)]
    [string]$Name,

    [string]$Base = 'main',
    [int]$Slot = 0,
    [switch]$Up,
    [switch]$NoHotReload,
    [switch]$Volumes,
    [switch]$Purge
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Agent.Lib.ps1')

function Invoke-Git {
    $output = & git @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed: $output" }
    $output
}

function Get-RepoContext {
    $common = (Invoke-Git rev-parse --path-format=absolute --git-common-dir | Select-Object -First 1).Trim()
    $mainRoot = (Split-Path $common -Parent)
    $top = (Invoke-Git rev-parse --show-toplevel | Select-Object -First 1).Trim()
    [pscustomobject]@{
        CommonDir     = $common
        MainRoot      = $mainRoot
        Toplevel      = $top
        RegistryPath  = Join-Path $common 'agent-slots.json'
        WorktreesRoot = Join-Path (Split-Path $mainRoot -Parent) ((Split-Path $mainRoot -Leaf) + '.worktrees')
    }
}

function Resolve-Agent {
    param($Context, [string]$AgentName)

    $registry = Read-SlotRegistry -Path $Context.RegistryPath

    if ([string]::IsNullOrEmpty($AgentName)) {
        $here = (Resolve-Path $Context.Toplevel).Path
        if ($here -eq (Resolve-Path $Context.MainRoot).Path) {
            return [pscustomobject]@{ Name = 'main'; Slot = 0; Path = $Context.MainRoot }
        }
        foreach ($key in $registry.Keys) {
            if ((Test-Path $registry[$key].path) -and (Resolve-Path $registry[$key].path).Path -eq $here) {
                $AgentName = $key
            }
        }
        if ([string]::IsNullOrEmpty($AgentName)) { throw "This directory is not a registered agent worktree. Pass a <name> (see: agent.ps1 list)." }
    }

    if ($AgentName -eq 'main') {
        return [pscustomobject]@{ Name = 'main'; Slot = 0; Path = $Context.MainRoot }
    }
    if (-not $registry.ContainsKey($AgentName)) { throw "No agent named '$AgentName'. See: agent.ps1 list" }
    [pscustomobject]@{ Name = $AgentName; Slot = [int]$registry[$AgentName].slot; Path = $registry[$AgentName].path }
}

function Invoke-Compose {
    param($Agent, [string[]]$ComposeArgs, [bool]$HotReload = $true)

    $dir = Join-Path $Agent.Path 'iac/local'
    $files = @('-f', 'docker-compose.yml', '-f', 'docker-compose.dev.yml')
    if (-not $HotReload) { $files = @('-f', 'docker-compose.yml') }

    Push-Location $dir
    try {
        & docker compose @files @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose $($ComposeArgs -join ' ') failed (exit $LASTEXITCODE)" }
    } finally { Pop-Location }
}

function Invoke-Tofu {
    param($Agent)

    Push-Location (Join-Path $Agent.Path 'iac/tofu')
    try {
        & tofu init -input=false
        if ($LASTEXITCODE -ne 0) { throw 'tofu init failed' }
        & tofu apply -input=false -auto-approve
        if ($LASTEXITCODE -ne 0) { throw 'tofu apply failed' }
    } finally { Pop-Location }
}

function Write-LfFile {
    param([string]$Path, [string[]]$Lines)
    [System.IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
}

function Show-MemoryWarning {
    $total = [long](& docker info --format '{{.MemTotal}}' 2>$null)
    $running = @(& docker ps --format '{{.Label "com.docker.compose.project"}}' 2>$null | Where-Object { $_ -like 'freedom-*' } | Sort-Object -Unique).Count
    if ($total -gt 0 -and -not (Test-StackMemoryBudget -TotalBytes $total -RunningStacks $running)) {
        Write-Warning ("Docker has {0:N1} GB and {1} stack(s) are already running; one more needs about 3.5 GB. " -f ($total / 1GB), $running +
            "Expect ENOMEM crashes in dotnet watch and Keycloak. Raise the Docker Desktop / WSL memory limit, or 'agent.ps1 down' an idle stack.")
    }
}

function Start-Stack {
    param($Agent, [bool]$HotReload)

    Show-MemoryWarning

    $envPath = Join-Path $Agent.Path 'iac/local/.env'
    if ($Agent.Slot -gt 0 -and (Test-Path $envPath)) {
        Write-LfFile -Path $envPath -Lines (Set-EnvComposeFiles -Lines (Get-Content $envPath) -HotReload $HotReload)
    }

    # Same order as CI: backing services and the schema first, then tofu (realm, queues, blob
    # containers, database principals), then the application — it needs all of those at boot.
    # db-seed is deliberately not started: it is an opt-in one-shot and a --wait cannot cope with it.
    $up = @('up', '-d', '--wait', '--wait-timeout', '900')
    Invoke-Compose -Agent $Agent -HotReload $HotReload -ComposeArgs ($up + @('mssql', 'db-deploy', 'keycloak', 'azurite', 'telemetry', 'wiremock', 'mailpit'))
    Invoke-Tofu -Agent $Agent
    $apps = @('app', 'customs-worker', 'manifest-worker', 'edge', 'website')
    if ($HotReload) { $apps += 'web' }
    Invoke-Compose -Agent $Agent -HotReload $HotReload -ComposeArgs ($up + $apps)
}

function Get-EnvValue {
    param([string]$Path, [string]$Key, [string]$Default)

    if (Test-Path $Path) {
        $line = Get-Content $Path | Where-Object { $_ -like "$Key=*" } | Select-Object -Last 1
        if ($line) { return ($line -split '=', 2)[1] }
    }
    $Default
}

function Write-AgentFiles {
    param($Context, $Agent)

    $localDir = Join-Path $Agent.Path 'iac/local'
    $baseEnv = @('.env', '.env.example') |
        ForEach-Object { Join-Path $Context.MainRoot "iac/local/$_" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $baseEnv) { throw 'No iac/local/.env or .env.example found in the main checkout.' }

    $envLines = New-AgentEnvContent -BaseLines (Get-Content $baseEnv) -Name $Agent.Name -Slot $Agent.Slot
    $envPath = Join-Path $localDir '.env'
    Write-LfFile -Path $envPath -Lines $envLines

    Write-LfFile -Path (Join-Path $Agent.Path 'iac/tofu/terraform.tfvars') -Lines (New-AgentTfVars -Name $Agent.Name -Slot $Agent.Slot)

    $passwords = @{
        App       = Get-EnvValue $envPath 'FREEDOM_APP_DB_PASSWORD' 'Local_Freedom_App_1'
        Sensitive = Get-EnvValue $envPath 'FREEDOM_SENSITIVE_DB_PASSWORD' 'Local_Freedom_Sensitive_1'
    }
    $agentDir = Join-Path $Agent.Path '.agent'
    New-Item -ItemType Directory -Force $agentDir | Out-Null
    Write-LfFile -Path (Join-Path $agentDir 'env.ps1') -Lines (New-AgentTestEnv -Slot $Agent.Slot -Shell powershell -Passwords $passwords)
    Write-LfFile -Path (Join-Path $agentDir 'env.sh') -Lines (New-AgentTestEnv -Slot $Agent.Slot -Shell sh -Passwords $passwords)
    Write-LfFile -Path (Join-Path $agentDir 'env.container.sh') -Lines (New-AgentContainerTestEnv -Slot $Agent.Slot -Passwords $passwords)

    Write-DevContainerConfig -Context $Context -Agent $Agent
}

function Write-DevContainerConfig {
    param($Context, $Agent)

    $basePath = Join-Path $Agent.Path '.devcontainer/devcontainer.json'
    if (-not (Test-Path $basePath)) { return }

    $config = Get-Content $basePath -Raw | ConvertFrom-Json -AsHashtable
    $containerPath = "/workspaces/$($Agent.Name)"
    $gitDirName = Split-Path $Agent.Path -Leaf

    $config['name'] = "freedom-$($Agent.Name)"
    $config['build'] = @{ dockerfile = '../Dockerfile'; context = '../..' }
    $config['workspaceFolder'] = $containerPath
    $config['workspaceMount'] = "source=$($Agent.Path),target=$containerPath,type=bind"
    $config['mounts'] = @("source=$($Context.CommonDir),target=/mnt/main-git,type=bind")
    $agentEnv = @{
        GIT_DIR        = "/mnt/main-git/worktrees/$gitDirName"
        GIT_COMMON_DIR = '/mnt/main-git'
        GIT_WORK_TREE  = $containerPath
        FREEDOM_AGENT  = $Agent.Name
        FREEDOM_SLOT   = "$($Agent.Slot)"
    }
    $merged = if ($config.ContainsKey('containerEnv')) { $config['containerEnv'] } else { @{} }
    foreach ($key in $agentEnv.Keys) { $merged[$key] = $agentEnv[$key] }
    $config['containerEnv'] = $merged
    $config['postCreateCommand'] = 'dotnet restore UA.Action.Freedom.slnx && echo ". /workspaces/' + $Agent.Name + '/.agent/env.container.sh" >> ~/.bashrc'

    $dir = Join-Path $Agent.Path '.devcontainer/agent'
    New-Item -ItemType Directory -Force $dir | Out-Null
    Write-LfFile -Path (Join-Path $dir 'devcontainer.json') -Lines ($config | ConvertTo-Json -Depth 8)
}

function Show-Urls {
    param($Agent)
    $p = Get-AgentPorts -Slot $Agent.Slot
    "  app        http://localhost:$($p.EDGE_HTTP_PORT)/health/ready"
    "  operator   http://localhost:$($p.VITE_PORT)/app/   (Vite, hot reload)"
    "  keycloak   http://localhost:$($p.KEYCLOAK_PORT)"
    "  grafana    http://localhost:$($p.GRAFANA_PORT)"
    "  mailpit    http://localhost:$($p.MAILPIT_UI_PORT)"
    "  tests      . ./.agent/env.ps1   (in $($Agent.Path))"
}

$context = Get-RepoContext

switch ($Command) {

    'ports' {
        $n = if ($Slot -gt 0) { $Slot } elseif ($Name) { (Resolve-Agent $context $Name).Slot } else { (Resolve-Agent $context '').Slot }
        (Get-AgentPorts -Slot $n).GetEnumerator() | ForEach-Object { '{0,-22} {1}' -f $_.Key, $_.Value }
    }

    'new' {
        if (-not (Test-AgentName -Name $Name)) { throw "Name must be lowercase letters, digits and dashes, start with a letter, max 20 chars (got '$Name')." }
        $worktreePath = Join-Path $context.WorktreesRoot $Name

        $assigned = Use-SlotRegistryLock -Path $context.RegistryPath -Action {
            $registry = Read-SlotRegistry -Path $context.RegistryPath
            if ($registry.ContainsKey($Name)) { throw "Agent '$Name' already exists (slot $($registry[$Name].slot))." }

            $chosen = Select-AgentSlot -Registry (Get-RegistrySlots -Registry $registry) -Requested $Slot
            $busy = (Get-AgentPorts -Slot $chosen).GetEnumerator() | Where-Object { -not (Test-PortFree -Port $_.Value) }
            if ($busy) { throw "Slot $chosen has ports in use already: $(($busy | ForEach-Object { $_.Value }) -join ', '). Pick another with -Slot." }

            $registry[$Name] = @{ slot = $chosen; path = $worktreePath; branch = "agent/$Name"; created = (Get-Date -Format o) }
            Write-SlotRegistry -Path $context.RegistryPath -Registry $registry
            $chosen
        }

        $createdBranch = $false
        try {
            $existingBranch = & git -C $context.MainRoot branch --list "agent/$Name"
            if ($existingBranch) { Invoke-Git -C $context.MainRoot worktree add $worktreePath "agent/$Name" | Out-Null }
            else { Invoke-Git -C $context.MainRoot worktree add $worktreePath -b "agent/$Name" $Base | Out-Null; $createdBranch = $true }

            $agent = [pscustomobject]@{ Name = $Name; Slot = $assigned; Path = $worktreePath }
            Write-AgentFiles -Context $context -Agent $agent
        } catch {
            if (Test-Path $worktreePath) { & git -C $context.MainRoot worktree remove --force $worktreePath 2>&1 | Out-Null }
            if ($createdBranch) { & git -C $context.MainRoot branch -D "agent/$Name" 2>&1 | Out-Null }
            Use-SlotRegistryLock -Path $context.RegistryPath -Action {
                $registry = Read-SlotRegistry -Path $context.RegistryPath
                $registry.Remove($Name)
                Write-SlotRegistry -Path $context.RegistryPath -Registry $registry
            }
            throw
        }

        Write-Host "Agent '$Name' ready: slot $assigned, branch agent/$Name"
        Write-Host "  worktree   $worktreePath"
        Show-Urls $agent | Write-Host
        if ($Up) {
            Start-Stack -Agent $agent -HotReload (-not $NoHotReload)
        } else {
            Write-Host "Start it with: pwsh scripts/agent/agent.ps1 up $Name"
        }
    }

    'up' {
        $agent = Resolve-Agent $context $Name
        Start-Stack -Agent $agent -HotReload (-not $NoHotReload)
        Show-Urls $agent | Write-Host
    }

    'down' {
        $agent = Resolve-Agent $context $Name
        $args2 = @('down', '--remove-orphans')
        if ($Volumes) { $args2 += '-v' }
        Invoke-Compose -Agent $agent -ComposeArgs $args2
        if ($Volumes) {
            Get-ChildItem (Join-Path $agent.Path 'iac/tofu') -Filter 'terraform.tfstate*' | Remove-Item -Force
            Write-Host 'Volumes and tofu state wiped; the next `up` provisions from scratch.'
        }
    }

    'db' {
        $agent = Resolve-Agent $context $Name
        Invoke-Compose -Agent $agent -ComposeArgs @('run', '--rm', '--no-deps', 'db-deploy')
    }

    'status' {
        $agent = Resolve-Agent $context $Name
        Invoke-Compose -Agent $agent -ComposeArgs @('ps', '--all')
    }

    'refresh' {
        $agent = Resolve-Agent $context $Name
        if ($agent.Name -eq 'main') { throw 'The main checkout is slot 0 and has no generated files.' }
        Write-AgentFiles -Context $context -Agent $agent
        Write-Host "Regenerated the files for '$($agent.Name)' (slot $($agent.Slot)). Re-run 'up' if ports or secrets changed."
    }

    'env' {
        $agent = Resolve-Agent $context $Name
        Write-Host "PowerShell: . '$($agent.Path)/.agent/env.ps1'"
        Write-Host "bash:       . '$($agent.Path)/.agent/env.sh'"
    }

    'list' {
        $registry = Read-SlotRegistry -Path $context.RegistryPath
        $running = @(& docker ps --format '{{.Label "com.docker.compose.project"}}' 2>$null | Sort-Object -Unique)
        $rows = @([pscustomobject]@{ Name = 'main'; Slot = 0; Branch = (Invoke-Git -C $context.MainRoot rev-parse --abbrev-ref HEAD | Select-Object -First 1); Edge = 8080; Running = ($running -contains 'freedom-local'); Path = $context.MainRoot })
        foreach ($key in ($registry.Keys | Sort-Object { $registry[$_].slot })) {
            $rows += [pscustomobject]@{
                Name = $key; Slot = $registry[$key].slot; Branch = $registry[$key].branch
                Edge = (Get-AgentPorts -Slot ([int]$registry[$key].slot)).EDGE_HTTP_PORT
                Running = ($running -contains "freedom-$key"); Path = $registry[$key].path
            }
        }
        $rows | Format-Table -AutoSize
    }

    'remove' {
        $agent = Resolve-Agent $context $Name
        if ($agent.Name -eq 'main') { throw 'Refusing to remove the main checkout.' }

        if (Test-Path $agent.Path) {
            $dirty = & git -C $agent.Path status --porcelain
            if ($dirty -and -not $Purge) { throw "Worktree has uncommitted changes. Commit or stash them, or pass -Purge to discard." }
        }

        if (Test-Path (Join-Path $agent.Path 'iac/local')) {
            Invoke-Compose -Agent $agent -ComposeArgs @('down', '--remove-orphans', '-v')
        }
        # `down -v` only removes volumes the compose files still declare; sweep the rest by label.
        $strays = @(& docker volume ls -q --filter "label=com.docker.compose.project=freedom-$($agent.Name)" 2>$null)
        if ($strays.Count -gt 0) { & docker volume rm @strays 2>&1 | Out-Null }
        & docker image rm "ua-action-freedom/app:$($agent.Name)" "ua-action-freedom/customs-worker:$($agent.Name)" "ua-action-freedom/manifest-worker:$($agent.Name)" "ua-action-freedom/db-deploy:$($agent.Name)" 2>$null | Out-Null

        if (Test-Path $agent.Path) {
            $removeArgs = @('worktree', 'remove', $agent.Path)
            if ($Purge) { $removeArgs += '--force' }
            Invoke-Git -C $context.MainRoot @removeArgs | Out-Null
        }

        $branch = "agent/$($agent.Name)"
        $deleteFlag = if ($Purge) { '-D' } else { '-d' }
        & git -C $context.MainRoot branch $deleteFlag $branch 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { Write-Warning "Branch $branch kept (not merged). Use -Purge to delete it anyway." }

        Use-SlotRegistryLock -Path $context.RegistryPath -Action {
            $registry = Read-SlotRegistry -Path $context.RegistryPath
            $registry.Remove($agent.Name)
            Write-SlotRegistry -Path $context.RegistryPath -Registry $registry
        }
        Write-Host "Agent '$($agent.Name)' removed; slot $($agent.Slot) is free."
    }
}
