<script setup lang="ts">
import type { AiEndpointTestResult, AiEngineMode, ReminderCategoryKey } from '../types'
import {
  Activity,
  BrainCircuit,

  CheckCircle2,
  Cpu,
  Database,
  FolderOpen,
  Layers,
  Play,
  RotateCcw,
  ShieldCheck,
  Sliders,
  Sparkles,
  UserCheck,
  Volume2,
  Zap,
} from '@lucide/vue'
import { computed, onMounted, ref, watch } from 'vue'
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
import { useAppStore } from '../store'

const {
  state,
  updateSettings,
  getReminderMode,
  setReminderMode,
  setAllReminderModes,
  resetReminderModes,
  openCacheDirectory,
  clearCacheDirectory,
  selectCacheFolder,
  selectModelFile,
  showToast,
} = useAppStore()

const cacheDirInput = ref(state.settings.cache_directory || '')
const testingEndpoint = ref(false)
const testResult = ref<AiEndpointTestResult | null>(null)
const playerInputId = ref(state.settings.dota_player_id || '')
const verifyingPlayer = ref(false)
const playerBindError = ref('')

// 本地模型列表与远程模型列表 (使用真实开源模型)
const localModels = ref<string[]>(['qwen2.5:7b', 'qwen2.5:3b', 'deepseek-r1:7b', 'llama3.2:3b'])
const remoteModels = ref<string[]>(['deepseek-chat', 'deepseek-reasoner'])
const fetchingLocalModels = ref(false)
const fetchingRemoteModels = ref(false)

const RECOMMENDED_TTS_LIST = [
  {
    id: 'cosyvoice',
    name: 'CosyVoice (阿里开源极速拟真)',
    desc: '🌟 强烈推荐：多情感深度拟真、团战解说表现力最强，适合本地或局域网部署',
    endpoint: 'http://127.0.0.1:9233',
    badge: '电竞首选',
  },
  {
    id: 'chattts',
    name: 'ChatTTS (极速端侧对话语音)',
    desc: '⚡ 轻量推荐：专为对话场景设计，本地显存占用低至 1.5GB，毫秒级快速发音',
    endpoint: 'http://127.0.0.1:9966',
    badge: '端侧极速',
  },
  {
    id: 'openai_tts',
    name: 'OpenAI 协议 TTS (兼容端点)',
    desc: '🌐 开放标准：支持任何实现 /v1/audio/speech 标准的外部 TTS 服务与自建模型',
    endpoint: 'https://api.openai.com/v1',
    badge: '标准协议',
  },
  {
    id: 'edge',
    name: '微软 Edge-TTS (内置云端引擎)',
    desc: '🛡️ 内置默认：免配置即开即用，支持云希/云健等专业电竞解说男声，低延迟保底',
    endpoint: '',
    badge: '内置免配置',
  },
]

onMounted(() => {
  if (currentAiMode.value === 'local_npu') {
    fetchLocalModels(false)
  }
})

watch(
  () => state.settings.cache_directory,
  (val) => {
    if (val) cacheDirInput.value = val
  },
)

const currentAiMode = computed<AiEngineMode>(() => {
  const m = state.settings.ai_mode
  if (m === 'rule_only') return 'rule_only'
  if (m === 'cloud' || m === 'cloud_api') return 'cloud_api'
  return 'local_npu'
})

function setAiMode(mode: AiEngineMode) {
  updateSettings({ ai_mode: mode })
  if (mode === 'local_npu') {
    fetchLocalModels(false)
  }
}

async function handlePickModelFile() {
  const selected = await selectModelFile()
  if (selected) {
    showToast(`已成功选择本地模型文件: ${state.settings.npu_model}`, 'success')
  } else {
    showToast('未选择文件或当前环境不支持原生文件对话框', 'info')
  }
}

async function fetchLocalModels(showManualToast = false) {
  fetchingLocalModels.value = true
  try {
    const endpoint = state.settings.npu_endpoint || 'http://127.0.0.1:11434/v1'
    const models = await fetchLocalModelsList(endpoint)
    if (models.length > 0) {
      localModels.value = models
      if (!localModels.value.includes(state.settings.npu_model)) {
        updateSettings({ npu_model: localModels.value[0] })
      }
      if (showManualToast) {
        showToast(`已成功连接本地 Ollama，共获取到 ${localModels.value.length} 个本地模型！`, 'success')
      }
      return
    }
    if (showManualToast) {
      showToast('未在 127.0.0.1:11434 探测到运行中的 Ollama 服务，您可点击上方直接选择本地模型文件', 'warning')
    }
  } catch (err: any) {
    if (showManualToast) {
      showToast('连接本地服务超时或未启动，建议直接点击【选择模型文件】使用本地模型', 'warning')
    }
  } finally {
    fetchingLocalModels.value = false
  }
}

async function autoFetchLocalOllamaModels() {
  await fetchLocalModels(true)
}

