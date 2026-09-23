/**
 * 系统文件与系统原生交互 API 模块 (src/api/system.ts)
 * 封装目录管理、原生对话框、本地 GGUF 模型扫描、第三方 TTS 连通测试等
 * 遵循 Wails v3 前后端绑定唯一事实来源铁律：统一走 callGo 进行强类型直调与平滑降级
 */

import type { DiskModelFile } from './types'
import * as AppBindings from '../../bindings/aegis-coach/app'
import { callGo, isCancelError } from './client'

export { isCancelError }

export const systemApi = {
  /**
   * 获取系统权威推荐的默认存储目录
   */
  async getDefaultCacheDirectory(): Promise<string> {
    return callGo(
      () => AppBindings.GetDefaultCacheDirectory(),
      ['main.App.GetDefaultCacheDirectory', 'services.SystemService.GetDefaultCacheDirectory'],
    )
  },

  /**
   * 获取当前生效的存储目录
   */
  async getCacheDirectory(): Promise<string> {
    return callGo(
      () => AppBindings.GetCacheDirectory(),
      ['main.App.GetCacheDirectory', 'services.SystemService.GetCacheDirectory'],
    )
  },

  /**
   * 设置自定义存储目录
   */
  async setCacheDirectory(customDir: string): Promise<string> {
    return callGo(
      () => AppBindings.SetCacheDirectory(customDir),
      ['main.App.SetCacheDirectory', 'services.SystemService.SetCacheDirectory'],
      customDir,
    )
  },

  /**
   * 确保目录及子文件夹层级完整性
   */
  async ensureCacheDirectories(customDir: string = ''): Promise<string> {
    return callGo(
      () => AppBindings.EnsureCacheDirectories(customDir),
      ['main.App.EnsureCacheDirectories', 'services.SystemService.EnsureCacheDirectories'],
      customDir,
    )
  },

  /**
   * 弹出系统原生选择文件夹对话框 (优先使用 Wails v3 官方 Dialogs.OpenFile，平滑回退至后端 IPC)
   * 遵循单一职责原则：仅负责唤起弹窗并返回用户选中的物理路径
   */
  async selectDirectoryDialog(): Promise<string> {
    const win = typeof window !== 'undefined' ? (window as any) : null

    // 1. 优先尝试 Wails v3 官方前端原生 Dialogs.OpenFile 协议
    if (win?.wails?.Dialogs?.OpenFile) {
      try {
        const currentDir = await this.getCacheDirectory().catch(() => '')
        const res = await win.wails.Dialogs.OpenFile({
          CanChooseDirectories: true,
          CanChooseFiles: false,
          AllowsMultipleSelection: false,
          Title: '选择神盾教练数据缓存文件夹',
          Directory: currentDir || '',
        })
        const selected = Array.isArray(res) ? res[0] : res
        if (selected && typeof selected === 'string' && selected.trim()) {
          return selected.trim()
        }
        return ''
      } catch (dialogErr: unknown) {
        if (isCancelError(dialogErr)) {
          return ''
        }
        console.warn('[systemApi] Wails 原生前端 Dialogs.OpenFile 异常，尝试后端 IPC 降级:', dialogErr)
      }
    }

    // 2. 统一走 callGo 进行强类型绑定或通用 IPC 寻址
    try {
      const res = await callGo(
        () => AppBindings.SelectDirectoryDialog(),
        ['main.App.SelectDirectoryDialog', 'services.SystemService.SelectDirectoryDialog'],
      )
      if (res && typeof res === 'string' && res.trim()) {
        return res.trim()
      }
      return ''
    } catch (ipcErr: unknown) {
      if (isCancelError(ipcErr)) {
        return ''
      }
      console.warn('[systemApi] 调用 SelectDirectoryDialog 失败:', ipcErr)
    }

    // 3. 浏览器开发预览环境降级
    if (typeof window !== 'undefined' && (window as any).showDirectoryPicker) {
      try {
        await (window as any).showDirectoryPicker()
        throw new Error('浏览器安全沙箱限制无法获取本地绝对物理路径，请在桌面端使用')
      } catch (err: unknown) {
        if (isCancelError(err)) return ''
        throw err
      }
    }
    return ''
  },

  /**
   * 弹出系统原生选择模型文件对话框 (优先使用 Wails v3 官方 Dialogs.OpenFile)
   */
  async selectModelFileDialog(): Promise<string> {
    const win = typeof window !== 'undefined' ? (window as any) : null

    // 1. 优先尝试 Wails v3 官方前端原生 Dialogs.OpenFile 协议
    if (win?.wails?.Dialogs?.OpenFile) {
      try {
        const currentDir = await this.getCacheDirectory().catch(() => '')
        const res = await win.wails.Dialogs.OpenFile({
          CanChooseDirectories: false,
          CanChooseFiles: true,
          AllowsMultipleSelection: false,
          Title: '选择本地 AI 模型文件 (支持 GGUF / ONNX / Bin)',
          Directory: currentDir || '',
          Filters: [
            { DisplayName: 'AI 模型文件 (*.gguf;*.onnx;*.bin)', Pattern: '*.gguf;*.onnx;*.bin' },
            { DisplayName: '所有文件 (*.*)', Pattern: '*.*' },
          ],
        })
        const selected = Array.isArray(res) ? res[0] : res
        if (selected && typeof selected === 'string' && selected.trim()) {
          return selected.trim()
        }
        return ''
      } catch (dialogErr: unknown) {
        if (isCancelError(dialogErr)) {
          return ''
        }
        console.warn('[systemApi] Wails 原生前端 Dialogs.OpenFile 异常，尝试后端 IPC 降级:', dialogErr)
      }
    }

    // 2. 统一走 callGo 进行强类型绑定或通用 IPC 寻址
    try {
      const res = await callGo(
        () => AppBindings.SelectModelFileDialog(),
        ['main.App.SelectModelFileDialog', 'services.SystemService.SelectModelFileDialog'],
      )
      if (res && typeof res === 'string' && res.trim()) {
        return res.trim()
      }
      return ''
    } catch (ipcErr: unknown) {
      if (isCancelError(ipcErr)) {
        return ''
      }
      console.warn('[systemApi] 调用 SelectModelFileDialog 失败:', ipcErr)
    }

    // 3. 浏览器开发预览环境降级：支持 HTML 文件选择器
    if (typeof window !== 'undefined' && typeof document !== 'undefined') {
      return new Promise<string>((resolve) => {
        const input = document.createElement('input')
        input.type = 'file'
        input.accept = '.gguf,.onnx,.bin'
        input.onchange = () => {
          const file = input.files?.[0]
          resolve(file ? file.name : '')
        }
        input.oncancel = () => resolve('')
        input.click()
      })
    }
    return ''
  },

  /**
   * 扫描本地已下载的模型文件列表
   */
  async scanLocalModelFiles(): Promise<DiskModelFile[]> {
    const res = await callGo(
      () => AppBindings.ScanLocalModelFiles(),
      ['main.App.ScanLocalModelFiles', 'services.SystemService.ScanLocalModelFiles'],
    )
    return Array.isArray(res) ? res : []
  },

  /**
   * 在系统资源管理器中打开指定目录
   */
  async openCacheDirectory(dir: string): Promise<void> {
    const cleanDir = dir ? dir.trim() : ''
    await callGo(
      () => AppBindings.OpenCacheDirectory(cleanDir),
      ['main.App.OpenCacheDirectory', 'services.SystemService.OpenCacheDirectory'],
      cleanDir,
    )
  },

  /**
   * 清理指定目录下的缓存内容
   */
  async clearCacheDirectory(dir: string): Promise<void> {
    await callGo(
      () => AppBindings.ClearCacheDirectory(dir),
      ['main.App.ClearCacheDirectory', 'services.SystemService.ClearCacheDirectory'],
      dir,
    )
  },

  /**
   * 测试第三方 TTS 端点连通性
   */
  async testTtsEndpoint(
    endpoint: string,
    apiKey: string = '',
    engineType: string = 'openai',
    voice: string = '',
  ): Promise<string> {
    return callGo(
      () => AppBindings.TestTtsEndpoint(endpoint, apiKey, engineType, voice),
      ['main.App.TestTtsEndpoint', 'services.SystemService.TestTtsEndpoint'],
      endpoint,
      apiKey,
      engineType,
      voice,
    )
  },

  /**
   * 触发本地图标全量预热与缓存统计
   */
  async cacheAllIcons(): Promise<{ cached_heroes: number, cached_items: number, downloaded: number }> {
    return callGo(
      () => AppBindings.CacheAllIcons(),
      ['main.App.CacheAllIcons', 'services.SystemService.CacheAllIcons'],
    )
  },

  /**
   * 从本地缓存文件夹中优先读取结构化游戏数据
   */
  async loadCachedGameData(dataType: string): Promise<string> {
    return callGo(
      () => AppBindings.LoadCachedGameData(dataType),
      ['main.App.LoadCachedGameData', 'services.SystemService.LoadCachedGameData'],
      dataType,
    )
  },

  /**
   * 将官方/OpenDota拉取的最新完整结构化数据持久化至本地缓存目录
   */
  async saveCachedGameData(dataType: string, content: string): Promise<void> {
    await callGo(
      () => AppBindings.SaveCachedGameData(dataType, content),
      ['main.App.SaveCachedGameData', 'services.SystemService.SaveCachedGameData'],
      dataType,
      content,
    )
  },

  /**
   * 优先从设置文件夹内读取应用配置，若不存在或损坏则自动新建并返回默认配置
   */
  async loadSettings(): Promise<string> {
    return callGo(
      () => AppBindings.LoadSettings(),
      ['main.App.LoadSettings', 'services.SystemService.LoadSettings'],
    )
  },

  /**
   * 将应用最新完整配置安全原子化落盘持久化至设置文件夹 settings.json
   */
  async saveSettings(settingsJson: string): Promise<void> {
    await callGo(
      () => AppBindings.SaveSettings(settingsJson),
      ['main.App.SaveSettings', 'services.SystemService.SaveSettings'],
      settingsJson,
    )
  },

  /**
   * 从后端原生抓取 Valve 官方最新版本号与详细更新日志并自动缓存落盘
   */
  async fetchLatestPatchInfo(): Promise<string> {
    return callGo(
      () => AppBindings.FetchLatestPatchInfo(),
      ['main.App.FetchLatestPatchInfo', 'services.SystemService.FetchLatestPatchInfo'],
    )
  },
}
