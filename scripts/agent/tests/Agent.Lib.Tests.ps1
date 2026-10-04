BeforeAll {
    . (Join-Path $PSScriptRoot '..' 'Agent.Lib.ps1')
}

Describe 'Get-AgentPorts' {
    It 'keeps the main stack on the default ports for slot 0' {
        $ports = Get-AgentPorts -Slot 0
        $ports.EDGE_HTTP_PORT | Should -Be 8080
        $ports.KEYCLOAK_PORT | Should -Be 8081
        $ports.MSSQL_PORT | Should -Be 1433
        $ports.AZURITE_BLOB_PORT | Should -Be 10000
        $ports.VITE_PORT | Should -Be 5173
    }

    It 'puts slot N in the block starting at 20000 + N*100' {
        $ports = Get-AgentPorts -Slot 3
        $ports.EDGE_HTTP_PORT | Should -Be 20300
        $ports.KEYCLOAK_PORT | Should -Be 20303
        $ports.MSSQL_PORT | Should -Be 20309
        $ports.AZURITE_TABLE_PORT | Should -Be 20312
        $ports.VITE_PORT | Should -Be 20313
    }

    It 'never hands the same host port to two slots' {
        $all = 0..9 | ForEach-Object { (Get-AgentPorts -Slot $_).Values } | ForEach-Object { $_ }
        ($all | Sort-Object -Unique).Count | Should -Be $all.Count
    }

    It 'rejects a slot outside 0-9' {
        { Get-AgentPorts -Slot 10 } | Should -Throw
        { Get-AgentPorts -Slot -1 } | Should -Throw
    }
}

Describe 'Test-AgentName' {
    It 'accepts short lowercase names' {
        Test-AgentName -Name 'alpha' | Should -BeTrue
        Test-AgentName -Name 'plan-09' | Should -BeTrue
    }

    It 'rejects names that cannot be a compose project or image tag' {
        Test-AgentName -Name 'Alpha' | Should -BeFalse
        Test-AgentName -Name '9lives' | Should -BeFalse
        Test-AgentName -Name 'has space' | Should -BeFalse
        Test-AgentName -Name 'a-very-long-agent-name-indeed' | Should -BeFalse
        Test-AgentName -Name '' | Should -BeFalse
    }
}

Describe 'Select-AgentSlot' {
    It 'takes the lowest free slot' {
        Select-AgentSlot -Registry @{ alpha = 1; beta = 3 } | Should -Be 2
    }

    It 'starts at slot 1, because slot 0 is the main checkout' {
        Select-AgentSlot -Registry @{} | Should -Be 1
    }

    It 'honours a requested slot that is free' {
        Select-AgentSlot -Registry @{ alpha = 1 } -Requested 5 | Should -Be 5
    }

    It 'refuses a requested slot that is taken' {
        { Select-AgentSlot -Registry @{ alpha = 1 } -Requested 1 } | Should -Throw '*alpha*'
    }

    It 'refuses when every slot is taken' {
        $full = @{}
        1..9 | ForEach-Object { $full["a$_"] = $_ }
        { Select-AgentSlot -Registry $full } | Should -Throw '*full*'
    }
}

Describe 'New-AgentEnvContent' {
    BeforeAll {
        $base = @(
            '# secrets',
            'MSSQL_SA_PASSWORD=Local_Freedom_Dev_1',
            'FREEDOM_APP_DB_PASSWORD=Local_Freedom_App_1',
            'EDGE_HTTP_PORT=8080',
            'KEYCLOAK_PORT=8081',
            'COMPOSE_PROJECT_NAME=stale'
        )
        $content = New-AgentEnvContent -BaseLines $base -Name 'alpha' -Slot 2
    }

    It 'keeps the secrets from the base file' {
        $content | Should -Contain 'MSSQL_SA_PASSWORD=Local_Freedom_Dev_1'
        $content | Should -Contain 'FREEDOM_APP_DB_PASSWORD=Local_Freedom_App_1'
    }

    It 'copes with blank lines in the base file' {
        $withBlank = New-AgentEnvContent -BaseLines @('A=1', '', '# c', '') -Name 'alpha' -Slot 1
        $withBlank | Should -Contain 'A=1'
    }

    It 'names the compose project, container prefix and image tag after the agent' {
        $content | Should -Contain 'COMPOSE_PROJECT_NAME=freedom-alpha'
        $content | Should -Contain 'FREEDOM_PREFIX=freedom-alpha'
        $content | Should -Contain 'FREEDOM_IMAGE_TAG=alpha'
    }

    It 'replaces the base ports and identifiers instead of repeating them' {
        $content | Should -Contain 'EDGE_HTTP_PORT=20200'
        $content | Should -Contain 'KEYCLOAK_PORT=20203'
        @($content | Where-Object { $_ -like 'EDGE_HTTP_PORT=*' }).Count | Should -Be 1
        @($content | Where-Object { $_ -like 'COMPOSE_PROJECT_NAME=*' }).Count | Should -Be 1
    }
}

