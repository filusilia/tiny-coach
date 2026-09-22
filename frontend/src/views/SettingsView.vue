<script setup lang="ts">
/**
 * 首选项与系统设置核心视图 (src/views/SettingsView.vue)
 * 职责：
 * 1. 战术语音播报与提醒策略矩阵配置 (9 大提醒类目的单项控制与预设套用)
 * 2. AI 战术推理引擎设置：优先适配端侧 NPU (Qwen 3.5 / 3.8 系列，严格 ≤ 2GB 内存开销)，兼顾云端 API 与纯规则降级
 * 3. 第三方拟真 TTS 语音引擎管理：支持 OpenAI 标准协议、CosyVoice、ChatTTS，以及内置微软 Edge-TTS 原生保底
 * 4. Dota 2 玩家数字 ID 绑定与画像分析服务对接
 * 5. 数据存储目录与本地模型管理 (系统原生对话框集成)
 * 6. 底部关于卡片：版本号 v1.0.0 与 Valve GSI 零外挂合规声明
 */

import type { AiEndpointTestResult, AiEngineMode } from '../types'
import {
  Activity,
  BrainCircuit,
  Check,
  CheckCircle2,
  Copy,
  Database,
  DownloadCloud,
  ExternalLink,
  FolderOpen,
  Play,
  RefreshCw,
  RotateCcw,
  Shield,
  ShieldCheck,
  Sliders,
  Sparkles,
  UserCheck,
  Volume2,
} from '@lucide/vue'
import { computed, onMounted, ref, watch } from 'vue'
import { aiApi, systemApi } from '../api'
import ThreeSegmentToggle from '../components/coach/ThreeSegmentToggle.vue'
import { DEFAULT_REMINDER_CATEGORIES } from '../data/reminderCategories'
import {
  aiCoachService,
  fetchCloudModelsList,
  fetchLocalModelsList,
  RECOMMENDED_NPU_MODELS,
} from '../services/aiCoachService'
import { normalizeDotaAccountId, playerProfileService } from '../services/playerProfileService'
import { speechService } from '../services/speechService'
import { useAppStore } from '../stores'

const appStore = useAppStore()

/** 当前激活的 AI 引擎模式 */
const currentAiMode = computed<AiEngineMode>({
  get: () => appStore.settings.ai_mode || 'local_npu',
  set: (val: AiEngineMode) => {
    appStore.updateSettings({ ai_mode: val })
    if (val === 'local_npu') {
      void fetchLocalModels(false)
    }
  },
})

/** 缓存路径文本框响应式状态 */
const cacheDirInput = ref(appStore.settings.cache_directory || '')
/** AI 端点探测中状态 */
const testingEndpoint = ref(false)
/** AI 端点探测返回结果 */
const testResult = ref<AiEndpointTestResult | null>(null)
/** 玩家编号输入框状态 */
const playerInputId = ref(appStore.settings.dota_player_id || '')
/** 玩家编号验证中状态 */
const verifyingPlayer = ref(false)
/** 玩家绑定错误提示 */
const playerBindError = ref('')

/** 本地已扫描/可用模型列表 (默认预设精选 Qwen 3.5/3.8 等 ≤ 2GB 内存模型) */
const localModels = ref<string[]>([
  'qwen3.5:1.5b',
  'qwen3.5:0.5b',
  'qwen3.8:1.5b',
  'llama3.2:1b',
])
/** 云端商用模型列表 */
const remoteModels = ref<string[]>(['deepseek-chat', 'deepseek-reasoner'])
/** 正在刷新本地模型列表 */
const fetchingLocalModels = ref(false)
/** 正在拉取云端模型列表 */
const fetchingRemoteModels = ref(false)

/** 模型自动下载拉取中状态 */
const pullingModelId = ref<string | null>(null)
/** 模型下载进度或状态消息 */
const pullStatusMessage = ref<string>('')
/** 已成功复制命令的模型 ID */
const copiedModelId = ref<string | null>(null)

/** 第三方 TTS 端点连通测试状态 */
const testingTtsEndpoint = ref(false)
/** 第三方 TTS 连通探测响应结果 */
const ttsEndpointTestResult = ref<{
  success: boolean
  latencyMs?: number
  message: string
  testedAt: string
} | null>(null)
/** 第三方 TTS 试听发音中状态 */
const testingTtsVoice = ref(false)
/** 语音试听测试用示例文本 */
const ttsTestText = ref('神盾教练战术提示：肉山大魔王已刷新，黑皇杖已就绪，全队开雾组织推进。')

/** 第三方与内置推荐 TTS 引擎预设配置项 */
const RECOMMENDED_TTS_LIST = [
  {
    id: 'edge',
    name: '微软 Edge-TTS (内置默认免配置)',
    desc: '🛡️ 内置默认首选：零配置即开即用，支持云希/晓晓等电竞级解说男声，低延迟原生保底。',
    endpoint: '',
    engineType: 'edge',
    badge: '内置原生',
  },
  {
    id: 'openai_tts',
    name: 'OpenAI 协议 TTS (标准开放规范)',
    desc: '🌐 标准开放协议：POST /v1/audio/speech，兼容 OpenAI、ChatTTS-OpenAI、CosyVoice-API 及各类代理。',
    endpoint: 'http://127.0.0.1:8000',
    engineType: 'openai',
    badge: '标准协议',
  },
  {
    id: 'cosyvoice',
    name: 'CosyVoice (阿里开源极速拟真)',
    desc: '🌟 深度拟真首选：多情感音色拟真、解说临场感强，适合本地 NPU/GPU 部署 (默认 9233 端口)。',
    endpoint: 'http://127.0.0.1:9233',
    engineType: 'openai',
    badge: '深度拟真',
  },
  {
    id: 'chattts',
    name: 'ChatTTS (极速端侧对话语音)',
    desc: '⚡ 轻量推荐：专为短对话设计，本地内存开销低，毫秒级快速发音 (默认 9966 端口)。',
    endpoint: 'http://127.0.0.1:9966',
    engineType: 'get_stream',
    badge: '端侧极速',
  },
]

const cachingIcons = ref(false)
const iconCacheStats = ref<{ cached_heroes: number, cached_items: number } | null>(null)

async function refreshIconCacheStats() {
  try {
    const stats = await systemApi.cacheAllIcons()
    iconCacheStats.value = stats
  } catch {
    // 忽略异常
  }
}

async function handleCacheAllIcons() {
  cachingIcons.value = true
  try {
    const stats = await systemApi.cacheAllIcons()
    iconCacheStats.value = stats
    appStore.showToast(`本地图标缓存就绪！已缓存英雄: ${stats.cached_heroes} 个，物品: ${stats.cached_items} 个`, 'success')
  } catch (err: any) {
    appStore.showToast(`缓存图标失败: ${err?.message || '未知错误'}`, 'error')
  } finally {
    cachingIcons.value = false
  }
}

onMounted(async () => {
  await appStore.syncCacheDirectory()
  await refreshIconCacheStats()
  if (currentAiMode.value === 'local_npu') {
    await fetchLocalModels(false)
  }
})

watch(
  () => appStore.settings.cache_directory,
  (val) => {
    if (val) cacheDirInput.value = val
  },
)

