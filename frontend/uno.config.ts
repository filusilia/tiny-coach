import presetRemToPx from '@unocss/preset-rem-to-px'
import {
  defineConfig,
  presetWind4,
} from 'unocss'

export default defineConfig({
  content: {
    filesystem: [
      'src/**/*.{vue,js,ts,html}',
    ],
  },
  presets: [
    presetWind4(),
    presetRemToPx({
      baseFontSize: 16,
    }),
  ],
  theme: {
    colors: {
      dota: {
        'bg': 'var(--color-dota-bg)',
        'surface': 'var(--color-dota-surface)',
        'panel': 'var(--color-dota-panel)',
        'card': 'var(--color-dota-card)',
        'hover': 'var(--color-dota-hover)',
        'border': 'var(--color-dota-border)',
        'borderLight': 'var(--color-dota-border-light)',
        'border-light': 'var(--color-dota-border-light)',
        'gold': 'var(--color-dota-gold)',
        'goldLight': 'var(--color-dota-gold-light)',
        'gold-light': 'var(--color-dota-gold-light)',
        'cyan': 'var(--color-dota-cyan)',
        'red': 'var(--color-dota-red)',
        'green': 'var(--color-dota-green)',
        'text': 'var(--color-dota-text)',
        'muted': 'var(--color-dota-muted)',
        'subtle': 'var(--color-dota-subtle)',
      },
      slate: {
        950: 'var(--color-slate-950)',
        900: 'var(--color-slate-900)',
        800: 'var(--color-slate-800)',
        700: 'var(--color-slate-700)',
        600: 'var(--color-slate-600)',
        500: 'var(--color-slate-500)',
        400: 'var(--color-slate-400)',
        300: 'var(--color-slate-300)',
        200: 'var(--color-slate-200)',
        100: 'var(--color-slate-100)',
        50: 'var(--color-slate-50)',
      },
    },
    fontSize: {
      '3xs': ['var(--font-size-3xs)', '1.15'],
      '2xs': ['var(--font-size-2xs)', '1.2'],
      'xs': ['var(--font-size-xs)', '1.35'],
      'sm': ['var(--font-size-sm)', '1.4'],
      'base': ['var(--font-size-base)', '1.5'],
      'md': ['var(--font-size-md)', '1.5'],
      'lg': ['var(--font-size-lg)', '1.4'],
      'xl': ['var(--font-size-xl)', '1.3'],
      '2xl': ['var(--font-size-2xl)', '1.2'],
      'main': ['var(--font-size-main)', '1.5'],
    },
    fontFamily: {
      base: 'var(--font-family-base)',
      mono: 'var(--font-family-mono)',
    },
  },
  rules: [
    [/^text-\[8px\]$/, () => ({ 'font-size': 'var(--font-size-3xs)' })],
    [/^text-\[9px\]$/, () => ({ 'font-size': 'var(--font-size-2xs)' })],
    [/^text-\[10px\]$/, () => ({ 'font-size': 'var(--font-size-xs)' })],
    [/^text-\[11px\]$/, () => ({ 'font-size': 'var(--font-size-xs)' })],
    [/^text-\[12px\]$/, () => ({ 'font-size': 'var(--font-size-sm)' })],
  ],
  shortcuts: {
    'dota-panel': 'bg-dota-panel border border-dota-border rounded',
    'dota-card': 'bg-dota-card border border-dota-border rounded',
    'dota-hud-card': 'bg-dota-surface/90 backdrop-blur-sm border border-dota-border/80 shadow-lg',
  },
})
