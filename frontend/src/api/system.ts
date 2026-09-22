/**
 * 系统文件与系统原生交互 API 模块 (src/api/system.ts)
 * 封装目录管理、原生对话框、本地 GGUF 模型扫描、第三方 TTS 连通测试等
 */

import type { DiskModelFile } from './types'
import { invokeWails } from './client'

/**
 * 校验异常是否属于用户主动取消原生对话框操作
 */
export function isCancelError(e: unknown): boolean {
  if (!e) return false
  const anyErr = e as any
  const msg = String(anyErr?.message || e || '').toLowerCase()
  return (
    anyErr?.name === 'AbortError'
    || anyErr?.code === 1223
    || anyErr?.code === -2147023673
    || anyErr?.code === '0x800704c7'
    || /cancel|取消|0x800704c7|abort/i.test(msg)
  )
}

export const systemApi = {
  /**
   * 获取系统权威推荐的默认存储目录
   */
  async getDefaultCacheDirectory(): Promise<string> {
    return invokeWails<string>([
      'main.App.GetDefaultCacheDirectory',
      'services.SystemService.GetDefaultCacheDirectory',
      'app.App.GetDefaultCacheDirectory',
    ])
  },

  /**
   * 获取当前生效的存储目录
   */
  async getCacheDirectory(): Promise<string> {
    return invokeWails<string>([
      'main.App.GetCacheDirectory',
      'services.SystemService.GetCacheDirectory',
      'app.App.GetCacheDirectory',
    ])
  },

  /**
   * 设置自定义存储目录
   */
  async setCacheDirectory(customDir: string): Promise<string> {
    return invokeWails<string>(
      [
        'main.App.SetCacheDirectory',
        'services.SystemService.SetCacheDirectory',
        'app.App.SetCacheDirectory',
      ],
      customDir,
    )
  },

  /**
   * 确保目录及子文件夹层级完整性
   */
  async ensureCacheDirectories(customDir: string = ''): Promise<string> {
    return invokeWails<string>(
      [
        'main.App.EnsureCacheDirectories',
        'services.SystemService.EnsureCacheDirectories',
        'app.App.EnsureCacheDirectories',
      ],
      customDir,
    )
  },

  /**
   * 弹出系统原生选择文件夹对话框 (优先使用 Wails v3 官方 Dialogs.OpenFile，平滑回退至后端 IPC)
   * 遵循单一职责原则：仅负责唤起弹窗并返回用户选中的物理路径，绝不在此掺杂落盘或修改状态副作用
   * 参考官方规范: https://v3.wails.io/zh-cn/features/dialogs/file/
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
        if (selected === '' || (Array.isArray(res) && res.length === 0)) {
          return ''
        }
      } catch (dialogErr: unknown) {
        if (isCancelError(dialogErr)) {
          return ''
        }
        console.warn('[systemApi] Wails 原生前端 Dialogs.OpenFile 异常，尝试后端 IPC 降级:', dialogErr)
      }
    }

    // 2. 降级通过 Go 后端 IPC 绑定方法调用
    try {
      const res = await invokeWails<string>([
        'main.App.SelectDirectoryDialog',
        'services.SystemService.SelectDirectoryDialog',
        'aegis-coach/internal/services.SystemService.SelectDirectoryDialog',
        'app.App.SelectDirectoryDialog',
      ])
      if (res && typeof res === 'string' && res.trim()) {
        return res.trim()
      }
      return ''
    } catch (ipcErr: unknown) {
      if (isCancelError(ipcErr)) {
        return ''
      }
      console.warn('[systemApi] 调用后端 SelectDirectoryDialog 失败，尝试下一级降级:', ipcErr)
      // 注意：严禁在此处 throw ipcErr，必须保持多级降级通道连贯性
    }

    // 3. 浏览器开发预览环境降级：检测沙箱限制并友好提示，严格遵循零伪造原则
    if (typeof window !== 'undefined' && (window as any).showDirectoryPicker) {
      try {
        await (window as any).showDirectoryPicker()
        throw new Error('浏览器安全沙箱限制无法获取本地绝对物理路径，请在桌面端使用')
      } catch (err: unknown) {
        if (isCancelError(err)) return ''
        console.warn('[systemApi] 浏览器选择目录受限:', err)
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
        if (selected === '' || (Array.isArray(res) && res.length === 0)) {
          return ''
        }
      } catch (dialogErr: unknown) {
        if (isCancelError(dialogErr)) {
          return ''
        }
        console.warn('[systemApi] Wails 原生前端 Dialogs.OpenFile 异常，尝试后端 IPC 降级:', dialogErr)
      }
    }

    // 2. 降级通过 Go 后端 IPC 绑定方法调用
    try {
      const res = await invokeWails<string>([
        'main.App.SelectModelFileDialog',
        'services.SystemService.SelectModelFileDialog',
        'aegis-coach/internal/services.SystemService.SelectModelFileDialog',
        'app.App.SelectModelFileDialog',
      ])
      if (res && typeof res === 'string' && res.trim()) {
        return res.trim()
      }
      return ''
    } catch (ipcErr: unknown) {
      if (isCancelError(ipcErr)) {
        return ''
      }
      console.warn('[systemApi] 调用后端 SelectModelFileDialog 失败，尝试下一级降级:', ipcErr)
      // 注意：严禁在此处 throw ipcErr，保持降级通道连贯性
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
    const res = await invokeWails<DiskModelFile[]>([
      'main.App.ScanLocalModelFiles',
      'services.SystemService.ScanLocalModelFiles',
      'app.App.ScanLocalModelFiles',
    ])
    return Array.isArray(res) ? res : []
  },

  /**
   * 在系统资源管理器中打开指定目录
   */
  async openCacheDirectory(dir: string): Promise<void> {
    await invokeWails<void>(
      [
        'main.App.OpenCacheDirectory',
        'aegis-coach/internal/services.SystemService.OpenCacheDirectory',
        'services.SystemService.OpenCacheDirectory',
        'app.App.OpenCacheDirectory',
      ],
      dir,
    )
  },

  /**
   * 清理指定目录下的缓存内容
   */
  async clearCacheDirectory(dir: string): Promise<void> {
    await invokeWails<void>(
      [
        'main.App.ClearCacheDirectory',
        'services.SystemService.ClearCacheDirectory',
        'app.App.ClearCacheDirectory',
      ],
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
    return invokeWails<string>(
      [
        'main.App.TestTtsEndpoint',
        'aegis-coach/internal/services.SystemService.TestTtsEndpoint',
        'services.SystemService.TestTtsEndpoint',
      ],
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
    return invokeWails<{ cached_heroes: number, cached_items: number, downloaded: number }>([
      'main.App.CacheAllIcons',
      'aegis-coach/internal/services.SystemService.CacheAllIcons',
      'services.SystemService.CacheAllIcons',
    ])
  },
}
