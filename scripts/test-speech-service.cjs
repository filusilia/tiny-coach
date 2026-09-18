const assert = require('assert');

// 简单模拟浏览器 Web Speech API 环境
class MockUtterance {
  constructor(text) {
    this.text = text;
    this.voice = null;
    this.lang = '';
    this.volume = 1;
    this.rate = 1;
    this.pitch = 1;
    this.onend = null;
    this.onerror = null;
  }
}

class MockSpeechSynthesis {
  constructor() {
    this.paused = false;
    this.spokenUtterances = [];
    this.cancelled = false;
    this.onvoiceschanged = null;
  }

  getVoices() {
    return [
      { name: 'Microsoft Xiaoxiao Online (Natural) - Chinese (Mainland)', lang: 'zh-CN', default: true },
      { name: 'Microsoft Yunxi Online (Natural) - Chinese (Mainland)', lang: 'zh-CN', default: false },
      { name: 'Microsoft Yunjian Online (Natural) - Chinese (Mainland)', lang: 'zh-CN', default: false },
      { name: 'Microsoft David - English (United States)', lang: 'en-US', default: false },
    ];
  }

  resume() {
    this.paused = false;
  }

  cancel() {
    this.cancelled = true;
  }

  speak(utterance) {
    this.spokenUtterances.push(utterance);
    setTimeout(() => {
      if (utterance.onend) utterance.onend();
    }, 10);
  }
}

global.window = {
  speechSynthesis: new MockSpeechSynthesis(),
};
global.SpeechSynthesisUtterance = MockUtterance;

console.log('[test-speech] 开始验证语音播报与参数纠偏逻辑...');

// 测试 1: 文本规范化
const TACTICAL_LEXICON = {
  HP: '生命值',
  hp: '生命值',
  MP: '魔法值',
  mp: '魔法值',
  CD: '冷却',
  cd: '冷却',
  BKB: '黑皇杖',
  bkb: '黑皇杖',
  A杖: '阿哈利姆神杖',
  魔晶: '阿哈利姆魔晶',
  TP: '回城卷轴',
  tp: '回城卷轴',
  跳刀: '科勒的匕首',
  羊刀: '邪恶意图',
  肉山: '肉山大魔王',
  买活: '立即买活',
};

function normalizeTacticalSpeech(text) {
  let result = text;
  for (const [key, value] of Object.entries(TACTICAL_LEXICON)) {
    result = result.replaceAll(key, value);
  }
  return result;
}

const normResult = normalizeTacticalSpeech('生命危急 (25% HP)，开启 BKB 跳刀 TP 回泉水！');
assert.strictEqual(
  normResult,
  '生命危急 (25% 生命值)，开启 黑皇杖 科勒的匕首 回城卷轴 回泉水！',
  '词典转换应将 HP, BKB, 跳刀, TP 替换为中文标准称谓'
);
console.log('✅ 测试 1 通过: 战术词典发音平滑替换正常');

// 测试 2: 参数纠偏判定
function parseSpeakArgs(optionsOrMode, volumeArg, rateArg) {
  let mode = 2;
  let volume = 85;
  let rate = 1.05;

  if (typeof optionsOrMode === 'object' && optionsOrMode !== null) {
    if (optionsOrMode.mode !== undefined) mode = optionsOrMode.mode;
    if (optionsOrMode.volume !== undefined) volume = optionsOrMode.volume;
    if (optionsOrMode.rate !== undefined) rate = optionsOrMode.rate;
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

  return { mode, volume, rate };
}

// 模拟历史错误调用: speak(text, voice_speed, voice_volume) -> (text, 1.0, 85)
const healed = parseSpeakArgs(1.0, 85);
assert.strictEqual(healed.mode, 2, '当误传 rate=1.0, vol=85 时，自愈模式必须纠偏为 mode=2 发声');
assert.strictEqual(healed.rate, 1.0, '语速应正确识别为 1.0');
assert.strictEqual(healed.volume, 85, '音量应正确识别为 85');
console.log('✅ 测试 2 通过: 历史参数错位自动纠偏自愈成功，不再静音');

// 测试 3: 静音档位正常静音
const muted0 = parseSpeakArgs(0, 85, 1.0);
assert.strictEqual(muted0.mode, 0, '档位 0 应保持静音');
const muted1 = parseSpeakArgs(1, undefined, undefined);
assert.strictEqual(muted1.mode, 1, '档位 1 (仅屏幕) 应保持静音');
const muted1Standard = parseSpeakArgs(1, 85, 1.0);
assert.strictEqual(muted1Standard.mode, 1, '标准4参数调用模式下档位 1 (仅屏幕) 应保持静音');
console.log('✅ 测试 3 通过: 静音与屏幕档位正常生效');

// 测试 4: 标准 options 传参
const optionsParsed = parseSpeakArgs({ mode: 2, volume: 90, rate: 1.2 });
assert.strictEqual(optionsParsed.mode, 2);
assert.strictEqual(optionsParsed.volume, 90);
assert.strictEqual(optionsParsed.rate, 1.2);
console.log('✅ 测试 4 通过: SpeakOptions 规范对象传参解析正常');

console.log('[test-speech] 🎉 全部 4 项核心测试验证通过！');
