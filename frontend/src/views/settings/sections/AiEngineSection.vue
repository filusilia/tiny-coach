<script setup lang="ts">
/**
 * AI 战术推理引擎设置组件 (src/views/settings/sections/AiEngineSection.vue)
 * 职责：
 * 1. 模式分为: 本地 Ollama (走 /chat/completions)、远程 Response API (走 /responses) 与 纯规则模式 (rule_only)
 * 2. 移除所有冗余硬编码预设按钮，保持配置面板纯净
 * 3. 支持端点连通性测试与模型拉取
 */

import type { AiEndpointTestResult, AiEngineMode } from '@/types'
import {
  Activity,
  BrainCircuit,
  Check,
  CheckCircle2,
  ChevronDown,
  Download,
  RefreshCw,
  Server,
  Terminal,
  XCircle,
  Zap,
} from '@lucide/vue'
import { computed, ref } from 'vue'
import { aiCoachService, fetchModelsList } from '@/services/aiCoachService.ts'
import { useAppStore } from '@/stores'

defineOptions({ name: 'AiEngineSection' })

const appStore = useAppStore()

// 映射规范 mode: 'ollama' | 'remote' | 'rule_only'
const activeMode = computed<AiEngineMode>({
  get: () => {
    const m = appStore.settings.ai_mode
    if (m === 'local_npu' || m === 'npu') return 'ollama'
    if (m === 'cloud' || m === 'cloud_api' || m === 'response_api') return 'remote'
    if (m === 'rule_only') return 'rule_only'
    return m === 'remote' ? 'remote' : 'ollama'
  },
  set: (val: AiEngineMode) => {
    appStore.updateSettings({ ai_mode: val })
  },
})

// 端点配置绑定
const currentEndpoint = computed<string>({
  get: () => appStore.settings.ai_endpoint || (activeMode.value === 'remote' ? 'https://api.deepseek.com/v1' : 'http://127.0.0.1:11434/v1'),
  set: (val: string) => {
    appStore.updateSettings({ ai_endpoint: val })
  },
})

const currentApiKey = computed<string>({
  get: () => appStore.settings.ai_api_key ?? '',
  set: (val: string) => {
    appStore.updateSettings({ ai_api_key: val })
  },
})

const currentModel = computed<string>({
  get: () => appStore.settings.ai_model || (activeMode.value === 'remote' ? 'deepseek-chat' : 'qwen3.5:1.5b'),
  set: (val: string) => {
    appStore.updateSettings({ ai_model: val })
  },
})

const testingEndpoint = ref(false)
const testResult = ref<AiEndpointTestResult | null>(null)
const availableModels = ref<string[]>([])
const isModelDropdownOpen = ref(false)
const fetchingModels = ref(false)
const pullingModel = ref(false)
const pullStatus = ref('')

function onEndpointChange() {
  let val = (currentEndpoint.value || '').trim()
  if (val.endsWith('/openapi') || val.endsWith('/openapi/')) {
    val = val.replace(/\/openapi\/?$/, '/v1')
    currentEndpoint.value = val
    appStore.showToast('已自动将 OpenAPI 文档路由修正为 /v1 兼容端点', 'info')
  }
}

async function fetchRemoteModels() {
  if (activeMode.value !== 'remote') return
  fetchingModels.value = true
  try {
    let ep = currentEndpoint.value.trim()
    if (ep.endsWith('/openapi') || ep.endsWith('/openapi/')) {
      ep = ep.replace(/\/openapi\/?$/, '/v1')
      currentEndpoint.value = ep
    }
    const list = await fetchModelsList(ep, currentApiKey.value)
    if (list && list.length > 0) {
      availableModels.value = list
      isModelDropdownOpen.value = true
      if (!currentModel.value || !list.includes(currentModel.value)) {
        currentModel.value = list[0]
      }
      appStore.showToast(`成功拉取到 ${list.length} 个可用模型！已选入: ${currentModel.value}`, 'success')
    } else {
      appStore.showToast('服务端点未返回模型列表，请确认服务已启动或手动输入模型名', 'warning')
    }
  } catch (err: any) {
    appStore.showToast(`获取模型列表失败: ${err?.message || '端点未响应'}`, 'error')
  } finally {
    fetchingModels.value = false
  }
}

