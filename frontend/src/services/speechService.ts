import type { VoiceMode } from '@/types'

/**
 * Dota 2 专属战术语音发音词典
 * 将缩写（如 BKB、HP、TP）、行话（如 A杖、魔晶、跳刀）平滑替换为自然中文读音，杜绝英文逐字母生硬朗读
 */
const TACTICAL_LEXICON: Record<string, string> = {
  // 常见缩写与机制数值
  HP: '生命值',
  hp: '生命值',
  MP: '魔法值',
  mp: '魔法值',
  CD: '冷却',
  cd: '冷却',
  EXP: '经验值',
  exp: '经验值',
  DPS: '每秒伤害',
  dps: '每秒伤害',
  GPM: '每分金钱',
  gpm: '每分金钱',
  XPM: '每分经验',
  xpm: '每分经验',
  BKB: '黑皇杖',
  bkb: '黑皇杖',
  A杖: '阿哈利姆神杖',
  魔晶: '阿哈利姆魔晶',
  TP: '回城卷轴',
  tp: '回城卷轴',
  跳刀: '科勒的匕首',
  羊刀: '邪恶意图',
  冰甲: '希瓦的守护',
  强袭: '强袭胸甲',
  大根: '达戈尼尔之神力',
  风杖: '尤尔的神圣权杖',
  刷新: '刷新球',
  肉山: '肉山大魔王',
  买活: '立即买活',
  盾: '不朽之守护',
  奶酪: '活力奶酪',
  圣水符: '圣水神符',
  极速符: '加速神符',
  双倍符: '双倍伤害神符',
  隐身符: '隐匿神符',
  奥术符: '奥术神符',
  幻想符: '幻象神符',
  金币符: '赏金神符',
}

/**
 * 战术文本语音规范化预处理
 * 使用电竞专用词典全字匹配替换，大幅提高语音朗读的流畅度与沉浸感
 * @param text 待朗读的原始战术提醒文本
 * @returns 替换后的自然汉语文本
 */
export function normalizeTacticalSpeech(text: string): string {
  let result = text
  for (const [key, value] of Object.entries(TACTICAL_LEXICON)) {
    result = result.replaceAll(key, value)
  }
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
  /** 指定匹配的目标音色名称 (如 zh-CN-XiaoxiaoNeural) */
  voiceName?: string
  /** 是否启用第三方自定义 HTTP TTS 引擎 */
  customTtsEnabled?: boolean
  /** 第三方 TTS 引擎协议类型 ('openai' | 'get_stream' | 'edge') */
  customTtsEngine?: 'openai' | 'get_stream' | 'edge' | string
  /** 第三方 TTS 请求服务端点 (如 http://127.0.0.1:9233) */
  customTtsEndpoint?: string
  /** 第三方 TTS API 鉴权密钥 */
  customTtsApiKey?: string
  /** 第三方 TTS 自定义音色名称 */
  customTtsVoice?: string
}

/**
 * 智能电竞语音播报合成服务 (SpeechService)
 * 封装 Web Speech API 与第三方 TTS 音频流，提供防 GC 吞音、WebView2 挂起唤醒、防堆叠及电竞黑话规范化能力
 */
class SpeechService {
  /** 浏览器底层语音合成器实例 */
  private synth: SpeechSynthesis | null = null
  /** 当前检测到的可用系统发音人列表 */
  private voices: SpeechSynthesisVoice[] = []
  /**
   * 保持当前播放 utterance 的强引用
   * 核心防退化设计：解决 Chromium/V8 引擎垃圾回收过快导致长句播报中途被截断/吞音的底层缺陷
   */
  private currentUtterance: SpeechSynthesisUtterance | null = null

  constructor() {
    if (typeof window !== 'undefined' && 'speechSynthesis' in window) {
      this.synth = window.speechSynthesis
      this.initVoiceListener()
    }
  }

  /**
   * 初始化发音人就绪事件监听
   */
  private initVoiceListener() {
    if (!this.synth) return
    this.refreshVoices()
    if (this.synth.onvoiceschanged !== undefined) {
      this.synth.onvoiceschanged = () => {
        this.refreshVoices()
      }
    }
  }

  /**
   * 刷新并获取最新系统音色列表
   */
  private refreshVoices(): SpeechSynthesisVoice[] {
    if (!this.synth) return []
    this.voices = this.synth.getVoices()
    return this.voices
  }

