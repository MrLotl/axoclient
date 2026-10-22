// Prüft, ob alle in der UI benutzten Text-Schlüssel in de.json stehen.
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Z]:)/, '$1')), '..');
const dict = JSON.parse(fs.readFileSync(path.join(root, 'src/lib/i18n/de.json'), 'utf8'));

function has(node, parts) {
  if (parts.length === 0) return typeof node === 'string';
  if (typeof node !== 'object' || node === null) return false;
  for (let i = parts.length; i > 0; i--) {
    const key = parts.slice(0, i).join('.');
    if (key in node && has(node[key], parts.slice(i))) return true;
  }
  return false;
}

const files = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p);
    else if (/\.(svelte|ts)$/.test(e.name) && !e.name.endsWith('.test.ts')) files.push(p);
  }
})(path.join(root, 'src'));

const missing = new Map();
for (const file of files) {
  const text = fs.readFileSync(file, 'utf8');
  for (const m of text.matchAll(/\bt\(\s*'([a-zA-Z][\w.]*)'/g)) {
    if (!has(dict, m[1].split('.'))) missing.set(m[1], path.relative(root, file));
  }
}
// Text-Codes, die C# als TextRef schickt: String-Literale, deren erster Teil ein Bereich in de.json ist
const csFiles = [];
(function walkCs(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (!/^(bin|obj|wwwroot|AxoClient\.Tests)$/.test(e.name)) walkCs(p);
    } else if (e.name.endsWith('.cs')) csFiles.push(p);
  }
})(path.join(root, '../src'));
for (const file of csFiles) {
  const text = fs.readFileSync(file, 'utf8');
  for (const m of text.matchAll(/(?:AxoException|TextRef|TextRef\.Of|\.Status)\(\s*"([a-z][a-zA-Z]*(?:\.[a-zA-Z0-9_]+)+)"/g)) {
    const top = m[1].split('.')[0];
    if (typeof dict[top] === 'object' && !has(dict, m[1].split('.'))) missing.set(m[1], path.relative(root, file));
  }
}

for (const [key, file] of [...missing].sort()) console.log(`${key}\t${file}`);
console.error(`${missing.size} fehlende Schlüssel`);
process.exit(missing.size ? 1 : 0);