async function pullLocalModel() {
  const modelToPull = currentModel.value.trim()
  if (!modelToPull) {
    appStore.showToast('请先输入需要拉取的模型名称', 'warning')
    return
  }

  pullingModel.value = true
  pullStatus.value = `正在连接 Ollama 并拉取 ${modelToPull}...`
  try {
    // 注意：aiCoachService 上的拉取方法名为 pullOllamaModel（本地处理函数同名 pullLocalModel 与之无关）
    await aiCoachService.pullOllamaModel(modelToPull, currentEndpoint.value)
    pullStatus.value = `模型 ${modelToPull} 拉取就绪！`
    appStore.showToast(`模型 ${modelToPull} 拉取成功！`, 'success')
  } catch (err: any) {
    pullStatus.value = `拉取失败: ${err?.message || '未知错误'}`
    appStore.showToast(`模型拉取失败: ${err?.message || '请确认已开启 ollama serve'}`, 'error')
  } finally {
    pullingModel.value = false
  }
}

async function runEndpointTest() {
  testingEndpoint.value = true
  testResult.value = null

  try {
    let ep = currentEndpoint.value.trim()
    if (ep.endsWith('/openapi') || ep.endsWith('/openapi/')) {
      ep = ep.replace(/\/openapi\/?$/, '/v1')
      currentEndpoint.value = ep
    }

    const res = await aiCoachService.testEndpoint(
      activeMode.value,
      ep,
      currentApiKey.value,
      currentModel.value,
    )
    testResult.value = res
    if (res.success) {
      appStore.showToast(`AI 端点握手成功！延迟: ${res.latencyMs || 0}ms`, 'success')
    } else {
      appStore.showToast(`AI 端点连接失败: ${res.errorMessage || '请检查配置'}`, 'error')
    }
  } catch (err: any) {
    testResult.value = {
      success: false,
      latencyMs: 0,
      errorMessage: err.message || '连接异常',
      testedAt: new Date().toLocaleTimeString('zh-CN', { hour12: false }),
    }
    appStore.showToast(`测试失败: ${err.message}`, 'error')
  } finally {
    testingEndpoint.value = false
  }
}

function onModelInputFocus() {
  if (availableModels.value.length > 0) {
    isModelDropdownOpen.value = true
  }
}
</script>