/**
 * 切换第三方 TTS 启用状态 (无任何 CSS 定位逃逸，杜绝大黑块)
 */
function handleToggleCustomTts() {
  const next = !appStore.settings.custom_tts_enabled
  appStore.updateSettings({ custom_tts_enabled: next })
  appStore.showToast(next ? '已开启第三方 TTS 语音服务' : '已恢复系统内置 Edge-TTS 语音', 'info')
}

/**
 * 获取本地安装/可用的模型列表
 * @param showNotification 是否在拉取完成时弹窗通知
 */
async function fetchLocalModels(showNotification = true) {
  fetchingLocalModels.value = true
  try {
    const endpoint = appStore.settings.npu_endpoint || 'http://127.0.0.1:11434/v1'
    const models = await fetchLocalModelsList(endpoint)
    if (models.length > 0) {
      localModels.value = models
      if (showNotification) {
        appStore.showToast(`成功探测到本地 ${models.length} 个就绪大模型！`, 'success')
      }
    } else {
      if (showNotification) {
        appStore.showToast('未在本地检测到已下载的模型，您可直接在下方一键复制下载命令', 'info')
      }
    }
  } catch (err: any) {
    if (showNotification) {
      appStore.showToast(`探测本地模型失败: ${err.message || '请确认本地 Ollama/端侧推理服务已启动'}`, 'warning')
    }
  } finally {
    fetchingLocalModels.value = false
  }
}

/**
 * 自动拉取或下载端侧推荐模型
 * @param modelId 模型名称标签 (如 "qwen3.5:1.5b")
 */
async function pullRecommendedModel(modelId: string) {
  if (pullingModelId.value) return
  pullingModelId.value = modelId
  pullStatusMessage.value = `正在连接后端拉取 ${modelId}，请保持网络畅通...`
  appStore.showToast(`开始请求后端拉取模型 ${modelId}，请耐心等待...`, 'info')

  try {
    const endpoint = appStore.settings.npu_endpoint || 'http://127.0.0.1:11434/v1'
    const res = await aiApi.pullLocalModel(modelId, endpoint)
    pullStatusMessage.value = res || '模型拉取任务已完成！'
    appStore.showToast(`模型 ${modelId} 拉取完成！已自动设为当前端侧推理模型。`, 'success')
    appStore.updateSettings({ npu_model: modelId })
    await fetchLocalModels(false)
  } catch (err: any) {
    pullStatusMessage.value = `拉取异常: ${err?.message || '请确保本地已安装并启动了 Ollama'}`
    appStore.showToast(`下载失败: ${err?.message || '请检查 Ollama 服务是否启动'}`, 'error')
  } finally {
    pullingModelId.value = null
  }
}

/**
 * 复制终端拉取运行命令至剪贴板
 * @param modelId 模型标识
 */
async function copyOllamaCommand(modelId: string) {
  const cmd = `ollama run ${modelId}`
  try {
    await navigator.clipboard.writeText(cmd)
    copiedModelId.value = modelId
    appStore.showToast(`已复制终端命令: ${cmd}`, 'success')
    setTimeout(() => {
      if (copiedModelId.value === modelId) copiedModelId.value = null
    }, 2500)
  } catch {
    appStore.showToast(`命令: ${cmd}`, 'info')
  }
}

/**
 * 在系统默认浏览器中打开 ModelScope 魔搭社区端侧 GGUF 选型页
 */
function openModelScopePage() {
  window.open('https://www.modelscope.cn/models?name=qwen3.5', '_blank')
}

/**
 * 拉取云端服务商提供的已开通模型列表
 */
async function fetchRemoteModels() {
  const apiKey = appStore.settings.cloud_api_key || ''
  const baseUrl = appStore.settings.cloud_base_url || 'https://api.deepseek.com/v1'

  fetchingRemoteModels.value = true
  try {
    const models = await fetchCloudModelsList(baseUrl, apiKey)
    if (models.length > 0) {
      remoteModels.value = models
      appStore.showToast(`成功获取到 ${remoteModels.value.length} 个可用云端模型！`, 'success')
    } else {
      appStore.showToast('已连通云端服务，但服务商未返回模型列表，您可直接手动输入模型 ID', 'info')
    }
  } catch (err: any) {
    appStore.showToast(`获取云端模型失败: ${err.message || '请检查端点 URL 与 API Key 是否填写正确'}`, 'error')
  } finally {
    fetchingRemoteModels.value = false
  }
}

/**
 * 持久化 AI 推理相关配置
 */
function persistAiConfig() {
  appStore.updateSettings({
    npu_endpoint: appStore.settings.npu_endpoint,
    npu_model: appStore.settings.npu_model,
    cloud_api_key: appStore.settings.cloud_api_key,
    cloud_base_url: appStore.settings.cloud_base_url,
    cloud_model: appStore.settings.cloud_model,
  })
}

/**
 * 应用选中的推荐模型
 * @param modelId 模型名称
 */
function applyRecommendedModel(modelId: string) {
  appStore.updateSettings({
    ai_mode: 'local_npu',
    npu_model: modelId,
  })
  appStore.showToast(`已将端侧推理模型切换为: ${modelId}`, 'info')
}

/**
 * 运行当前配置的 AI 模型端点连通性实测
 */
async function runEndpointTest() {
  testingEndpoint.value = true
  testResult.value = null

  try {
    const mode = currentAiMode.value
    const endpoint = (mode === 'cloud_api' ? appStore.settings.cloud_base_url : appStore.settings.npu_endpoint) || ''
    const apiKey = (mode === 'cloud_api' ? appStore.settings.cloud_api_key : '') || ''
    const model = (mode === 'cloud_api' ? appStore.settings.cloud_model : appStore.settings.npu_model) || ''

    const res = await aiCoachService.testEndpoint(mode, endpoint, apiKey, model)
    testResult.value = res
    if (res.success) {
      appStore.showToast(`端点探测连通成功！往返时延: ${res.latencyMs}ms`, 'success')
      if (mode === 'cloud_api') {
        void fetchRemoteModels()
      } else if (mode === 'local_npu') {
        void fetchLocalModels(false)
      }
    } else {
      appStore.showToast(`端点测试未通过: ${res.errorMessage || '无法连接目标服务器'}`, 'error')
    }
  } catch (err: any) {
    testResult.value = {
      success: false,
      latencyMs: 0,
      statusCode: 500,
      modelUsed: '未知',
      responsePreview: `探测错误: ${err?.message || '无法连接端点'}`,
      errorMessage: err?.message,
      testedAt: new Date().toLocaleTimeString('zh-CN', { hour12: false }),
    }
    appStore.showToast(`端点探测异常: ${err?.message || '网络连接失败'}`, 'error')
  } finally {
    testingEndpoint.value = false
  }
}

/**
 * 校验并绑定 Dota 2 玩家编号
 */