  /**
   * 智能挑选最佳匹配中文发音人
   * 优先匹配用户指定的角色名，再降级到高质量微软/系统中文，最后保底系统默认
   * @param targetName 目标音色匹配关键字 (如 "Xiaoxiao", "Yunxi")
   * @returns 匹配到的最佳发音人对象，无匹配返回系统默认或 null
   */
  public selectVoice(targetName?: string): SpeechSynthesisVoice | null {
    const list = this.refreshVoices()
    if (list.length === 0) return null

    if (targetName) {
      const key = targetName.replace(/zh-CN-|Neural/gi, '').toLowerCase()
      const matched = list.find((v) => {
        const lower = v.name.toLowerCase()
        return (
          lower.includes(key)
          || (key.includes('yunxi') && (v.name.includes('云希') || lower.includes('yunxi')))
          || (key.includes('xiaoxiao') && (v.name.includes('晓晓') || lower.includes('xiaoxiao')))
          || (key.includes('yunjian') && (v.name.includes('云健') || lower.includes('yunjian')))
        )
      })
      if (matched) return matched
    }

    // 优先选择自然微软中文/高品质中文语音
    const preferred = list.find(
      v =>
        v.lang.toLowerCase().includes('zh')
        && (v.name.includes('Natural')
          || v.name.includes('Online')
          || v.name.includes('Xiaoxiao')
          || v.name.includes('Yunxi')
          || v.name.includes('Microsoft')
          || v.name.includes('Chinese')),
    )
    if (preferred) return preferred

    // 降级选择任意中文发音
    const anyZh = list.find(v => v.lang.toLowerCase().includes('zh'))
    if (anyZh) return anyZh

    // 保底系统默认
    return list.find(v => v.default) || list[0] || null
  }

  /**
   * 播报战术语音
   * 包含多重关键容错与电竞优化特性：
   * 1. 重载支持 SpeakOptions 对象或传统 (text, mode, volume, rate) 传参；
   * 2. 智能纠偏：若误将语速传至 mode 且音量偏大，自动重定向至标准档位；
   * 3. 第三方 TTS 优先：若配置了第三方 HTTP 端点 (如 CosyVoice / ChatTTS)，优先流式解码播放；
   * 4. 唤醒 WebView2 挂起态：主动触发 resume 避免 Chromium 偶发静默；
   * 5. 防堆叠：cancel 上一段未播完语音，确保关键战术告警秒级入耳；
   * 6. 防 GC 吞音：在当前实例上挂载强引用，播报结束或异常时安全解引用。
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
    if (!this.synth) {
      return Promise.resolve()
    }

    // 解析参数（支持对象与数字重载，并对误将 rate 传到 mode 进行智能纠偏）
    let mode: VoiceMode = 2
    let volume = 85
    let rate = 1.05
    let pitch = 1.0
    let voiceName: string | undefined

    if (typeof optionsOrMode === 'object' && optionsOrMode !== null) {
      if (optionsOrMode.mode !== undefined) mode = optionsOrMode.mode
      if (optionsOrMode.volume !== undefined) volume = optionsOrMode.volume
      if (optionsOrMode.rate !== undefined) rate = optionsOrMode.rate
      if (optionsOrMode.pitch !== undefined) pitch = optionsOrMode.pitch
      if (optionsOrMode.voiceName !== undefined) voiceName = optionsOrMode.voiceName
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

    // 档位 0 (静音) 或 1 (仅屏幕通报) 时不发出声音
    if (mode === 0 || mode === 1) {
      return Promise.resolve()
    }

    // 若启用了第三方自定义 HTTP TTS 服务且端点非空，尝试优先调用第三方音频流；若服务未运行或播放失败，无缝降级至内置引擎
    const shouldUseCustomTts = Boolean(
      typeof optionsOrMode === 'object'
      && optionsOrMode?.customTtsEnabled
      && optionsOrMode?.customTtsEndpoint?.trim(),
    )

    if (shouldUseCustomTts) {
      const opts = optionsOrMode as SpeakOptions
      const endpoint = opts.customTtsEndpoint!.trim().replace(/\/+$/, '')
      const cleanText = normalizeTacticalSpeech(text)
      const engineType = opts.customTtsEngine || 'openai'
      const apiKey = opts.customTtsApiKey?.trim() || ''

      const playedByCustom = await new Promise<boolean>((resolveCustom) => {
        (async () => {
          try {
            if (engineType === 'openai') {
              // 1. OpenAI 规范 (/v1/audio/speech，兼容用户已填写 /v1 的情况)
              const baseEndpoint = endpoint.endsWith('/v1') ? endpoint : `${endpoint}/v1`
              const reqUrl = `${baseEndpoint}/audio/speech`
              const res = await fetch(reqUrl, {
                method: 'POST',
                headers: {
                  'Content-Type': 'application/json',
                  ...(apiKey ? { Authorization: `Bearer ${apiKey}` } : {}),
                },
                body: JSON.stringify({
                  model: 'tts-1',
                  input: cleanText,
                  voice: opts.customTtsVoice || opts.voiceName || 'alloy',
                  speed: Math.max(0.25, Math.min(4.0, rate)),
                }),
                signal: AbortSignal.timeout(3500),
              })
              if (!res.ok) {
                resolveCustom(false)
                return
              }
              const blob = await res.blob()
              const audioUrl = URL.createObjectURL(blob)
              const audio = new Audio(audioUrl)
              audio.volume = Math.max(0, Math.min(1, volume / 100))
              audio.onended = () => {
                URL.revokeObjectURL(audioUrl)
                resolveCustom(true)
              }
              audio.onerror = () => {
                URL.revokeObjectURL(audioUrl)
                resolveCustom(false)
              }
              await audio.play()
            } else {
              // 2. 通用 GET 流式接口 (/tts?text=...)
              const audio = new Audio(`${endpoint}/tts?text=${encodeURIComponent(cleanText)}`)
              audio.volume = Math.max(0, Math.min(1, volume / 100))

              let finished = false
              const finish = (ok: boolean) => {
                if (!finished) {
                  finished = true
                  resolveCustom(ok)
                }
              }

              const timer = setTimeout(() => {
                audio.pause()
                finish(false)
              }, 2000)

              audio.onplaying = () => {
                clearTimeout(timer)
                finish(true)
              }
              audio.onerror = () => {
                clearTimeout(timer)
                finish(false)
              }
              audio.play().catch(() => {
                clearTimeout(timer)
                finish(false)
              })
            }
          } catch {
            resolveCustom(false)
          }
        })()
      })

      if (playedByCustom) {
        return
      }
      console.warn('[SpeechService] 第三方 TTS 服务未响应或调用失败，已自动降级至系统内置语音引擎。')
    }

    return new Promise((resolve) => {
      // 唤醒处于 paused/frozen 状态的合成器 (解决 WebView2/Chromium 后台静默 bug)
      if (this.synth!.paused) {
        try {
          this.synth!.resume()
        } catch {
          // ignore
        }
      }

      // 停止上一句播报，避免堆叠延误战机
      this.synth!.cancel()

      const normalizedText = normalizeTacticalSpeech(text)
      const utterance = new SpeechSynthesisUtterance(normalizedText)

      // 强引用挂载防 GC
      this.currentUtterance = utterance

      const chosenVoice = this.selectVoice(voiceName)
      if (chosenVoice) {
        utterance.voice = chosenVoice
      }
      utterance.lang = 'zh-CN'
      utterance.volume = Math.max(0, Math.min(1, volume / 100))
      utterance.rate = Math.max(0.5, Math.min(2.0, rate))
      utterance.pitch = Math.max(0.5, Math.min(2.0, pitch))

      const cleanup = () => {
        if (this.currentUtterance === utterance) {
          this.currentUtterance = null
        }
        resolve()
      }

      utterance.onend = cleanup
      utterance.onerror = cleanup

      this.synth!.speak(utterance)
    })
  }

  /**
   * 立即中断并停止当前所有正在进行的语音播报
   * 重置底层合成器与强引用变量，避免声音残留
   */
  public stop() {
    if (this.synth) {
      this.synth.cancel()
      this.currentUtterance = null
    }
  }

