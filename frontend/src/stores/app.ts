/**
 * 客户端全局应用状态 Store (src/stores/app.ts)
 * 职责：
 * 1. 管理客户端全局 UI 状态 (Tab, 搜索关键词, Toast)
 * 2. 管理客户端持久化配置 AppSettings 与策略预设
 * 3. 衔接 systemApi 与 overlayApi，实现原生弹框与悬浮窗控制
 */

import type { AppSettings, BroadcastMode, ReminderCategoryKey } from '@/types'
import { defineStore } from 'pinia'
import { computed, reactive, ref } from 'vue'
import { overlayApi, systemApi } from '@/api'
import { DEFAULT_REMINDER_MODES } from '@/data/reminderCategories.ts'

const SETTINGS_STORAGE_KEY = 'aegis_coach_settings_v2'

function loadPersistedSettings(): Partial<AppSettings> {
  if (typeof window === 'undefined' || !window.localStorage) return {}
  try {
    const raw = localStorage.getItem(SETTINGS_STORAGE_KEY)
    if (!raw) return {}
    return JSON.parse(raw)
  } catch (err) {
    console.warn('读取本地配置失败，采用默认值:', err)
    return {}
  }
}

function persistSettings(settings: AppSettings) {
  if (typeof window !== 'undefined' && window.localStorage) {
    try {
      localStorage.setItem(SETTINGS_STORAGE_KEY, JSON.stringify(settings))
    } catch (err) {
      console.warn('保存本地配置失败:', err)
    }
  }
  // 同步原子化持久化落盘至设置文件夹的 settings.json
  try {
    systemApi.saveSettings(JSON.stringify(settings)).catch(() => {})
  } catch {
    // 忽略未就绪调用
  }
}

