#!/usr/bin/env node

/**
 * Cross-platform CLI wrapper for SeelessUIA
 *
 * Spawns the native SeelessUIA.exe binary with inherited stdio.
 * The .NET binary handles daemon auto-start and command dispatch.
 */

import { spawn } from 'child_process';
import { existsSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';
import { platform, arch } from 'os';

const __dirname = dirname(fileURLToPath(import.meta.url));

function getBinaryName() {
    if (platform() !== 'win32') {
        console.error('Error: SeelessUIA only supports Windows (requires UI Automation).');
        process.exit(1);
    }
    return 'SeelessUIA.exe';
}

function main() {
    const binaryName = getBinaryName();
    const binaryPath = join(__dirname, binaryName);

    if (!existsSync(binaryPath)) {
        console.error(`Error: Binary not found at ${binaryPath}`);
        console.error('Run "npm run build:native" or "pnpm build:native" to build.');
        console.error('Or: dotnet build SeelessUIA.slnx -c Release');
        process.exit(1);
    }

    const child = spawn(binaryPath, process.argv.slice(2), {
        stdio: 'inherit',
        windowsHide: false,
    });

    child.on('error', (err) => {
        console.error(`Error executing binary: ${err.message}`);
        process.exit(1);
    });

    child.on('close', (code) => {
        process.exit(code ?? 0);
    });
}

main();
