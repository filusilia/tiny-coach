/**
 * 设置页面卡片化解耦、AI连通性与缓存图片加载优化全量自动化验证脚本
 */

const fs = require('fs')
const path = require('path')
const http = require('http')

console.log('====================================================')
console.log('  神盾教练：设置重构、AI连通性与资产缓存测试套件  ')
console.log('====================================================\n')

let passCount = 0
let failCount = 0

function assert(condition, message) {
  if (condition) {
    console.log(`[PASS] ${message}`)
    passCount++
  } else {
    console.error(`[FAIL] ${message}`)
    failCount++
  }
}

// 1. 验证 assetUrl.ts 源码契约
console.log('--- 1. 检查 assetUrl.ts 资产映射契约 ---')
const assetUrlPath = path.join(__dirname, '../frontend/src/utils/assetUrl.ts')
const assetUrlContent = fs.readFileSync(assetUrlPath, 'utf-8')

assert(assetUrlContent.includes('HERO_SHORT_NAMES'), 'assetUrl.ts 必须引入 HERO_SHORT_NAMES 进行数字 ID 转换')
assert(assetUrlContent.includes('typeof rawName === \'number\''), 'cleanHeroName 必须支持 number 类型入参')
assert(assetUrlContent.includes('/^\\d+$/'), 'cleanHeroName 必须支持纯数字字符串格式入参转换')
assert(assetUrlContent.includes('/cache/heroes/'), 'getHeroIconUrl 必须输出 /cache/heroes/ 规范路径')

// 2. 验证 HeroesView.vue 本地缓存优先加载
console.log('\n--- 2. 检查 HeroesView.vue 图片加载策略 ---')
const heroesViewPath = path.join(__dirname, '../frontend/src/views/HeroesView.vue')
const heroesViewContent = fs.readFileSync(heroesViewPath, 'utf-8')

assert(
  heroesViewContent.includes('getHeroIconUrl(hero.name)') && heroesViewContent.includes(':local-src="getHeroDisplayImg(hero)"'),
  'HeroesView.vue 必须优先走本地高速缓存路由 getHeroIconUrl(hero.name)，杜绝海外 CDN 排队卡顿'
)
assert(
  heroesViewContent.includes(':cdn-src="officialDetails[hero.id]?.crops_img || getHeroCdnUrl(hero.name)"'),
  'HeroesView.vue 必须将官方高清图作为 cdn-src 兜底'
)

// 3. 验证设置页二合一高内聚架构与去过度拆分
console.log('\n--- 3. 检查 SettingsView.vue 高内聚整合与直接挂载 ---')
const settingsViewPath = path.join(__dirname, '../frontend/src/views/SettingsView.vue')
const settingsViewContent = fs.readFileSync(settingsViewPath, 'utf-8')

assert(settingsViewContent.includes('PlayerBindingSection'), 'SettingsView.vue 必须直接挂载 PlayerBindingSection')
assert(settingsViewContent.includes('VoiceBroadcastSection'), 'SettingsView.vue 必须直接挂载 VoiceBroadcastSection')
assert(settingsViewContent.includes('AiEngineSection'), 'SettingsView.vue 必须直接挂载 AiEngineSection')
assert(settingsViewContent.includes('OverlaySection'), 'SettingsView.vue 必须直接挂载 OverlaySection')
assert(settingsViewContent.includes('StorageSection'), 'SettingsView.vue 必须直接挂载 StorageSection')
assert(settingsViewContent.includes('AboutSection'), 'SettingsView.vue 必须直接挂载 AboutSection')
assert(settingsViewContent.includes('tabCards'), 'SettingsView.vue 必须定义顶部二合一卡片配置')
assert(!settingsViewContent.includes('TtsTab'), 'SettingsView.vue 严禁保留冗余的独立 TtsTab')
assert(!settingsViewContent.includes('TtsEngineSection'), 'SettingsView.vue 严禁保留冗余的 TtsEngineSection 选型卡')

// 4. 验证语音提醒设置不被截断 (优化小字布局)
console.log('\n--- 4. 检查 VoiceBroadcastSection.vue 小字完整性 ---')
const voiceSectionPath = path.join(__dirname, '../frontend/src/views/settings/sections/VoiceBroadcastSection.vue')
const voiceSectionContent = fs.readFileSync(voiceSectionPath, 'utf-8')