async function handleBindPlayerId() {
  const raw = playerInputId.value.trim()
  if (!raw) {
    playerBindError.value = '请输入有效的 Dota 2 玩家数字编号 (Account ID) 或社区个人链接'
    appStore.showToast('请输入有效的 Dota 2 玩家编号', 'warning')
    return
  }
  verifyingPlayer.value = true
  playerBindError.value = ''

  try {
    const cleanId = (await normalizeDotaAccountId(raw)) || raw
    playerInputId.value = cleanId

    const res = await fetch(`https://api.opendota.com/api/players/${encodeURIComponent(cleanId)}`, { signal: AbortSignal.timeout(6000) })
    if (res.ok) {
      const data = await res.json()
      const name = data?.profile?.personaname || data?.profile?.name || `玩家 #${cleanId}`
      const avatar = data?.profile?.avatarfull || data?.profile?.avatar || ''
      appStore.updateSettings({
        dota_player_id: cleanId,
        dota_player_name: name,
        dota_player_avatar: avatar,
      })
      playerProfileService.setAccountId(cleanId)
      await playerProfileService.fetchRecentMatches()
      appStore.showToast(`Dota 2 主账号绑定成功: ${name}`, 'success')
    } else {
      appStore.updateSettings({
        dota_player_id: cleanId,
        dota_player_name: `玩家 #${cleanId}`,
      })
      playerProfileService.setAccountId(cleanId)
      playerBindError.value = `提示: 离线模式已保存绑定 (查询状态码: ${res.status})`
      appStore.showToast(`离线模式已保存绑定账号 #${cleanId}`, 'info')
    }
  } catch (e: any) {
    const cleanId = (await normalizeDotaAccountId(raw)) || raw
    appStore.updateSettings({
      dota_player_id: cleanId,
      dota_player_name: `玩家 #${cleanId}`,
    })
    playerProfileService.setAccountId(cleanId)
    playerBindError.value = `提示: 离线模式已保存绑定 (${e?.message || '网络查询超时'})`
    appStore.showToast(`离线模式已保存绑定账号 #${cleanId}`, 'info')
  } finally {
    verifyingPlayer.value = false
  }
}

/**
 * 解绑当前 Dota 2 玩家账号
 */
function handleUnbindPlayer() {
  appStore.updateSettings({
    dota_player_id: '',
    dota_player_name: '',
    dota_player_avatar: '',
  })
  playerProfileService.setAccountId('')
  playerInputId.value = ''
  playerBindError.value = ''
  appStore.showToast('已解除玩家账号绑定', 'info')
}

/**
 * 唤起系统原生选择缓存文件夹窗口
 */
async function handleSelectCacheFolder() {
  try {
    const selected = await appStore.selectCacheFolder()
    if (selected) {
      cacheDirInput.value = selected
      appStore.showToast(`已成功配置数据存储目录: ${selected}`, 'success')
    } else {
      appStore.showToast('未选择目录或操作已取消', 'info')
    }
  } catch (err: any) {
    appStore.showToast(`打开目录选择窗口失败: ${err?.message || '系统限制'}`, 'error')
  }
}

/**
 * 唤起系统原生选择本地大模型文件窗口
 */
async function handleSelectModelFile() {
  try {
    const selected = await appStore.selectModelFile()
    if (selected) {
      appStore.showToast(`已成功关联本地模型文件: ${selected}`, 'success')
    } else {
      appStore.showToast('未选择模型文件或操作已取消', 'info')
    }
  } catch (err: any) {
    appStore.showToast(`选择模型文件失败: ${err?.message || '系统限制'}`, 'error')
  }
}

/**
 * 在 Windows 资源管理器中打开当前缓存目录
 */
async function handleOpenCacheDir() {
  try {
    await appStore.openCacheDirectory()
    appStore.showToast('已在文件资源管理器中打开缓存目录', 'success')
  } catch (err: any) {
    appStore.showToast(`打开目录失败: ${err?.message || '路径不存在'}`, 'error')
  }
}

/**
 * 重置缓存目录为系统默认用户文档路径
 */
async function handleResetCacheDir() {
  try {
    const defaultDir = await systemApi.getDefaultCacheDirectory()
    if (defaultDir) {
      await appStore.updateCacheDirectory(defaultDir)
      cacheDirInput.value = defaultDir
      appStore.showToast(`已重置为系统默认文档目录: ${defaultDir}`, 'info')
    }
  } catch (err: any) {
    appStore.showToast(`获取默认目录失败: ${err?.message || '系统错误'}`, 'error')
  }
}

/**
 * 清理指定缓存目录临时文件
 */
async function handleClearCache() {
  try {
    await appStore.clearCacheDirectory()
    appStore.showToast('临时缓存文件清理完成！', 'success')
  } catch (err: any) {
    appStore.showToast(`清理失败: ${err?.message || '文件可能被占用'}`, 'error')
  }
}

/**
 * 测试第三方 TTS 服务的网络连通性与 HTTP 状态
 */
async function testTtsEndpointConnection() {
  testingTtsEndpoint.value = true
  ttsEndpointTestResult.value = null
  const endpoint = appStore.settings.custom_tts_endpoint || 'http://127.0.0.1:9233'
  const apiKey = appStore.settings.custom_tts_api_key || ''
  const engineType = appStore.settings.custom_tts_engine || 'openai'
  const voice = appStore.settings.custom_tts_voice || ''

  try {
    const msg = await systemApi.testTtsEndpoint(endpoint, apiKey, engineType, voice)
    ttsEndpointTestResult.value = {
      success: true,
      message: msg || '第三方 TTS 服务端点握手成功！',
      testedAt: new Date().toLocaleTimeString('zh-CN', { hour12: false }),
    }
    appStore.showToast('第三方 TTS 服务端点握手成功！', 'success')
  } catch (err: any) {
    ttsEndpointTestResult.value = {
      success: false,
      message: err?.message || '端点连接失败，请确认本地服务已启动并放行对应端口',
      testedAt: new Date().toLocaleTimeString('zh-CN', { hour12: false }),
    }
    appStore.showToast(`TTS 服务连通探测失败: ${err?.message || '无法连接'}`, 'error')
  } finally {
    testingTtsEndpoint.value = false
  }
}

/**
 * 试听第三方 TTS 发音效果 (支持第三方引擎直测与本地系统发音保底)
 */
async function testTtsSpeechPlayback() {
  if (testingTtsVoice.value) return
  testingTtsVoice.value = true
  const text = ttsTestText.value.trim() || '神盾教练战术语音试听播报。'

  try {
    if (appStore.settings.custom_tts_enabled) {
      const res = await speechService.testThirdPartyTts(text, {
        customTtsEngine: appStore.settings.custom_tts_engine,
        customTtsEndpoint: appStore.settings.custom_tts_endpoint,
        customTtsApiKey: appStore.settings.custom_tts_api_key,
        customTtsVoice: appStore.settings.custom_tts_voice,
      })
      if (res.success) {
        appStore.showToast(res.message, 'success')
      } else {
        appStore.showToast(`第三方发音失败: ${res.message}，正在尝试本地系统发音兜底...`, 'warning')
        await speechService.speak(text, {
          mode: 2,
          rate: appStore.settings.voice_speed,
          volume: appStore.settings.voice_volume,
          voiceName: appStore.settings.edge_voice_name || appStore.settings.voice_name,
        })
      }
    } else {
      await speechService.speak(text, {
        mode: 2,
        rate: appStore.settings.voice_speed,
        volume: appStore.settings.voice_volume,
        voiceName: appStore.settings.edge_voice_name || appStore.settings.voice_name,
      })
      appStore.showToast('语音试听播放完成！', 'success')
    }
  } catch (err: any) {
    appStore.showToast(`语音发音失败: ${err?.message || '播放异常'}`, 'error')
  } finally {
    testingTtsVoice.value = false
  }
}
</script>

