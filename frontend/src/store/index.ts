import type { AppSettings, BroadcastMode, CoachAdvice, ReminderCategoryKey } from '@/types'
import { reactive } from 'vue'
import { DEFAULT_REMINDER_MODES } from '../data/reminderCategories'

/**
 * 全局应用响应式状态实体
 */
export interface AppState {
  /** 当前主视图激活 Tab ('heroes' | 'items' | 'coach' | 'analysis' | 'settings') */
  currentTab: 'heroes' | 'items' | 'coach' | 'analysis' | 'settings'
  /** 顶部全局搜索栏输入的过滤关键字 */
  searchQuery: string
  /** 英雄图鉴当前高亮选中的英雄数字 ID (1 为敌法师) */
  selectedHeroId: number | null
  /** 物品图鉴当前高亮选中的物品数字 ID */
  selectedItemId: number | null
  /** 是否已与 Valve GSI 本机 4444 端口建立实时数据通信 */
  gsiConnected: boolean
  /** 实时战术建议流列表 (上限 50 条先进先出) */
  advices: CoachAdvice[]
  /** 系统全局持久化配置实体 */
  settings: AppSettings
  /** 兼容模式: 内嵌悬浮窗开关 */
  isOverlayOpen: boolean
  /** 独立置顶桌面悬浮窗进程是否正在后台运行 */
  isOverlayRunning: boolean
  /** 全局轻量 Toast 浮动消息状态 */
  toast: {
    message: string
    type: 'info' | 'success' | 'warning' | 'error'
    visible: boolean
  }
}

/** 本地 localStorage 存储配置的唯一键名 */
const SETTINGS_STORAGE_KEY = 'aegis_coach_settings_v2'

/**
 * 从本地持久化存储中读取用户配置
 * @returns 浅合并的局部配置对象
 */
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

/**
 * 将全局设置落盘写入本地 localStorage
 * @param settings 当前全量配置对象
 */
function persistSettings(settings: AppSettings) {
  if (typeof window === 'undefined' || !window.localStorage) return
  try {
    localStorage.setItem(SETTINGS_STORAGE_KEY, JSON.stringify(settings))
  } catch (err) {
    console.warn('保存本地配置失败:', err)
  }
}

const initialSaved = loadPersistedSettings()

/**
 * 全局共享的单例应用状态
 */
const state = reactive<AppState>({
  currentTab: 'heroes',
  searchQuery: '',
  selectedHeroId: 1, // 默认选中 1 敌法师
  selectedItemId: null,
  gsiConnected: false,
  advices: [],
  settings: {
    gsi_port: 4444,
    auto_start_gsi: true,
    smart_coach_enabled: true,
    voice_broadcast_mode: 2, // 2: 播报-语音
    voice_volume: 100,
    voice_speed: 1.0,
    voice_name: 'zh-CN-YunxiNeural',
    edge_voice_name: 'zh-CN-YunxiNeural',
    voice_lexicon_enabled: true,
    voice_ssml_enabled: true,
    auto_show_overlay: false,
    auto_overlay_on_game_start: true,
    cache_directory: '',
    // 绑定 Dota 2 玩家账号
    dota_player_id: '',
    dota_player_name: '',
    dota_player_avatar: '',
    // 第三方 TTS 配置
    custom_tts_enabled: false,
    custom_tts_engine: 'edge',
    custom_tts_endpoint: 'http://127.0.0.1:9233',
    custom_tts_api_key: '',
    custom_tts_voice: '',
    // AI 推理引擎默认配置 (端侧真实开源 Qwen 2.5 优先)
    ai_mode: 'local_npu',
    npu_endpoint: 'http://127.0.0.1:11434/v1',
    npu_model: 'qwen2.5:7b',
    cloud_api_key: '',
    cloud_base_url: 'https://api.deepseek.com/v1',
    cloud_model: 'deepseek-chat',
    user_context: '',
    ...initialSaved,
    // 确保 reminder_modes 包含新键且不被覆盖为空
    reminder_modes: {
      ...DEFAULT_REMINDER_MODES,
      ...(initialSaved.reminder_modes || {}),
    },
  },
  isOverlayOpen: false,
  isOverlayRunning: false,
  toast: {
    message: '',
    type: 'info',
    visible: false,
  },
})

let toastTimer: any = null

/**
 * 全局应用状态管理 Hook (Pinia 风格原生 Composition API)
 * 提供状态变更、提醒模式分级控制、原生后端桥接联动与持久化方法
 */
