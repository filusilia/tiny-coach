/**
 * 超保真拟人 (Go-Speech) 原生离线语音 API 模块 (src/api/tts.ts)
 * 职责：纯粹封装 Go 后端 MeloTTS 极简调用，零技术术语暴露
 *
 * 绑定唯一事实来源（铁律 9）：直接 import 生成绑定并调用。
 * 引擎状态返回类型直接复用生成绑定导出的 EngineStatus，避免手工重复声明契约。
 */

import type { EngineStatus } from '../../bindings/aegis-coach/internal/services/tts/models'
import * as AppBindings from '../../bindings/aegis-coach/app'

export const ttsApi = {
  /**
   * 查询超保真拟人语音引擎是否已就绪
   */
  async getGoSpeechStatus(): Promise<boolean> {
    return AppBindings.GetGoSpeechStatus()
  },

  /**
   * 使用超保真拟人原生离线引擎合成文本为 Base64 音频
   * @param text 待朗读文本
   * @param speed 语速倍率
   * @returns Base64 Data URL (data:audio/wav;base64,...)
   */
  async synthesizeSpeech(text: string, speed: number = 1.0): Promise<string> {
    return AppBindings.SynthesizeSpeech(text, speed)
  },

  /**
   * 测试超保真拟人发音
   * @param text 待朗读文本
   * @param speed 语速倍率
   */
  async testGoSpeech(text: string, speed: number = 1.0): Promise<string> {
    return AppBindings.TestGoSpeech(text, speed)
  },

  /**
   * 获取 MeloTTS 引擎完整状态快照（就绪、下载中、进度等）
   */
  async getTtsStatus(): Promise<EngineStatus> {
    return AppBindings.GetTtsStatus()
  },
}
