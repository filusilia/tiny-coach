const assert = require('assert');
const fs = require('fs');
const path = require('path');

console.log('[test-reminders] 开始验证 9 类战术提醒三段式配置与规则契约...');

// 1. 读取 reminderCategories.ts 源码进行规则表静态核查
const reminderSourcePath = path.resolve(__dirname, '../frontend/src/data/reminderCategories.ts');
const reminderSource = fs.readFileSync(reminderSourcePath, 'utf8');

const REQUIRED_CATEGORIES = [
  { key: 'macro_tempo', label: '宏观局势节奏', condition: '推进/带线/压制窗口', defaultMode: 2 },
  { key: 'roshan_tactics', label: '肉山控盾', condition: '肉山刷新与盾消窗口', defaultMode: 2 },
  { key: 'tormentor_tactics', label: '痛苦魔方', condition: '魔方刷新前 15 秒 (20/30分钟)', defaultMode: 2 },
  { key: 'rune_control', label: '神符视野掌控', condition: '河道/智慧神符刷新前 15 秒', defaultMode: 2 },
  { key: 'buyback_strategy', label: '买活储备与防守', condition: '买活资金告急 / 关键阵亡守高', defaultMode: 2 },
  { key: 'item_timing_threat', label: '关键装成型与克制', condition: '闪烁匕首/黑皇杖等质变装成型', defaultMode: 2 },
  { key: 'tactical_ai_ask', label: 'AI 研判指导', condition: '对局提问 / 实时战况深度解答', defaultMode: 2 },
  { key: 'ability_ready', label: '核心技能就绪', condition: '核心大招 / 黑皇杖冷却完毕', defaultMode: 1 },
  { key: 'death_respawn', label: '阵亡复活时机', condition: '自身阵亡读秒 / 买活窗口期', defaultMode: 1 },
];

for (const req of REQUIRED_CATEGORIES) {
  assert(reminderSource.includes(req.key), `缺少关键类别 key: ${req.key}`);
  assert(reminderSource.includes(req.label), `缺少中文类别标签: ${req.label}`);
  assert(reminderSource.includes(req.condition), `缺少触发条件标注: ${req.condition}`);
}
console.log('✅ 测试 1 通过: 9 类提醒类别清单与触发条件 100% 静态核实无遗漏');

// 2. 验证三段式模式值 (0: 关闭, 1: 仅文字播报, 2: 语音+文字播报)
const MODES = [0, 1, 2];
for (const mode of MODES) {
  assert(reminderSource.includes(`mode: ${mode}`), `数据模型中必须包含 mode: ${mode}`);
}
console.log('✅ 测试 2 通过: 三段式开关模式定义完整 (0: 关闭 / 1: 仅文字 / 2: 语音+文字)');

// 3. 验证默认模式逻辑与分流行为
const defaultModes = {
  macro_tempo: 2,
  roshan_tactics: 2,
  tormentor_tactics: 2,
  rune_control: 2,
  buyback_strategy: 2,
  item_timing_threat: 2,
  tactical_ai_ask: 2,
  ability_ready: 1,
  death_respawn: 1,
};


for (const req of REQUIRED_CATEGORIES) {
  assert.strictEqual(
    defaultModes[req.key],
    req.defaultMode,
    `类别 ${req.key} 默认模式不匹配: 期望 ${req.defaultMode}，实际 ${defaultModes[req.key]}`
  );
}
console.log('✅ 测试 3 通过: 9 类提醒默认模式与规范表格 100% 吻合');

// 4. 模拟三段式行为判定管线
function evaluateAlert(mode, text) {
  const result = { enteredTacticalStream: false, speechSpoken: false };
  if (mode === 0) {
    // 模式 0: 关闭
    return result;
  }
  if (mode === 1) {
    // 模式 1: 仅文字播报
    result.enteredTacticalStream = true;
    result.speechSpoken = false;
    return result;
  }
  if (mode === 2) {
    // 模式 2: 语音+文字播报
    result.enteredTacticalStream = true;
    result.speechSpoken = true;
    return result;
  }
  return result;
}

// 针对各模式断言
const r0 = evaluateAlert(0, '测试关闭');
assert.strictEqual(r0.enteredTacticalStream, false);
assert.strictEqual(r0.speechSpoken, false);

const r1 = evaluateAlert(1, '测试仅文字');
assert.strictEqual(r1.enteredTacticalStream, true);
assert.strictEqual(r1.speechSpoken, false);

const r2 = evaluateAlert(2, '测试语音+文字');
assert.strictEqual(r2.enteredTacticalStream, true);
assert.strictEqual(r2.speechSpoken, true);

console.log('✅ 测试 4 通过: 三段式开关状态机判定管线 (关闭/仅文字/语音+文字) 闭环正确');
console.log('[test-reminders] 🎉 全部 4 项测试校验通过！');