<template>
  <section class="p-4 border border-slate-700/80 rounded-2xl bg-slate-900/90 select-none antialiased shadow-xl space-y-3">
    <!-- 顶部标题与模式切换 -->
    <div class="flex flex-col gap-2 justify-between sm:flex-row sm:items-center">
      <div class="flex items-center space-x-2.5">
        <BrainCircuit class="text-amber-400 shrink-0 h-4.5 w-4.5" />
        <div>
          <h2 class="text-sm text-slate-100 font-bold">
            战术大模型引擎配置
          </h2>
        </div>
      </div>

      <!-- 模式三选一 -->
      <div class="p-1 border border-slate-800 rounded-xl bg-slate-950 flex flex-wrap gap-1 items-center">
        <button
          type="button"
          class="text-xs px-3 py-1.5 rounded-lg flex gap-1.5 cursor-pointer transition-all items-center"
          :class="[
            activeMode === 'ollama'
              ? 'bg-amber-500 text-slate-950 font-bold shadow-sm'
              : 'text-slate-300 hover:text-white',
          ]"
          @click="activeMode = 'ollama'"
        >
          <Terminal class="h-3.5 w-3.5" />
          本地 Ollama
        </button>

        <button
          type="button"
          class="text-xs px-3 py-1.5 rounded-lg flex gap-1.5 cursor-pointer transition-all items-center"
          :class="[
            activeMode === 'remote'
              ? 'bg-amber-500 text-slate-950 font-bold shadow-sm'
              : 'text-slate-300 hover:text-white',
          ]"
          @click="activeMode = 'remote'"
        >
          <Server class="h-3.5 w-3.5" />
          远程 Response API
        </button>

        <button
          type="button"
          class="text-xs px-3 py-1.5 rounded-lg flex gap-1.5 cursor-pointer transition-all items-center"
          :class="[
            activeMode === 'rule_only'
              ? 'bg-amber-500 text-slate-950 font-bold shadow-sm'
              : 'text-slate-300 hover:text-white',
          ]"
          @click="activeMode = 'rule_only'"
        >
          <Zap class="h-3.5 w-3.5" />
          纯规则引擎
        </button>
      </div>
    </div>

    <!-- 1. 本地 Ollama 模式面板 (/chat/completions) -->
    <div v-if="activeMode === 'ollama'" class="p-4 border border-slate-800 rounded-xl bg-slate-950/70 space-y-4">
      <div class="text-xs text-slate-300 flex items-center justify-between">
        <span class="text-amber-300 font-medium flex gap-1.5 items-center">
          <Terminal class="h-3.5 w-3.5" />
          本地 Ollama 引擎（基于 /chat/completions 协议，默认免 Key）
        </span>
      </div>

      <div class="gap-3 grid grid-cols-1 md:grid-cols-2">
        <div>
          <label class="text-xs text-slate-200 font-bold mb-1.5 block">Ollama 端点地址</label>
          <input
            v-model="currentEndpoint"
            type="text"
            placeholder="http://127.0.0.1:11434/v1"
            class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-900 h-9 w-full focus:ring-1 focus:ring-amber-400"
            @change="onEndpointChange"
          >
        </div>

        <div>
          <label class="text-xs text-slate-200 font-bold mb-1.5 block">本地模型名称 (Model)</label>
          <div class="flex gap-2">
            <input
              v-model="currentModel"
              type="text"
              placeholder="例如: qwen3.5:1.5b"
              class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-900 flex-1 h-9 focus:ring-1 focus:ring-amber-400"
            >
            <button
              type="button"
              :disabled="pullingModel"
              class="text-xs text-slate-200 px-3 py-1.5 border border-slate-700 rounded-lg bg-slate-800 flex shrink-0 gap-1.5 cursor-pointer transition-colors items-center hover:bg-slate-700 disabled:opacity-50"
              title="向本地 Ollama 发起模型拉取指令"
              @click="pullLocalModel"
            >
              <Download class="h-3.5 w-3.5" :class="{ 'animate-bounce': pullingModel }" />
              <span>{{ pullingModel ? '拉取中...' : '拉取模型' }}</span>
            </button>
          </div>
        </div>
      </div>

      <div v-if="pullStatus" class="text-xs text-amber-300/90 font-mono px-3 py-1.5 border border-amber-900/40 rounded-lg bg-amber-950/20">
        {{ pullStatus }}
      </div>
    </div>

    <!-- 2. 远程 Response API 模式面板 (/responses) -->
    <div v-else-if="activeMode === 'remote'" class="p-4 border border-slate-800 rounded-xl bg-slate-950/70 space-y-4">
      <div class="text-xs text-slate-300 flex items-center justify-between">
        <span class="text-amber-300 font-medium flex gap-1.5 items-center">
          <Server class="h-3.5 w-3.5" />
          远程 Response API 引擎（基于 /responses 规范，需配置 API Key）
        </span>
      </div>

      <div class="gap-3 grid grid-cols-1 md:grid-cols-2">
        <div>
          <label class="text-xs text-slate-200 font-bold mb-1.5 block">远程端点 Base URL</label>
          <input
            v-model="currentEndpoint"
            type="text"
            placeholder="例如: https://api.deepseek.com/v1"
            class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-900 h-9 w-full focus:ring-1 focus:ring-amber-400"
            @change="onEndpointChange"
          >
        </div>

        <div>
          <label class="text-xs text-slate-200 font-bold mb-1.5 block">API 密钥 (API Key)</label>
          <input
            v-model="currentApiKey"
            type="password"
            placeholder="输入云端 API 密钥 (Bearer Token)"
            class="text-xs text-slate-100 font-mono px-3 outline-none border border-slate-700 rounded-lg bg-slate-900 h-9 w-full focus:ring-1 focus:ring-amber-400"
          >
        </div>
      </div>

      <div>
        <label class="text-xs text-slate-200 font-bold mb-1.5 flex items-center justify-between">
          <span>模型名称 (Model)</span>
          <span v-if="availableModels.length > 0" class="text-2xs text-amber-400/90 font-mono">
            已就绪 {{ availableModels.length }} 个模型
          </span>
        </label>

        <div class="flex gap-2 relative">
          <!-- 统一电竞暗黑样式的可输入 Combobox 容器 -->
          <div class="flex-1 relative">
            <input
              v-model="currentModel"
              type="text"
              placeholder="键入模型名或从下拉候选选用 (例如: deepseek-chat 或 qwen3.5:1.5b)"
              class="text-xs text-slate-100 font-mono pl-3 pr-8 outline-none border border-slate-700 rounded-lg bg-slate-900 h-9 w-full focus:ring-1 focus:ring-amber-400"
              @focus="onModelInputFocus"
            >
            <!-- 下拉展开/收起按钮 -->
            <button
              v-if="availableModels.length > 0"
              type="button"
              class="text-slate-400 cursor-pointer transition-colors right-2.5 top-2 absolute hover:text-amber-300"
              title="展开已拉取的模型候选列表"
              @click="isModelDropdownOpen = !isModelDropdownOpen"
            >
              <ChevronDown class="h-4 w-4 transition-transform duration-200" :class="{ 'rotate-180': isModelDropdownOpen }" />
            </button>

            <!-- 统一暗黑 HUD 下拉候选菜单 -->
            <div
              v-if="isModelDropdownOpen && availableModels.length > 0"
              class="border border-slate-700 rounded-lg bg-slate-900 max-h-56 shadow-2xl left-0 right-0 top-10 absolute z-50 overflow-y-auto divide-slate-800 divide-y"
            >
              <div
                v-for="m in availableModels"
                :key="m"
                class="text-xs text-slate-200 font-mono px-3 py-2 flex cursor-pointer transition-colors items-center justify-between hover:text-amber-300 hover:bg-slate-800"
                :class="{ 'bg-amber-500/15 text-amber-300 font-bold': currentModel === m }"
                @mousedown.prevent="currentModel = m; isModelDropdownOpen = false"
              >
                <span>{{ m }}</span>
                <Check v-if="currentModel === m" class="text-amber-400 shrink-0 h-3.5 w-3.5" />
              </div>
            </div>
          </div>

          <!-- 拉取模型按钮 -->
          <button
            type="button"
            :disabled="fetchingModels"
            class="text-xs text-amber-300 px-3 py-1.5 border border-amber-500/40 rounded-lg bg-amber-500/15 flex shrink-0 gap-1.5 cursor-pointer shadow-xs transition-colors items-center hover:bg-amber-500/25 disabled:opacity-50"
            title="向远程端点拉取可用模型列表"
            @click="fetchRemoteModels"
          >
            <RefreshCw class="h-3.5 w-3.5" :class="{ 'animate-spin': fetchingModels }" />
            <span>{{ fetchingModels ? '拉取中...' : '拉取模型' }}</span>
          </button>
        </div>
      </div>
    </div>

    <!-- 3. 本地纯规则模式 -->
    <div v-else class="p-4 py-6 text-center border border-slate-800 rounded-xl bg-slate-950/70 space-y-2">
      <Zap class="text-amber-400 mx-auto h-7 w-7" />
      <div class="text-xs text-slate-200 font-bold">
        已启用纯规则战术模式
      </div>
    </div>

    <!-- 测试端点按钮与结果 (仅在模型模式下展示) -->
    <div v-if="activeMode !== 'rule_only'" class="pt-1 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <button
        type="button"
        class="text-xs text-slate-950 font-bold px-4 py-2 rounded-xl bg-amber-500 flex cursor-pointer shadow-md transition-all items-center justify-center space-x-1.5 hover:bg-amber-400 disabled:opacity-50"
        :disabled="testingEndpoint"
        @click="runEndpointTest"
      >
        <Activity
          class="h-3.5 w-3.5"
          :class="{ 'animate-spin': testingEndpoint }"
        />
        <span>{{ testingEndpoint ? '正在测试连通性...' : '测试端点连通性' }}</span>
      </button>

      <!-- 测试结果反馈 -->
      <div v-if="testResult" class="flex-1 sm:max-w-md">
        <div
          v-if="testResult.success"
          class="text-xs text-emerald-400 font-mono p-2 border border-emerald-800/60 rounded-lg bg-emerald-950/40 flex items-center space-x-2"
        >
          <CheckCircle2 class="text-emerald-400 shrink-0 h-4 w-4" />
          <span class="truncate">
            连接成功 (延迟: {{ testResult.latencyMs }}ms) | 模型: {{ testResult.modelUsed || currentModel }}
          </span>
        </div>
        <div
          v-else
          class="text-xs text-rose-400 font-mono p-2 border border-rose-800/60 rounded-lg bg-rose-950/40 flex items-start space-x-2"
        >
          <XCircle class="text-rose-400 mt-0.5 shrink-0 h-4 w-4" />
          <div class="leading-relaxed break-all">
            {{ testResult.errorMessage || '端点连接失败' }}
          </div>
        </div>
      </div>
    </div>
  </section>
</template>
