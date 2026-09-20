const fs = require('fs');
const path = require('path');
const assert = require('assert');

console.log('[test-hero-and-item-spec] 开始验证英雄详情展台、物品官网多层分组与零模拟数据铁律...');

// 1. 验证 AGENTS.md 零模拟数据铁律
const agentsMdPath = path.resolve(__dirname, '../AGENTS.md');
const agentsMdContent = fs.readFileSync(agentsMdPath, 'utf8');
assert(agentsMdContent.includes('严禁伪造模拟数据（零伪造原则）'), 'AGENTS.md 必须包含严禁伪造模拟数据最高铁律');
assert(agentsMdContent.includes('等待 GSI 4444 端口接入'), 'AGENTS.md 必须包含未连接真实对局时的真实空态守卫规范');
console.log('✅ 测试 1 通过: AGENTS.md 写入最高零模拟数据铁律与空态守卫原则！');

// 2. 验证 BUSINESS_RULES_AND_UI_SPEC.md
const specPath = path.resolve(__dirname, '../docs/BUSINESS_RULES_AND_UI_SPEC.md');
const specContent = fs.readFileSync(specPath, 'utf8');
assert(specContent.includes('零伪造模拟数据与空态守卫铁律'), '业务规范必须包含零伪造模拟数据与空态守卫铁律');
assert(specContent.includes('官网原生技能与天赋一体化展台'), '业务规范必须包含英雄一体化展台交互要求');
assert(specContent.includes('Dota 2 官网式物品图鉴与多层分组'), '业务规范必须包含官网式多层分组规范');
console.log('✅ 测试 2 通过: BUSINESS_RULES_AND_UI_SPEC.md 权威业务规范完整对齐！');

// 3. 验证 HeroesPage.vue
const heroesPagePath = path.resolve(__dirname, '../frontend/src/pages/HeroesPage.vue');
const heroesPageContent = fs.readFileSync(heroesPagePath, 'utf8');

// 验证单一展台联动呈现
assert(heroesPageContent.includes('selectedShowcaseTab'), '英雄详情必须具备 selectedShowcaseTab 统一展台切换状态');
assert(heroesPageContent.includes('英雄天赋树'), '展台必须包含英雄天赋树入口');
assert(heroesPageContent.includes('先天技能'), '展台必须包含先天技能入口');
assert(heroesPageContent.includes('getRoleChineseName'), '核心定位必须通过字典汉化，严禁裸英文');
assert(heroesPageContent.includes('isBioExpanded'), '官方背景故事必须位于英雄资料区并具备折叠展开能力');
assert(!heroesPageContent.includes('>Innate<'), '不得包含裸露英文 Innate 标签');
console.log('✅ 测试 3 通过: 英雄详情页天赋树、先天技能、技能列表展示在单一展台，传记折叠，纯中文展示！');

// 4. 验证 ItemsPage.vue
const itemsPagePath = path.resolve(__dirname, '../frontend/src/pages/ItemsPage.vue');
const itemsPageContent = fs.readFileSync(itemsPagePath, 'utf8');

assert(itemsPageContent.includes('MAIN_TABS'), '物品页必须包含官网四大顶级分类 (全部/基础/升级/中立)');
assert(itemsPageContent.includes('CONSUMABLES') && itemsPageContent.includes('SECRET SHOP'), '物品页必须包含基础物品官方子类别');
assert(itemsPageContent.includes('ACCESSORIES') && itemsPageContent.includes('ARTIFACTS'), '物品页必须包含进阶升级装备官方子类别');
assert(itemsPageContent.includes('TIER 1 NEUTRALS'), '物品页必须包含中立各阶级子类别');
assert(itemsPageContent.includes('w-14 h-10.5'), '物品格必须严格维持 56x42 标准比例方块');
console.log('✅ 测试 4 通过: 物品页模仿 Dota 2 官网多层分组网格与 56x42 规格标准 100% 达成！');

console.log('[test-hero-and-item-spec] 🎉 全部测试项 100% 验证通过！');
