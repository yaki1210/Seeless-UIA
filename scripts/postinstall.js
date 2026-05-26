/**
 * Post-install script: builds the native .NET binary and copies it to bin/.
 */

import { execSync } from 'child_process';
import { copyFileSync, existsSync, mkdirSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = join(__dirname, '..');
const srcDir = join(root, 'src');
const binDir = join(root, 'bin');

// Check if dotnet is available
try {
    execSync('dotnet --version', { stdio: 'pipe' });
} catch {
    console.log('Note: .NET SDK not found. Skipping native build.');
    console.log('  Install from https://dotnet.microsoft.com/download');
    console.log('  Then run: dotnet build src/SeelessUIA.slnx -c Release');
    process.exit(0);
}

// Build the solution
console.log('Building SeelessUIA...');
try {
    execSync('dotnet build SeelessUIA.slnx -c Release', {
        cwd: root,
        stdio: 'inherit',
    });
} catch {
    console.error('Build failed. See errors above.');
    process.exit(1);
}

// Copy binary to bin/
const exePath = join(srcDir, 'bin', 'Release', 'net10.0-windows', 'SeelessUIA.exe');
if (existsSync(exePath)) {
    if (!existsSync(binDir)) mkdirSync(binDir, { recursive: true });
    copyFileSync(exePath, join(binDir, 'SeelessUIA.exe'));
    console.log('SeelessUIA.exe copied to bin/');
} else {
    console.error(`Binary not found at expected path: ${exePath}`);
    process.exit(1);
}
