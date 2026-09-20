<script setup lang="ts">
import { AlertCircle, CheckCircle2, Info, X } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import Sidebar from './components/layout/Sidebar.vue'
import TitleBar from './components/layout/TitleBar.vue'
import AnalysisPage from './pages/AnalysisPage.vue'
import CoachPage from './pages/CoachPage.vue'
import HeroesPage from './pages/HeroesPage.vue'
import ItemsPage from './pages/ItemsPage.vue'
import SettingsPage from './pages/SettingsPage.vue'
import { dataSyncService } from './services/dataSyncService'
import { gsiRuleEngine } from './services/gsiRuleEngine'
import { useAppStore } from './store'
import OverlayWindow from './windows/OverlayWindow.vue'

const { state, syncCacheDirectory, checkOverlayStatus, hideToast } = useAppStore()

function checkOverlayModeFromUrl(): boolean {
  if (typeof window === 'undefined') return false
  const params = new URLSearchParams(window.location.search)
  return params.get('window') === 'overlay' || window.location.hash.includes('overlay')
}

const isStandaloneOverlay = ref(checkOverlayModeFromUrl())

onMounted(async () => {
  const appApi = (window as any).go?.main?.App
  if (appApi?.IsOverlayMode) {
    try {
      const mode = await appApi.IsOverlayMode()
      if (mode) {
        isStandaloneOverlay.value = true
      }
    } catch (e) {
      console.warn('检测是否为悬浮窗模式失败:', e)
    }
  }

  if (!isStandaloneOverlay.value) {
    gsiRuleEngine.init()
    syncCacheDirectory()
    checkOverlayStatus()
    dataSyncService.initDataSync()

    // 监听 Wails v3 原生系统应用菜单与托盘导航事件 (开工铁律第 6 条与第 13 条)
    const win = window as any
    if (win?.runtime?.EventsOn) {
      win.runtime.EventsOn('navigate_to', (targetRoute: string) => {
        if (!targetRoute) return
        if (targetRoute.includes('settings')) state.currentTab = 'settings'
        else if (targetRoute.includes('coach')) state.currentTab = 'coach'
        else if (targetRoute.includes('analysis')) state.currentTab = 'analysis'
        else if (targetRoute.includes('heroes')) state.currentTab = 'heroes'
        else if (targetRoute.includes('items')) state.currentTab = 'items'
      })
    }
  }
})
</script>

<template>
  <!-- 1. 独立置顶桌面悬浮窗子进程/独立窗口模式 -->
  <div v-if="isStandaloneOverlay" class="bg-transparent h-screen w-screen overflow-hidden">
    <OverlayWindow />
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
        v-if="state.toast.visible"
        class="px-4 py-2.5 border rounded-xl flex max-w-md shadow-2xl items-center left-1/2 top-11 fixed z-[100] backdrop-blur-md space-x-2.5 -translate-x-1/2"
        :class="[
          state.toast.type === 'success' ? 'bg-emerald-950/95 border-emerald-500/60 text-emerald-200 shadow-[0_0_16px_rgba(16,185,129,0.25)]'
          : state.toast.type === 'warning' ? 'bg-amber-950/95 border-amber-500/60 text-amber-200 shadow-[0_0_16px_rgba(245,158,11,0.25)]'
            : state.toast.type === 'error' ? 'bg-rose-950/95 border-rose-500/60 text-rose-200 shadow-[0_0_16px_rgba(244,63,94,0.25)]'
              : 'bg-slate-900/95 border-slate-700/80 text-slate-200 shadow-[0_0_16px_rgba(15,23,42,0.5)]',
        ]"
      >
        <CheckCircle2 v-if="state.toast.type === 'success'" class="text-emerald-400 shrink-0 h-4 w-4" />
        <AlertCircle v-else-if="state.toast.type === 'warning'" class="text-amber-400 shrink-0 h-4 w-4" />
        <AlertCircle v-else-if="state.toast.type === 'error'" class="text-rose-400 shrink-0 h-4 w-4" />
        <Info v-else class="text-cyan-400 shrink-0 h-4 w-4" />

        <span class="text-xs leading-normal font-medium select-text">{{ state.toast.message }}</span>

        <button
          class="ml-2 p-0.5 rounded opacity-70 shrink-0 cursor-pointer transition-opacity hover:bg-white/10 hover:opacity-100"
          @click="hideToast"
        >
          <X class="h-3.5 w-3.5" />
        </button>
      </div>
    </transition>

    <!-- 顶部标题栏 -->
    <TitleBar />

    <!-- 下方主容器 (侧边栏 + 内容视口) -->
    <div class="flex flex-1 overflow-hidden">
      <Sidebar />

      <main class="flex flex-1 flex-col min-h-0 min-w-0 overflow-hidden">
        <HeroesPage v-if="state.currentTab === 'heroes'" />
        <ItemsPage v-else-if="state.currentTab === 'items'" />
        <CoachPage v-else-if="state.currentTab === 'coach'" />
        <AnalysisPage v-else-if="state.currentTab === 'analysis'" />
        <SettingsPage v-else-if="state.currentTab === 'settings'" />
      </main>
    </div>

    <!-- 局内经济悬浮窗 (支持在所有页面随时唤起与交互) -->
    <OverlayWindow v-if="state.isOverlayOpen" />
  </div>
</template>