  /**
   * 彻底清空本地缓存的发音人列表并重新触发刷新扫描
   */
  public clearCache(): void {
    this.stop()
    this.voices = []
    this.refreshVoices()
  }

  /**
   * 获取当前宿主系统 (Windows / Edge WebView2) 检测到的所有可用发音人列表
   * @returns 语音发音人对象数组
   */
  public getAvailableVoices(): SpeechSynthesisVoice[] {
    return this.refreshVoices()
  }

  /**
   * 单独测试第三方 TTS 发音与连通性
   */
  public async testThirdPartyTts(
    text: string,
    options: SpeakOptions,
  ): Promise<{ success: boolean, message: string }> {
    const endpoint = options.customTtsEndpoint?.trim().replace(/\/+$/, '')
    if (!endpoint) {
      return { success: false, message: '第三方 TTS 端点 URL 不能为空' }
    }

    const cleanText = normalizeTacticalSpeech(text || '神盾教练语音测试，肉山大魔王已刷新，黑皇杖就绪。')
    const engineType = options.customTtsEngine || 'openai'
    const apiKey = options.customTtsApiKey?.trim() || ''

    try {
      if (engineType === 'openai') {
        const baseEndpoint = endpoint.endsWith('/v1') ? endpoint : `${endpoint}/v1`
        const reqUrl = `${baseEndpoint}/audio/speech`
        const res = await fetch(reqUrl, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            ...(apiKey ? { Authorization: `Bearer ${apiKey}` } : {}),
          },
          body: JSON.stringify({
            model: 'tts-1',
            input: cleanText,
            voice: options.customTtsVoice || 'alloy',
            speed: 1.0,
          }),
          signal: AbortSignal.timeout(5000),
        })
        if (!res.ok) {
          return {
            success: false,
            message: `服务端返回异常 HTTP ${res.status}: ${res.statusText}`,
          }
        }
        const blob = await res.blob()
        const audioUrl = URL.createObjectURL(blob)
        const audio = new Audio(audioUrl)
        audio.volume = 1.0
        audio.onended = () => URL.revokeObjectURL(audioUrl)
        await audio.play()
        return { success: true, message: 'OpenAI 协议 TTS 发音测试成功！' }
      } else {
        const audioUrl = `${endpoint}/tts?text=${encodeURIComponent(cleanText)}`
        const audio = new Audio(audioUrl)
        audio.volume = 1.0
        await audio.play()
        return { success: true, message: 'HTTP GET 流式 TTS 发音测试成功！' }
      }
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err)
      return {
        success: false,
        message: `无法连接到服务 (${endpoint}): ${errMsg}。请检查本地 TTS 服务是否已启动。`,
      }
    }
  }
}

export const speechService = new SpeechService()
