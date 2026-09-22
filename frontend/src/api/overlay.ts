/**
 * 独立经济悬浮窗与原生桌面窗口 API 模块 (src/api/overlay.ts)
 * 封装悬浮窗子进程拉起、窗口穿透、置顶控制
 */

import * as AppBindings from '../../bindings/aegis-coach/app'
import { invokeWails } from './client'

export const overlayApi = {
  /**
   * 判断当前客户端是否处于独立悬浮窗进程模式中
   */
  async isOverlayMode(): Promise<boolean> {
    try {
      return await AppBindings.IsOverlayMode()
    } catch {
      return invokeWails<boolean>([
        'main.App.IsOverlayMode',
        'aegis-coach/internal/services.OverlayService.IsOverlayMode',
        'services.OverlayService.IsOverlayMode',
        'app.App.IsOverlayMode',
      ])
    }
  },

  /**
   * 判断悬浮窗子进程当前是否在运行
   */
  async isOverlayRunning(): Promise<boolean> {
    try {
      return await AppBindings.IsOverlayRunning()
    } catch {
      return invokeWails<boolean>([
        'main.App.IsOverlayRunning',
        'aegis-coach/internal/services.OverlayService.IsOverlayRunning',
        'services.OverlayService.IsOverlayRunning',
        'app.App.IsOverlayRunning',
      ])
    }
  },

  /**
   * 切换悬浮窗显示/隐藏或拉起/关闭子进程 (第一优先直连 Wails 官方生成的强类型绑定)
   */
  async toggleOverlayWindow(): Promise<boolean> {
    try {
      return await AppBindings.ToggleOverlayWindow()
    } catch (bindErr) {
      console.warn('[overlayApi] AppBindings.ToggleOverlayWindow 异常，尝试降级:', bindErr)
      return invokeWails<boolean>([
        'main.App.ToggleOverlayWindow',
        'aegis-coach/internal/services.OverlayService.ToggleOverlayWindow',
        'services.OverlayService.ToggleOverlayWindow',
        'app.App.ToggleOverlayWindow',
      ])
    }
  },

  /**
   * 启用或禁用悬浮窗的鼠标穿透（透明区域忽略鼠标点击）
   */
  async setIgnoreMouseEvents(ignore: boolean): Promise<void> {
    try {
      await AppBindings.SetIgnoreMouseEvents(ignore)
    } catch {
      await invokeWails<void>(
        [
          'main.App.SetIgnoreMouseEvents',
          'aegis-coach/internal/services.OverlayService.SetIgnoreMouseEvents',
          'services.OverlayService.SetIgnoreMouseEvents',
          'app.App.SetIgnoreMouseEvents',
        ],
        ignore,
      )
    }
  },
}