async function fetchRemoteModels() {
  const apiKey = state.settings.cloud_api_key || ''
  const baseUrl = state.settings.cloud_base_url || 'https://api.deepseek.com/v1'

  fetchingRemoteModels.value = true
  try {
    const models = await fetchCloudModelsList(baseUrl, apiKey)
    if (models.length > 0) {
      remoteModels.value = models
      showToast(`成功获取到 ${remoteModels.value.length} 个可用云端模型！`, 'success')
    } else {
      showToast('已连通云端服务，但服务商未返回模型列表，您可直接手动输入模型 ID', 'info')
    }
  } catch (err: any) {
    showToast(`获取云端模型失败: ${err.message || '请检查端点 URL 与 API Key 是否填写正确'}`, 'error')
  } finally {
    fetchingRemoteModels.value = false
  }
}

function persistAiConfig() {
  updateSettings({
    npu_endpoint: state.settings.npu_endpoint,
    npu_model: state.settings.npu_model,
    cloud_api_key: state.settings.cloud_api_key,
    cloud_base_url: state.settings.cloud_base_url,
    cloud_model: state.settings.cloud_model,
  })
}

function applyRecommendedModel(modelId: string) {
  updateSettings({
    ai_mode: 'local_npu',
    npu_model: modelId,
  })
  showToast(`已将端侧推理模型切换为: ${modelId}`, 'info')
}

