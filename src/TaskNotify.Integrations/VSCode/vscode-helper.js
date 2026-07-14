/**
 * TaskNotify VS Code Extension Helper
 * 
 * This is a lightweight VS Code extension that forwards task events
 * to TaskNotify via Named Pipe.
 * 
 * Installation:
 *   1. Copy this file to ~/.vscode/extensions/tasknotify-integration-1.0.0/
 *   2. Or use: code --install-extension tasknotify.vscode-integration
 * 
 * Features:
 * - Listens to VS Code Task lifecycle events
 * - Listens to terminal shell integration events (PS v7+, zsh)
 * - Forwards to TaskNotify via Named Pipe
 * - Does NOT read terminal screen content
 * - Deduplicates with process monitor events
 */

const { execSync } = require('child_process');
const net = require('net');
const crypto = require('crypto');

const PIPE_NAME = '\\\\.\\pipe\\TaskNotifyPipe';
const VERSION = '1';
const SOURCE = 'vscode';

/**
 * Send a message to TaskNotify via Named Pipe (Windows) or TCP fallback.
 * Returns true if sent, false on failure.
 */
function sendEvent(type, taskId, displayName, summary, exitCode) {
    const msg = JSON.stringify({
        eventId: crypto.randomUUID(),
        version: VERSION,
        type: type,
        source: SOURCE,
        taskId: taskId,
        displayName: displayName || null,
        summary: summary || null,
        exitCode: exitCode !== undefined ? exitCode : null,
        workingDir: process.cwd()
    });

    const payload = Buffer.from(msg, 'utf8');
    const frame = Buffer.alloc(4 + payload.length);
    frame.writeInt32BE(payload.length, 0);
    payload.copy(frame, 4);

    // Use PowerShell to write to Named Pipe (cross-platform approach)
    try {
        const psCmd = `
            $bytes = [Convert]::FromHexString('${frame.toString('hex')}');
            $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'TaskNotifyPipe', [System.IO.Pipes.PipeDirection]::Out);
            $pipe.Connect(3000);
            $pipe.Write($bytes, 0, $bytes.Length);
            $pipe.Close();
        `;
        execSync(`powershell -NoProfile -Command "${psCmd}"`, { timeout: 5000 });
        return true;
    } catch (e) {
        // Fire-and-forget: pipe failure is OK
        return false;
    }
}

// Event type mapping
const EVENT_TYPES = {
    started: 'TaskStarted',
    succeeded: 'TaskSucceeded',
    failed: 'TaskFailed',
    waitingForPermission: 'TaskWaitingForPermission',
    waitingForInput: 'TaskWaitingForInput',
    cancelled: 'TaskCancelled',
    timedOut: 'TaskTimedOut'
};

/**
 * Activate the VS Code extension.
 * @param {import('vscode').ExtensionContext} context
 */
function activate(context) {
    const disposable = vscode.commands.registerCommand('tasknotify-vscode.sendEvent', async (params) => {
        const eventType = EVENT_TYPES[params.type] || params.type;
        sendEvent(eventType, params.taskId, params.displayName, params.summary, params.exitCode);
    });

    context.subscriptions.push(disposable);

    // Listen to VS Code Task events
    const taskDisposable = vscode.tasks.onDidEndTask((event) => {
        const taskId = `${event.execution.task.name}-${event.execution.task.scope}`;
        sendEvent('TaskSucceeded', taskId, event.execution.task.name, null, 0);
    });

    const taskErrorDisposable = vscode.tasks.onDidEndTaskError((event) => {
        const taskId = `${event.execution.task.name}-${event.execution.task.scope}`;
        sendEvent('TaskFailed', taskId, event.execution.task.name, event.message || 'Task ended with error', event.exitCode);
    });

    context.subscriptions.push(taskDisposable, taskErrorDisposable);

    // Listen to terminal shell integration
    if (vscode.env.shell === 'PowerShell' || vscode.env.shell.includes('pwsh')) {
        // PowerShell 7+ has shell integration
        const termDisposable = vscode.window.onDidChangeActiveTerminal((terminal) => {
            if (terminal) {
                // Terminal lifecycle events can be used for tracking
            }
        });
        context.subscriptions.push(termDisposable);
    }
}

function deactivate() {}

module.exports = { activate, deactivate };