export const useAppStore = defineStore('app', () => {
  /** 当前主视图激活 Tab */
  const currentTab = ref<'heroes' | 'items' | 'coach' | 'analysis' | 'settings'>('heroes')

  /** 顶部全局搜索栏输入的过滤关键字 */
  const searchQuery = ref<string>('')

  /** 内嵌悬浮窗展示状态 */
  const isOverlayOpen = ref<boolean>(false)

  /** 独立置顶桌面悬浮窗子进程运行状态 */
  const isOverlayRunning = ref<boolean>(false)

  /** 全局轻量 Toast 状态 */
  const toast = reactive<{
    message: string
    type: 'info' | 'success' | 'warning' | 'error'
    visible: boolean
  }>({
    message: '',
    type: 'info',
    visible: false,
  })

  let toastTimer: ReturnType<typeof setTimeout> | null = null

  const initialSaved = loadPersistedSettings()
  let initialAiMode = initialSaved.ai_mode || 'response_api'
  if (initialAiMode === 'local_npu' || initialAiMode === 'cloud_api' || initialAiMode === 'npu' || initialAiMode === 'cloud') {
    initialAiMode = 'response_api'
  }
  const defaultEndpoint = initialSaved.ai_endpoint
    || (initialSaved.ai_mode === 'cloud_api' ? initialSaved.cloud_base_url : initialSaved.npu_endpoint)
    || 'http://127.0.0.1:11434/v1'
  const defaultApiKey = initialSaved.ai_api_key ?? initialSaved.cloud_api_key ?? ''
  const defaultModel = initialSaved.ai_model
    || (initialSaved.ai_mode === 'cloud_api' ? initialSaved.cloud_model : (initialSaved.npu_model || 'qwen2.5:1.5b'))
    || 'qwen2.5:1.5b'

  /** 系统全局持久化配置 */
  const settings = reactive<AppSettings>({
    gsi_port: 4444,
    auto_start_gsi: true,
    smart_coach_enabled: true,
    voice_broadcast_mode: 2,
    reminder_lead_time_seconds: 15,
    voice_volume: 100,
    voice_speed: 1.0,
    voice_name: 'MeloTTS 超保真拟人',
    voice_lexicon_enabled: true,
    voice_ssml_enabled: true,
    melo_auto_download: true,
    auto_show_overlay: false,
    auto_overlay_on_game_start: true,
    overlay_opacity: 90,
    hero_rank_tier: 7,
    hero_sort_mode: 'win_desc',
    cache_directory: '',
    dota_player_id: '',
    dota_player_name: '',
    dota_player_avatar: '',
    // 以下五项为历史别名字段的缺省兜底：initialSaved 中若不存在则由这里提供默认值，
    // 若存在则由紧随其后的 ...initialSaved 展开覆盖（故不可删除）。
    npu_endpoint: defaultEndpoint,
    npu_model: defaultModel,
    cloud_api_key: defaultApiKey,
    cloud_base_url: defaultEndpoint,
    cloud_model: defaultModel,
    user_context: '',
    ...initialSaved,
    // 以下四项必须置于 initialSaved 展开之后：initialSaved 中是原始落盘值（可能含
    // local_npu/npu/cloud_api 等历史别名），而 initialAiMode/defaultEndpoint/defaultApiKey/
    // defaultModel 在上面已完成别名归一化与缺省兜底，此处用归一化结果覆盖原始值。
    // 注意：不得在 ...initialSaved 之前重复声明这四项，否则构成对象字面量重复键（TS1117）。
    ai_mode: initialAiMode,
    ai_endpoint: defaultEndpoint,
    ai_api_key: defaultApiKey,
    ai_model: defaultModel,
    reminder_advance_seconds: {
      rune_control: 15,
      tormentor_tactics: 20,
      roshan_tactics: 30,
      ...(initialSaved.reminder_advance_seconds || {}),
    },
    reminder_modes: {
      ...DEFAULT_REMINDER_MODES,
      ...(initialSaved.reminder_modes || {}),
    },
  })

  function setCurrentTab(tab: 'heroes' | 'items' | 'coach' | 'analysis' | 'settings') {
    currentTab.value = tab
  }

  function setSearchQuery(query: string) {
    searchQuery.value = query
  }

  function setIsOverlayOpen(open: boolean) {
    isOverlayOpen.value = open
  }

  function setOverlayRunning(running: boolean) {
    isOverlayRunning.value = running
  }

  function updateSettings(partial: Partial<AppSettings>) {
    Object.assign(settings, partial)
    persistSettings(settings)
  }

  function getReminderMode(key: ReminderCategoryKey): BroadcastMode {
    const mode = settings.reminder_modes[key]
    if (mode !== undefined) return mode
    return DEFAULT_REMINDER_MODES[key] ?? 2
  }

  function setReminderMode(key: ReminderCategoryKey, mode: BroadcastMode) {
    settings.reminder_modes[key] = mode
    persistSettings(settings)
  }

  function setAllReminderModes(mode: BroadcastMode) {
    const keys = Object.keys(DEFAULT_REMINDER_MODES) as ReminderCategoryKey[]
    for (const k of keys) {
      settings.reminder_modes[k] = mode
    }
    persistSettings(settings)
  }

  function resetReminderModes() {
    settings.reminder_modes = { ...DEFAULT_REMINDER_MODES }
    persistSettings(settings)
  }

  function applyBroadcastStrategy(strategy: 'all_coach' | 'focus_ranked' | 'silent_hud') {
    if (strategy === 'all_coach') {
      setAllReminderModes(2)
    } else if (strategy === 'focus_ranked') {
      settings.reminder_modes = {
        macro_tempo: 1,
        roshan_tactics: 2,
        tormentor_tactics: 1,
        rune_control: 1,
        buyback_strategy: 2,
        item_timing_threat: 2,
        tactical_ai_ask: 1,
        ability_ready: 1,
        death_respawn: 1,
      }
      persistSettings(settings)
    } else if (strategy === 'silent_hud') {
      setAllReminderModes(1)
    }
  }

  const currentBroadcastStrategy = computed<'all_coach' | 'focus_ranked' | 'silent_hud' | 'custom'>(() => {
    const modes = settings.reminder_modes
    const isAllVoice = Object.values(modes).every(m => m === 2)
    if (isAllVoice) return 'all_coach'
    const isAllText = Object.values(modes).every(m => m === 1)
    if (isAllText) return 'silent_hud'
    if (
      modes.roshan_tactics === 2
      && modes.buyback_strategy === 2
      && modes.item_timing_threat === 2
      && modes.macro_tempo === 1
      && modes.tormentor_tactics === 1
      && modes.rune_control === 1
    ) {
      return 'focus_ranked'
    }
    return 'custom'
  })

  // 系统存储与原生对话框对接
  async function syncCacheDirectory() {
    try {
      if (!settings.cache_directory) {
        const dir = await systemApi.getCacheDirectory()
        if (dir) {
          settings.cache_directory = dir
          persistSettings(settings)
        }
      } else {
        await systemApi.setCacheDirectory(settings.cache_directory)
      }
    } catch (e) {
      console.warn('[AppStore] 同步缓存目录失败:', e)
    }
  }

  async function updateCacheDirectory(newDir: string) {
    settings.cache_directory = newDir
    persistSettings(settings)
    try {
      const applied = await systemApi.setCacheDirectory(newDir)
      if (applied) {
        settings.cache_directory = applied
      }
    } catch (e) {
      console.warn('[AppStore] 更新缓存目录失败:', e)
    }
  }

  async function openCacheDirectory() {
    let dir = settings.cache_directory
    if (!dir || !dir.trim()) {
      dir = await systemApi.getCacheDirectory().catch(() => '')
    }
    if (!dir || !dir.trim()) {
      throw new Error('尚未配置数据缓存目录')
    }
    await systemApi.openCacheDirectory(dir.trim())
  }

  async function clearCacheDirectory() {
    try {
      await systemApi.clearCacheDirectory(settings.cache_directory || '')
    } catch (e) {
      console.warn('[AppStore] 清空缓存目录失败:', e)
    }
  }

  async function selectCacheFolder(): Promise<string | null> {
    try {
      const selected = await systemApi.selectDirectoryDialog()
      if (selected && selected.trim()) {
        const cleanPath = selected.trim()
        settings.cache_directory = cleanPath
        persistSettings(settings)

        // 异步通知后端存储管理器生效并建构规范化子目录结构（安全隔离，非阻塞核心返回）
        try {
          const applied = await systemApi.setCacheDirectory(cleanPath)
          if (applied && applied.trim()) {
            settings.cache_directory = applied.trim()
            persistSettings(settings)
          }
        } catch (setErr) {
          console.warn('[AppStore] 同步后端存储根目录失败 (已维持前端有效设置):', setErr)
        }

        return settings.cache_directory
      }
      return null
    } catch (e) {
      console.warn('[AppStore] 调用原生选择文件夹失败:', e)
      throw e
    }
  }

  async function selectModelFile(): Promise<string | null> {
    try {
      const selected = await systemApi.selectModelFileDialog()
      if (selected && selected.trim()) {
        settings.local_model_path = selected.trim()
        const fileName = selected.trim().split(/[\\/]/).pop() || selected.trim()
        settings.npu_model = fileName
        persistSettings(settings)
        return selected.trim()
      }
      return null
    } catch (e) {
      console.warn('[AppStore] 选择本地模型文件失败:', e)
      throw e
    }
  }

  async function checkOverlayStatus(): Promise<boolean> {
    try {
      const running = await overlayApi.isOverlayRunning()
      isOverlayRunning.value = running
      return running
    } catch (e) {
      console.warn('[AppStore] 检查悬浮窗运行状态失败:', e)
    }
    return false
  }

  async function toggleStandaloneOverlay(): Promise<boolean> {
    try {
      const newState = await overlayApi.toggleOverlayWindow()
      isOverlayRunning.value = newState
      isOverlayOpen.value = newState
      showToast(newState ? '局内独立置顶悬浮窗已呼出' : '局内独立置顶悬浮窗已隐藏', 'info')
      // 延时二次校准，确保原生/子进程创建与按钮状态 100% 同步
      setTimeout(() => {
        checkOverlayStatus()
      }, 300)
      setTimeout(() => {
        checkOverlayStatus()
      }, 800)
      return newState
    } catch (e: any) {
      console.warn('[AppStore] 切换原生独立悬浮窗异常:', e)
      showToast(`唤起独立悬浮窗失败: ${e?.message || '系统限制'}`, 'error')
      return false
    }
  }

  async function toggleMouseThrough(): Promise<boolean> {
    try {
      const active = await overlayApi.toggleMouseThrough()
      showToast(active ? '悬浮窗鼠标穿透已开启 (HUD 纯观战，点击直通游戏)' : '悬浮窗鼠标穿透已关闭 (可正常拖拽点击)', 'info')
      return active
    } catch (e: any) {
      console.warn('[AppStore] 切换鼠标穿透异常:', e)
      return false
    }
  }

  async function resetOverlayPosition(): Promise<void> {
    try {
      await overlayApi.resetOverlayPosition()
      showToast('已将悬浮窗重置居中并清除历史位置记忆', 'success')
    } catch (e: any) {
      console.warn('[AppStore] 重置悬浮窗位置异常:', e)
      showToast('重置悬浮窗位置失败', 'error')
    }
  }

  function showToast(
    message: string,
    type: 'info' | 'success' | 'warning' | 'error' = 'info',
    durationMs: number = 3200,
  ) {
    if (toastTimer) clearTimeout(toastTimer)
    toast.message = message
    toast.type = type
    toast.visible = true
    toastTimer = setTimeout(() => {
      toast.visible = false
    }, durationMs)
  }

  function hideToast() {
    if (toastTimer) clearTimeout(toastTimer)
    toast.visible = false
  }

  /**
   * 遵循“先从设置文件夹内取设置，没有再新建”的权威生命周期
   */
  async function initSettings(): Promise<void> {
    try {
      const raw = await systemApi.loadSettings()
      if (raw && raw.trim()) {
        const parsed = JSON.parse(raw)
        if (parsed && typeof parsed === 'object') {
          Object.assign(settings, parsed)
          if (parsed.reminder_modes) {
            settings.reminder_modes = {
              ...DEFAULT_REMINDER_MODES,
              ...parsed.reminder_modes,
            }
          }
          if (typeof window !== 'undefined' && window.localStorage) {
            localStorage.setItem(SETTINGS_STORAGE_KEY, JSON.stringify(settings))
          }
          console.log('[AppStore] 成功从设置文件夹同步最新配置')
        }
      }
    } catch (e) {
      console.warn('[AppStore] 优先从设置文件夹读取配置失败，维持现有配置:', e)
    }
  }

  let settingsInitPromise: Promise<void> | null = null

  /**
   * 确保设置已从本地设置文件夹优先载入完成 (单例并发安全)
   */
  async function ensureSettingsLoaded(): Promise<void> {
    if (!settingsInitPromise) {
      settingsInitPromise = initSettings()
    }
    return settingsInitPromise
  }

  return {
    currentTab,
    searchQuery,
    isOverlayOpen,
    isOverlayRunning,
    toast,
    settings,
    initSettings,
    ensureSettingsLoaded,
    setCurrentTab,
    setSearchQuery,
    setIsOverlayOpen,
    setOverlayRunning,
    updateSettings,
    getReminderMode,
    setReminderMode,
    setAllReminderModes,
    resetReminderModes,
    applyBroadcastStrategy,
    currentBroadcastStrategy,
    syncCacheDirectory,
    updateCacheDirectory,
    openCacheDirectory,
    clearCacheDirectory,
    selectCacheFolder,
    selectModelFile,
    checkOverlayStatus,
    toggleStandaloneOverlay,
    toggleMouseThrough,
    resetOverlayPosition,
    showToast,
    hideToast,
  }
})
