#!/usr/bin/env node
// Print one section of the library, not the whole chapter.
//
//   node library/show.mjs 7-9 1234            the section starting at line 1234 (from find.mjs)
//   node library/show.mjs 7-4 "deadlock"      the first section whose heading contains "deadlock"
//   node library/show.mjs 7-4                 the chapter's section list, with line numbers
//
// A section runs until the next heading at the same or a higher level. Output is capped at 250 lines;
// pass --all to remove the cap.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const all = args.includes('--all');
const [id, ...rest] = args.filter((a) => a !== '--all');
const target = rest.join(' ').trim();
if (!id) { console.error('Usage: node library/show.mjs <chapter id like 7-4> [line number | heading words] [--all]'); process.exit(1); }

const index = JSON.parse(readFileSync(join(here, 'index.json'), 'utf8'));
const chapter = index.parts.flatMap((p) => p.chapters).find((c) => c.id === id || c.number === id);
if (!chapter) { console.error(`No chapter "${id}". Chapter ids look like 7-4; see library/index.json.`); process.exit(1); }
const lines = readFileSync(join(here, chapter.file), 'utf8').split(/\r?\n/);

// Headings outside code fences.
const heads = []; let fence = null;
lines.forEach((line, i) => {
  const f = line.match(/^\s*(`{3,}|~{3,})/);
  if (f) { if (!fence) fence = f[1]; else if (line.trim().startsWith(fence)) fence = null; }
  const h = !fence && line.match(/^(#{2,6})\s+(.+?)\s*$/);
  if (h) heads.push({ line: i + 1, level: h[1].length, text: h[2] });
});

if (!target) {
  console.log(`${chapter.number} ${chapter.title}  (library/${chapter.file}, ${chapter.words} words)`);
  for (const h of heads.filter((h) => h.level <= 4)) console.log(`${String(h.line).padStart(6)}  ${'  '.repeat(Math.max(0, h.level - 3))}${h.text}`);
  process.exit(0);
}

const start = /^\d+$/.test(target)
  ? heads.find((h) => h.line === Number(target)) ?? { line: Number(target), level: 6 }
  : heads.find((h) => h.text.toLowerCase().includes(target.toLowerCase()));
if (!start) { console.error(`No heading containing "${target}" in ${chapter.number}. Run without a target to list sections.`); process.exit(1); }
const next = heads.find((h) => h.line > start.line && h.level <= start.level);
const end = next ? next.line - 1 : lines.length;
const body = lines.slice(start.line - 1, end);
const cap = all ? body.length : Math.min(body.length, 250);
console.log(`[${chapter.number} ${chapter.title}, lines ${start.line}-${start.line + cap - 1} of library/${chapter.file}]\n`);
console.log(body.slice(0, cap).join('\n'));
if (cap < body.length) console.log(`\n[... ${body.length - cap} more lines. Add --all, or run show.mjs on a sub-heading.]`);
