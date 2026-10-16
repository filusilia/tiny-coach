<script setup lang="ts">
import { RefreshCw, Search, X } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import WindowControls from '@/components/layout/WindowControls.vue'
import { useAppStore } from '@/stores'

const appStore = useAppStore()
const route = useRoute()

// 仅在英雄库 (/heroes) 与装备库 (/items) 页面显示搜索框
const showSearch = computed(() => {
  const p = route?.path || ''
  return p.startsWith('/heroes') || p.startsWith('/items')
})

// 实时教练 (/coach) 与赛后分析 (/analysis) 页面不需要顶部搜索框和刷新按钮
const showRefresh = computed(() => {
  const p = route?.path || ''
  return !p.startsWith('/coach') && !p.startsWith('/analysis')
})

function onSearchInput(e: Event) {
  appStore.setSearchQuery((e.target as HTMLInputElement).value)
}

// 检查是否存在 Wails 原生 runtime API 且为无边框模式
const isWailsFrameless = computed(() => {
  return typeof window !== 'undefined' && Boolean((window as any).runtime?.WindowMinimise && (window as any).__WAILS_FRAMELESS__)
})

function reloadPage() {
  window.location.reload()
}
</script>

<template>
  <header class="px-3 border-b border-dota-border bg-dota-surface flex h-11 select-none items-center justify-between z-50">
    <!-- 1. 左侧 Logo 与名称 -->
    <div class="flex min-w-[200px] items-center space-x-2.5">
      <img src="/assets/icons/app_256.png" alt="AegisCoach" class="h-6 w-6 object-contain">
      <span class="text-base text-dota-goldLight tracking-wide font-bold">神盾教练</span>
    </div>

    <!-- 2. 搜索框 (仅在英雄库与物品库呈现，实时教练与赛后分析不呈现) -->
    <div v-if="showSearch" class="max-w-sm w-80 relative">
      <input
        type="text"
        :value="appStore.searchQuery"
        placeholder="搜索英雄或物品 (Ctrl+F)..."
        class="text-xs text-dota-text px-8 outline-none border border-dota-border rounded bg-dota-panel h-7 w-full transition-colors placeholder:text-dota-subtle focus:border-dota-gold"
        @input="onSearchInput"
      >
      <Search class="text-dota-subtle h-3.5 w-3.5 left-2.5 top-2 absolute" />
      <button
        v-if="appStore.searchQuery"
        class="text-dota-subtle right-2.5 top-1.5 absolute hover:text-dota-text"
        @click="appStore.setSearchQuery('')"
      >
        <X class="h-3.5 w-3.5" />
      </button>
    </div>

    <!-- 3. 中间拖拽区 -->
    <div
      :style="isWailsFrameless ? { '--wails-draggable': 'drag' } : {}"
      class="flex-1 h-full cursor-default"
    />

    <!-- 4. 右侧动作按钮区 (功能按钮 + 条件渲染的窗口三键) -->
    <div class="flex items-center space-x-1">
      <button
        v-if="showRefresh"
        title="刷新数据"
        class="text-dota-muted rounded flex h-7 w-7 transition-colors items-center justify-center hover:bg-dota-hover"
        @click="reloadPage"
      >
        <RefreshCw class="h-3.5 w-3.5" />
      </button>

      <WindowControls />
    </div>
  </header>
</template>
