import type { VoiceMode } from '@/types'
import { ttsApi } from '@/api'
import { useAppStore } from '@/stores'

/**
 * 战术文本语音规范化预处理
 * 使用电竞专用正则进行安全归一化，杜绝全字盲目替换导致的叠词（如“肉山大魔”变“肉山大魔王大魔”）与动词误杀
 * @param text 待朗读的原始战术提醒文本
 * @returns 替换后的自然汉语文本
 */
export function normalizeTacticalSpeech(text: string): string {
  if (!text) return ''

  let result = text

  // 1. 核心肉山大魔王幂等归一化：无论是“肉山”、“肉山大魔”还是“肉山大魔王”，均严格归一为“肉山大魔王”，绝不产生叠词
  result = result.replace(/肉山(?:大魔王?)?/g, '肉山大魔王')

  // 2. 常见英文机制缩写（使用单词边界 \b 精准匹配，避免误伤其他词汇）
  result = result
    .replace(/\bBKB\b/gi, '黑皇杖')
    .replace(/\bTP\b/gi, '回城卷轴')
    .replace(/\bHP\b/gi, '生命值')
    .replace(/\bMP\b/gi, '魔法值')
    .replace(/\bCD\b/gi, '冷却')
    .replace(/\bEXP\b/gi, '经验值')
    .replace(/\bDPS\b/gi, '每秒伤害')
    .replace(/\bGPM\b/gi, '每分金钱')
    .replace(/\bXPM\b/gi, '每分经验')

  // 3. 常见装备黑话（使用负向前瞻/回顾，避免重复拼接已有全称）
  result = result
    .replace(/(?<!阿哈利姆)A杖/g, '阿哈利姆神杖')
    .replace(/(?<!阿哈利姆)魔晶/g, '阿哈利姆魔晶')
    .replace(/跳刀/g, '科勒的匕首')
    .replace(/羊刀/g, '邪恶意图')
    .replace(/冰甲/g, '希瓦的守护')
    .replace(/强袭(?![胸甲])/g, '强袭胸甲')
    .replace(/大根/g, '达戈尼尔之神力')
    .replace(/风杖/g, '尤尔的神圣权杖')

  // 4. 神符类型规范化
  result = result
    .replace(/双倍符/g, '双倍伤害神符')
    .replace(/极速符/g, '加速神符')
    .replace(/隐身符/g, '隐匿神符')
    .replace(/奥术符/g, '奥术神符')
    .replace(/金币符/g, '赏金神符')
    .replace(/圣水符/g, '圣水神符')
    .replace(/幻想符/g, '幻象神符')

  return result
}

/**
 * 语音播报播放参数实体
 */
export interface SpeakOptions {
  /** 播报模式 (0: 关闭, 1: 仅文字, 2: 语音+文字) */
  mode?: VoiceMode
  /** 播放音量大小 (0 ~ 100) */
  volume?: number
  /** 语速倍率 (0.5 ~ 2.0) */
  rate?: number
  /** 音调高低 (0.5 ~ 2.0) */
  pitch?: number
  /** 目标音色（保留兼容，纯 MeloTTS 下默认原生音色） */
  voiceName?: string
}

/**
 * 智能电竞语音播报合成服务 (SpeechService)
 * 纯 MeloTTS 本地原生离线超保真引擎，提供零延迟秒级发音、战术防堆叠与黑话规范化能力
 */
class SpeechService {
  /** 当前正在播放的 Audio 实例 */
  private currentAudio: HTMLAudioElement | null = null

