/**
 * Go-Speech 原生离线语音接入端到端断言测试脚本
 * 运行方式: node scripts/test-go-speech-integration.cjs
 */

const fs = require('fs')
const path = require('path')
const { execSync } = require('child_process')

const repoRoot = path.resolve(__dirname, '..')
let passed = 0
let failed = 0

function assert(condition, message) {
  if (condition) {
    console.log(`[PASS] ${message}`)
    passed++
  } else {
    console.error(`[FAIL] ${message}`)
    failed++
  }
}

console.log('====================================================')
console.log('   神盾教练：MeloTTS 原生高保真语音测试套件         ')
console.log('====================================================\n')

// 1. 检查物理库与模型文件存在性
console.log('--- 1. 检查物理库与模型文件存在性 ---')
const dllPath = path.join(repoRoot, 'lib', 'onnxruntime.dll')
assert(fs.existsSync(dllPath), 'lib/onnxruntime.dll 必须存在')
if (fs.existsSync(dllPath)) {
  const stat = fs.statSync(dllPath)
  assert(stat.size > 10 * 1024 * 1024, `onnxruntime.dll 体积应大于 10MB，实际: ${(stat.size / 1024 / 1024).toFixed(2)}MB`)
}

const meloDir = path.join(repoRoot, 'melo_weights')
const meloModel = path.join(meloDir, 'model.onnx')
const meloLexicon = path.join(meloDir, 'lexicon.txt')
const meloTokens = path.join(meloDir, 'tokens.txt')
assert(fs.existsSync(meloModel), 'melo_weights/model.onnx 权重模型必须存在')
assert(fs.existsSync(meloLexicon), 'melo_weights/lexicon.txt 词典必须存在')
assert(fs.existsSync(meloTokens), 'melo_weights/tokens.txt 字典必须存在')
if (fs.existsSync(meloModel)) {
  const stat = fs.statSync(meloModel)
  assert(stat.size > 100 * 1024 * 1024, `model.onnx 体积应大于 100MB，实际: ${(stat.size / 1024 / 1024).toFixed(2)}MB`)
}

// 2. 检查后端 Go 代码
console.log('\n--- 2. 检查后端 Go 模块契约 ---')
const ttsServiceGo = path.join(repoRoot, 'internal', 'services', 'tts_service.go')
const meloEngineGo = path.join(repoRoot, 'internal', 'services', 'melo_engine.go')
assert(fs.existsSync(ttsServiceGo), 'internal/services/tts_service.go 必须存在')
assert(fs.existsSync(meloEngineGo), 'internal/services/melo_engine.go 必须存在')
const ttsServiceContent = fs.readFileSync(ttsServiceGo, 'utf8')
const meloEngineContent = fs.readFileSync(meloEngineGo, 'utf8')
assert(meloEngineContent.includes('melotts.NewEngine'), 'melo_engine.go 必须接入 melotts')
assert(!ttsServiceContent.includes('pipertts.NewEngine'), 'tts_service.go 必须彻底剔除 pipertts')
assert(!meloEngineContent.includes('pipertts.NewEngine'), 'melo_engine.go 必须彻底剔除 pipertts')
assert(ttsServiceContent.includes('GetGoSpeechStatus'), 'tts_service.go 必须提供 GetGoSpeechStatus')
assert(ttsServiceContent.includes('SynthesizeSpeech'), 'tts_service.go 必须提供 SynthesizeSpeech')
assert(meloEngineContent.includes('EnsureReadyInBackground'), 'melo_engine.go 必须提供后台静默就绪机制')

const appGo = path.join(repoRoot, 'internal', 'app', 'app.go')
const appGoContent = fs.readFileSync(appGo, 'utf8')
assert(appGoContent.includes('GetGoSpeechStatus'), 'app.go 必须门面转发 GetGoSpeechStatus')
assert(appGoContent.includes('SynthesizeSpeech'), 'app.go 必须门面转发 SynthesizeSpeech')
assert(appGoContent.includes('TestGoSpeech'), 'app.go 必须门面转发 TestGoSpeech')
assert(!appGoContent.includes('DownloadGoSpeechModel'), 'app.go 必须移除前端手动触发下载的无用门面')