export function useAppStore() {
  /** 切换导航 Tab 视图 */
  const setCurrentTab = (tab: AppState['currentTab']) => {
    state.currentTab = tab
  }

  /** 更新全局搜索关键词 */
  const setSearchQuery = (query: string) => {
    state.searchQuery = query
  }

  /** 设置当前选中的英雄 ID (联动英雄展台与传记面板) */
  const setSelectedHeroId = (id: number | null) => {
    state.selectedHeroId = id
  }

  /** 设置当前选中的物品 ID */
  const setSelectedItemId = (id: number | null) => {
    state.selectedItemId = id
  }

  /** 更新 GSI 实时连接在线状态 */
  const setGsiConnected = (connected: boolean) => {
    state.gsiConnected = connected
  }

  /** 向战术卡片建议流新增一条提醒 (上限保留最新 50 条) */
  const addAdvice = (advice: CoachAdvice) => {
    state.advices = [advice, ...state.advices].slice(0, 50)
  }

  /** 清空所有战术卡片流水 */
  const clearAdvices = () => {
    state.advices = []
  }

  /** 更新系统配置并自动持久化写入本地存储 */
  const updateSettings = (partial: Partial<AppSettings>) => {
    Object.assign(state.settings, partial)
    persistSettings(state.settings)
  }

  /** 查询指定提醒分类的播报模式 (0: 关闭, 1: 仅文字, 2: 语音+文字) */
  const getReminderMode = (key: ReminderCategoryKey): BroadcastMode => {
    const mode = state.settings.reminder_modes[key]
    if (mode !== undefined) {
      return mode
    }
    return DEFAULT_REMINDER_MODES[key] ?? 2
  }

  /** 设置指定分类的播报模式并持久化 */
  const setReminderMode = (key: ReminderCategoryKey, mode: BroadcastMode) => {
    state.settings.reminder_modes[key] = mode
    persistSettings(state.settings)
  }

  /** 一键将所有提醒分类统一设为指定播报模式 */
  const setAllReminderModes = (mode: BroadcastMode) => {
    const keys = Object.keys(DEFAULT_REMINDER_MODES) as ReminderCategoryKey[]
    for (const k of keys) {
      state.settings.reminder_modes[k] = mode
    }
    persistSettings(state.settings)
  }

  /** 恢复所有提醒分类为出厂默认播报模式 */
  const resetReminderModes = () => {
    state.settings.reminder_modes = { ...DEFAULT_REMINDER_MODES }
    persistSettings(state.settings)
  }

  /** 设置内嵌悬浮窗开关 */
  const setIsOverlayOpen = (open: boolean) => {
    state.isOverlayOpen = open
  }

  /**
   * 与 Go 后端双向同步缓存路径
   * 若前端未设定，主动查询 Go 端默认 documents/AegisCoach 路径；若前端已配置，则向 Go 端同步
   */
  const syncCacheDirectory = async () => {
    try {
      const appApi = (window as any).go?.main?.App
      if (appApi?.GetCacheDirectory) {
        if (!state.settings.cache_directory) {
          const dir = await appApi.GetCacheDirectory()
          if (dir) {
            state.settings.cache_directory = dir
            persistSettings(state.settings)
          }
        } else {
          await appApi.SetCacheDirectory(state.settings.cache_directory)
        }
      }
    } catch (e) {
      console.warn('同步缓存目录失败:', e)
    }
  }

  /**
   * 更新自定义缓存存储目录并通知 Go 后端
   * @param newDir 用户指定的文件目录路径
   */
  const updateCacheDirectory = async (newDir: string) => {
    state.settings.cache_directory = newDir
    persistSettings(state.settings)
    try {
      const appApi = (window as any).go?.main?.App
      if (appApi?.SetCacheDirectory) {
        const applied = await appApi.SetCacheDirectory(newDir)
        if (applied) {
          state.settings.cache_directory = applied
        }
      }
    } catch (e) {
      console.warn('更新缓存目录失败:', e)
    }
  }

  /**
   * 调用 Go 原生系统能力在 Windows 资源管理器中打开当前缓存文件夹
   */
  const openCacheDirectory = async () => {
    try {
      const appApi = (window as any).go?.main?.App
      if (appApi?.OpenCacheDirectory) {
        await appApi.OpenCacheDirectory(state.settings.cache_directory || '')
      }
    } catch (e) {
      console.warn('打开缓存目录失败:', e)
    }
  }

  /**
   * 调用 Go 原生系统能力安全清理缓存文件夹中的临时数据与日志
   */
  const clearCacheDirectory = async () => {
    try {
      const appApi = (window as any).go?.main?.App
      if (appApi?.ClearCacheDirectory) {
        await appApi.ClearCacheDirectory(state.settings.cache_directory || '')
      }
    } catch (e) {
      console.warn('清空缓存目录失败:', e)
    }
  }

  /**
   * 检查独立置顶悬浮窗进程是否存活运行中
   * @returns 是否正在运行
   */
  const checkOverlayStatus = async (): Promise<boolean> => {
    try {
      const appApi = (window as any).go?.main?.App
      if (appApi?.IsOverlayRunning) {
        const running = await appApi.IsOverlayRunning()
        state.isOverlayRunning = Boolean(running)
        return Boolean(running)
      }
    } catch (e) {
      console.warn('检查悬浮窗运行状态失败:', e)
    }
    return false
  }

  /**
   * 切换独立置顶经济与战术悬浮窗开关
   * 原生 Wails 环境优先通过 Go 端调控原生置顶独立小窗；
   * 纯前端或无原生环境时无缝切换内嵌可拖拽置顶悬浮窗
   * @returns 切换后的最新运行状态
   */
  const toggleStandaloneOverlay = async (): Promise<boolean> => {
    try {
      const appApi = (window as any).go?.main?.App
      if (appApi?.ToggleOverlayWindow) {
        const newState = await appApi.ToggleOverlayWindow()
        if (newState !== null && newState !== undefined) {
          state.isOverlayRunning = Boolean(newState)
          return Boolean(newState)
        }
      }
    } catch (e) {
      console.warn('切换原生独立悬浮窗异常，回退至内嵌悬浮窗:', e)
    }

    // 纯前端或原生桥接未响应时的自愈式回退：切换内嵌浮窗
    state.isOverlayOpen = !state.isOverlayOpen
    state.isOverlayRunning = state.isOverlayOpen
    return state.isOverlayOpen
  }

  /**
   * 唤起操作系统原生文件选择对话框供用户挑选本地 AI 模型文件 (.gguf, .onnx, .bin)
   * @returns 用户选中的模型文件绝对路径，若取消返回 null
   */
  const selectModelFile = async (): Promise<string | null> => {
    try {
      const appApi = (window as any).go?.main?.App
      if (appApi?.SelectModelFileDialog) {
        const selected = await appApi.SelectModelFileDialog()
        if (selected && selected.trim()) {
          state.settings.local_model_path = selected.trim()
          // 提取纯文件名作为模型展示名
          const fileName = selected.trim().split(/[\\/]/).pop() || selected.trim()
          state.settings.npu_model = fileName
          persistSettings(state.settings)
          return selected.trim()
        }
      }
    } catch (e) {
      console.warn('选择本地模型文件失败:', e)
    }
    return null
  }

  /**
   * 唤起操作系统原生目录选择对话框 (Windows 文件夹拾取器)
   * @returns 用户选中的绝对文件夹路径，若取消返回 null
   */
  const selectCacheFolder = async (): Promise<string | null> => {
    try {
      const appApi = (window as any).go?.main?.App
      if (appApi?.SelectDirectoryDialog) {
        const selected = await appApi.SelectDirectoryDialog()
        if (selected) {
          state.settings.cache_directory = selected
          persistSettings(state.settings)
          return selected
        }
      }
    } catch (e) {
      console.warn('调用原生选择文件夹对话框失败:', e)
    }
    return null
  }

  /**
   * 显示全局轻量 Toast 提示
   * @param message 提示文本
   * @param type 消息类型 ('info' | 'success' | 'warning' | 'error')
   * @param durationMs 显示时长 (毫秒，默认 3200ms)
   */
  const showToast = (
    message: string,
    type: 'info' | 'success' | 'warning' | 'error' = 'info',
    durationMs: number = 3200,
  ) => {
    if (toastTimer) clearTimeout(toastTimer)
    state.toast.message = message
    state.toast.type = type
    state.toast.visible = true
    toastTimer = setTimeout(() => {
      state.toast.visible = false
    }, durationMs)
  }

  /**
   * 即时关闭当前 Toast 提示
   */
  const hideToast = () => {
    if (toastTimer) clearTimeout(toastTimer)
    state.toast.visible = false
  }

  return {
    state,
    setCurrentTab,
    setSearchQuery,
    setSelectedHeroId,
    setSelectedItemId,
    setGsiConnected,
    addAdvice,
    clearAdvices,
    updateSettings,
    getReminderMode,
    setReminderMode,
    setAllReminderModes,
    resetReminderModes,
    setIsOverlayOpen,
    syncCacheDirectory,
    updateCacheDirectory,
    openCacheDirectory,
    clearCacheDirectory,
    checkOverlayStatus,
    toggleStandaloneOverlay,
    selectCacheFolder,
    selectModelFile,
    showToast,
    hideToast,
  }
}