  /**
   * 播报战术语音 (纯 MeloTTS 架构)
   * @param text 待朗读文本
   * @param optionsOrMode 播报配置对象或模式编号
   * @param volumeArg 独立音量参数 (0 ~ 100)
   * @param rateArg 独立语速倍率 (0.5 ~ 2.0)
   */
  public async speak(
    text: string,
    optionsOrMode?: SpeakOptions | VoiceMode | number,
    volumeArg?: number,
    rateArg?: number,
  ): Promise<void> {
    // 解析参数（支持对象与数字重载，并对误将 rate 传到 mode 进行智能纠偏）
    let mode: VoiceMode = 2
    let volume = 85
    let rate = 1.05

    if (typeof optionsOrMode === 'object' && optionsOrMode !== null) {
      if (optionsOrMode.mode !== undefined) mode = optionsOrMode.mode
      if (optionsOrMode.volume !== undefined) volume = optionsOrMode.volume
      if (optionsOrMode.rate !== undefined) rate = optionsOrMode.rate
    } else if (typeof optionsOrMode === 'number') {
      if (optionsOrMode === 0) {
        mode = 0
        if (volumeArg !== undefined) volume = volumeArg
        if (rateArg !== undefined) rate = rateArg
      } else if (rateArg === undefined && volumeArg !== undefined && volumeArg > 10) {
        mode = 2
        rate = optionsOrMode
        volume = volumeArg
      } else {
        mode = (optionsOrMode === 1 || optionsOrMode === 2) ? optionsOrMode : 2
        if (volumeArg !== undefined) volume = volumeArg
        if (rateArg !== undefined) rate = rateArg
      }
    }

    try {
      const appStore = useAppStore()
      if (typeof optionsOrMode !== 'object' || optionsOrMode?.volume === undefined) {
        volume = appStore.settings.voice_volume
      }
      if (typeof optionsOrMode !== 'object' || optionsOrMode?.rate === undefined) {
        rate = appStore.settings.voice_speed
      }
    } catch {
      // 容错: 若在 Pinia 挂载前调用则使用默认值
    }

    // 档位 0 (静音) 或 1 (仅屏幕通报) 时不发出声音
    if (mode === 0 || mode === 1) {
      return Promise.resolve()
    }

    const cleanText = normalizeTacticalSpeech(text)
    if (!cleanText) {
      return Promise.resolve()
    }

    return new Promise<void>((resolve) => {
      (async () => {
        try {
          // 停止上一句音频播报，防声音堆叠
          this.stop()

          // 超保真拟人使用 MeloTTS 原生离线推理：双参数契约 (cleanText, rate)
          const audioUrl = await ttsApi.synthesizeSpeech(cleanText, rate)
          if (!audioUrl) {
            resolve()
            return
          }

          const audio = new Audio(audioUrl)
          this.currentAudio = audio
          audio.volume = Math.max(0, Math.min(1, volume / 100))

          audio.onended = () => {
            if (this.currentAudio === audio) {
              this.currentAudio = null
            }
            resolve()
          }

          audio.onerror = () => {
            if (this.currentAudio === audio) {
              this.currentAudio = null
            }
            resolve()
          }

          await audio.play()
        } catch (err) {
          console.warn('[SpeechService] MeloTTS 播报失败或未就绪:', err)
          resolve()
        }
      })()
    })
  }

  /**
   * 立即中断并停止当前所有正在进行的语音播报
   */
  public stop() {
    if (this.currentAudio) {
      try {
        this.currentAudio.pause()
        this.currentAudio.src = ''
      } catch {
        // ignore
      }
      this.currentAudio = null
    }
  }

  /**
   * 彻底清空本地缓存
   */
  public clearCache(): void {
    this.stop()
  }

  /**
   * 单独测试 MeloTTS 超保真拟人发音
   */
  public async testThirdPartyTts(
    text: string,
    options?: SpeakOptions,
  ): Promise<{ success: boolean, message: string }> {
    const rawText = text?.trim()
    if (!rawText) {
      return { success: false, message: '未提供测试发音文本' }
    }
    const cleanText = normalizeTacticalSpeech(rawText)
    try {
      const speed = options?.rate ?? 1.0
      const audioUrl = await ttsApi.synthesizeSpeech(cleanText, speed)
      this.stop()
      const audio = new Audio(audioUrl)
      this.currentAudio = audio
      audio.volume = (options?.volume ?? 100) / 100
      await audio.play()
      return { success: true, message: '超保真拟人语音合成发音成功！' }
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err)
      return { success: false, message: `超保真拟人合成失败: ${errMsg}` }
    }
  }
}

export const speechService = new SpeechService()
