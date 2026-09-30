/**
 * 原生屏幕视觉与截屏 API 模块 (src/api/vision.ts)
 * 职责：纯粹封装 Go 后端 VisionService 截屏与热键调用
 *
 * 绑定唯一事实来源（铁律 9）：直接 import 生成绑定并调用。
 */

import type { CaptureResult } from '../../bindings/aegis-coach/internal/models/models'
import * as AppBindings from '../../bindings/aegis-coach/app'
import * as VisionBindings from '../../bindings/aegis-coach/internal/services/vision/visionservice'

export const visionApi = {
  /**
   * 触发顶部 18% ROI 截屏（用于 BP 双方 10 英雄槽位识别）
   */
  async captureTopScreen(): Promise<CaptureResult | null> {
    return AppBindings.CaptureTopScreen()
  },

  /**
   * 通用底层截屏：截取屏幕指定矩形区域
   */
  async captureRect(x: number, y: number, w: number, h: number): Promise<CaptureResult | null> {
    return AppBindings.CaptureRect(x, y, w, h)
  },

  /**
   * 截取左下角 298x298 小地图区域
   */
  async captureMinimap(): Promise<CaptureResult | null> {
    return AppBindings.CaptureMinimap()
  },

  /**
   * 启动全局 F10 截屏热键监听
   */
  async startF10Hotkey(): Promise<boolean> {
    return AppBindings.StartF10CaptureHotkey()
  },

  /**
   * 停止全局 F10 截屏热键监听
   */
  async stopF10Hotkey(): Promise<void> {
    return VisionBindings.StopF10CaptureHotkey()
  },

  /**
   * 查询全局热键是否处于激活状态
   */
  async isHotkeyActive(): Promise<boolean> {
    return AppBindings.IsHotkeyActive()
  },

  /**
   * 获取最近一次成功截屏缓存
   */
  async getLastCapture(): Promise<CaptureResult | null> {
    return AppBindings.GetLastCapture()
  },
}
