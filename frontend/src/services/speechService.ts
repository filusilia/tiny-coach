// 电竞战术语音发音词典与播报服务 (SpeechService)
import { VoiceMode } from '../types';

// Dota 2 专属战术词典：针对缩写、英雄黑话、装备俚语进行发音平滑替换
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
};

// 文本规范化：使用电竞词典转换，提高语音合成自然度
export function normalizeTacticalSpeech(text: string): string {
  let result = text;
  for (const [key, value] of Object.entries(TACTICAL_LEXICON)) {
    result = result.replaceAll(key, value);
  }
  return result;
}

export interface SpeakOptions {
  mode?: VoiceMode;
  volume?: number;    // 0 - 100
  rate?: number;      // 0.5 - 2.0
  pitch?: number;     // 0.5 - 2.0
  voiceName?: string; // 指定期望的声音，如 zh-CN-YunxiNeural 等
}

class SpeechService {
  private synth: SpeechSynthesis | null = null;
  private voices: SpeechSynthesisVoice[] = [];
  // 保持当前播放 utterance 的强引用，防止被 Chromium/V8 GC 中途垃圾回收导致吞音或 onend 不触发
  private currentUtterance: SpeechSynthesisUtterance | null = null;

  constructor() {
    if (typeof window !== 'undefined' && 'speechSynthesis' in window) {
      this.synth = window.speechSynthesis;
      this.initVoiceListener();
    }
  }

  private initVoiceListener() {
    if (!this.synth) return;
    this.refreshVoices();
    if (this.synth.onvoiceschanged !== undefined) {
      this.synth.onvoiceschanged = () => {
        this.refreshVoices();
      };
    }
  }

  private refreshVoices(): SpeechSynthesisVoice[] {
    if (!this.synth) return [];
    this.voices = this.synth.getVoices();
    return this.voices;
  }

  /**
   * 智能挑选发音人
   * 优先匹配用户指定的角色名，再降级到高质量微软/系统中文，最后保底系统默认
   */
  public selectVoice(targetName?: string): SpeechSynthesisVoice | null {
    const list = this.refreshVoices();
    if (list.length === 0) return null;

    if (targetName) {
      const key = targetName.replace(/zh-CN-|Neural/gi, '').toLowerCase();
      const matched = list.find((v) => {
        const lower = v.name.toLowerCase();
        return (
          lower.includes(key) ||
          (key.includes('yunxi') && (v.name.includes('云希') || lower.includes('yunxi'))) ||
          (key.includes('xiaoxiao') && (v.name.includes('晓晓') || lower.includes('xiaoxiao'))) ||
          (key.includes('yunjian') && (v.name.includes('云健') || lower.includes('yunjian')))
        );
      });
      if (matched) return matched;
    }

    // 优先选择自然微软中文/高品质中文语音
    const preferred = list.find(
      (v) =>
        v.lang.toLowerCase().includes('zh') &&
        (v.name.includes('Natural') ||
          v.name.includes('Online') ||
          v.name.includes('Xiaoxiao') ||
          v.name.includes('Yunxi') ||
          v.name.includes('Microsoft') ||
          v.name.includes('Chinese'))
    );
    if (preferred) return preferred;

    // 降级选择任意中文发音
    const anyZh = list.find((v) => v.lang.toLowerCase().includes('zh'));
    if (anyZh) return anyZh;

    // 保底系统默认
    return list.find((v) => v.default) || list[0] || null;
  }

  /**
   * 播报战术语音 (支持 SpeakOptions 选项对象或传统形参，带智能参数纠偏与防挂起唤醒)
   */
  public speak(
    text: string,
    optionsOrMode?: SpeakOptions | VoiceMode | number,
    volumeArg?: number,
    rateArg?: number
  ): Promise<void> {
    if (!this.synth) {
      return Promise.resolve();
    }

    // 解析参数（支持对象与数字重载，并对误将 rate 传到 mode 进行智能纠偏）
    let mode: VoiceMode = 2;
    let volume = 85;
    let rate = 1.05;
    let pitch = 1.0;
    let voiceName: string | undefined = undefined;

    if (typeof optionsOrMode === 'object' && optionsOrMode !== null) {
      if (optionsOrMode.mode !== undefined) mode = optionsOrMode.mode;
      if (optionsOrMode.volume !== undefined) volume = optionsOrMode.volume;
      if (optionsOrMode.rate !== undefined) rate = optionsOrMode.rate;
      if (optionsOrMode.pitch !== undefined) pitch = optionsOrMode.pitch;
      if (optionsOrMode.voiceName !== undefined) voiceName = optionsOrMode.voiceName;
    } else if (typeof optionsOrMode === 'number') {
      if (optionsOrMode === 0) {
        mode = 0;
        if (volumeArg !== undefined) volume = volumeArg;
        if (rateArg !== undefined) rate = rateArg;
      } else if (rateArg === undefined && volumeArg !== undefined && volumeArg > 10) {
        mode = 2;
        rate = optionsOrMode;
        volume = volumeArg;
      } else {
        mode = (optionsOrMode === 1 || optionsOrMode === 2) ? optionsOrMode : 2;
        if (volumeArg !== undefined) volume = volumeArg;
        if (rateArg !== undefined) rate = rateArg;
      }
    }

    // 档位 0 (静音) 或 1 (仅屏幕通报) 时不发出声音
    if (mode === 0 || mode === 1) {
      return Promise.resolve();
    }

    return new Promise((resolve) => {
      // 唤醒处于 paused/frozen 状态的合成器 (解决 WebView2/Chromium 后台静默 bug)
      if (this.synth!.paused) {
        try {
          this.synth!.resume();
        } catch {
          // ignore
        }
      }

      // 停止上一句播报，避免堆叠延误战机
      this.synth!.cancel();

      const normalizedText = normalizeTacticalSpeech(text);
      const utterance = new SpeechSynthesisUtterance(normalizedText);

      // 强引用挂载防 GC
      this.currentUtterance = utterance;

      const chosenVoice = this.selectVoice(voiceName);
      if (chosenVoice) {
        utterance.voice = chosenVoice;
      }
      utterance.lang = 'zh-CN';
      utterance.volume = Math.max(0, Math.min(1, volume / 100));
      utterance.rate = Math.max(0.5, Math.min(2.0, rate));
      utterance.pitch = Math.max(0.5, Math.min(2.0, pitch));

      const cleanup = () => {
        if (this.currentUtterance === utterance) {
          this.currentUtterance = null;
        }
        resolve();
      };

      utterance.onend = cleanup;
      utterance.onerror = cleanup;

      this.synth!.speak(utterance);
    });
  }

  /**
   * 停止当前所有播报
   */
  public stop() {
    if (this.synth) {
      this.synth.cancel();
      this.currentUtterance = null;
    }
  }

  /**
   * 清理本地缓存与刷新语音列表
   */
  public clearCache(): void {
    this.stop();
    this.voices = [];
    this.refreshVoices();
  }

  /**
   * 获取系统当前检测到的所有语音列表
   */
  public getAvailableVoices(): SpeechSynthesisVoice[] {
    return this.refreshVoices();
  }
}

export const speechService = new SpeechService();
