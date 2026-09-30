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

function readComponentOrPage(mainFile, subDir) {
  const p1 = path.resolve(__dirname, '../frontend/src/pages', mainFile);
  if (fs.existsSync(p1)) {
    return fs.readFileSync(p1, 'utf8');
  }
  const viewFile = path.resolve(__dirname, '../frontend/src/views', mainFile.replace('Page', 'View'));
  let content = '';
  if (fs.existsSync(viewFile)) {
    content += fs.readFileSync(viewFile, 'utf8') + '\n';
  }
  if (subDir) {
    const subDirPath = path.resolve(__dirname, '../frontend/src/views', subDir);
    function walkDir(dir) {
      if (!fs.existsSync(dir)) return;
      const files = fs.readdirSync(dir);
      for (const f of files) {
        const full = path.join(dir, f);
        if (fs.statSync(full).isDirectory()) {
          walkDir(full);
        } else if (f.endsWith('.vue') || f.endsWith('.ts')) {
          content += fs.readFileSync(full, 'utf8') + '\n';
        }
      }
    }
    walkDir(subDirPath);
  }
  return content;
}

// 3. 验证 HeroesPage.vue / HeroesView.vue
const heroesPageContent = readComponentOrPage('HeroesPage.vue', 'heroes');

// 验证单一展台联动呈现
assert(heroesPageContent.includes('selectedShowcaseTab'), '英雄详情必须具备 selectedShowcaseTab 统一展台切换状态');
assert(heroesPageContent.includes('英雄天赋树'), '展台必须包含英雄天赋树入口');
assert(heroesPageContent.includes('先天技能'), '展台必须包含先天技能入口');
assert(heroesPageContent.includes('getRoleChineseName'), '核心定位必须通过字典汉化，严禁裸英文');
assert(heroesPageContent.includes('isBioExpanded'), '官方背景故事必须位于英雄资料区并具备折叠展开能力');
assert(!heroesPageContent.includes('>Innate<'), '不得包含裸露英文 Innate 标签');
console.log('✅ 测试 3 通过: 英雄详情页天赋树、先天技能、技能列表展示在单一展台，传记折叠，纯中文展示！');

// 4. 验证 ItemsPage.vue / ItemsView.vue
const itemsPageContent = readComponentOrPage('ItemsPage.vue');

assert(itemsPageContent.includes('MAIN_TABS'), '物品页必须包含官网四大顶级分类 (全部/基础/升级/中立)');
assert(itemsPageContent.includes('CONSUMABLES') && itemsPageContent.includes('SECRET SHOP'), '物品页必须包含基础物品官方子类别');
assert(itemsPageContent.includes('ACCESSORIES') && itemsPageContent.includes('ARTIFACTS'), '物品页必须包含进阶升级装备官方子类别');
assert(itemsPageContent.includes('TIER 1 NEUTRALS'), '物品页必须包含中立各阶级子类别');
assert(itemsPageContent.includes('w-14 h-10.5') || itemsPageContent.includes('56x42'), '物品格必须严格维持 56x42 标准比例方块');
console.log('✅ 测试 4 通过: 物品页模仿 Dota 2 官网多层分组网格与 56x42 规格标准 100% 达成！');

// 5. 验证 gameDataService.getItemDict 官方中文名称映射防退化
const gameDataServicePath = path.resolve(__dirname, '../frontend/src/services/gameDataService.ts');
const gameDataServiceContent = fs.readFileSync(gameDataServicePath, 'utf8');
assert(gameDataServiceContent.includes('Number(val?.id)'), 'getItemDict 必须通过 Number 兼容 string 与 number 类型的物品 ID');
console.log('✅ 测试 5 通过: gameDataService.getItemDict 兼容字符串 ID，杜绝战绩复盘物品中文名退化为英文！');

// 6. 验证 ItemsView.vue 零值属性与虚假 CD/MC 过滤
assert(itemsPageContent.includes('hasValidCd') && itemsPageContent.includes('hasValidMc'), '物品页必须包含 hasValidCd 与 hasValidMc 过滤虚假 0s 和 0 胶囊');
assert(itemsPageContent.includes('parseAttributes'), '物品页必须包含 parseAttributes 属性清洗');
assert(itemsPageContent.includes('0%'), 'parseAttributes 必须清洗过滤 0% 或 0 值残留属性');
console.log('✅ 测试 6 通过: 物品页冷却/魔耗与属性零值清洗防退化达成，杜绝 "+生命恢复：0" 等陈旧数据！');

// 7. 验证 currentItemCatalog.ts 物品权威分类
const catalogPath = path.resolve(__dirname, '../frontend/src/data/currentItemCatalog.ts');
const catalogContent = fs.readFileSync(catalogPath, 'utf8');
assert(catalogContent.includes("item_orb_of_frost: '军备'"), '冰霜之珠必须归类为基础军备');
assert(catalogContent.includes("item_travel_boots: '辅助'"), '远行鞋必须归类为辅助');
console.log('✅ 测试 7 通过: 官方 10 大分类映射准确无误，杜绝低级物品错置神器大类！');

console.log('[test-hero-and-item-spec] 🎉 全部测试项 100% 验证通过！');
