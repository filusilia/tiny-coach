/**
 * 独立经济悬浮窗与原生桌面窗口 API 模块 (src/api/overlay.ts)
 * 封装悬浮窗子进程拉起、窗口穿透、置顶控制
 *
 * 职责边界（铁律 4）：本层只负责调用后端绑定并返回结果。
 * 悬浮窗运行状态的持久化与跨窗口广播由 stores/app.ts 统一负责，本层不写 localStorage；
 * 错误一律向上抛出，由 Store / UI 层决定如何呈现，不在本层静默吞掉。
 */

import * as AppBindings from '../../bindings/aegis-coach/app'

export const overlayApi = {
  /**
   * 判断悬浮窗是否正在后台运行或处于可见状态
   */
  async isOverlayRunning(): Promise<boolean> {
    return AppBindings.IsOverlayRunning()
  },

  /**
   * 彻底关闭并销毁悬浮窗
   */
  async closeOverlay(): Promise<void> {
    await AppBindings.CloseOverlay()
  },

  /**
   * 切换局内独立置顶经济悬浮窗显隐状态
   * @returns 切换后是否处于运行状态
   */
  async toggleOverlayWindow(): Promise<boolean> {
    return Boolean(await AppBindings.ToggleOverlayWindow())
  },

  /**
   * 启用或禁用悬浮窗的鼠标穿透（透明区域忽略鼠标点击）
   */
  async setIgnoreMouseEvents(ignore: boolean): Promise<void> {
    await AppBindings.SetIgnoreMouseEvents(ignore)
  },

  /**
   * 切换鼠标穿透状态
   */
  async toggleMouseThrough(): Promise<boolean> {
    return Boolean(await AppBindings.ToggleMouseThrough())
  },

  /**
   * 重置悬浮窗位置至屏幕中央
   */
  async resetOverlayPosition(): Promise<void> {
    await AppBindings.ResetOverlayPosition()
  },

  /**
   * 动态设置悬浮窗尺寸，紧凑贴合内容消除透明冗余
   */
  async setOverlaySize(width: number, height: number): Promise<void> {
    if ((AppBindings as any).SetOverlaySize) {
      await (AppBindings as any).SetOverlaySize(width, height)
    }
  },
}
