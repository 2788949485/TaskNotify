# TaskNotify PowerShell integration for PowerShell, Windows Terminal, and WezTerm.
$script:TaskNotifyPipeName = 'TaskNotifyPipe'
$script:TaskNotifyPendingCommand = $null
$script:TaskNotifyOriginalPrompt = $null
$script:TaskNotifyInitialized = $false

function Send-ToTaskNotify {
    param(
        [ValidateSet('Started', 'Succeeded', 'Failed', 'EndedUnknown')]
        [string]$Action,
        [string]$TaskId,
        [string]$DisplayName,
        [Nullable[int]]$ExitCode
    )

    $eventType = switch ($Action) {
        'Started' { 'TaskStarted' }
        'Succeeded' { 'TaskSucceeded' }
        'Failed' { 'TaskFailed' }
        'EndedUnknown' { 'TaskEndedUnknown' }
    }

    $message = @{
        eventId = [Guid]::NewGuid().ToString('N')
        version = '1'
        type = $eventType
        source = 'powershell'
        taskId = $TaskId
        displayName = $DisplayName
        workingDir = (Get-Location).Path
        exitCode = $ExitCode
    } | ConvertTo-Json -Compress

    try {
        $payload = [System.Text.Encoding]::UTF8.GetBytes($message)
        $frame = [byte[]]::new(4 + $payload.Length)
        $frame[0] = [byte](($payload.Length -shr 24) -band 0xFF)
        $frame[1] = [byte](($payload.Length -shr 16) -band 0xFF)
        $frame[2] = [byte](($payload.Length -shr 8) -band 0xFF)
        $frame[3] = [byte]($payload.Length -band 0xFF)
        [System.Buffer]::BlockCopy($payload, 0, $frame, 4, $payload.Length)

        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $script:TaskNotifyPipeName, [System.IO.Pipes.PipeDirection]::Out)
        try {
            $pipe.Connect(100)
            $pipe.Write($frame, 0, $frame.Length)
        }
        finally {
            $pipe.Dispose()
        }
    }
    catch {
        # The desktop app must never delay or change the user's command result.
    }
}

function Complete-TaskNotifyCommand {
    param([bool]$Succeeded, [Nullable[int]]$ExitCode)

    $pending = $script:TaskNotifyPendingCommand
    if ($null -eq $pending) { return }

    $script:TaskNotifyPendingCommand = $null
    if ($null -eq $ExitCode) {
        $action = if ($Succeeded) { 'EndedUnknown' } else { 'Failed' }
        Send-ToTaskNotify $action $pending.TaskId $pending.DisplayName $null
        return
    }

    $action = if ($ExitCode.Value -eq 0) { 'Succeeded' } else { 'Failed' }
    Send-ToTaskNotify $action $pending.TaskId $pending.DisplayName $ExitCode.Value
}

function Initialize-TaskNotifyIntegration {
    if ($script:TaskNotifyInitialized) { return }
    if ($null -eq (Get-Command Set-PSReadLineKeyHandler -ErrorAction SilentlyContinue)) { return }

    $existingPrompt = Get-Command prompt -CommandType Function -ErrorAction SilentlyContinue
    if ($null -eq $existingPrompt) { return }

    $script:TaskNotifyOriginalPrompt = $existingPrompt.ScriptBlock
    Set-PSReadLineKeyHandler -Key Enter -ScriptBlock {
        param($key, $arg)
        $line = ''
        $cursor = 0
        [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$line, [ref]$cursor)
        $command = $line.Trim()
        if ($command.Length -gt 0) {
            $script:TaskNotifyPendingCommand = [pscustomobject]@{
                TaskId = [Guid]::NewGuid().ToString('N')
                DisplayName = ($command -split '\s+', 2)[0]
            }
            $global:LASTEXITCODE = $null
            Send-ToTaskNotify 'Started' $script:TaskNotifyPendingCommand.TaskId $script:TaskNotifyPendingCommand.DisplayName 0
        }

        [Microsoft.PowerShell.PSConsoleReadLine]::AcceptLine()
    }

    function global:prompt {
        $taskNotifySucceeded = $?
        $taskNotifyExitCode = $global:LASTEXITCODE
        Complete-TaskNotifyCommand $taskNotifySucceeded $taskNotifyExitCode
        & $script:TaskNotifyOriginalPrompt
    }

    $script:TaskNotifyInitialized = $true
}

Export-ModuleMember -Function Initialize-TaskNotifyIntegration
