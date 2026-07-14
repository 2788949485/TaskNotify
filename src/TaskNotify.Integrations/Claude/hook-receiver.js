#!/usr/bin/env node
'use strict';

const crypto = require('crypto');
const net = require('net');

function eventType(input) {
    switch (input.hook_event_name) {
        case 'UserPromptSubmit': return 'TaskStarted';
        case 'Stop': return 'TaskSucceeded';
        case 'StopFailure': return 'TaskFailed';
        case 'PermissionRequest': return 'TaskWaitingForPermission';
        case 'Notification':
            return input.notification_type === 'permission_prompt'
                ? 'TaskWaitingForPermission'
                : 'TaskWaitingForInput';
        default: return null;
    }
}

function send(input) {
    return new Promise((resolve) => {
        const type = eventType(input);
        if (!type) return resolve();

        const sessionHash = crypto.createHash('sha256')
            .update(String(input.session_id || 'unknown'))
            .digest('hex')
            .slice(0, 16);
        const payload = Buffer.from(JSON.stringify({
            eventId: crypto.randomUUID(),
            version: '1',
            type,
            source: 'claude',
            taskId: `claude-${sessionHash}`,
            displayName: 'Claude Code',
            workingDir: input.cwd || null,
            exitCode: type === 'TaskSucceeded' ? 0 : type === 'TaskFailed' ? 1 : null
        }), 'utf8');
        const frame = Buffer.allocUnsafe(payload.length + 4);
        frame.writeUInt32BE(payload.length, 0);
        payload.copy(frame, 4);

        const socket = net.createConnection('\\\\.\\pipe\\TaskNotifyPipe');
        let done = false;
        const finish = () => {
            if (done) return;
            done = true;
            socket.destroy();
            resolve();
        };
        socket.setTimeout(100, finish);
        socket.on('connect', () => socket.end(frame, finish));
        socket.on('error', finish);
    });
}

let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => { input += chunk; });
process.stdin.on('end', async () => {
    try { await send(JSON.parse(input)); } catch { }
    process.exit(0);
});
