// Text contrast is at least 4.5:1 (WCAG 2.2 AA) for every text colour on every background the UI puts it on, in both
// themes. The colours are read from index.css, so changing a token re-checks everything that uses it.
import { describe, expect, it } from 'vitest';
import css from '../index.css?raw';

function tokens(block: string): Record<string, string> {
  return Object.fromEntries([...block.matchAll(/--([\w-]+):\s*(#[0-9a-fA-F]{6})/g)].map((m) => [m[1], m[2]]));
}

const light = tokens(css.slice(css.indexOf(':root {'), css.indexOf('}', css.indexOf(':root {'))));
const darkStart = css.indexOf('@media (prefers-color-scheme: dark)');
const dark = { ...light, ...tokens(css.slice(darkStart, css.indexOf('}', css.indexOf(':root {', darkStart)))) };

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** Text token on background token, as the stylesheet uses them. */
const PAIRS: [text: string, background: string, where: string][] = [
  ['ink', 'bg', 'page text'],
  ['ink', 'surface', 'cards, panels'],
  ['ink', 'surface-2', 'secondary surfaces'],
  ['ink', 'brand-soft', 'countdown, active nav'],
  ['ink', 'info-soft', 'info notices'],
  ['ink', 'ok-soft', 'success notices'],
  ['muted', 'bg', 'hints on the page'],
  ['muted', 'surface', 'hints in cards'],
  ['muted', 'booked', 'booked slot labels'],
  ['muted', 'brand-soft', 'countdown note'],
  ['muted', 'info-soft', 'hints in notices'],
  ['brand', 'surface', 'links, ghost buttons'],
  ['brand', 'bg', 'links on the page'],
  ['brand', 'brand-soft', 'active nav item'],
  ['brand-ink', 'brand', 'primary buttons, chosen slot'],
  ['danger', 'danger-soft', 'error notices, urgent countdown'],
  ['danger', 'surface', 'cancelled status, field errors'],
  ['danger-ink', 'danger', 'danger buttons'],
  ['ok', 'surface', 'confirmed status'],
  ['toast-ink', 'toast-bg', 'toasts'],
];

it('reads both themes from the stylesheet', () => {
  expect(Object.keys(light).length).toBeGreaterThan(15);
  expect(dark.bg).not.toBe(light.bg);
});

describe.each([['light', light], ['dark', dark]] as const)('%s theme', (_, theme) => {
  it.each(PAIRS)('%s on %s (%s) is at least 4.5:1', (text, background) => {
    expect(theme[text], `--${text}`).toBeDefined();
    expect(theme[background], `--${background}`).toBeDefined();
    expect(contrast(theme[text], theme[background])).toBeGreaterThanOrEqual(4.5);
  });
});
