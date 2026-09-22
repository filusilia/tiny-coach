<script setup lang="ts">
import { AlertCircle, CheckCircle2, Info, X } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { overlayApi } from './api'
import Sidebar from './components/layout/Sidebar.vue'
import TitleBar from './components/layout/TitleBar.vue'
import { useGsiStream } from './composables/useGsiStream'
import { useNativeNavigation } from './composables/useNativeNavigation'
import { dataSyncService } from './services/dataSyncService'
import { gsiRuleEngine } from './services/gsiRuleEngine'
import { useAppStore } from './stores/app'
import OverlayView from './views/OverlayView.vue'

const appStore = useAppStore()

// 启用 GSI 10Hz 流合并与原生菜单导航集成
useGsiStream()
useNativeNavigation()

function checkOverlayModeFromUrl(): boolean {
  if (typeof window === 'undefined') return false
  const params = new URLSearchParams(window.location.search)
  return params.get('window') === 'overlay' || window.location.hash.includes('overlay')
}

const isStandaloneOverlay = ref(checkOverlayModeFromUrl())

onMounted(async () => {
  try {
    const mode = await overlayApi.isOverlayMode()
    if (mode) {
      isStandaloneOverlay.value = true
    }
  } catch (e) {
    console.warn('[App] 检测是否为悬浮窗模式失败:', e)
  }

  if (!isStandaloneOverlay.value) {
    gsiRuleEngine.init()
    appStore.syncCacheDirectory()
    appStore.checkOverlayStatus()
    dataSyncService.initDataSync()

    // 注册全局 Alt+F2 快捷键响应，确保前端获得焦点时一触即发
    window.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.altKey && (e.key === 'F2' || e.code === 'F2')) {
        e.preventDefault()
        overlayApi.toggleOverlayWindow().catch((err) => {
          console.warn('[App] Alt+F2 切换悬浮窗异常:', err)
        })
      }
    })
  }
})
</script>

<template>
  <!-- 1. 独立置顶桌面悬浮窗子进程/独立窗口模式 -->
  <div v-if="isStandaloneOverlay" class="bg-transparent h-screen w-screen overflow-hidden">
    <OverlayView />
  </div>

  <!-- 2. 主程序界面 -->
  <div v-else class="bg-dota-bg flex flex-col h-screen w-screen relative overflow-hidden">
    <!-- 全局轻量 Toast 消息通知 (替代原生 alert) -->
    <transition
      enter-active-class="transition duration-200 ease-out"
      enter-from-class="transform -translate-y-4 opacity-0"
      enter-to-class="transform translate-y-0 opacity-100"
      leave-active-class="transition duration-150 ease-in"
      leave-from-class="transform translate-y-0 opacity-100"
      leave-to-class="transform -translate-y-4 opacity-0"
    >
      <div
        v-if="appStore.toast.visible"
        class="px-4 py-2.5 border rounded-xl flex max-w-md shadow-2xl items-center left-1/2 top-11 fixed z-[100] backdrop-blur-md space-x-2.5 -translate-x-1/2"
        :class="[
          appStore.toast.type === 'success' ? 'bg-emerald-950/95 border-emerald-500/60 text-emerald-200 shadow-[0_0_16px_rgba(16,185,129,0.25)]'
          : appStore.toast.type === 'warning' ? 'bg-amber-950/95 border-amber-500/60 text-amber-200 shadow-[0_0_16px_rgba(245,158,11,0.25)]'
            : appStore.toast.type === 'error' ? 'bg-rose-950/95 border-rose-500/60 text-rose-200 shadow-[0_0_16px_rgba(244,63,94,0.25)]'
              : 'bg-slate-900/95 border-slate-700/80 text-slate-200 shadow-[0_0_16px_rgba(15,23,42,0.5)]',
        ]"
      >
        <CheckCircle2 v-if="appStore.toast.type === 'success'" class="text-emerald-400 shrink-0 h-4 w-4" />
        <AlertCircle v-else-if="appStore.toast.type === 'warning'" class="text-amber-400 shrink-0 h-4 w-4" />
        <AlertCircle v-else-if="appStore.toast.type === 'error'" class="text-rose-400 shrink-0 h-4 w-4" />
        <Info v-else class="text-cyan-400 shrink-0 h-4 w-4" />

        <span class="text-xs leading-normal font-medium select-text">{{ appStore.toast.message }}</span>

        <button
          class="ml-2 p-0.5 rounded opacity-70 shrink-0 cursor-pointer transition-opacity hover:bg-white/10 hover:opacity-100"
          @click="appStore.hideToast"
        >
          <X class="h-3.5 w-3.5" />
        </button>
      </div>
    </transition>

    <!-- 顶部标题栏 -->
    <TitleBar />

    <!-- 下方主容器 (侧边栏 + 路由视口) -->
    <div class="flex flex-1 overflow-hidden">
      <Sidebar />

      <main class="flex flex-1 flex-col min-h-0 min-w-0 overflow-hidden">
        <router-view />
      </main>
    </div>
  </div>
</template>