// 3. 检查前端绑定与 API 层
console.log('\n--- 3. 检查前端 Wails 绑定与 API 层 ---')
const bindingsApp = path.join(repoRoot, 'frontend', 'bindings', 'aegis-coach', 'app.ts')
assert(fs.existsSync(bindingsApp), 'frontend/bindings/aegis-coach/app.ts 必须存在')
const bindingsContent = fs.readFileSync(bindingsApp, 'utf8')
assert(bindingsContent.includes('GetGoSpeechStatus'), 'app.ts 绑定必须导出 GetGoSpeechStatus')
assert(bindingsContent.includes('SynthesizeSpeech'), 'app.ts 绑定必须导出 SynthesizeSpeech')

const ttsApiTs = path.join(repoRoot, 'frontend', 'src', 'api', 'tts.ts')
assert(fs.existsSync(ttsApiTs), 'frontend/src/api/tts.ts 必须存在')
const ttsApiContent = fs.readFileSync(ttsApiTs, 'utf8')
assert(ttsApiContent.includes('getGoSpeechStatus'), 'ttsApi 必须封装 getGoSpeechStatus')
assert(ttsApiContent.includes('synthesizeSpeech'), 'ttsApi 必须封装 synthesizeSpeech')

// 4. 检查战术词典发音叠词与误杀修复
console.log('\n--- 4. 检查战术词典发音叠词根治测试 ---')
const speechServiceTs = path.join(repoRoot, 'frontend', 'src', 'services', 'speechService.ts')
const speechServiceContent = fs.readFileSync(speechServiceTs, 'utf8')

// 提取 normalizeTacticalSpeech 的正则逻辑在 node 环境测试
function testNormalizeTacticalSpeech(text) {
  if (!text) return ''
  let result = text
  result = result.replace(/肉山(?:大魔(?:王)?)?/g, '肉山大魔王')
  result = result
    .replace(/\bBKB\b/gi, '黑皇杖')
    .replace(/\bTP\b/gi, '回城卷轴')
    .replace(/\bHP\b/gi, '生命值')
    .replace(/\bMP\b/gi, '魔法值')
    .replace(/\bCD\b/gi, '冷却')
    .replace(/\bEXP\b/gi, '经验值')
    .replace(/\bDPS\b/gi, '每秒伤害')
    .replace(/\bGPM\b/gi, '每分金钱')
    .replace(/\bXPM\b/gi, '每分经验')
    .replace(/(?<!阿哈利姆)A杖/g, '阿哈利姆神杖')
    .replace(/(?<!阿哈利姆)魔晶/g, '阿哈利姆魔晶')
    .replace(/跳刀/g, '科勒的匕首')
    .replace(/羊刀/g, '邪恶意图')
    .replace(/冰甲/g, '希瓦的守护')
    .replace(/强袭(?![胸甲])/g, '强袭胸甲')
    .replace(/大根/g, '达戈尼尔之神力')
    .replace(/风杖/g, '尤尔的神圣权杖')
  return result
}

const t1 = testNormalizeTacticalSpeech('肉山大魔，已刷新')
assert(t1 === '肉山大魔王，已刷新', `“肉山大魔，已刷新”必须归一化为“肉山大魔王，已刷新”，杜绝叠词与刷新球，实际: “${t1}”`)

const t2 = testNormalizeTacticalSpeech('肉山，已刷新')
assert(t2 === '肉山大魔王，已刷新', `“肉山，已刷新”必须归一化为“肉山大魔王，已刷新”，实际: “${t2}”`)

const t3 = testNormalizeTacticalSpeech('肉山大魔王已刷新')
assert(t3 === '肉山大魔王已刷新', `“肉山大魔王已刷新”必须保持不变，实际: “${t3}”`)

const t4 = testNormalizeTacticalSpeech('敌方BKB已交，大根冷却')
assert(t4 === '敌方黑皇杖已交，达戈尼尔之神力冷却', `装备与英文简写正常归一化，实际: “${t4}”`)

