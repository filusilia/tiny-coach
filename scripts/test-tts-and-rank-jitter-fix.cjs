/**
 * TTS 超保真拟人参数修复与英雄天梯分段选择器防抖动测试套件
 * 运行方式: node scripts/test-tts-and-rank-jitter-fix.cjs
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
console.log('   神盾教练：TTS 超保真拟人与段位选择防抖动测试套件   ')
console.log('====================================================\n')

// 1. 检查 speechService.ts 代码契约
console.log('--- 1. 检查 speechService.ts 合成调用与 Store 联动契约 ---')
const speechServiceTs = path.join(repoRoot, 'frontend', 'src', 'services', 'speechService.ts')
assert(fs.existsSync(speechServiceTs), 'speechService.ts 必须存在')
const speechContent = fs.readFileSync(speechServiceTs, 'utf8')

assert(speechContent.includes('useAppStore'), 'speechService.ts 必须引入 useAppStore')
assert(speechContent.includes('ttsApi.synthesizeSpeech(cleanText, rate)'), 'speechService.ts speak 方法必须为双参数调用 (cleanText, rate)，严禁携带旧 melotts 参数')
assert(!speechContent.includes('ttsApi.synthesizeSpeech(cleanText, \'melotts\''), 'speechService.ts 严禁出现残余的三参数 synthesizeSpeech 调用')
assert(speechContent.includes('appStore.settings.voice_volume'), 'speechService.ts speak 必须具备从 appStore 自动读取全局 voice_volume 的兜底机制')
assert(speechContent.includes('appStore.settings.voice_speed'), 'speechService.ts speak 必须具备从 appStore 自动读取全局 voice_speed 的兜底机制')

// 2. 检查 VoiceBroadcastSection.vue 报错与提示 (已完成整合)
console.log('\n--- 2. 检查 VoiceBroadcastSection.vue 语音引擎与提示 ---')
const voiceSectionVue = path.join(repoRoot, 'frontend', 'src', 'views', 'settings', 'sections', 'VoiceBroadcastSection.vue')
assert(fs.existsSync(voiceSectionVue), 'VoiceBroadcastSection.vue 必须存在')
const voiceSectionContent = fs.readFileSync(voiceSectionVue, 'utf8')
assert(voiceSectionContent.includes('testNativeMelo') || voiceSectionContent.includes('speechService'), 'VoiceBroadcastSection.vue 必须包含语音试听逻辑')

// 3. 运行 Go 后端 MeloTTS 真实双参数合成测试
console.log('\n--- 3. 运行 Go 后端原生 MeloTTS 合成推理测试 ---')
try {
  const goTestCode = `package main
import (
	"fmt"
	"aegis-coach/internal/services"
)
func main() {
	svc := services.GetTtsService()
	b64, err := svc.SynthesizeSpeech("神盾教练超保真拟人测试，肉山大魔王已刷新。", 1.0)
	if err != nil {
		panic(err)
	}
	fmt.Printf("SYNTH_LEN:%d\\n", len(b64))
}`
  const tmpFile = path.join(repoRoot, 'scripts', 'tmp_test_tts_args.go')
  fs.writeFileSync(tmpFile, goTestCode, 'utf8')
  const stdout = execSync(`go run scripts/tmp_test_tts_args.go`, { cwd: repoRoot, encoding: 'utf8' })
  fs.unlinkSync(tmpFile)

  const match = stdout.match(/SYNTH_LEN:(\d+)/)
  const synthLen = match ? parseInt(match[1], 10) : 0
  assert(synthLen > 10000, `Go 端 SynthesizeSpeech 真实合成 Base64 音频长度必须 > 10000 (实际: ${synthLen})`)
} catch (e) {
  assert(false, `Go 原生测试运行异常: ${e.message}`)
}

// 4. 检查 HeroesView.vue 天梯分段选择器定宽与防抖动契约
console.log('\n--- 4. 检查 HeroesView.vue 天梯分段选择器防抖动契约 ---')
const heroesViewPath = path.join(repoRoot, 'frontend', 'src', 'views', 'HeroesView.vue')
assert(fs.existsSync(heroesViewPath), 'HeroesView.vue 必须存在')
const heroesContent = fs.readFileSync(heroesViewPath, 'utf8')

assert(heroesContent.includes('w-30'), 'HeroesView.vue 段位选择按钮必须设置固定宽度 w-30 (120px) 防抖')
assert(heroesContent.includes('transition-colors'), 'HeroesView.vue 段位选择按钮必须使用 transition-colors 避免几何形变抖动')
assert(heroesContent.includes('ref="rankDropdownRef"'), 'HeroesView.vue 下拉菜单必须拥有独立的 rankDropdownRef 容器')
assert(heroesContent.includes('handleClickOutside'), 'HeroesView.vue 必须包含全局点击外部收起下拉框的监听函数')
assert(heroesContent.includes('document.removeEventListener(\'click\', handleClickOutside)'), 'HeroesView.vue 必须在 onUnmounted 中注销全局 click 监听器以防句柄泄漏')
assert(heroesContent.includes('[scrollbar-gutter:stable]'), 'HeroesView.vue 英雄网格容器必须包含 [scrollbar-gutter:stable] 预留滚动槽位')

console.log('\n====================================================')
console.log(`测试结果统计: ${passed} 项通过，${failed} 项失败`)
console.log('====================================================')

if (failed > 0) {
  process.exit(1)
}
