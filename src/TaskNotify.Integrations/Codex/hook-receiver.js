#!/usr/bin/env node
'use strict';

const crypto = require('crypto');
const net = require('net');

function eventType(name) {
    switch (name) {
        case 'UserPromptSubmit': return 'TaskStarted';
        case 'PermissionRequest': return 'TaskWaitingForPermission';
        case 'Stop': return 'TaskSucceeded';
        default: return null;
    }
}

function send(input) {
    return new Promise((resolve) => {
        const type = eventType(input.hook_event_name);
        if (!type) return resolve();

        const taskHash = crypto.createHash('sha256')
            .update(`${input.session_id || 'unknown'}:${input.turn_id || 'unknown'}`)
            .digest('hex')
            .slice(0, 16);
        const payload = Buffer.from(JSON.stringify({
            eventId: crypto.randomUUID(),
            version: '1',
            type,
            source: 'codex',
            taskId: `codex-${taskHash}`,
            displayName: 'Codex',
            workingDir: input.cwd || null,
            exitCode: type === 'TaskSucceeded' ? 0 : null
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
