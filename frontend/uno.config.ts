import {
  defineConfig,
  presetWind4,
} from 'unocss'
import { createRemToPxProcessor } from '@unocss/preset-wind4/utils'
import type { Theme } from '@unocss/preset-wind4/theme'

export default defineConfig({
  content: {
    filesystem: [
      'src/**/*.{vue,js,ts,html}',
    ],
  },
  presets: [
    presetWind4({
      preflights: {
        theme: {
          process: createRemToPxProcessor(),
        },
      },
    }),
  ],
  postprocess: [createRemToPxProcessor()],
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
    font: {
      base: `-apple-system, BlinkMacSystemFont, 'Segoe UI Variable', 'Segoe UI', 'Segoe UI Emoji', 'Segoe UI Symbol', 'Apple Color Emoji', 'Noto Color Emoji', 'Microsoft YaHei UI', 'Microsoft YaHei', 'PingFang SC', 'Hiragino Sans GB', 'Noto Sans CJK SC', 'Source Han Sans SC', 'Malgun Gothic', 'Meiryo', 'Yu Gothic UI', Roboto, 'Helvetica Neue', Arial, sans-serif`,
      mono: `ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace`,
    },
  } satisfies Theme,
  shortcuts: {
    'dota-panel': 'bg-dota-panel border border-dota-border rounded',
    'dota-card': 'bg-dota-card border border-dota-border rounded',
    'dota-hud-card': 'bg-dota-surface/90 backdrop-blur-sm border border-dota-border/80 shadow-lg',
  },
})