Describe 'compose files in the agent .env' {
    It 'includes the dev override by default, so a bare docker compose sees the whole stack' {
        $content = New-AgentEnvContent -BaseLines @('A=1') -Name 'alpha' -Slot 1
        $content | Should -Contain 'COMPOSE_PATH_SEPARATOR=:'
        $content | Should -Contain 'COMPOSE_FILE=docker-compose.yml:docker-compose.dev.yml'
    }

    It 'drops the override when hot reload is off' {
        $content = New-AgentEnvContent -BaseLines @('A=1') -Name 'alpha' -Slot 1 -HotReload $false
        $content | Should -Contain 'COMPOSE_FILE=docker-compose.yml'
    }

    It 'switches an existing .env between modes without touching anything else' {
        $before = New-AgentEnvContent -BaseLines @('A=1') -Name 'alpha' -Slot 1
        $after = Set-EnvComposeFiles -Lines $before -HotReload $false
        $after | Should -Contain 'COMPOSE_FILE=docker-compose.yml'
        $after | Should -Contain 'A=1'
        @($after | Where-Object { $_ -like 'COMPOSE_FILE=*' }).Count | Should -Be 1
        $after.Count | Should -Be $before.Count
    }
}

Describe 'agent memory caps' {
    It 'caps Keycloak and SQL Server in an agent stack, because three uncapped stacks exhaust a 16 GB Docker VM' {
        $content = New-AgentEnvContent -BaseLines @('A=1') -Name 'alpha' -Slot 1
        $content | Should -Contain 'KEYCLOAK_JAVA_HEAP="-Xms128m -Xmx640m"'
        $content | Should -Contain 'MSSQL_MEMORY_LIMIT_MB=1024'
    }

    It 'replaces caps already present in the base file' {
        $content = New-AgentEnvContent -BaseLines @('MSSQL_MEMORY_LIMIT_MB=8192') -Name 'alpha' -Slot 1
        @($content | Where-Object { $_ -like 'MSSQL_MEMORY_LIMIT_MB=*' }).Count | Should -Be 1
    }
}

Describe 'Test-StackMemoryBudget' {
    It 'is fine when the Docker VM has room for the stacks that would be running' {
        Test-StackMemoryBudget -TotalBytes 32GB -RunningStacks 2 | Should -BeTrue
    }

    It 'complains when one more stack would not fit' {
        Test-StackMemoryBudget -TotalBytes 16GB -RunningStacks 4 | Should -BeFalse
    }

    It 'counts the stack being started' {
        Test-StackMemoryBudget -TotalBytes 4GB -RunningStacks 0 | Should -BeTrue
        Test-StackMemoryBudget -TotalBytes 3GB -RunningStacks 0 | Should -BeFalse
    }
}

Describe 'New-AgentTfVars' {
    It 'points every tofu URL and container at the slot' {
        $text = (New-AgentTfVars -Name 'alpha' -Slot 1) -join "`n"
        $text | Should -Match 'keycloak_url\s*=\s*"http://localhost:20103"'
        $text | Should -Match 'edge_url\s*=\s*"http://localhost:20100"'
        $text | Should -Match 'vite_dev_url\s*=\s*"http://localhost:20113"'
        $text | Should -Match 'azurite_blob_endpoint\s*=\s*"http://127.0.0.1:20110/devstoreaccount1"'
        $text | Should -Match 'azurite_queue_endpoint\s*=\s*"http://127.0.0.1:20111/devstoreaccount1"'
        $text | Should -Match 'mssql_container\s*=\s*"freedom-alpha-mssql"'
        $text | Should -Match 'wiremock_port\s*=\s*20104'
    }
}

