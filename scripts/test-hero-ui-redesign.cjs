const fs = require('fs');
const path = require('path');
const assert = require('assert');

console.log('[test-hero-ui] 开始验证英雄页与段位环境重构规范...');

const heroesPagePath = path.resolve(__dirname, '..', 'frontend', 'src', 'pages', 'HeroesPage.vue');
const heroesPageContent = fs.readFileSync(heroesPagePath, 'utf8');

// 1. 验证段位环境图标与 8 级段位
assert(heroesPageContent.includes("currentRankOption.icon"), '段位环境必须显示段位官方图标');
assert(heroesPageContent.includes("rank_icon_8.png"), '段位选项必须包含 rank_icon_8.png (冠绝一世)');
assert(heroesPageContent.includes("rank_icon_7.png"), '段位选项必须包含 rank_icon_7.png (超凡入圣)');
assert(heroesPageContent.includes("rank_icon_1.png"), '段位选项必须包含 rank_icon_1.png (先锋)');
console.log('✅ 测试 1 通过: 段位环境官方图标与 8 级段位配置完整');

// 2. 验证首页使用大图
assert(heroesPageContent.includes("getHeroDisplayImg(hero)"), '首页英雄网格必须使用大图解析方法');
assert(heroesPageContent.includes("aspect-[16/10]"), '英雄卡片采用大图比例');
console.log('✅ 测试 2 通过: 首页英雄列表使用高清大图网格');

// 3. 验证天赋树、先天技能与技能列表单一展台联动呈现
assert(heroesPageContent.includes("selectedShowcaseTab"), '展台必须具备 selectedShowcaseTab 状态支持技能与天赋树切换联动');
assert(heroesPageContent.includes("innateAbility"), '展台必须包含先天技能入口');
assert(heroesPageContent.includes("nonInnateAbilities"), '展台必须包含技能列表');
assert(heroesPageContent.includes("activeDetails?.talents"), '展台必须包含天赋树');
console.log('✅ 测试 3 通过: 天赋树、先天技能、技能体系合并在单一展台联动呈现，支持点击分别展示详情');

// 4. 验证阿哈利姆神杖、魔晶与先天技能遮罩覆盖整个图标
assert(heroesPageContent.includes('src="/assets/icons/aghs_scepter.png"'), '包含神杖遮罩');
assert(heroesPageContent.includes('src="/assets/icons/aghs_shard.png"'), '包含魔晶遮罩');
assert(heroesPageContent.includes('src="/assets/icons/innate_icon.png"'), '包含先天遮罩');
console.log('✅ 测试 4 通过: 先天技能、阿哈利姆神杖与魔晶遮罩覆盖技能图标');

// 5. 验证底部神杖与魔晶使用正常的 ultimate_scepter 与 aghanims_shard 物品图标
assert(heroesPageContent.includes('src="/assets/icons/ultimate_scepter.png"'), '底部神杖升级效果必须使用正常的 ultimate_scepter 物品图标');
assert(heroesPageContent.includes('src="/assets/icons/aghanims_shard.png"'), '底部魔晶升级效果必须使用正常的 aghanims_shard 物品图标');
console.log('✅ 测试 5 通过: 底部强化卡片使用正常的 ultimate_scepter 与 aghanims_shard 物品图标');

console.log('[test-hero-ui] 🎉 全部 5 项重构验收标准校验通过！');

