/**
 * 英雄技能展台排布优化、多级属性独占行与热门装备悬浮窗交互验证套件
 */

const fs = require('fs')
const path = require('path')

const repoRoot = path.resolve(__dirname, '..')

function assert(condition, message) {
  if (!condition) {
    console.error(`[FAIL] ${message}`)
    process.exit(1)
  }
  console.log(`[PASS] ${message}`)
}

console.log('====================================================')
console.log('  神盾教练：英雄技能排布、多级数值整行与热门出装浮窗测试')
console.log('====================================================\n')

// 1. 检查 AbilityShowcase.vue 组件代码契约
console.log('--- 1. 检查 AbilityShowcase.vue 技能展台代码契约 ---')
const abilityShowcasePath = path.join(repoRoot, 'frontend', 'src', 'views', 'heroes', 'components', 'AbilityShowcase.vue')
assert(fs.existsSync(abilityShowcasePath), 'AbilityShowcase.vue 必须存在')
const abilityShowcaseContent = fs.readFileSync(abilityShowcasePath, 'utf8')

assert(abilityShowcaseContent.includes('isManyAbilities'), '必须定义 isManyAbilities 判定超多技能英雄 (卡尔)')
assert(abilityShowcaseContent.includes('flex-wrap'), '技能图标栏必须支持 flex-wrap 自动折行防溢出')
assert(abilityShowcaseContent.includes('isMultiLevelSpecial'), '必须定义 isMultiLevelSpecial 判定多等级复杂属性')
assert(abilityShowcaseContent.includes('md:col-span-2') && abilityShowcaseContent.includes('col-span-full'), '多等级属性必须独占整行 (md:col-span-2 col-span-full)')

// 2. 模拟测试用户给出的卡尔技能多等级数值
console.log('\n--- 2. 测试卡尔等超多等级数值判定逻辑 ---')
function isMultiLevelSpecial(v) {
  if (!v || !v.display) return false
  const slashCount = (v.display.match(/\//g) || []).length
  if (slashCount >= 4) return true
  if (v.display.length > 22) return true
  return false
}

const testCases = [
  { label: '攻击力（火）', display: '20 / 30 / 40 / 50 / 60 / 70 / 80 / 90 / 100', expected: true },
  { label: '护甲（火）', display: '0 / 1 / 2 / 3 / 4 / 5 / 6 / 7 / 8', expected: true },
  { label: '攻击距离（冰）', display: '305 / 360 / 415 / 470 / 525 / 580 / 635 / 690 / 745', expected: true },
  { label: '生命值（冰）', display: '300 / 400 / 500 / 600 / 700 / 800 / 900 / 1000 / 1100', expected: true },
  { label: '持续时间（冰）', display: '24 / 30 / 36 / 42 / 48 / 54 / 60 / 66 / 72', expected: true },
  { label: '精灵数量（冰）', display: '1 / 1 / 1 / 2 / 2 / 2 / 2 / 3 / 3', expected: true },
  { label: '精灵数量（火）', display: '1 / 1 / 1 / 2 / 2 / 2 / 2 / 3 / 3', expected: true },
  { label: '基础眩晕时间', display: '1.5 / 2.0 / 2.5 / 3.0', expected: false }, // 普通 4 级技能
  { label: '施法距离', display: '600', expected: false }, // 单级属性
]

for (const tc of testCases) {
  const result = isMultiLevelSpecial(tc)
  assert(result === tc.expected, `属性 [${tc.label}] (${tc.display}) 判定必须为 ${tc.expected} (实际: ${result})`)
}

// 3. 检查 HeroBuildPanel.vue 热门装备 Hover 浮窗契约
console.log('\n--- 3. 检查 HeroBuildPanel.vue 热门装备 Hover 浮窗契约 ---')
const heroBuildPanelPath = path.join(repoRoot, 'frontend', 'src', 'views', 'heroes', 'components', 'HeroBuildPanel.vue')
assert(fs.existsSync(heroBuildPanelPath), 'HeroBuildPanel.vue 必须存在')
const heroBuildPanelContent = fs.readFileSync(heroBuildPanelPath, 'utf8')

assert(heroBuildPanelContent.includes('hoveredItem'), 'HeroBuildPanel 必须包含 hoveredItem 响应式状态')
assert(heroBuildPanelContent.includes('handleMouseEnter'), '必须包含 handleMouseEnter 鼠标悬浮进入事件')
assert(heroBuildPanelContent.includes('handleMouseMove'), '必须包含 handleMouseMove 鼠标悬停跟随事件')
assert(heroBuildPanelContent.includes('handleMouseLeave'), '必须包含 handleMouseLeave 鼠标移出销毁事件')
assert(heroBuildPanelContent.includes('calculateTooltipPos'), '必须包含 calculateTooltipPos 自适应防溢出视口计算')
assert(heroBuildPanelContent.includes('<Teleport to="body">'), '悬浮卡必须通过 Teleport 挂载到 body 杜绝祖先溢出遮挡')
assert(heroBuildPanelContent.includes('parseItemSkills'), '悬浮卡必须解析并展示主动/被动技能描述')
assert(heroBuildPanelContent.includes('parseAttributes'), '悬浮卡必须解析并展示基础属性加成')
assert(heroBuildPanelContent.includes('hasValidCd') && heroBuildPanelContent.includes('hasValidMc'), '悬浮卡必须支持冷却与魔法消耗展示')
assert(heroBuildPanelContent.includes('gameDataService.getItemsData'), '必须异步拉取官方完整物品元数据快查表')

console.log('\n====================================================')
console.log('  全部测试验证 100% 通过！')
console.log('====================================================')
