/**
 * Dota 2 GSI 数据流 API 模块 (src/api/gsi.ts)
 *
 * 绑定唯一事实来源（铁律 9）：直接 import 生成绑定并调用，不做任何方法名猜测。
 */

import * as AppBindings from '../../bindings/aegis-coach/app'

export const gsiApi = {
  /**
   * 获取最近一次缓存的 GSI 数据快照 JSON
   * 未接入对局时后端返回 {"connected": false} 空态标记，绝不伪造战局数据
   */
  async getLatestSnapshot(): Promise<string> {
    return AppBindings.GetLatestGsiSnapshot()
  },

  /**
   * 检查 GSI 监听服务当前是否处于活跃运行状态
   */
  async isGsiRunning(): Promise<boolean> {
    return AppBindings.IsGsiRunning()
  },
}
