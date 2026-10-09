#!/usr/bin/env node
// Search the library by topic, without reading it.
//
//   node library/find.mjs "double booking overlap"           top 10 sections
//   node library/find.mjs "idempotency key" --limit 5
//   node library/find.mjs "outbox" --chapter 12-3            only chapter 12.3
//
// Prints one line per hit: chapter, section heading, file:line, and the command to read it.
// Uses only Node's standard library, so it works on Windows and macOS with no install.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args.splice(i, 2)[1] : fallback; };
const limit = Number(opt('--limit', 10));
const onlyChapter = opt('--chapter', null);
const query = args.join(' ').trim();
if (!query) { console.error('Usage: node library/find.mjs "<topic words>" [--limit N] [--chapter 7-3]'); process.exit(1); }

const STOP = new Set('a an and are as at be by for from how in is it of on or the to what when with your you'.split(' '));
const stem = (w) => (w.length > 4 && w.endsWith('ies') ? w.slice(0, -3) + 'y' : w.length > 3 && w.endsWith('s') && !w.endsWith('ss') ? w.slice(0, -1) : w);
const terms = (s) => (s.toLowerCase().replace(/c#/g, 'csharp').replace(/\.net\b/g, 'dotnet').match(/[\p{L}\p{N}]+/gu) ?? [])
  .filter((w) => !STOP.has(w)).map(stem);

const index = JSON.parse(readFileSync(join(here, 'index.json'), 'utf8'));
const chapters = index.parts.flatMap((p) => p.chapters);

// Split every chapter into sections at ### and #### headings (outside code fences).
const sections = [];
for (const c of chapters) {
  if (onlyChapter && c.id !== onlyChapter) continue;
  const lines = readFileSync(join(here, c.file), 'utf8').split(/\r?\n/);
  let fence = null; let cur = null;
  lines.forEach((line, i) => {
    const f = line.match(/^\s*(`{3,}|~{3,})/);
    if (f) { if (!fence) fence = f[1]; else if (line.trim().startsWith(fence)) fence = null; }
    const h = !fence && line.match(/^(#{3,4})\s+(.+?)\s*$/);
    if (h) {
      cur = { chapter: c, heading: h[2].replace(/[*`]/g, ''), line: i + 1, text: '' };
      sections.push(cur);
    } else if (cur) cur.text += line + '\n';
  });
}

const q = [...new Set(terms(query))];
const N = sections.length;
const df = new Map(q.map((t) => [t, 0]));
const tfs = sections.map((s) => {
  const counts = new Map();
  for (const w of terms(s.heading)) counts.set(w, (counts.get(w) ?? 0) + 4);
  for (const w of terms(s.text)) counts.set(w, (counts.get(w) ?? 0) + 1);
  s.length = [...counts.values()].reduce((a, b) => a + b, 0) || 1;
  for (const t of q) if (counts.has(t)) df.set(t, df.get(t) + 1);
  return counts;
});
const avg = sections.reduce((a, s) => a + s.length, 0) / Math.max(N, 1);
const phrase = query.toLowerCase();
const scored = sections.map((s, i) => {
  let score = 0; let hit = 0;
  for (const t of q) {
    const tf = tfs[i].get(t) ?? 0; if (!tf) continue; hit++;
    const idf = Math.log(1 + (N - df.get(t) + 0.5) / (df.get(t) + 0.5));
    score += idf * (tf * 2.2) / (tf + 1.2 * (0.25 + 0.75 * s.length / avg));
  }
  score *= 0.4 + 0.6 * hit / q.length;
  if (q.length > 1 && (s.heading + ' ' + s.text).toLowerCase().includes(phrase)) score *= 1.5;
  return { s, score };
}).filter((x) => x.score > 0).sort((a, b) => b.score - a.score).slice(0, limit);

if (!scored.length) { console.log(`No sections match "${query}". Try fewer or different words.`); process.exit(0); }
for (const { s } of scored) {
  console.log(`${s.chapter.number.padEnd(5)} ${s.chapter.title} > ${s.heading}`);
  console.log(`      library/${s.chapter.file}:${s.line}   read: node library/show.mjs ${s.chapter.id} ${s.line}`);
}