<template>
  <div class="bg-dota-bg text-slate-100 flex flex-1 flex-col select-none overflow-hidden">
    <!-- 1. 顶部标题栏 (纯标题展示，版本号移至最底部关于卡片) -->
    <header class="border-dota-border bg-dota-surface px-6 border-b flex shrink-0 h-14 items-center justify-between">
      <div class="flex items-center space-x-3">
        <Sliders class="text-amber-400 h-5 w-5" />
        <h1 class="text-base text-amber-400 tracking-wide font-bold">
          系统首选项与 AI 引擎配置
        </h1>
      </div>
      <div class="text-xs text-slate-400 font-mono flex items-center space-x-2">
        <Shield class="text-emerald-400 h-4 w-4" />
        <span>GSI 4444 端口就绪 · 零外挂合规</span>
      </div>
    </header>

    <!-- 2. 设置内容主滚动区域 -->
    <div class="p-6 flex-1 overflow-y-auto space-y-6">
      <!-- ========================================================================= -->
      <!-- 模块 1：战术语音播报与策略矩阵 -->
      <!-- ========================================================================= -->
      <section class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
        <div class="flex flex-col gap-3 justify-between sm:flex-row sm:items-center">
          <div class="flex items-center space-x-3">
            <Volume2 class="text-amber-400 h-5 w-5" />
            <div>
              <h2 class="text-sm text-slate-100 font-bold">
                战术语音提醒与三态播报矩阵
              </h2>
              <p class="text-xs text-slate-400">
                独立设定 9 大战场核心维度的提醒方式：关闭、仅 HUD 文本横幅、AI 语音合成播报
              </p>
            </div>
          </div>

          <!-- 一键快捷套用策略方案 -->
          <div class="flex items-center space-x-2">
            <button
              class="text-xs px-2.5 py-1 border rounded-lg cursor-pointer transition-colors"
              :class="[
                appStore.currentBroadcastStrategy === 'all_coach'
                  ? 'bg-amber-500/20 border-amber-500/80 text-amber-300 font-bold'
                  : 'bg-slate-800 border-slate-700 text-slate-300 hover:bg-slate-700',
              ]"
              @click="appStore.applyBroadcastStrategy('all_coach')"
            >
              全语音教练
            </button>
            <button
              class="text-xs px-2.5 py-1 border rounded-lg cursor-pointer transition-colors"
              :class="[
                appStore.currentBroadcastStrategy === 'focus_ranked'
                  ? 'bg-amber-500/20 border-amber-500/80 text-amber-300 font-bold'
                  : 'bg-slate-800 border-slate-700 text-slate-300 hover:bg-slate-700',
              ]"
              @click="appStore.applyBroadcastStrategy('focus_ranked')"
            >
              天梯专注 (推荐)
            </button>
            <button
              class="text-xs px-2.5 py-1 border rounded-lg cursor-pointer transition-colors"
              :class="[
                appStore.currentBroadcastStrategy === 'silent_hud'
                  ? 'bg-amber-500/20 border-amber-500/80 text-amber-300 font-bold'
                  : 'bg-slate-800 border-slate-700 text-slate-300 hover:bg-slate-700',
              ]"
              @click="appStore.applyBroadcastStrategy('silent_hud')"
            >
              静音仅 HUD
            </button>
            <button
              class="text-slate-400 p-1 border border-slate-700 rounded-lg bg-slate-800 cursor-pointer transition-colors hover:text-white"
              title="重置为默认策略"
              @click="appStore.resetReminderModes"
            >
              <RotateCcw class="h-3.5 w-3.5" />
            </button>
          </div>
        </div>

        <!-- 9 项细分策略列表 -->
        <div class="gap-3 grid grid-cols-1 lg:grid-cols-3 md:grid-cols-2">
          <div
            v-for="cat in DEFAULT_REMINDER_CATEGORIES"
            :key="cat.key"
            class="p-3 border border-slate-800 rounded-xl bg-slate-950/60 flex items-center justify-between"
          >
            <div class="space-y-0.5">
              <div class="text-xs text-slate-200 font-bold">
                {{ cat.label }}
              </div>
              <div class="text-[11px] text-slate-400 line-clamp-1">
                {{ cat.description }}
              </div>
            </div>
            <ThreeSegmentToggle
              :model-value="appStore.getReminderMode(cat.key)"
              @update:model-value="(val) => appStore.setReminderMode(cat.key, val)"
            />
          </div>
        </div>

        <!-- 音量与语速微调 -->
        <div class="pt-3 border-t border-slate-800/80 gap-4 grid grid-cols-1 sm:grid-cols-2">
          <div class="flex items-center space-x-3">
            <span class="text-xs text-slate-300 font-medium shrink-0">播报音量:</span>
            <input
              v-model.number="appStore.settings.voice_volume"
              type="range"
              min="10"
              max="100"
              step="5"
              class="appearance-none accent-amber-500 rounded-lg bg-slate-800 flex-1 h-1.5 cursor-pointer"
              @change="appStore.updateSettings({ voice_volume: appStore.settings.voice_volume })"
            >
            <span class="text-xs text-slate-300 font-mono text-right w-8">{{ appStore.settings.voice_volume }}%</span>
          </div>

          <div class="flex items-center space-x-3">
            <span class="text-xs text-slate-300 font-medium shrink-0">播报语速:</span>
            <input
              v-model.number="appStore.settings.voice_speed"
              type="range"
              min="0.8"
              max="1.6"
              step="0.1"
              class="appearance-none accent-amber-500 rounded-lg bg-slate-800 flex-1 h-1.5 cursor-pointer"
              @change="appStore.updateSettings({ voice_speed: appStore.settings.voice_speed })"
            >
            <span class="text-xs text-slate-300 font-mono text-right w-8">{{ appStore.settings.voice_speed }}x</span>
          </div>
        </div>
      </section>

      <!-- ========================================================================= -->
      <!-- 模块 2：AI 战术推理引擎 (升级 Qwen 3.5 / 3.8 选型，严格 ≤ 2GB 内存开销) -->
      <!-- ========================================================================= -->
      <section class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
        <div class="flex flex-col gap-2 justify-between sm:flex-row sm:items-center">
          <div class="flex items-center space-x-3">
            <BrainCircuit class="text-amber-400 h-5 w-5" />
            <div>
              <h2 class="text-sm text-slate-100 font-bold">
                AI 战术推理引擎架构与选型
              </h2>
              <p class="text-xs text-slate-400">
                推荐使用端侧 Qwen 3.5/3.8 高能效模型（开销 ≤ 2GB，16G 电脑边打 Dota 边跑 0 掉帧），亦支持 DeepSeek 云端 API
              </p>
            </div>
          </div>

          <!-- 模式切换三段单选 -->
          <div class="p-0.5 border border-slate-800 rounded-xl bg-slate-950 flex items-center">
            <button
              class="text-xs px-3 py-1.5 rounded-lg cursor-pointer transition-colors"
              :class="[
                currentAiMode === 'local_npu'
                  ? 'bg-amber-500 text-slate-950 font-extrabold shadow-sm'
                  : 'text-slate-400 hover:text-slate-200',
              ]"
              @click="currentAiMode = 'local_npu'"
            >
              端侧高能效 (NPU/本地)
            </button>
            <button
              class="text-xs px-3 py-1.5 rounded-lg cursor-pointer transition-colors"
              :class="[
                currentAiMode === 'cloud_api'
                  ? 'bg-amber-500 text-slate-950 font-extrabold shadow-sm'
                  : 'text-slate-400 hover:text-slate-200',
              ]"
              @click="currentAiMode = 'cloud_api'"
            >
              云端大模型 (DeepSeek)
            </button>
            <button
              class="text-xs px-3 py-1.5 rounded-lg cursor-pointer transition-colors"
              :class="[
                currentAiMode === 'rule_only'
                  ? 'bg-amber-500 text-slate-950 font-extrabold shadow-sm'
                  : 'text-slate-400 hover:text-slate-200',
              ]"
              @click="currentAiMode = 'rule_only'"
            >
              纯规则引擎降级
            </button>
          </div>
        </div>

        <!-- 2.1 端侧 NPU / 本地模式配置面板 -->
        <div v-if="currentAiMode === 'local_npu'" class="space-y-4">
          <!-- 官方推荐社区模型卡片 (全量升级 Qwen 3.5 / 3.8，开销严格 ≤ 2GB) -->
          <div class="space-y-2">
            <div class="text-xs text-slate-300 flex items-center justify-between">
              <span class="font-bold flex items-center space-x-1.5">
                <Sparkles class="text-amber-400 h-3.5 w-3.5" />
                <span>精选端侧模型 (专为 16GB 物理内存 + Dota 2 双开严苛优化，开销 ≤ 2GB)</span>
              </span>
              <button
                class="text-amber-400 flex cursor-pointer items-center space-x-1 hover:text-amber-300"
                @click="openModelScopePage"
              >
                <span>前往魔搭社区选型</span>
                <ExternalLink class="h-3 w-3" />
              </button>
            </div>

            <div class="gap-3 grid grid-cols-1 md:grid-cols-2">
              <div
                v-for="m in RECOMMENDED_NPU_MODELS"
                :key="m.id"
                class="p-3.5 border rounded-xl bg-slate-950/70 transition-all space-y-2"
                :class="[
                  appStore.settings.npu_model === m.id
                    ? 'border-amber-500/80 bg-amber-500/5 shadow-[0_0_12px_rgba(245,158,11,0.15)]'
                    : 'border-slate-800 hover:border-slate-700',
                ]"
              >
                <div class="flex items-start justify-between">
                  <div>
                    <div class="text-xs text-slate-100 font-extrabold flex items-center space-x-1.5">
                      <span>{{ m.name }}</span>
                      <span v-if="m.isPriority" class="text-[10px] text-amber-400 font-mono px-1 py-0.2 border border-amber-500/40 rounded bg-amber-500/20">首选</span>
                    </div>
                    <div class="text-[11px] text-emerald-400 font-mono">
                      {{ m.size }} · {{ m.contextWindow }}
                    </div>
                  </div>
                  <button
                    class="text-xs px-2.5 py-1 border rounded cursor-pointer transition-colors"
                    :class="[
                      appStore.settings.npu_model === m.id
                        ? 'bg-amber-500 text-slate-950 border-amber-400 font-bold'
                        : 'bg-slate-800 text-slate-200 border-slate-700 hover:bg-slate-700',
                    ]"
                    @click="applyRecommendedModel(m.id)"
                  >
                    {{ appStore.settings.npu_model === m.id ? '当前使用中' : '选用此模型' }}
                  </button>
                </div>

                <p class="text-[11px] text-slate-400 leading-relaxed">
                  {{ m.description }}
                </p>

                <div class="text-[11px] pt-2 border-t border-slate-800/80 flex items-center justify-between">
                  <span class="text-slate-500 font-mono">适用: {{ m.recommendedFor }}</span>
                  <div class="flex items-center space-x-1.5">
                    <button
                      class="text-slate-300 px-2 py-0.5 rounded bg-slate-800 flex cursor-pointer transition-colors items-center space-x-1 hover:bg-slate-700"
                      :title="`复制命令: ollama run ${m.id}`"
                      @click="copyOllamaCommand(m.id)"
                    >
                      <Check v-if="copiedModelId === m.id" class="text-emerald-400 h-3 w-3" />
                      <Copy v-else class="h-3 w-3" />
                      <span>{{ copiedModelId === m.id ? '已复制命令' : '复制拉取命令' }}</span>
                    </button>
                    <button
                      class="text-amber-300 px-2 py-0.5 border border-amber-500/40 rounded bg-amber-500/20 flex cursor-pointer transition-colors items-center space-x-1 hover:bg-amber-500/30"
                      :disabled="pullingModelId === m.id"
                      @click="pullRecommendedModel(m.id)"
                    >
                      <DownloadCloud class="h-3 w-3" />
                      <span>{{ pullingModelId === m.id ? '拉取中...' : '一键下载' }}</span>
                    </button>
                  </div>
                </div>
              </div>
            </div>

            <div v-if="pullStatusMessage" class="text-xs text-amber-300 font-mono p-2.5 border border-amber-500/40 rounded-lg bg-slate-950">
              {{ pullStatusMessage }}
            </div>
          </div>

          <!-- 端点与本地模型选择 -->
          <div class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/60 space-y-3">
            <div class="gap-3 grid grid-cols-1 md:grid-cols-2">
              <div>
                <label class="text-xs text-slate-300 font-bold mb-1 block">端侧推理 API 端点 (Ollama / vLLM / llama.cpp)</label>
                <div class="flex space-x-2">
                  <input
                    v-model="appStore.settings.npu_endpoint"
                    type="text"
                    placeholder="http://127.0.0.1:11434/v1"
                    class="text-xs text-slate-100 font-mono px-3 py-1.5 outline-none border border-slate-700 rounded-lg bg-slate-900 flex-1 focus:ring-1 focus:ring-amber-400"
                    @change="persistAiConfig"
                  >
                  <button
                    class="text-xs text-slate-200 px-3 py-1.5 border border-slate-700 rounded-lg bg-slate-800 flex cursor-pointer items-center space-x-1 hover:bg-slate-700"
                    :disabled="fetchingLocalModels"
                    @click="fetchLocalModels(true)"
                  >
                    <RefreshCw class="h-3.5 w-3.5" :class="{ 'animate-spin': fetchingLocalModels }" />
                    <span>检测模型</span>
                  </button>
                </div>
              </div>

              <div>
                <label class="text-xs text-slate-300 font-bold mb-1 block">当前生效模型 (可手动输入或选择本地文件)</label>
                <div class="flex space-x-2">
                  <input
                    v-model="appStore.settings.npu_model"
                    type="text"
                    placeholder="qwen3.5:1.5b"
                    class="text-xs text-slate-100 font-mono px-3 py-1.5 outline-none border border-slate-700 rounded-lg bg-slate-900 flex-1 focus:ring-1 focus:ring-amber-400"
                    @change="persistAiConfig"
                  >
                  <button
                    class="text-xs text-amber-300 px-3 py-1.5 border border-amber-500/50 rounded-lg bg-amber-500/20 flex cursor-pointer items-center space-x-1 hover:bg-amber-500/30"
                    title="选择本地磁盘上的 .gguf / .onnx 模型文件"
                    @click="handleSelectModelFile"
                  >
                    <FolderOpen class="h-3.5 w-3.5" />
                    <span>选择模型文件</span>
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- 2.2 云端大模型模式配置面板 -->
        <div v-else-if="currentAiMode === 'cloud_api'" class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/60 space-y-3">
          <div class="gap-3 grid grid-cols-1 md:grid-cols-3">
            <div>
              <label class="text-xs text-slate-300 font-bold mb-1 block">云端 API Base URL</label>
              <input
                v-model="appStore.settings.cloud_base_url"
                type="text"
                placeholder="https://api.deepseek.com/v1"
                class="text-xs text-slate-100 font-mono px-3 py-1.5 outline-none border border-slate-700 rounded-lg bg-slate-900 w-full focus:ring-1 focus:ring-amber-400"
                @change="persistAiConfig"
              >
            </div>
            <div>
              <label class="text-xs text-slate-300 font-bold mb-1 block">API Key</label>
              <input
                v-model="appStore.settings.cloud_api_key"
                type="password"
                placeholder="sk-..."
                class="text-xs text-slate-100 font-mono px-3 py-1.5 outline-none border border-slate-700 rounded-lg bg-slate-900 w-full focus:ring-1 focus:ring-amber-400"
                @change="persistAiConfig"
              >
            </div>
            <div>
              <label class="text-xs text-slate-300 font-bold mb-1 block">模型名称 (如 deepseek-chat)</label>
              <div class="flex space-x-2">
                <input
                  v-model="appStore.settings.cloud_model"
                  list="cloud-models-datalist"
                  type="text"
                  placeholder="deepseek-chat"
                  class="text-xs text-slate-100 font-mono px-3 py-1.5 outline-none border border-slate-700 rounded-lg bg-slate-900 flex-1 focus:ring-1 focus:ring-amber-400"
                  @change="persistAiConfig"
                >
                <datalist id="cloud-models-datalist">
                  <option v-for="m in remoteModels" :key="m" :value="m">
                    {{ m }}
                  </option>
                </datalist>
                <button
                  class="text-xs text-slate-200 px-3 py-1.5 border border-slate-700 rounded-lg bg-slate-800 flex cursor-pointer items-center space-x-1 hover:bg-slate-700"
                  :disabled="fetchingRemoteModels"
                  title="向云端供应商 API 探测当前可用大模型列表"
                  @click="fetchRemoteModels"
                >
                  <RefreshCw class="h-3.5 w-3.5" :class="{ 'animate-spin': fetchingRemoteModels }" />
                  <span>拉取列表</span>
                </button>
              </div>
            </div>
          </div>

          <!-- 可选云端模型芯片列表（测试连接或拉取后呈现可选项） -->
          <div v-if="remoteModels.length > 0" class="pt-2 border-t border-slate-800/80 flex flex-wrap gap-1.5 items-center">
            <span class="text-[11px] text-slate-400 mr-1 shrink-0">可用模型选项 (点击选用):</span>
            <button
              v-for="m in remoteModels"
              :key="m"
              type="button"
              class="text-[11px] font-mono px-2.5 py-1 border rounded-lg flex cursor-pointer transition-all items-center space-x-1"
              :class="appStore.settings.cloud_model === m
                ? 'border-amber-500 bg-amber-500/20 text-amber-300 font-bold shadow-sm'
                : 'border-slate-800 bg-slate-900 text-slate-300 hover:border-slate-700 hover:text-white'"
              @click="() => { appStore.settings.cloud_model = m; persistAiConfig(); appStore.showToast(`已切换云端模型: ${m}`, 'info') }"
            >
              <Check v-if="appStore.settings.cloud_model === m" class="text-amber-400 h-3 w-3" />
              <span>{{ m }}</span>
            </button>
          </div>
        </div>

        <!-- 2.3 端点连通性实测栏 -->
        <div class="pt-2 border-t border-slate-800 flex items-center justify-between">
          <div class="flex items-center space-x-2">
            <button
              class="text-xs text-slate-950 font-bold px-3.5 py-1.5 rounded-lg bg-amber-500 flex cursor-pointer shadow-sm transition-colors items-center space-x-1.5 hover:bg-amber-400"
              :disabled="testingEndpoint"
              @click="runEndpointTest"
            >
              <Activity class="h-3.5 w-3.5" :class="{ 'animate-spin': testingEndpoint }" />
              <span>{{ testingEndpoint ? '正在实测端点...' : '实测 AI 模型连通性' }}</span>
            </button>
            <span v-if="testResult" class="text-xs font-mono" :class="testResult.success ? 'text-emerald-400' : 'text-rose-400'">
              {{ testResult.success ? `连通成功 (${testResult.latencyMs}ms)` : `探测失败: ${testResult.errorMessage || '请检查服务'}` }}
            </span>
          </div>
          <span class="text-[11px] text-slate-500">超时判定：8000ms 自动转规则引擎</span>
        </div>
      </section>

      <!-- ========================================================================= -->
      <!-- 模块 3：第三方拟真 TTS 语音合成服务 (重构无黑块开关 + Edge-TTS 关停方案) -->
      <!-- ========================================================================= -->
      <section class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
        <div class="flex items-center justify-between">
          <div class="flex items-center space-x-3">
            <Volume2 class="text-amber-400 h-5 w-5" />
            <div>
              <h2 class="text-sm text-slate-100 font-bold">
                第三方拟真 TTS 语音引擎
              </h2>
              <p class="text-xs text-slate-400">
                支持对接 CosyVoice、ChatTTS 或兼容 OpenAI 协议的本地极速语音服务；关闭时 100% 采用内置微软 Edge-TTS
              </p>
            </div>
          </div>

          <!-- 彻底修复大黑块：组件化语义 Switch 按钮，具有 relative 容器 -->
          <div class="flex items-center space-x-2">
            <span class="text-xs text-slate-300 font-medium">启用第三方 TTS</span>
            <button
              type="button"
              role="switch"
              :aria-checked="appStore.settings.custom_tts_enabled"
              class="border-2 border-transparent rounded-full inline-flex shrink-0 h-6 w-11 cursor-pointer transition-colors duration-200 ease-in-out relative focus:outline-none"
              :class="appStore.settings.custom_tts_enabled ? 'bg-amber-500' : 'bg-slate-800'"
              @click="handleToggleCustomTts"
            >
              <span
                aria-hidden="true"
                class="rounded-full bg-white h-5 w-5 inline-block pointer-events-none ring-0 shadow-lg transform transition duration-200 ease-in-out"
                :class="appStore.settings.custom_tts_enabled ? 'translate-x-5' : 'translate-x-0'"
              />
            </button>
          </div>
        </div>

        <!-- 推荐服务选项卡片 -->
        <div class="gap-2.5 grid grid-cols-1 lg:grid-cols-4 sm:grid-cols-2">
          <div
            v-for="eng in RECOMMENDED_TTS_LIST"
            :key="eng.id"
            class="p-3 border rounded-xl cursor-pointer select-none transition-all"
            :class="[
              eng.id === 'edge' && !appStore.settings.custom_tts_enabled
                ? 'bg-amber-500/15 border-amber-500/70 shadow-sm'
                : eng.id === appStore.settings.custom_tts_engine && appStore.settings.custom_tts_enabled
                  ? 'bg-amber-500/15 border-amber-500/70 shadow-sm'
                  : 'bg-slate-950/60 border-slate-800 hover:border-slate-700',
            ]"
            @click="() => {
              if (eng.id === 'edge') {
                appStore.updateSettings({ custom_tts_enabled: false, custom_tts_engine: 'edge' })
                appStore.showToast('已切换为内置微软 Edge-TTS 原生保底', 'info')
              }
              else {
                appStore.updateSettings({
                  custom_tts_enabled: true,
                  custom_tts_engine: eng.id as any,
                  custom_tts_endpoint: eng.endpoint || appStore.settings.custom_tts_endpoint,
                })
                appStore.showToast(`已选用 ${eng.name}`, 'info')
              }
            }"
          >
            <div class="mb-1 flex items-center justify-between">
              <span class="text-xs text-slate-100 font-bold">{{ eng.name }}</span>
              <span class="text-[10px] text-amber-300 font-mono px-1.5 py-0.2 border border-amber-500/40 rounded bg-amber-500/20">
                {{ eng.badge }}
              </span>
            </div>
            <p class="text-[11px] text-slate-300 leading-relaxed font-normal">
              {{ eng.desc }}
            </p>
          </div>
        </div>

        <!-- 第三方 TTS 参数配置与测试区 (仅在启用第三方时展开) -->
        <div v-if="appStore.settings.custom_tts_enabled" class="pt-3 border-t border-slate-800 space-y-3">
          <div class="gap-3 grid grid-cols-1 sm:grid-cols-3">
            <div>
              <label class="text-xs text-slate-200 font-bold mb-1 block">服务 API 端点 URL</label>
              <input
                v-model="appStore.settings.custom_tts_endpoint"
                type="text"
                placeholder="http://127.0.0.1:9233"
                class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-900 h-8 w-full focus:ring-1 focus:ring-amber-400"
                @change="appStore.updateSettings({ custom_tts_endpoint: appStore.settings.custom_tts_endpoint })"
              >
            </div>

            <div>
              <label class="text-xs text-slate-200 font-bold mb-1 block">API Key (可选密钥)</label>
              <input
                v-model="appStore.settings.custom_tts_api_key"
                type="password"
                placeholder="Bearer Token (无则留空)"
                class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-900 h-8 w-full focus:ring-1 focus:ring-amber-400"
                @change="appStore.updateSettings({ custom_tts_api_key: appStore.settings.custom_tts_api_key })"
              >
            </div>

            <div>
              <label class="text-xs text-slate-200 font-bold mb-1 block">音色名称 / Voice ID (可选)</label>
              <input
                v-model="appStore.settings.custom_tts_voice"
                type="text"
                placeholder="如 alloy / zh-CN-YunxiNeural"
                class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-900 h-8 w-full focus:ring-1 focus:ring-amber-400"
                @change="appStore.updateSettings({ custom_tts_voice: appStore.settings.custom_tts_voice })"
              >
            </div>
          </div>

          <!-- 测试文本与测试操作栏 -->
          <div>
            <label class="text-xs text-slate-200 font-bold mb-1 block">测试播报文本</label>
            <div class="flex flex-col gap-2 sm:flex-row">
              <input
                v-model="ttsTestText"
                type="text"
                class="text-xs text-slate-100 px-3 outline-none border border-slate-700 rounded-lg bg-slate-900 flex-1 h-8 focus:ring-1 focus:ring-amber-400"
              >
              <div class="flex items-center space-x-2">
                <button
                  class="text-xs text-slate-200 font-bold px-3 border border-slate-700 rounded-lg bg-slate-800 flex h-8 cursor-pointer items-center space-x-1.5 hover:bg-slate-700"
                  :disabled="testingTtsEndpoint"
                  @click="testTtsEndpointConnection"
                >
                  <Activity class="h-3.5 w-3.5" :class="{ 'animate-spin': testingTtsEndpoint }" />
                  <span>探测连通性</span>
                </button>
                <button
                  class="text-xs text-slate-950 font-bold px-3 rounded-lg bg-amber-500 flex h-8 cursor-pointer items-center space-x-1.5 hover:bg-amber-400"
                  :disabled="testingTtsVoice"
                  @click="testTtsSpeechPlayback"
                >
                  <Play class="h-3.5 w-3.5" />
                  <span>发音试听</span>
                </button>
              </div>
            </div>
            <div v-if="ttsEndpointTestResult" class="text-xs font-mono mt-2" :class="ttsEndpointTestResult.success ? 'text-emerald-400' : 'text-rose-400'">
              {{ ttsEndpointTestResult.message }} ({{ ttsEndpointTestResult.testedAt }})
            </div>
          </div>
        </div>
      </section>

      <!-- ========================================================================= -->
      <!-- 模块 4：Dota 2 玩家数字 ID 绑定与画像接入 -->
      <!-- ========================================================================= -->
      <section class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
        <div class="flex items-center space-x-3">
          <UserCheck class="text-amber-400 h-5 w-5" />
          <div>
            <h2 class="text-sm text-slate-100 font-bold">
              Dota 2 玩家数字编号 (Account ID) 绑定
            </h2>
            <p class="text-xs text-slate-400">
              绑定后系统将自动抓取近 20 场天梯战绩与英雄绝活池，AI 复盘可针对性定制打法建议
            </p>
          </div>
        </div>

        <div class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/60 space-y-3">
          <div class="flex flex-col gap-3 items-center sm:flex-row">
            <div class="flex-1 w-full">
              <label class="text-xs text-slate-300 font-bold mb-1 block">玩家 32 位数字编号 / Steam 个人主页链接</label>
              <input
                v-model="playerInputId"
                type="text"
                placeholder="例如 136414902 或 steamcommunity.com/id/xxx"
                class="text-xs text-slate-100 font-mono px-3 py-1.5 outline-none border border-slate-700 rounded-lg bg-slate-900 w-full focus:ring-1 focus:ring-amber-400"
                @keyup.enter="handleBindPlayerId"
              >
            </div>
            <div class="pt-4 flex w-full items-center space-x-2 sm:w-auto">
              <button
                class="text-xs text-slate-950 font-bold px-4 py-1.5 rounded-lg bg-amber-500 flex flex-1 cursor-pointer shadow-sm items-center justify-center space-x-1.5 hover:bg-amber-400 sm:flex-none"
                :disabled="verifyingPlayer"
                @click="handleBindPlayerId"
              >
                <CheckCircle2 class="h-3.5 w-3.5" :class="{ 'animate-spin': verifyingPlayer }" />
                <span>{{ verifyingPlayer ? '查询中...' : '绑定账号' }}</span>
              </button>
              <button
                v-if="appStore.settings.dota_player_id"
                class="text-xs text-rose-400 px-3 py-1.5 border border-rose-900 rounded-lg cursor-pointer hover:bg-rose-950/60"
                @click="handleUnbindPlayer"
              >
                解绑
              </button>
            </div>
          </div>

          <div v-if="appStore.settings.dota_player_name" class="p-2.5 border border-slate-800 rounded-lg bg-slate-900/80 flex items-center space-x-3">
            <img
              v-if="appStore.settings.dota_player_avatar"
              :src="appStore.settings.dota_player_avatar"
              class="border border-amber-400 rounded-full h-9 w-9 object-cover"
              alt="头像"
            >
            <div>
              <div class="text-xs text-amber-400 font-bold">
                {{ appStore.settings.dota_player_name }}
              </div>
              <div class="text-[11px] text-slate-400 font-mono">
                ID: {{ appStore.settings.dota_player_id }} · 战绩画像已建立同步
              </div>
            </div>
          </div>

          <div v-if="playerBindError" class="text-xs text-amber-400 font-mono">
            {{ playerBindError }}
          </div>
        </div>
      </section>

      <!-- ========================================================================= -->
      <!-- 模块 5：数据存储目录与缓存管理 -->
      <!-- ========================================================================= -->
      <section class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
        <div class="flex items-center space-x-3">
          <Database class="text-amber-400 h-5 w-5" />
          <div>
            <h2 class="text-sm text-slate-100 font-bold">
              数据缓存与本地文件目录
            </h2>
            <p class="text-xs text-slate-400">
              管理英雄静态图谱、战局 GSI 快照日志、赛后复盘分析数据及本地 GGUF 模型存储路径
            </p>
          </div>
        </div>

        <div class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/60 space-y-3">
          <div>
            <label class="text-xs text-slate-300 font-bold mb-1 block">当前数据存储绝对路径</label>
            <div class="flex flex-col gap-2 sm:flex-row">
              <input
                v-model="cacheDirInput"
                type="text"
                readonly
                class="text-xs text-slate-200 font-mono px-3 py-1.5 outline-none border border-slate-700 rounded-lg bg-slate-900 flex-1 select-all"
              >
              <div class="flex items-center space-x-2">
                <button
                  class="text-xs text-amber-300 font-bold px-3 py-1.5 border border-amber-500/50 rounded-lg bg-amber-500/20 flex cursor-pointer items-center space-x-1.5 hover:bg-amber-500/30"
                  @click="handleSelectCacheFolder"
                >
                  <FolderOpen class="h-3.5 w-3.5" />
                  <span>选择文件夹</span>
                </button>
                <button
                  class="text-xs text-slate-200 px-3 py-1.5 border border-slate-700 rounded-lg bg-slate-800 flex cursor-pointer items-center space-x-1 hover:bg-slate-700"
                  @click="handleOpenCacheDir"
                >
                  <ExternalLink class="h-3.5 w-3.5" />
                  <span>在资源管理器打开</span>
                </button>
              </div>
            </div>
          </div>

          <!-- 图标离线本地缓存状态与预热 -->
          <div class="p-2.5 border border-slate-800/80 rounded-lg bg-slate-900/50 flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
            <div class="space-y-0.5">
              <div class="text-xs text-slate-200 font-bold flex items-center space-x-1.5">
                <span>本地图标缓存状态</span>
                <span v-if="iconCacheStats" class="text-[10px] text-emerald-400 font-mono px-1.5 py-0.2 rounded bg-emerald-950/60 border border-emerald-500/30">
                  已缓存英雄: {{ iconCacheStats.cached_heroes }} | 装备: {{ iconCacheStats.cached_items }}
                </span>
              </div>
              <div class="text-[11px] text-slate-400">
                默认优先加载本地缓存文件，离线秒级呈现；缺失时自动落盘并由官方 CDN 兜底。
              </div>
            </div>
            <button
              :disabled="cachingIcons"
              class="text-xs text-emerald-300 font-bold px-3 py-1.5 border border-emerald-500/40 rounded-lg bg-emerald-950/40 flex cursor-pointer items-center space-x-1 hover:bg-emerald-900/50 disabled:opacity-50"
              @click="handleCacheAllIcons"
            >
              <Sparkles class="h-3.5 w-3.5" />
              <span>{{ cachingIcons ? '正在检查缓存...' : '刷新本地图标缓存' }}</span>
            </button>
          </div>

          <div class="text-xs pt-2 border-t border-slate-800/80 flex items-center justify-between">
            <button
              class="text-slate-400 cursor-pointer transition-colors hover:text-slate-200"
              @click="handleResetCacheDir"
            >
              重置为系统默认用户文档路径
            </button>
            <button
              class="text-rose-400 cursor-pointer transition-colors hover:text-rose-300"
              @click="handleClearCache"
            >
              清理临时缓存文件
            </button>
          </div>
        </div>
      </section>

      <!-- ========================================================================= -->
      <!-- 模块 6：关于神盾教练 (About AegisCoach，移至最底部) -->
      <!-- ========================================================================= -->
      <section class="p-5 border border-slate-800 rounded-2xl bg-slate-950/80 shadow-xl space-y-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center space-x-3">
            <div class="border border-amber-500/40 rounded-xl bg-amber-500/10 flex h-9 w-9 items-center justify-center">
              <ShieldCheck class="text-amber-400 h-5 w-5" />
            </div>
            <div>
              <div class="text-sm text-slate-100 font-extrabold flex items-center space-x-2">
                <span>神盾教练 (AegisCoach)</span>
                <span class="text-[10px] text-amber-400 font-bold font-mono px-1.5 py-0.2 border border-amber-500/40 rounded bg-amber-500/20">
                  v1.0.0 (Release 2026)
                </span>
              </div>
              <p class="text-xs text-slate-400">
                Dota 2 极轻量级端侧 AI 战术助手与局内经济雷达
              </p>
            </div>
          </div>
          <span class="text-xs text-emerald-400 font-mono px-2 py-0.5 border border-emerald-800/60 rounded bg-emerald-950 flex items-center space-x-1">
            <CheckCircle2 class="h-3 w-3" />
            <span>Valve GSI 官方认证标准</span>
          </span>
        </div>

        <div class="text-[11px] text-slate-400 pt-2 border-t border-slate-900 gap-3 grid grid-cols-1 sm:grid-cols-3">
          <div class="p-2.5 border border-slate-800/80 rounded-lg bg-slate-900/60 space-y-1">
            <div class="text-slate-200 font-bold">
              极轻混合架构
            </div>
            <div>Wails v3 + Go 1.27 + Vue 3 + TypeScript</div>
            <div class="text-slate-500">
              单可执行文件零外部运行时依赖
            </div>
          </div>
          <div class="p-2.5 border border-slate-800/80 rounded-lg bg-slate-900/60 space-y-1">
            <div class="text-slate-200 font-bold">
              安全合规最高红线
            </div>
            <div>零内存注入 · 零 Hook 渲染 · 零模拟键鼠</div>
            <div class="text-slate-500">
              仅接收 Valve 原生 4444 端口 HTTP POST
            </div>
          </div>
          <div class="p-2.5 border border-slate-800/80 rounded-lg bg-slate-900/60 space-y-1">
            <div class="text-slate-200 font-bold">
              开工铁律与数据隐私
            </div>
            <div>零伪造原则 · 本地凭据安全存储</div>
            <div class="text-slate-500">
              Token 与 Steam ID 严禁进入公共日志
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>
