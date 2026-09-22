/**
 * Dota 2 GSI 数据流 API 模块 (src/api/gsi.ts)
 * 遵循 Wails v3 前后端绑定唯一事实来源铁律：优先直调 AppBindings
 */

import * as AppBindings from '../../bindings/aegis-coach/app'
import { invokeWails } from './client'

export const gsiApi = {
  /**
   * 获取最近一次缓存的 GSI 数据快照 JSON
   */
  async getLatestSnapshot(): Promise<string> {
    try {
      const res = await AppBindings.GetLatestGsiSnapshot()
      if (typeof res === 'string') return res
    } catch (bindErr) {
      console.warn('[gsiApi] AppBindings.GetLatestGsiSnapshot 异常，尝试降级:', bindErr)
    }

    return invokeWails<string>([
      'main.App.GetLatestGsiSnapshot',
      'services.GsiService.GetLatestSnapshot',
      'app.App.GetLatestGsiSnapshot',
    ])
  },

  /**
   * 检查 GSI 监听服务当前是否处于活跃运行状态
   */
  async isGsiRunning(): Promise<boolean> {
    try {
      return await AppBindings.IsGsiRunning()
    } catch (bindErr) {
      console.warn('[gsiApi] AppBindings.IsGsiRunning 异常，尝试降级:', bindErr)
    }

    return invokeWails<boolean>([
      'services.GsiService.IsRunning',
      'main.App.IsGsiRunning',
      'app.App.IsGsiRunning',
    ])
  },
}
