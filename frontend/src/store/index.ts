/**
 * 状态管理向下兼容桥接层 (src/store/index.ts)
 * 职责：平滑桥接旧组件的 useAppStore() 访问到标准化 Pinia 架构 (src/stores/*)
 */

import type { AppSettings, BroadcastMode, CoachAdvice, ReminderCategoryKey } from '../types'
import {
  useBattleStore,
  useHeroStore,
  useItemStore,
  useAppStore as usePiniaAppStore,
} from '../stores'

export type AppState = ReturnType<typeof useAppStore>

/**
 * 向下兼容性 useAppStore Hook
 * 统一代理至底层模块化的 Pinia Stores，杜绝组件内部直接调用原生 API
 */
export function useAppStore() {
  const appStore = usePiniaAppStore()
  const battleStore = useBattleStore()
  const heroStore = useHeroStore()
  const itemStore = useItemStore()

  // 聚合响应式兼容对象
  const state = {
    get currentTab() {
      return appStore.currentTab
    },
    set currentTab(val) {
      appStore.currentTab = val
    },
    get searchQuery() {
      return appStore.searchQuery
    },
    set searchQuery(val) {
      appStore.searchQuery = val
    },
    get selectedHeroId() {
      return heroStore.selectedHeroId
    },
    set selectedHeroId(val) {
      heroStore.selectedHeroId = val ?? 1
    },
    get selectedItemId() {
      return itemStore.selectedItemId
    },
    set selectedItemId(val) {
      itemStore.selectedItemId = val
    },
    get gsiConnected() {
      return battleStore.gsiConnected
    },
    set gsiConnected(val) {
      battleStore.setGsiConnected(val)
    },
    get advices() {
      return battleStore.advices
    },
    set advices(val) {
      battleStore.advices = val
    },
    get settings() {
      return appStore.settings
    },
    get isOverlayOpen() {
      return appStore.isOverlayOpen
    },
    set isOverlayOpen(val) {
      appStore.isOverlayOpen = val
    },
    get isOverlayRunning() {
      return appStore.isOverlayRunning
    },
    set isOverlayRunning(val) {
      appStore.isOverlayRunning = val
    },
    get toast() {
      return appStore.toast
    },
  }

  return {
    state,
    setCurrentTab: (tab: 'heroes' | 'items' | 'coach' | 'analysis' | 'settings') =>
      appStore.setCurrentTab(tab),
    setSearchQuery: (query: string) => appStore.setSearchQuery(query),
    setSelectedHeroId: (id: number | null) => heroStore.setSelectedHeroId(id ?? 1),
    setSelectedItemId: (id: number | null) => itemStore.setSelectedItemId(id),
    setGsiConnected: (connected: boolean) => battleStore.setGsiConnected(connected),
    addAdvice: (advice: CoachAdvice) => battleStore.addAdvice(advice),
    clearAdvices: () => battleStore.clearAdvices(),
    updateSettings: (partial: Partial<AppSettings>) => appStore.updateSettings(partial),
    getReminderMode: (key: ReminderCategoryKey): BroadcastMode => appStore.getReminderMode(key),
    setReminderMode: (key: ReminderCategoryKey, mode: BroadcastMode) =>
      appStore.setReminderMode(key, mode),
    setAllReminderModes: (mode: BroadcastMode) => appStore.setAllReminderModes(mode),
    resetReminderModes: () => appStore.resetReminderModes(),
    applyBroadcastStrategy: (strategy: 'all_coach' | 'focus_ranked' | 'silent_hud') =>
      appStore.applyBroadcastStrategy(strategy),
    currentBroadcastStrategy: appStore.currentBroadcastStrategy,
    setIsOverlayOpen: (open: boolean) => appStore.setIsOverlayOpen(open),
    syncCacheDirectory: () => appStore.syncCacheDirectory(),
    updateCacheDirectory: (newDir: string) => appStore.updateCacheDirectory(newDir),
    openCacheDirectory: () => appStore.openCacheDirectory(),
    clearCacheDirectory: () => appStore.clearCacheDirectory(),
    checkOverlayStatus: () => appStore.checkOverlayStatus(),
    toggleStandaloneOverlay: () => appStore.toggleStandaloneOverlay(),
    selectCacheFolder: () => appStore.selectCacheFolder(),
    selectModelFile: () => appStore.selectModelFile(),
    showToast: (
      message: string,
      type: 'info' | 'success' | 'warning' | 'error' = 'info',
      durationMs: number = 3200,
    ) => appStore.showToast(message, type, durationMs),
    hideToast: () => appStore.hideToast(),
  }
}
