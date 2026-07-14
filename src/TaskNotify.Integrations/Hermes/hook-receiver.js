#!/usr/bin/env node
'use strict';

const crypto = require('crypto');
const net = require('net');

function send(input) {
    return new Promise((resolve) => {
        const type = input.hook_event_name === 'pre_llm_call' ? 'TaskStarted'
            : input.hook_event_name === 'post_llm_call' ? 'TaskSucceeded' : null;
        if (!type) return resolve();

        const id = crypto.createHash('sha256')
            .update(String(input.session_id || 'unknown'))
            .digest('hex').slice(0, 16);
        const payload = Buffer.from(JSON.stringify({
            eventId: crypto.randomUUID(), version: '1', type, source: 'hermes',
            taskId: `hermes-${id}`, displayName: 'Hermes Agent',
            workingDir: input.cwd || null, exitCode: type === 'TaskSucceeded' ? 0 : null
        }), 'utf8');
        const frame = Buffer.allocUnsafe(payload.length + 4);
        frame.writeUInt32BE(payload.length, 0);
        payload.copy(frame, 4);

        const socket = net.createConnection('\\\\.\\pipe\\TaskNotifyPipe');
        let done = false;
        const finish = () => { if (!done) { done = true; socket.destroy(); resolve(); } };
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
    process.stdout.write('{}\n');
});
