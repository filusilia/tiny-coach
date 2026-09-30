/**
 * 系统文件与系统原生交互 API 模块 (src/api/system.ts)
 *
 * 职责边界（铁律 4）：本层只负责调用后端绑定并返回结果。
 * - 不在本层做状态持久化（写入 settings 由 Pinia Store 负责）
 * - 不在本层做二次跳转调用（例如选择目录时不再内部调用 getCacheDirectory，
 *   起始目录已由 Go 侧 SystemService 依据当前缓存目录自行设定）
 * - 不做 DOM/浏览器原生交互，原生对话框唯一通道是 Go 侧 Wails Dialog API
 *
 * 绑定唯一事实来源（铁律 9）：直接 import 生成绑定并调用，不做任何方法名猜测。
 */

import type { DiskModelFile } from './types'
import * as AppBindings from '../../bindings/aegis-coach/app'

export const systemApi = {
  /**
   * 获取系统权威推荐的默认存储目录
   */
  async getDefaultCacheDirectory(): Promise<string> {
    return AppBindings.GetDefaultCacheDirectory()
  },

  /**
   * 获取当前生效的存储目录
   */
  async getCacheDirectory(): Promise<string> {
    return AppBindings.GetCacheDirectory()
  },

  /**
   * 设置自定义存储目录
   */
  async setCacheDirectory(customDir: string): Promise<string> {
    return AppBindings.SetCacheDirectory(customDir)
  },

  /**
   * 确保目录及子文件夹层级完整性
   */
  async ensureCacheDirectories(customDir: string = ''): Promise<string> {
    return AppBindings.EnsureCacheDirectories(customDir)
  },

  /**
   * 弹出系统原生选择文件夹对话框
   *
   * 仅走 Go 侧官方 Dialog API：用户取消由 Go 统一返回空字符串，本层据此返回 ''，
   * 不将取消误判为异常。原生对话框不可用时 Go 会返回错误，由 Store/UI 层呈现。
   * @returns 用户选中的绝对物理路径；用户取消时为空字符串
   */
  async selectDirectoryDialog(): Promise<string> {
    const selected = await AppBindings.SelectDirectoryDialog()
    return typeof selected === 'string' ? selected.trim() : ''
  },

  /**
   * 弹出系统原生选择模型文件对话框
   * @returns 用户选中的绝对物理路径；用户取消时为空字符串
   */
  async selectModelFileDialog(): Promise<string> {
    const selected = await AppBindings.SelectModelFileDialog()
    return typeof selected === 'string' ? selected.trim() : ''
  },

  /**
   * 扫描本地已下载的模型文件列表
   */
  async scanLocalModelFiles(): Promise<DiskModelFile[]> {
    const res = await AppBindings.ScanLocalModelFiles()
    if (!Array.isArray(res)) return []
    return res.map((filePath: string) => {
      const parts = filePath.replace(/\\/g, '/').split('/')
      const name = parts[parts.length - 1] || filePath
      return {
        name,
        path: filePath,
        sizeBytes: 0,
        sizeDisplay: '本地文件',
      }
    })
  },

  /**
   * 在系统资源管理器中打开指定目录 (唯一通道：Go 侧 Wails Env.OpenFileManager)
   */
  async openCacheDirectory(dir: string): Promise<void> {
    const cleanDir = dir ? dir.trim() : ''
    await AppBindings.OpenCacheDirectory(cleanDir)
  },

  /**
   * 清理指定目录下的缓存内容
   */
  async clearCacheDirectory(dir: string): Promise<void> {
    await AppBindings.ClearCacheDirectory(dir)
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
    return AppBindings.TestTtsEndpoint(endpoint, apiKey, engineType, voice)
  },

  /**
   * 触发本地图标全量预热与缓存统计
   * @returns 已缓存的英雄数 / 装备数与本次新下载数量
   */
  async cacheAllIcons(): Promise<{ cached_heroes: number, cached_items: number, downloaded: number }> {
    const res = await AppBindings.CacheAllIcons()
    return {
      cached_heroes: res?.cached_heroes ?? 0,
      cached_items: res?.cached_items ?? 0,
      downloaded: res?.downloaded ?? 0,
    }
  },

  /**
   * 从本地缓存文件夹中优先读取结构化游戏数据
   */
  async loadCachedGameData(dataType: string): Promise<string> {
    return AppBindings.LoadCachedGameData(dataType)
  },

  /**
   * 将官方拉取的最新完整结构化数据持久化至本地缓存目录
   */
  async saveCachedGameData(dataType: string, content: string): Promise<void> {
    await AppBindings.SaveCachedGameData(dataType, content)
  },

  /**
   * 优先从设置文件夹内读取应用配置，若不存在或损坏则自动新建并返回默认配置
   */
  async loadSettings(): Promise<string> {
    return AppBindings.LoadSettings()
  },

  /**
   * 将应用最新完整配置安全原子化落盘持久化至设置文件夹 settings.json
   */
  async saveSettings(settingsJson: string): Promise<void> {
    await AppBindings.SaveSettings(settingsJson)
  },

  /**
   * 从后端原生抓取 Valve 官方最新版本号与详细更新日志并自动缓存落盘
   */
  async fetchLatestPatchInfo(): Promise<string> {
    return AppBindings.FetchLatestPatchInfo()
  },
}