Describe 'New-AgentTestEnv' {
    BeforeAll {
        $passwords = @{ App = 'AppPw1'; Sensitive = 'SensPw1' }
    }

    It 'renders PowerShell exports for the test projects and Playwright' {
        $text = (New-AgentTestEnv -Slot 1 -Shell powershell -Passwords $passwords) -join "`n"
        $text | Should -Match "\`$env:FREEDOM_BASE_URL = 'http://localhost:20100'"
        $text | Should -Match "\`$env:FREEDOM_OIDC_URL = 'http://localhost:20103/realms/freedom'"
        $text | Should -Match "\`$env:PLAYWRIGHT_BASE_URL = 'http://localhost:20100'"
        $text | Should -Match "\`$env:FREEDOM_REQUIRE_INTEGRATION = 'true'"
        $text | Should -Match 'Server=localhost,20109;Database=Freedom;User Id=freedom_app;Password=AppPw1'
        $text | Should -Match 'User Id=freedom_sensitive;Password=SensPw1'
        # The Azure SDK only treats an IP endpoint as path-style on a non-default port; with
        # "localhost" it drops the container from the blob URL (ContainerNotFound).
        $text | Should -Match 'BlobEndpoint=http://127.0.0.1:20110/devstoreaccount1'
    }

    It 'renders the same variables as shell exports' {
        $text = (New-AgentTestEnv -Slot 1 -Shell sh -Passwords $passwords) -join "`n"
        $text | Should -Match "export FREEDOM_BASE_URL='http://localhost:20100'"
        $text | Should -Match "export ConnectionStrings__Freedom='Server=localhost,20109"
    }

    It 'leaves integration optional when asked, so a half-built stack skips instead of failing' {
        $text = (New-AgentTestEnv -Slot 1 -Shell sh -Passwords $passwords -RequireIntegration:$false) -join "`n"
        $text | Should -Not -Match 'FREEDOM_REQUIRE_INTEGRATION'
    }
}

Describe 'New-AgentContainerTestEnv' {
    BeforeAll {
        $text = (New-AgentContainerTestEnv -Slot 2 -Passwords @{ App = 'a'; Sensitive = 's' }) -join "`n"
    }

    It 'reaches SQL, the edge and Keycloak through host.docker.internal' {
        $text | Should -Match 'Server=host.docker.internal,20209'
        $text | Should -Match "FREEDOM_BASE_URL='http://host.docker.internal:20200'"
    }

    It 'gives storage an IP, because the Azure SDK mishandles a hostname on a non-default port' {
        $text | Should -Match 'export FREEDOM_HOST_IP=\$\(getent'
        $text | Should -Match 'BlobEndpoint=http://''"\$FREEDOM_HOST_IP"'':20210/devstoreaccount1'
        $text | Should -Not -Match 'BlobEndpoint=http://host.docker.internal'
    }
}

Describe 'Slot registry' {
    BeforeEach {
        $script:path = Join-Path ([System.IO.Path]::GetTempPath()) ("slots-" + [guid]::NewGuid() + '.json')
    }
    AfterEach {
        Remove-Item $script:path -ErrorAction SilentlyContinue
    }

    It 'is empty when the file does not exist' {
        (Read-SlotRegistry -Path $script:path).Count | Should -Be 0
    }

    It 'round-trips name, slot and worktree path' {
        Write-SlotRegistry -Path $script:path -Registry @{ alpha = @{ slot = 1; path = 'C:/w/alpha' } }
        $back = Read-SlotRegistry -Path $script:path
        $back.alpha.slot | Should -Be 1
        $back.alpha.path | Should -Be 'C:/w/alpha'
    }

    It 'lets Select-AgentSlot read the slot out of registry entries' {
        Write-SlotRegistry -Path $script:path -Registry @{ alpha = @{ slot = 1; path = 'x' } }
        $slots = Get-RegistrySlots -Registry (Read-SlotRegistry -Path $script:path)
        Select-AgentSlot -Registry $slots | Should -Be 2
    }
}
