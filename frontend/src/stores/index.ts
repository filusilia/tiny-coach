/**
 * Pinia 全局状态中枢与 Stores 统一导出 (src/stores/index.ts)
 */

import { createPinia } from 'pinia'
import { useAppStore } from './app'
import { useBattleStore } from './battle'
import { useHeroStore } from './hero'
import { useItemStore } from './item'
import { usePlayerStore } from './player'

export const pinia = createPinia()

export {
  useAppStore,
  useBattleStore,
  useHeroStore,
  useItemStore,
  usePlayerStore,
}
