<script setup lang="ts">
/**
 * 现代化系统设置核心视图 (src/views/SettingsView.vue)
 * 职责：
 * 1. 终结过度拆分，页面高度整合为两大清晰高内聚面板：
 *    - 战术偏好与选手 (tactics)：紧凑型个人账号绑定与指定 + 战术语音播报矩阵 (含试听与音量/语速)
 *    - AI 引擎与系统 (system)：战术大模型引擎配置 + 局内悬浮窗 HUD + 数据缓存管理 + 关于合规
 * 2. TTS 引擎收敛固定为内置原生离线 MeloTTS，不提供多余的独立配置面板，试听统一集成在语音提醒矩阵
 */

import {
  BrainCircuit,
  Shield,
  Sliders,
  Volume2,
} from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { useAppStore } from '@/stores'
import AboutSection from '@/views/settings/sections/AboutSection.vue'
import AiEngineSection from '@/views/settings/sections/AiEngineSection.vue'
import OverlaySection from '@/views/settings/sections/OverlaySection.vue'
import PlayerBindingSection from '@/views/settings/sections/PlayerBindingSection.vue'
import StorageSection from '@/views/settings/sections/StorageSection.vue'
import VoiceBroadcastSection from '@/views/settings/sections/VoiceBroadcastSection.vue'

defineOptions({ name: 'SettingsView' })

type SettingsTabKey = 'tactics' | 'system'

const appStore = useAppStore()
const activeTab = ref<SettingsTabKey>('tactics')

interface SettingsTabCard {
  key: SettingsTabKey
  title: string
  icon: any
}

const tabCards: SettingsTabCard[] = [
  {
    key: 'tactics',
    title: '战术偏好与选手',
    icon: Volume2,
  },
  {
    key: 'system',
    title: 'AI 引擎与系统',
    icon: BrainCircuit,
  },
]

onMounted(async () => {
  await appStore.ensureSettingsLoaded()
  appStore.syncCacheDirectory()
  appStore.checkOverlayStatus()
})
</script>

<template>
  <div class="text-slate-100 bg-dota-bg flex flex-1 flex-col select-none overflow-hidden">
    <!-- 顶部状态栏 -->
    <header class="px-6 border-b border-dota-border bg-dota-surface flex shrink-0 h-14 items-center justify-between">
      <div class="flex items-center space-x-3">
        <Sliders class="text-amber-400 h-5 w-5" />
        <h1 class="text-base text-amber-400 tracking-wide font-bold">
          系统首选项与战术引擎配置
        </h1>
      </div>
      <div class="text-xs text-slate-400 font-mono flex items-center space-x-2">
        <Shield class="text-emerald-400 h-4 w-4" />
        <span>GSI 4444 端口就绪 · 零外挂合规</span>
      </div>
    </header>

    <!-- 顶部二合一卡片式切换导航栏 (拒绝过度拆分，高聚类) -->
    <div class="px-6 pt-4 bg-dota-bg/90 shrink-0">
      <div class="gap-3 grid grid-cols-1 sm:grid-cols-2">
        <button
          v-for="card in tabCards"
          :key="card.key"
          type="button"
          class="group p-3 text-left border rounded-xl flex cursor-pointer transition-all duration-200 items-center relative overflow-hidden space-x-3.5"
          :class="[
            activeTab === card.key
              ? 'border-amber-500 bg-gradient-to-r from-amber-500/15 via-slate-900 to-slate-950 shadow-[0_4px_20px_rgba(245,158,11,0.2)]'
              : 'border-slate-800 bg-slate-900/60 hover:border-slate-700 hover:bg-slate-900/90',
          ]"
          @click="activeTab = card.key"
        >
          <!-- 图标 -->
          <div
            class="p-2 border rounded-lg shrink-0 transition-colors"
            :class="[
              activeTab === card.key
                ? 'border-amber-500/40 bg-amber-500/20 text-amber-400'
                : 'border-slate-800 bg-slate-950/80 text-slate-400 group-hover:text-slate-200',
            ]"
          >
            <component :is="card.icon" class="h-5 w-5" />
          </div>

          <!-- 文字信息 -->
          <div class="flex-1 min-w-0">
            <div
              class="text-sm tracking-wide font-bold transition-colors"
              :class="[
                activeTab === card.key
                  ? 'text-amber-300'
                  : 'text-slate-200 group-hover:text-white',
              ]"
            >
              {{ card.title }}
            </div>
          </div>

          <!-- 激活状态指示光条 -->
          <div
            v-if="activeTab === card.key"
            class="rounded-full bg-amber-400 h-0.75 shadow-[0_0_8px_rgba(245,158,11,0.8)] bottom-0 left-3 right-3 absolute"
          />
        </button>
      </div>
    </div>

    <!-- 设置主内容滚动区域 -->
    <div class="p-6 pb-10 flex-1 overflow-y-auto">
      <KeepAlive>
        <!-- 分组 1：战术偏好与选手 (紧凑账号绑定 + 9类战术语音提醒矩阵) -->
        <div v-if="activeTab === 'tactics'" key="tab-tactics" class="space-y-4">
          <PlayerBindingSection />
          <VoiceBroadcastSection />
        </div>

        <!-- 分组 2：AI 战术引擎与系统高级配置 (大模型配置 + 局内悬浮窗 + 离线缓存 + 关于) -->
        <div v-else-if="activeTab === 'system'" key="tab-system" class="space-y-5">
          <AiEngineSection />
          <OverlaySection />
          <StorageSection />
          <AboutSection />
        </div>
      </KeepAlive>
    </div>
  </div>
</template>