async function runEndpointTest() {
  testingEndpoint.value = true
  testResult.value = null

  try {
    const mode = currentAiMode.value
    const endpoint = (mode === 'cloud_api' ? state.settings.cloud_base_url : state.settings.npu_endpoint) || ''
    const apiKey = (mode === 'cloud_api' ? state.settings.cloud_api_key : '') || ''
    const model = (mode === 'cloud_api' ? state.settings.cloud_model : state.settings.npu_model) || ''

    const res = await aiCoachService.testEndpoint(mode, endpoint, apiKey, model)
    testResult.value = res
    if (res.success) {
      showToast(`端点探测连通成功！往返时延: ${res.latencyMs}ms`, 'success')
    } else {
      showToast(`端点测试未通过: ${res.errorMessage || '无法连接目标服务器'}`, 'error')
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
    showToast(`端点探测异常: ${err?.message || '网络连接失败'}`, 'error')
  } finally {
    testingEndpoint.value = false
  }
}

async function handleBindPlayerId() {
  const raw = playerInputId.value.trim()
  if (!raw) {
    playerBindError.value = '请输入有效的 Dota 2 玩家数字编号 (Account ID) 或社区个人链接'
    showToast('请输入有效的 Dota 2 玩家编号', 'warning')
    return
  }
  verifyingPlayer.value = true
  playerBindError.value = ''

  try {
    // 鲁棒规范化提取 (过滤 Unicode/Emoji/全角/Steam3/链接等)
    const cleanId = (await normalizeDotaAccountId(raw)) || raw
    playerInputId.value = cleanId

    const res = await fetch(`https://api.opendota.com/api/players/${encodeURIComponent(cleanId)}`, { signal: AbortSignal.timeout(6000) })
    if (!res.ok) throw new Error(`查询失败，状态码: ${res.status}`)
    const data = await res.json()
    const name = data?.profile?.personaname || data?.profile?.name || `玩家 #${cleanId}`
    const avatar = data?.profile?.avatarfull || data?.profile?.avatar || ''
    updateSettings({
      dota_player_id: cleanId,
      dota_player_name: name,
      dota_player_avatar: avatar,
    })
    playerProfileService.setAccountId(cleanId)
    await playerProfileService.fetchRecentMatches()
    showToast(`Dota 2 主账号绑定成功: ${name}`, 'success')
  } catch (e: any) {
    const cleanId = (await normalizeDotaAccountId(raw)) || raw
    updateSettings({
      dota_player_id: cleanId,
      dota_player_name: `玩家 #${cleanId}`,
    })
    playerProfileService.setAccountId(cleanId)
    playerBindError.value = `提示: 离线模式已保存绑定 (${e?.message || '网络查询超时'})`
    showToast(`离线模式已保存绑定账号 #${cleanId}`, 'info')
  } finally {
    verifyingPlayer.value = false
  }
}

function handleUnbindPlayer() {
  updateSettings({
    dota_player_id: '',
    dota_player_name: '',
    dota_player_avatar: '',
  })
  playerProfileService.setAccountId('')
  playerInputId.value = ''
  showToast('已解除玩家账号绑定', 'info')
}

async function handlePickCacheDir() {
  const selected = await selectCacheFolder()
  if (selected) {
    cacheDirInput.value = selected
  }
}

async function handleResetCacheDir() {
  const appApi = (window as any).go?.main?.App
  if (appApi?.GetDefaultCacheDirectory) {
    const defaultDir = await appApi.GetDefaultCacheDirectory()
    if (defaultDir) {
      updateSettings({ cache_directory: defaultDir })
      cacheDirInput.value = defaultDir
      showToast('已重置为默认数据目录', 'info')
    }
  }
}

async function handleClearCache() {
  await clearCacheDirectory()
  showToast('缓存文件清理完成！', 'success')
}

function testVoice() {
  speechService.speak('神盾教练就绪。黑皇杖已就绪，建议配合全队开雾组织强势推进。', {
    mode: 2,
    rate: state.settings.voice_speed,
    volume: state.settings.voice_volume,
    voiceName: state.settings.edge_voice_name || state.settings.voice_name,
    customTtsEnabled: state.settings.custom_tts_enabled && state.settings.custom_tts_engine !== 'edge',
    customTtsEndpoint: state.settings.custom_tts_endpoint,
  })
}

function previewCategoryVoice(key: ReminderCategoryKey) {
  const item = DEFAULT_REMINDER_CATEGORIES.find(c => c.key === key)
  if (!item) return

  speechService.speak(`战术提醒：${item.label}。${item.description}`, {
    mode: 2,
    rate: state.settings.voice_speed,
    volume: state.settings.voice_volume,
    voiceName: state.settings.edge_voice_name || state.settings.voice_name,
    customTtsEnabled: state.settings.custom_tts_enabled && state.settings.custom_tts_engine !== 'edge',
    customTtsEndpoint: state.settings.custom_tts_endpoint,
  })
}
</script>

<template>
  <div class="bg-dota-bg text-slate-100 p-6 pb-8 flex-1 h-full min-h-0 w-full select-none overflow-y-auto space-y-6">
    <!-- 顶部标题 -->
    <div class="pb-3 border-b border-slate-700/80 flex items-center justify-between">
      <div>
        <h1 class="text-lg text-amber-400 tracking-wide font-bold flex items-center space-x-2">
          <span>应用全模态设置中枢</span>
          <span class="text-xs text-emerald-300 font-mono px-2 py-0.5 border border-emerald-700/50 rounded bg-emerald-950/80">
            全局集中管控
          </span>
        </h1>
        <p class="text-xs text-slate-300 font-normal mt-0.5">
          集中管控 AI 推理引擎接入、账号绑定、独立置顶悬浮窗、AI 战术提醒与第三方 TTS 语音
        </p>
      </div>

      <div class="flex items-center space-x-2">
        <span class="text-[11px] text-slate-300 font-mono px-2.5 py-1 border border-slate-700 rounded bg-slate-800">
          v1.0.0 2026 电竞旗舰版
        </span>
      </div>
    </div>

    <!-- 1. Dota 2 玩家账号绑定 (核心主玩家锚定) -->
    <div class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
      <div class="pb-3 border-b border-slate-800 flex flex-wrap gap-3 items-center justify-between">
        <div class="text-sm text-amber-400 font-bold flex items-center space-x-2">
          <UserCheck class="text-cyan-400 h-4 w-4" />
          <span>Dota 2 玩家账号绑定 (主账号)</span>
        </div>
        <span class="text-[11px] text-slate-400 font-mono">
          绑定后，实时教练与赛后分析都以该账号作为第一视角
        </span>
      </div>

      <div class="text-xs space-y-3">
        <!-- 未绑定或修改绑定 -->
        <div v-if="!state.settings.dota_player_id" class="space-y-2">
          <label class="text-slate-200 font-bold block">输入您的 Dota 2 玩家数字编号 (32位 Account ID)</label>
          <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
            <input
              v-model="playerInputId"
              type="text"
              placeholder="例如: 12345678 (在游戏主页个人昵称下方)"
              class="text-xs text-amber-300 font-mono px-3.5 outline-none border border-slate-700 rounded-lg bg-slate-950 flex-1 h-9 focus:border-amber-400"
              @keydown.enter="handleBindPlayerId"
            >
            <button
              :disabled="verifyingPlayer || !playerInputId.trim()"
              class="text-slate-950 font-bold px-5 rounded-lg bg-amber-500 flex shrink-0 h-9 shadow-md transition-all items-center justify-center space-x-1.5 hover:bg-amber-400 disabled:opacity-50"
              @click="handleBindPlayerId"
            >
              <Activity v-if="verifyingPlayer" class="h-3.5 w-3.5 animate-spin" />
              <UserCheck v-else class="h-3.5 w-3.5" />
              <span>{{ verifyingPlayer ? '正在校验绑定...' : '立即验证并绑定' }}</span>
            </button>
          </div>
          <span class="text-[11px] text-slate-400 block">
            提示：绑定成功后，实时教练将自动锁定您操作的英雄，赛后复盘也将直接展示您的专属对局表现。
          </span>
          <div v-if="playerBindError" class="text-xs text-amber-300 p-2 border border-amber-800/60 rounded bg-amber-950/40">
            {{ playerBindError }}
          </div>
        </div>

        <!-- 已绑定展示卡片 -->
        <div v-else class="p-4 border border-emerald-700/60 rounded-xl bg-slate-950 flex flex-col gap-3 justify-between sm:flex-row sm:items-center">
          <div class="flex items-center space-x-3.5">
            <img
              v-if="state.settings.dota_player_avatar"
              :src="state.settings.dota_player_avatar"
              alt="头像"
              class="border border-emerald-500/60 rounded-xl h-12 w-12 shadow"
            >
            <div class="space-y-0.5">
              <div class="flex items-center space-x-2">
                <span class="text-sm text-slate-100 font-extrabold">{{ state.settings.dota_player_name }}</span>
                <span class="text-[10px] text-emerald-300 font-mono px-1.5 py-0.2 border border-emerald-700 rounded bg-emerald-950">已绑定主玩家</span>
              </div>
              <div class="text-xs text-slate-400 font-mono">
                玩家编号: <strong class="text-amber-400">{{ state.settings.dota_player_id }}</strong>
              </div>
            </div>
          </div>

          <div class="flex items-center space-x-2">
            <button
              class="text-xs text-slate-400 px-3 py-1.5 border border-slate-800 rounded-lg bg-slate-900 transition-colors hover:text-rose-400 hover:border-rose-900/60"
              @click="handleUnbindPlayer"
            >
              解除绑定
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- 2. AI 战术推理引擎接入模式 (支持自动获取与 Ollama 连接测试) -->
    <div class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
      <div class="pb-3 border-b border-slate-800 flex flex-wrap gap-3 items-center justify-between">
        <div class="text-sm text-amber-400 font-bold flex items-center space-x-2">
          <Cpu class="text-emerald-400 h-4 w-4" />
          <span>AI 战术推理引擎接入</span>
        </div>

        <!-- 3 大模式切换单选胶囊组 -->
        <div class="p-1 border border-slate-800 rounded-xl bg-slate-950 flex items-center space-x-1.5">
          <button
            class="text-xs font-bold px-3 py-1 rounded-lg flex transition-all items-center space-x-1.5" :class="[
              currentAiMode === 'local_npu'
                ? 'bg-emerald-500 text-slate-950 shadow-md'
                : 'text-slate-400 hover:text-slate-200',
            ]"
            @click="setAiMode('local_npu')"
          >
            <Zap class="h-3.5 w-3.5" />
            <span>本地端侧 (Ollama / NPU)</span>
          </button>

          <button
            class="text-xs font-bold px-3 py-1 rounded-lg flex transition-all items-center space-x-1.5" :class="[
              currentAiMode === 'cloud_api'
                ? 'bg-amber-500 text-slate-950 shadow-md'
                : 'text-slate-400 hover:text-slate-200',
            ]"
            @click="setAiMode('cloud_api')"
          >
            <BrainCircuit class="h-3.5 w-3.5" />
            <span>远程云端 API</span>
          </button>

          <button
            class="text-xs font-bold px-3 py-1 rounded-lg flex transition-all items-center space-x-1.5" :class="[
              currentAiMode === 'rule_only'
                ? 'bg-cyan-600 text-white shadow-md'
                : 'text-slate-400 hover:text-slate-200',
            ]"
            @click="setAiMode('rule_only')"
          >
            <ShieldCheck class="h-3.5 w-3.5" />
            <span>规则引擎纯降级</span>
          </button>
        </div>
      </div>

      <!-- 模式 1: 本地端侧 (NPU / 本地模型文件 / Ollama) -->
      <div v-if="currentAiMode === 'local_npu'" class="space-y-4">
        <div class="text-xs text-emerald-300 leading-relaxed p-3.5 border border-emerald-800/50 rounded-xl bg-emerald-950/30 flex items-start space-x-2.5">
          <CheckCircle2 class="text-emerald-400 mt-0.5 shrink-0 h-4 w-4" />
          <div>
            <strong class="text-emerald-200 font-bold">本地端侧优势：</strong>
            0 延迟、0 费用、100% 离线隐私保护。支持直接挑选本地模型文件 (.gguf / .onnx / .bin) 使用，亦可连接本地 Ollama 服务。
          </div>
        </div>

        <!-- 本地 AI 模型文件挑选卡片 (用户直接选择模型文件) -->
        <div class="p-3.5 border border-slate-700/80 rounded-xl bg-slate-950 space-y-2.5">
          <div class="flex items-center justify-between">
            <div class="text-xs text-slate-200 font-bold flex items-center space-x-2">
              <FolderOpen class="text-emerald-400 h-4 w-4" />
              <span>本地 AI 模型文件 (.gguf / .onnx / .bin)</span>
            </div>
            <span class="text-[11px] text-slate-400">支持直接挂载本地模型文件运行</span>
          </div>

          <div class="flex items-center space-x-2">
            <input
              :value="state.settings.local_model_path || '未选择本地模型文件 (默认通过端点服务)'"
              readonly
              type="text"
              class="text-xs text-amber-300 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-900 flex-1 h-8"
            >
            <button
              class="text-xs text-slate-950 font-bold px-3.5 rounded-lg bg-emerald-600 flex shrink-0 h-8 cursor-pointer shadow transition-colors items-center space-x-1.5 hover:bg-emerald-500"
              @click="handlePickModelFile"
            >
              <FolderOpen class="h-3.5 w-3.5" />
              <span>选择模型文件</span>
            </button>
            <button
              v-if="state.settings.local_model_path"
              class="text-xs text-slate-400 px-2.5 border border-slate-700 rounded-lg bg-slate-800 h-8 cursor-pointer transition-colors hover:text-rose-300 hover:bg-rose-950"
              title="清除已选本地文件"
              @click="updateSettings({ local_model_path: '' })"
            >
              清除
            </button>
          </div>
          <p class="text-[11px] text-slate-400">
            支持选择存储在本地磁盘的 GGUF 或 ONNX 格式大模型文件，本机 NPU/GPU 直接驱动。
          </p>
        </div>

        <div class="text-xs gap-4 grid grid-cols-1 md:grid-cols-2">
          <div>
            <label class="text-slate-200 font-bold mb-1.5 block">本地端点 URL (OpenAI 兼容)</label>
            <div class="flex items-center space-x-2">
              <input
                v-model="state.settings.npu_endpoint"
                type="text"
                placeholder="http://127.0.0.1:11434/v1"
                class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-950 flex-1 h-8 focus:border-amber-400"
                @change="persistAiConfig"
              >
              <button
                :disabled="fetchingLocalModels"
                class="text-xs text-slate-200 px-3 rounded-lg bg-slate-800 flex shrink-0 h-8 cursor-pointer transition-colors items-center space-x-1 hover:bg-slate-700 disabled:opacity-50"
                title="探测本地 Ollama/vLLM 服务模型"
                @click="autoFetchLocalOllamaModels"
              >
                <Activity v-if="fetchingLocalModels" class="h-3.5 w-3.5 animate-spin" />
                <span>{{ fetchingLocalModels ? '探测中...' : '重新检测' }}</span>
              </button>
            </div>
            <span class="text-[11px] text-slate-400 mt-1 block">兼容 Ollama (11434)、LM Studio (1234)、vLLM 等本地端点</span>
          </div>

          <div>
            <label class="text-slate-200 font-bold mb-1.5 block">当前生效模型名称</label>
            <div class="flex items-center space-x-2">
              <select
                :value="state.settings.npu_model"
                class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-950 h-8 w-full focus:border-amber-400"
                @change="(e) => { updateSettings({ npu_model: (e.target as HTMLSelectElement).value }); persistAiConfig() }"
              >
                <option v-for="m in localModels" :key="m" :value="m">
                  {{ m }}
                </option>
              </select>
            </div>
            <span class="text-[11px] text-emerald-400 mt-1 block">已接入端侧推理，点击右侧【重新检测】可同步最新模型</span>
          </div>
        </div>

        <div class="pt-1 flex flex-wrap gap-2 items-center">
          <span class="text-xs text-slate-400">社区精选推荐开源端侧模型：</span>
          <button
            v-for="mod in RECOMMENDED_NPU_MODELS"
            :key="mod.id"
            class="text-[11px] font-mono px-2.5 py-1 border rounded-lg flex cursor-pointer transition-all items-center space-x-1.5" :class="[
              state.settings.npu_model === mod.id
                ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/50 font-bold'
                : 'bg-slate-950 text-slate-300 border-slate-700 hover:border-slate-500',
            ]"
            @click="applyRecommendedModel(mod.id)"
          >
            <span>{{ mod.name }}</span>
            <span class="text-[9px] text-emerald-400">({{ mod.tag }})</span>
          </button>
        </div>
      </div>

      <!-- 模式 2: 远程云端 API (带模型获取与 API Response 回显) -->
      <div v-else-if="currentAiMode === 'cloud_api'" class="space-y-4">
        <div class="text-xs gap-4 grid grid-cols-1 md:grid-cols-2">
          <div>
            <label class="text-slate-200 font-bold mb-1.5 block">云端服务 Base URL</label>
            <input
              v-model="state.settings.cloud_base_url"
              type="text"
              placeholder="https://api.deepseek.com/v1"
              class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-950 h-8 w-full focus:border-amber-400"
              @change="persistAiConfig"
            >
          </div>

          <div>
            <label class="text-slate-200 font-bold mb-1.5 block">API 鉴权密钥 (API Key)</label>
            <input
              v-model="state.settings.cloud_api_key"
              type="password"
              placeholder="sk-..."
              class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-950 h-8 w-full focus:border-amber-400"
              @change="persistAiConfig"
            >
          </div>
        </div>

        <div class="flex flex-col gap-2 justify-between sm:flex-row sm:items-center">
          <div class="flex-1 max-w-sm">
            <label class="text-slate-200 font-bold mb-1 block">生效模型标识</label>
            <div class="flex items-center space-x-2">
              <input
                v-model="state.settings.cloud_model"
                type="text"
                placeholder="deepseek-chat"
                class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-950 flex-1 h-8 focus:border-amber-400"
                @change="persistAiConfig"
              >
              <button
                :disabled="fetchingRemoteModels"
                class="text-xs text-slate-950 font-bold px-3 rounded-lg bg-amber-500 shrink-0 h-8 transition-colors hover:bg-amber-400 disabled:opacity-50"
                @click="fetchRemoteModels"
              >
                {{ fetchingRemoteModels ? '获取中...' : '获取可用模型' }}
              </button>
            </div>
          </div>

          <div class="pt-4 sm:pt-0">
            <button
              :disabled="testingEndpoint"
              class="text-xs text-slate-950 font-bold px-4 rounded-lg bg-amber-500 flex h-8 shadow transition-all items-center space-x-1.5 hover:bg-amber-400 disabled:opacity-50"
              @click="runEndpointTest"
            >
              <Activity v-if="testingEndpoint" class="h-3.5 w-3.5 animate-spin" />
              <Zap v-else class="h-3.5 w-3.5" />
              <span>{{ testingEndpoint ? '正在发送探测...' : '测试云端 API 连通性' }}</span>
            </button>
          </div>
        </div>

        <!-- 仅在远程页面呈现 API Response 调试回显面板 -->
        <div v-if="testResult" class="p-3.5 border border-slate-800 rounded-xl bg-slate-950 space-y-2">
          <div class="flex items-center justify-between">
            <div class="flex items-center space-x-2">
              <span
                class="text-[10px] font-bold font-mono px-2 py-0.5 rounded" :class="[
                  testResult.success
                    ? 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/40'
                    : 'bg-rose-500/20 text-rose-300 border border-rose-500/40',
                ]"
              >
                {{ testResult.success ? '探测成功 (200 OK)' : '探测失败' }}
              </span>
              <span class="text-xs text-slate-300">延迟: <strong class="text-amber-400 font-mono">{{ testResult.latencyMs }}ms</strong></span>
            </div>
            <span class="text-[10px] text-slate-500 font-mono">{{ testResult.testedAt }}</span>
          </div>
          <p class="text-xs text-slate-300 leading-relaxed font-mono p-2.5 border border-slate-800/80 rounded bg-black/50 whitespace-pre-wrap">
            {{ testResult.responsePreview }}
          </p>
        </div>
      </div>

      <!-- 模式 3: 纯规则引擎降级 -->
      <div v-else class="text-xs text-cyan-200 leading-relaxed p-3.5 border border-cyan-800/50 rounded-xl bg-cyan-950/20 space-y-1">
        <strong class="text-cyan-300 font-bold block">0ms 纯确定性电竞裁定模式：</strong>
        <p class="text-slate-300 font-normal">
          此模式下完全不调用任何外部模型，全量提醒由本地确定性 GSI 规则引擎毫秒级触发，零额外开销。
        </p>
      </div>
    </div>

    <!-- 3. 局内独立置顶悬浮窗设置 (单一开关) -->
    <div class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
      <div class="text-sm text-amber-400 font-bold pb-3 border-b border-slate-800 flex items-center space-x-2">
        <Layers class="text-amber-400 h-4 w-4" />
        <span>局内独立置顶经济悬浮窗</span>
      </div>

      <div class="p-4 border border-slate-800 rounded-xl bg-slate-950 flex items-center justify-between">
        <div class="space-y-1">
          <div class="text-xs text-slate-100 font-bold flex items-center space-x-2">
            <span>进入比赛自动弹出独立置顶悬浮窗</span>
            <span class="text-[9px] text-emerald-400 font-mono px-1.5 py-0.2 border border-emerald-500/40 rounded bg-emerald-950">
              原生置顶
            </span>
          </div>
          <p class="text-[11px] text-slate-300 leading-relaxed font-normal">
            进入比赛号角吹响后，自动在桌面右上角唤起超轻原生独立经济悬浮窗，在全屏游戏上层持续追踪敌我买活与经济差。
          </p>
        </div>

        <label class="ml-4 inline-flex shrink-0 cursor-pointer items-center relative">
          <input
            v-model="state.settings.auto_overlay_on_game_start"
            type="checkbox"
            class="peer sr-only"
            @change="updateSettings({ auto_overlay_on_game_start: state.settings.auto_overlay_on_game_start })"
          >
          <div class="peer rounded-full bg-slate-800 h-6 w-11 peer-focus:outline-none after:border after:border-slate-300 after:rounded-full after:bg-white peer-checked:bg-amber-500 after:h-5 after:w-5 after:content-[''] after:transition-all after:left-[2px] after:top-[2px] after:absolute peer-checked:after:border-white peer-checked:after:translate-x-full" />
        </label>
      </div>
    </div>

    <!-- 4. 战术语音与第三方 TTS 推荐配置 -->
    <div class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
      <div class="text-sm text-amber-400 font-bold pb-3 border-b border-slate-800 flex items-center justify-between">
        <div class="flex items-center space-x-2">
          <Volume2 class="text-amber-400 h-4 w-4" />
          <span>战术语音播报与第三方 TTS 集成</span>
        </div>
        <button
          class="text-xs text-amber-400 font-bold px-3 py-1 border border-slate-700 rounded-lg bg-slate-950 flex items-center space-x-1.5 hover:bg-slate-800"
          @click="testVoice"
        >
          <Play class="h-3 w-3" />
          <span>试听当前播报</span>
        </button>
      </div>

      <!-- 基础音量语速控制 -->
      <div class="text-xs gap-4 grid grid-cols-1 md:grid-cols-3">
        <div>
          <label class="text-slate-200 font-bold mb-1.5 block">内置 Edge-TTS 声线角色</label>
          <select
            :value="state.settings.edge_voice_name"
            class="text-xs text-slate-100 font-medium px-3 outline-none border border-slate-700 rounded-lg bg-slate-950 h-8 w-full focus:border-amber-400"
            @change="(e) => updateSettings({ edge_voice_name: (e.target as HTMLSelectElement).value })"
          >
            <option value="zh-CN-YunxiNeural">
              云希 (沉稳电竞解说男声 · 推荐)
            </option>
            <option value="zh-CN-XiaoxiaoNeural">
              晓晓 (清晰战术指挥女声)
            </option>
            <option value="zh-CN-YunjianNeural">
              云健 (激昂团战解说男声)
            </option>
          </select>
        </div>

        <div>
          <div class="text-slate-200 font-bold mb-1.5 flex justify-between">
            <span>播报音量</span>
            <span class="text-amber-400 font-mono">{{ state.settings.voice_volume }}%</span>
          </div>
          <input
            type="range"
            min="0"
            max="100"
            :value="state.settings.voice_volume"
            class="appearance-none accent-amber-500 rounded-lg bg-slate-950 h-1.5 w-full cursor-pointer"
            @input="(e) => updateSettings({ voice_volume: Number((e.target as HTMLInputElement).value) })"
          >
        </div>

        <div>
          <div class="text-slate-200 font-bold mb-1.5 flex justify-between">
            <span>播报语速</span>
            <span class="text-amber-400 font-mono">{{ state.settings.voice_speed.toFixed(1) }}x</span>
          </div>
          <input
            type="range"
            min="0.5"
            max="2.0"
            step="0.1"
            :value="state.settings.voice_speed"
            class="appearance-none accent-amber-500 rounded-lg bg-slate-950 h-1.5 w-full cursor-pointer"
            @input="(e) => updateSettings({ voice_speed: Number((e.target as HTMLInputElement).value) })"
          >
        </div>
      </div>

      <!-- 第三方 TTS 推荐方案卡片 -->
      <div class="p-4 border border-slate-800 rounded-xl bg-slate-950 space-y-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center space-x-2">
            <Sparkles class="text-amber-400 h-4 w-4" />
            <span class="text-xs text-slate-100 font-bold">第三方高品质 TTS 引擎推荐</span>
          </div>

          <label class="inline-flex cursor-pointer items-center space-x-2">
            <span class="text-xs text-slate-300">启用第三方 TTS</span>
            <input
              v-model="state.settings.custom_tts_enabled"
              type="checkbox"
              class="peer sr-only"
              @change="updateSettings({ custom_tts_enabled: state.settings.custom_tts_enabled })"
            >
            <div class="peer rounded-full bg-slate-800 h-5 w-9 peer-focus:outline-none after:border after:border-slate-300 after:rounded-full after:bg-white peer-checked:bg-amber-500 after:h-4 after:w-4 after:content-[''] after:transition-all after:left-[2px] after:top-[2px] after:absolute peer-checked:after:border-white peer-checked:after:translate-x-full" />
          </label>
        </div>

        <!-- 推荐列表 -->
        <div class="pt-1 gap-2.5 grid grid-cols-1 sm:grid-cols-2">
          <div
            v-for="eng in RECOMMENDED_TTS_LIST"
            :key="eng.id"
            class="p-3 border rounded-lg cursor-pointer select-none transition-all" :class="[
              state.settings.custom_tts_engine === eng.id
                ? 'bg-amber-500/15 border-amber-500/70 shadow-sm'
                : 'bg-slate-900/60 border-slate-800 hover:border-slate-700',
            ]"
            @click="updateSettings({
              custom_tts_engine: eng.id as any,
              custom_tts_endpoint: eng.endpoint || state.settings.custom_tts_endpoint,
              custom_tts_enabled: eng.id !== 'edge' ? true : state.settings.custom_tts_enabled,
            })"
          >
            <div class="mb-1 flex items-center justify-between">
              <span class="text-xs text-slate-100 font-bold">{{ eng.name }}</span>
              <span class="text-[10px] text-amber-300 font-mono px-1.5 py-0.2 border border-amber-500/40 rounded bg-amber-500/20">{{ eng.badge }}</span>
            </div>
            <p class="text-[11px] text-slate-300 leading-relaxed font-normal">
              {{ eng.desc }}
            </p>
          </div>
        </div>

        <!-- 第三方端点输入框 -->
        <div v-if="state.settings.custom_tts_enabled" class="pt-2 border-t border-slate-800 space-y-1">
          <label class="text-xs text-slate-200 font-bold block">第三方 TTS 服务 API 端点</label>
          <input
            v-model="state.settings.custom_tts_endpoint"
            type="text"
            placeholder="http://127.0.0.1:9233"
            class="text-xs text-slate-100 font-mono px-3.5 outline-none border border-slate-700 rounded-lg bg-slate-900 h-8 w-full focus:border-amber-400"
            @change="updateSettings({ custom_tts_endpoint: state.settings.custom_tts_endpoint })"
          >
          <span class="text-[11px] text-slate-400 block">若第三方服务不可用，系统将自动无缝回退至内置 Edge-TTS，确保实战零静音。</span>
        </div>
      </div>
    </div>

    <!-- 5. AI 提醒（已清理微观噪音，高对比度呈现） -->
    <div class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
      <div class="pb-3 border-b border-slate-800 flex flex-wrap gap-3 items-center justify-between">
        <div>
          <div class="text-sm text-amber-400 font-bold flex items-center space-x-2">
            <Sliders class="text-amber-400 h-4 w-4" />
            <span>AI 提醒</span>
          </div>
          <p class="text-[11px] text-slate-300 font-normal mt-0.5">
            全量聚焦宏观战局节奏与核心战术变量（肉山、魔方、神符、买活、技能就绪），微观琐碎噪音已全面清理
          </p>
        </div>

        <div class="text-xs flex items-center space-x-1.5">
          <button
            class="text-xs text-amber-400 font-semibold px-2.5 py-1 border border-slate-700 rounded-lg bg-slate-950 transition-colors hover:bg-slate-800"
            @click="setAllReminderModes(2)"
          >
            一键全开语音
          </button>
          <button
            class="text-xs text-cyan-400 font-semibold px-2.5 py-1 border border-slate-700 rounded-lg bg-slate-950 transition-colors hover:bg-slate-800"
            @click="setAllReminderModes(1)"
          >
            一键全开文字
          </button>
          <button
            class="text-xs text-slate-400 font-semibold px-2.5 py-1 border border-slate-700 rounded-lg bg-slate-950 transition-colors hover:text-rose-400 hover:bg-slate-800"
            @click="setAllReminderModes(0)"
          >
            一键全部关闭
          </button>
          <button
            class="text-xs text-slate-300 font-semibold px-2.5 py-1 border border-slate-700 rounded-lg bg-slate-950 flex transition-colors items-center space-x-1 hover:bg-slate-800"
            @click="resetReminderModes"
          >
            <RotateCcw class="h-3.5 w-3.5" />
            <span>恢复默认</span>
          </button>
        </div>
      </div>

      <!-- 9 类核心战术提醒列表 (高清晰度对比) -->
      <div class="space-y-2">
        <div
          v-for="item in DEFAULT_REMINDER_CATEGORIES"
          :key="item.key"
          class="p-3.5 border rounded-xl flex flex-col gap-3 transition-all justify-between md:flex-row md:items-center" :class="[
            getReminderMode(item.key) === 2
              ? 'bg-slate-950 border-amber-500/50 shadow-sm'
              : getReminderMode(item.key) === 1
                ? 'bg-slate-950/90 border-cyan-500/40'
                : 'bg-slate-950/40 border-slate-800 opacity-60',
          ]"
        >
          <div class="space-y-1">
            <div class="flex items-center space-x-2">
              <span class="text-xs text-slate-100 font-extrabold">{{ item.label }}</span>
              <span class="text-[10px] text-amber-300 font-medium font-mono px-2 py-0.5 border border-slate-700 rounded bg-slate-900">
                触发: {{ item.triggerCondition }}
              </span>
            </div>
            <p class="text-xs text-slate-300 leading-relaxed font-normal">
              {{ item.description }}
            </p>
          </div>

          <div class="flex shrink-0 items-center self-end space-x-3 md:self-auto">
            <button
              class="text-slate-400 p-1.5 rounded-lg transition-colors hover:text-amber-400 hover:bg-slate-800"
              title="试听当前播报"
              @click="previewCategoryVoice(item.key)"
            >
              <Play class="h-3.5 w-3.5" />
            </button>
            <ThreeSegmentToggle
              :model-value="getReminderMode(item.key)"
              @update:model-value="(val) => setReminderMode(item.key, val)"
            />
          </div>
        </div>
      </div>
    </div>

    <!-- 6. 本地缓存与存储管理 (支持 E 盘等用户文档目录) -->
    <div class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
      <div class="pb-3 border-b border-slate-800 flex flex-wrap gap-3 items-center justify-between">
        <div class="text-sm text-amber-400 font-bold flex items-center space-x-2">
          <Database class="text-cyan-400 h-4 w-4" />
          <span>本地缓存与存储管理 (用户文档目录)</span>
        </div>
      </div>

      <div class="text-xs space-y-3">
        <div>
          <label class="text-slate-200 font-bold mb-1.5 block">当前生效的数据存储路径</label>
          <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
            <input
              v-model="cacheDirInput"
              type="text"
              readonly
              class="text-xs text-amber-300 font-mono px-3.5 outline-none border border-slate-700 rounded-lg bg-slate-950 flex-1 h-9"
            >
            <button
              class="text-slate-950 font-bold px-4 rounded-lg bg-amber-500 flex shrink-0 h-9 transition-colors items-center space-x-1.5 hover:bg-amber-400"
              @click="handlePickCacheDir"
            >
              <FolderOpen class="h-4 w-4" />
              <span>选择文件夹</span>
            </button>
            <button
              class="text-slate-200 px-3.5 border border-slate-700 rounded-lg bg-slate-800 h-9 transition-colors hover:bg-slate-700"
              @click="openCacheDirectory"
            >
              打开目录
            </button>
          </div>
        </div>

        <div class="pt-1 flex items-center justify-between">
          <button
            class="text-xs text-slate-400 transition-colors hover:text-slate-200"
            @click="handleResetCacheDir"
          >
            重置为系统默认文档目录
          </button>
          <button
            class="text-xs text-rose-400 px-3 py-1.5 border border-slate-800 rounded-lg bg-slate-950 transition-colors hover:text-rose-300"
            @click="handleClearCache"
          >
            清理缓存文件
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
