/**
 * 独立经济悬浮窗与原生桌面窗口 API 模块 (src/api/overlay.ts)
 * 封装悬浮窗子进程拉起、窗口穿透、置顶控制
 */

import * as AppBindings from '../../bindings/aegis-coach/app'

export const overlayApi = {
  /**
   * 判断当前客户端进程是否处于独立悬浮窗进程模式中
   */
  async isOverlayMode(): Promise<boolean> {
    try {
      return await AppBindings.IsOverlayMode()
    } catch (e) {
      console.warn('[overlayApi] IsOverlayMode 调用失败:', e)
      return false
    }
  },

  /**
   * 判断悬浮窗是否正在后台运行或处于可见状态
   */
  async isOverlayRunning(): Promise<boolean> {
    try {
      return await AppBindings.IsOverlayRunning()
    } catch (e) {
      console.warn('[overlayApi] IsOverlayRunning 调用失败:', e)
      return false
    }
  },

  /**
   * 切换局内独立置顶经济悬浮窗显隐状态 (直连 Wails v3 强类型绑定)
   */
  async toggleOverlayWindow(): Promise<boolean> {
    try {
      return await AppBindings.ToggleOverlayWindow()
    } catch (e) {
      console.warn('[overlayApi] ToggleOverlayWindow 调用失败:', e)
      return false
    }
  },

  /**
   * 启用或禁用悬浮窗的鼠标穿透（透明区域忽略鼠标点击）
   */
  async setIgnoreMouseEvents(ignore: boolean): Promise<void> {
    try {
      await AppBindings.SetIgnoreMouseEvents(ignore)
    } catch (e) {
      console.warn('[overlayApi] SetIgnoreMouseEvents 调用失败:', e)
    }
  },

  /**
   * 切换鼠标穿透状态
   */
  async toggleMouseThrough(): Promise<boolean> {
    try {
      return await AppBindings.ToggleMouseThrough()
    } catch (e) {
      console.warn('[overlayApi] ToggleMouseThrough 调用失败:', e)
      return false
    }
  },

  /**
   * 重置悬浮窗位置至屏幕中央
   */
  async resetOverlayPosition(): Promise<void> {
    try {
      await AppBindings.ResetOverlayPosition()
    } catch (e) {
      console.warn('[overlayApi] ResetOverlayPosition 调用失败:', e)
    }
  },
}
