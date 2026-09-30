/**
 * 赛后战局独立详情页、团队战力走势图与全景数据库自动化测试
 * (scripts/test-match-analysis-detail-and-database.cjs)
 */

const assert = require('assert');
const fs = require('fs');
const path = require('path');

console.log('====================================================');
console.log('   神盾教练：赛后独立详情页与多维数据库验收测试套件   ');
console.log('====================================================\n');

let passCount = 0;
let failCount = 0;

function test(name, fn) {
  try {
    fn();
    console.log(`[PASS] ${name}`);
    passCount++;
  } catch (err) {
    console.error(`[FAIL] ${name} -> ${err.message}`);
    failCount++;
  }
}

// 1. 路由配置契约
test('router/index.ts 必须配置 /analysis/match/:id 独立比赛详情路由', () => {
  const routerPath = path.resolve(__dirname, '../frontend/src/router/index.ts');
  const code = fs.readFileSync(routerPath, 'utf8');
  assert(code.includes('/analysis/match/:id'), '缺少 /analysis/match/:id 路由');
  assert(code.includes("name: 'MatchDetail'"), '路由缺少 MatchDetail 名称');
  assert(code.includes('props: true'), 'MatchDetail 路由必须配置 props: true 接收参数');
});

// 2. 数据类型契约 (OpenDotaPlayer 与 OpenDotaMatchDetail)
test('player.ts 契约必须扩展背包、推塔、治疗量、视野与走势图字段', () => {
  const typesPath = path.resolve(__dirname, '../frontend/src/api/types/player.ts');
  const code = fs.readFileSync(typesPath, 'utf8');
  assert(code.includes('backpack_0?: number'), 'OpenDotaPlayer 必须包含 backpack_0 背包物品');
  assert(code.includes('backpack_1?: number'), 'OpenDotaPlayer 必须包含 backpack_1 背包物品');
  assert(code.includes('backpack_2?: number'), 'OpenDotaPlayer 必须包含 backpack_2 背包物品');
  assert(code.includes('hero_healing?: number'), 'OpenDotaPlayer 必须包含 hero_healing 治疗量');
  assert(code.includes('sen_placed?: number'), 'OpenDotaPlayer 必须包含 sen_placed 真眼插眼');
  assert(code.includes('obs_placed?: number'), 'OpenDotaPlayer 必须包含 obs_placed 假眼插眼');
  assert(code.includes('radiant_gold_adv?: number[]'), 'OpenDotaMatchDetail 必须包含 radiant_gold_adv 经济走势');
  assert(code.includes('radiant_xp_adv?: number[]'), 'OpenDotaMatchDetail 必须包含 radiant_xp_adv 经验走势');
  assert(code.includes('picks_bans?: OpenDotaPickBan[]'), 'OpenDotaMatchDetail 必须包含 picks_bans BP 序列');
});

// 3. AdvantageGraph 纯 SVG 团队走势图
test('AdvantageGraph.vue 必须使用纯 SVG 绘制经济与经验优势走势图', () => {
  const graphPath = path.resolve(__dirname, '../frontend/src/views/analysis/AdvantageGraph.vue');
  assert(fs.existsSync(graphPath), 'AdvantageGraph.vue 文件不存在');
  const code = fs.readFileSync(graphPath, 'utf8');
  assert(code.includes('radiantAreaPath') && code.includes('direAreaPath'), '走势图必须区分天辉绿色优势与夜魇红色优势区域');
  assert(code.includes('zeroY'), '走势图必须包含 0 轴均势基准线');
  assert(code.includes('handleMouseMove') && code.includes('currentHoverPoint'), '走势图必须支持鼠标 Hover 实时准星与数据气泡');
  assert(code.includes('maxRadiant') && code.includes('maxDire'), '走势图必须计算并展示天辉/夜魇最大领先峰值');
});

// 4. MatchDetailPanel 丰富数据库大盘
test('MatchDetailPanel.vue 必须包含 4 大专业 Tab 与置顶 AI 深度复盘中心', () => {
  const panelPath = path.resolve(__dirname, '../frontend/src/views/analysis/MatchDetailPanel.vue');
  assert(fs.existsSync(panelPath), 'MatchDetailPanel.vue 文件不存在');
  const code = fs.readFileSync(panelPath, 'utf8');
  assert(code.includes("activeTab === 'overview'"), '必须包含阵容与全装大盘 Tab');
  assert(code.includes("activeTab === 'advantage'"), '必须包含团队战力与经济走势 Tab');
  assert(code.includes("activeTab === 'advanced'"), '必须包含高阶攻防与视野贡献 Tab');
  assert(code.includes("activeTab === 'draft'"), '必须包含 BP 禁选与加点序列 Tab');
  assert(code.includes('emit(\'backToList\')'), '必须支持返回比赛列表 backToList 事件');
  assert(code.includes('emit(\'generateReview\')'), '必须支持 AI 深度复盘 generateReview 事件');
  assert(code.includes('backpack_0'), '装备展示必须包含 3 格背包物品');
});

// 5. RecentMatchList 列表交互
test('RecentMatchList.vue 必须呈现点击打开独立详情页指引与动效', () => {
  const listPath = path.resolve(__dirname, '../frontend/src/views/analysis/RecentMatchList.vue');
  const code = fs.readFileSync(listPath, 'utf8');
  assert(code.includes('ChevronRight'), '比赛列表卡片右侧必须包含进入详情页右箭头');
  assert(code.includes('点击比赛打开独立详情页与 AI 复盘'), '列表标题必须明确提示点击打开独立详情页');
});

// 6. AnalysisView 架构流转
test('AnalysisView.vue 必须支持列表模式与独立详情页模式双向无缝流转', () => {
  const viewPath = path.resolve(__dirname, '../frontend/src/views/AnalysisView.vue');
  const code = fs.readFileSync(viewPath, 'utf8');
  assert(code.includes('isDetailMode'), '必须定义 isDetailMode 视图状态');
  assert(code.includes('handleBackToList'), '必须提供 handleBackToList 返回列表方法');
  assert(code.includes('router.push'), '点击对局或返回列表时必须同步驱动路由');
  assert(code.includes('route.params.id'), '必须监听 route.params.id 自动载入比赛详情');
});

console.log('\n====================================================');
console.log(`测试结果统计: ${passCount} 项通过，${failCount} 项失败`);
console.log('====================================================');

if (failCount > 0) {
  process.exit(1);
}