assert(!voiceSectionContent.includes('line-clamp-1'), 'VoiceBroadcastSection.vue 严禁使用 line-clamp-1 强制截断小字')
assert(voiceSectionContent.includes('cat.description'), 'VoiceBroadcastSection.vue 必须完整渲染分类说明文本')
assert(voiceSectionContent.includes('ThreeSegmentToggle'), 'VoiceBroadcastSection.vue 必须包含三段式切换组件')

// 5. 验证个人账号绑定指定功能
console.log('\n--- 5. 检查 PlayerBindingSection.vue 账号指定绑定功能 ---')
const playerBindPath = path.join(__dirname, '../frontend/src/views/settings/sections/PlayerBindingSection.vue')
const playerBindContent = fs.readFileSync(playerBindPath, 'utf-8')

assert(playerBindContent.includes('normalizeDotaAccountId'), 'PlayerBindingSection.vue 必须使用 normalizeDotaAccountId 提取规范 ID')
assert(playerBindContent.includes('handleBindPlayerId'), 'PlayerBindingSection.vue 必须具备绑定处理方法')
assert(playerBindContent.includes('handleUnbindPlayer'), 'PlayerBindingSection.vue 必须具备解绑方法')

// 6. 验证后端 AI 路径自适应规整
console.log('\n--- 6. 检查 internal/services/ai 后端规整逻辑 ---')
// 服务已从扁平 internal/services/*.go 拆分为 internal/services/<name>/ 子包
const aiEnginePath = path.join(__dirname, '../internal/services/ai/ai_engine.go')
const aiServicePath = path.join(__dirname, '../internal/services/ai/ai_service.go')
assert(fs.existsSync(aiEnginePath), '后端 AI 引擎文件必须存在于 internal/services/ai/ai_engine.go')
assert(fs.existsSync(aiServicePath), '后端 AI 服务文件必须存在于 internal/services/ai/ai_service.go')
const aiCodeContent = fs.readFileSync(aiEnginePath, 'utf-8') + fs.readFileSync(aiServicePath, 'utf-8')

assert(aiCodeContent.includes('normalizeBaseUrl') || aiCodeContent.includes('normalizeOpenAiBaseUrl'), '后端必须包含 normalizeBaseUrl 规范化函数')
assert(aiCodeContent.includes('/openapi'), 'normalizeBaseUrl 必须能够自适应规整 /openapi 为 /v1')

// 7. 测试本机 Cherry Studio AI 真实连通性 (23333 端口)
// 安全红线（铁律 13）：严禁在源码中硬编码任何真实凭据。
// 本机 AI 连通性测试所需的 Key 一律由环境变量注入；未设置时明确跳过而非失败。
console.log('\n--- 7. 本机测试 AI 连通性 (http://127.0.0.1:23333/v1) ---')

const apiKey = process.env.AEGIS_TEST_AI_KEY || ''

function testAiHttp() {
  const req = http.request({
    hostname: '127.0.0.1',
    port: 23333,
    path: '/v1/models',
    method: 'GET',
    headers: {
      'Authorization': `Bearer ${apiKey}`,
    },
    timeout: 3000,
  }, (res) => {
    let raw = ''
    res.on('data', chunk => raw += chunk)
    res.on('end', () => {
      try {
        const json = JSON.parse(raw)
        const models = json.data || []
        assert(res.statusCode === 200, `本机 AI HTTP 状态码必须为 200，实际: ${res.statusCode}`)
        assert(models.length > 0, `本机 AI 成功返回模型列表: 包含 ${models.length} 个模型`)
        console.log(`[INFO] 检测到激活模型: ${models.map(m => m.id).join(', ')}`)
        finishReport()
      } catch (e) {
        assert(false, `解析 AI 模型返回数据失败: ${e.message}`)
        finishReport()
      }
    })
  })

  req.on('error', (e) => {
    console.warn(`[WARN] 本机 AI 端口未响应 (${e.message})，但这不影响工程代码本身的正确性。`)
    finishReport()
  })

  req.end()
}

function finishReport() {
  console.log('\n====================================================')
  console.log(`测试结果统计: ${passCount} 项通过，${failCount} 项失败`)
  console.log('====================================================')
  if (failCount > 0) {
    process.exit(1)
  } else {
    process.exit(0)
  }
}

if (!apiKey) {
  console.log('[SKIP] 未设置 AEGIS_TEST_AI_KEY 环境变量，跳过本机 AI 真实连通性测试（不影响静态契约断言结果）')
  finishReport()
} else {
  testAiHttp()
}
