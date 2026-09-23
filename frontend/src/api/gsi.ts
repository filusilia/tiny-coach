/**
 * Dota 2 GSI 数据流 API 模块 (src/api/gsi.ts)
 * 遵循 Wails v3 前后端绑定唯一事实来源铁律：统一走 callGo 进行强类型直调与平滑降级
 */

import * as AppBindings from '../../bindings/aegis-coach/app'
import { callGo } from './client'

export const gsiApi = {
  /**
   * 获取最近一次缓存的 GSI 数据快照 JSON
   */
  async getLatestSnapshot(): Promise<string> {
    return callGo(
      () => AppBindings.GetLatestGsiSnapshot(),
      ['main.App.GetLatestGsiSnapshot', 'services.GsiService.GetLatestSnapshot'],
    )
  },

  /**
   * 检查 GSI 监听服务当前是否处于活跃运行状态
   */
  async isGsiRunning(): Promise<boolean> {
    return callGo(
      () => AppBindings.IsGsiRunning(),
      ['main.App.IsGsiRunning', 'services.GsiService.IsRunning'],
    )
  },
}
