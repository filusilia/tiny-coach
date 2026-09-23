/**
 * 独立经济悬浮窗与原生桌面窗口 API 模块 (src/api/overlay.ts)
 * 封装悬浮窗子进程拉起、窗口穿透、置顶控制
 */

import * as AppBindings from '../../bindings/aegis-coach/app'
import { callGo } from './client'

export const overlayApi = {
  /**
   * 判断当前客户端进程是否处于独立悬浮窗进程模式中
   */
  async isOverlayMode(): Promise<boolean> {
    return callGo(
      () => AppBindings.IsOverlayMode(),
      ['main.App.IsOverlayMode', 'services.OverlayService.IsOverlayMode'],
    ).catch(() => false)
  },

  /**
   * 判断悬浮窗是否正在后台运行或处于可见状态
   */
  async isOverlayRunning(): Promise<boolean> {
    return callGo(
      () => AppBindings.IsOverlayRunning(),
      ['main.App.IsOverlayRunning', 'services.OverlayService.IsOverlayRunning'],
    ).catch(() => false)
  },

  /**
   * 切换局内独立置顶经济悬浮窗显隐状态
   */
  async toggleOverlayWindow(): Promise<boolean> {
    return callGo(
      () => AppBindings.ToggleOverlayWindow(),
      ['main.App.ToggleOverlayWindow', 'services.OverlayService.ToggleOverlayWindow'],
    ).catch(() => false)
  },

  /**
   * 启用或禁用悬浮窗的鼠标穿透（透明区域忽略鼠标点击）
   */
  async setIgnoreMouseEvents(ignore: boolean): Promise<void> {
    return callGo(
      () => AppBindings.SetIgnoreMouseEvents(ignore),
      ['main.App.SetIgnoreMouseEvents', 'services.OverlayService.SetIgnoreMouseEvents'],
      ignore,
    ).catch(() => {})
  },

  /**
   * 切换鼠标穿透状态
   */
  async toggleMouseThrough(): Promise<boolean> {
    return callGo(
      () => AppBindings.ToggleMouseThrough(),
      ['main.App.ToggleMouseThrough', 'services.OverlayService.ToggleMouseThrough'],
    ).catch(() => false)
  },

  /**
   * 重置悬浮窗位置至屏幕中央
   */
  async resetOverlayPosition(): Promise<void> {
    return callGo(
      () => AppBindings.ResetOverlayPosition(),
      ['main.App.ResetOverlayPosition', 'services.OverlayService.ResetOverlayPosition'],
    ).catch(() => {})
  },
}