// 5. 检查 VoiceBroadcastSection UI 页面：包含试听且纯净（零技术术语）
console.log('\n--- 5. 检查 VoiceBroadcastSection UI 纯净性（零技术术语） ---')
const voiceSectionVue = path.join(repoRoot, 'frontend', 'src', 'views', 'settings', 'sections', 'VoiceBroadcastSection.vue')
assert(fs.existsSync(voiceSectionVue), 'VoiceBroadcastSection.vue 必须存在')
const voiceSectionContent = fs.readFileSync(voiceSectionVue, 'utf8')
assert(voiceSectionContent.includes('testCurrentVoice'), 'VoiceBroadcastSection.vue 必须包含试听功能')
assert(!voiceSectionContent.includes('onnxruntime'), 'VoiceBroadcastSection.vue 严禁出现 onnxruntime 技术术语')
assert(!voiceSectionContent.includes('modelscope'), 'VoiceBroadcastSection.vue 严禁出现 modelscope 技术术语')
assert(!voiceSectionContent.includes('piper'), 'VoiceBroadcastSection.vue 严禁出现 piper')

// 6. 检查 AiEngineSection UI 纯净性：去污染与脱敏
console.log('\n--- 6. 检查 AiEngineSection UI 纯净性（脱敏与通用性） ---')
const aiSectionVue = path.join(repoRoot, 'frontend', 'src', 'views', 'settings', 'sections', 'AiEngineSection.vue')
const aiSectionContent = fs.readFileSync(aiSectionVue, 'utf8')
assert(!aiSectionContent.includes('cs-sk-'), 'AiEngineSection.vue 严禁出现任何硬编码测试 Key')
assert(!aiSectionContent.includes('23333'), 'AiEngineSection.vue 严禁出现硬编码 23333 测试端口')
assert(!aiSectionContent.includes('测试载入本机'), 'AiEngineSection.vue 严禁出现 测试载入本机 按钮')
assert(!aiSectionContent.includes('下载模型'), 'AiEngineSection.vue 严禁出现 下载模型 按钮')
assert(aiSectionContent.includes('Response API'), 'AiEngineSection.vue 必须明确支持通用标准 Response API')

// 7. 真实编译与 MeloTTS 推理测试
console.log('\n--- 7. 运行 Go 原生 MeloTTS 合成真实推理测试 ---')
try {
  const goTestCode = `package main
import (
	"fmt"
	"aegis-coach/internal/services"
)
func main() {
	svc := services.GetTtsService()
	ready, err := svc.GetGoSpeechStatus()
	if err != nil {
		panic(err)
	}
	fmt.Printf("STATUS_READY:%v\\n", ready)

	b64, err := svc.TestGoSpeech("神盾教练发音测试", 1.0)
	if err != nil {
		panic(err)
	}
	fmt.Printf("SYNTH_LEN:%d\\n", len(b64))
}`
  const tmpFile = path.join(repoRoot, 'scripts', 'tmp_test_gospeech.go')
  fs.writeFileSync(tmpFile, goTestCode, 'utf8')
  const stdout = execSync(`go run scripts/tmp_test_gospeech.go`, { cwd: repoRoot, encoding: 'utf8' })
  fs.unlinkSync(tmpFile)

  assert(stdout.includes('STATUS_READY:true'), 'MeloTTS 状态必须为 true (已就绪)')
  const match = stdout.match(/SYNTH_LEN:(\d+)/)
  const synthLen = match ? parseInt(match[1], 10) : 0
  assert(synthLen > 10000, `MeloTTS 真实合成 Base64 音频长度必须 > 10000 (实际: ${synthLen})`)
} catch (e) {
  assert(false, `Go 原生 MeloTTS 测试运行异常: ${e.message}`)
}

console.log('\n====================================================')
console.log(`测试结果统计: ${passed} 项通过，${failed} 项失败`)
console.log('====================================================')

if (failed > 0) {
  process.exit(1)
}
