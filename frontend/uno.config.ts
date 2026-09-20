import presetRemToPx from '@unocss/preset-rem-to-px'
import {
  defineConfig,
  presetWind4,
} from 'unocss'

export default defineConfig({
  content: {
    filesystem: [
      'src/**/*.{vue,js,ts}',
    ],
    pipeline: {
      exclude: [
        'src/utils/wechat-emoji-parser.ts',
        'src/utils/chat-export.ts',
      ],
    },
  },
  presets: [
    presetWind4(),
    presetRemToPx(),
  ],

})
