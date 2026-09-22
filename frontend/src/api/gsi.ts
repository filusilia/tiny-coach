/**
 * Dota 2 GSI 数据流 API 模块 (src/api/gsi.ts)
 * 封装单消费者 GSI 状态拉取与监听控制
 */

import { invokeWails } from './client'

export const gsiApi = {
  /**
   * 获取最近一次缓存的 GSI 数据快照 JSON
   */
  async getLatestSnapshot(): Promise<string> {
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
    return invokeWails<boolean>([
      'services.GsiService.IsRunning',
      'main.App.IsGsiRunning',
    ])
  },
}
