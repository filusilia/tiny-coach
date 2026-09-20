<script setup lang="ts">
import { Minus, RefreshCw, Search, Square, X } from '@lucide/vue'
import { computed } from 'vue'
import { useAppStore } from '../../store'

const { state, setSearchQuery } = useAppStore()

function onSearchInput(e: Event) {
  setSearchQuery((e.target as HTMLInputElement).value)
}

// 检查是否存在 Wails 原生 runtime API 且为无边框模式
const isWailsFrameless = computed(() => {
  return typeof window !== 'undefined' && Boolean((window as any).runtime?.WindowMinimise && (window as any).__WAILS_FRAMELESS__)
})

function minimizeWindow() {
  (window as any).runtime?.WindowMinimise()
}

function maximizeWindow() {
  (window as any).runtime?.WindowToggleMaximise()
}

function closeWindow() {
  (window as any).runtime?.Quit()
}

function reloadPage() {
  window.location.reload()
}
</script>

<template>
  <header class="bg-dota-surface border-dota-border px-3 border-b flex h-11 select-none items-center justify-between z-50">
    <!-- 1. 左侧 Logo 与名称 -->
    <div class="flex min-w-[200px] items-center space-x-2.5">
      <img src="/assets/icons/app_256.png" alt="AegisCoach" class="h-6 w-6 object-contain">
      <span class="text-dota-goldLight text-base tracking-wide font-bold">神盾教练</span>
    </div>

    <!-- 2. 搜索框 -->
    <div class="max-w-sm w-80 relative">
      <input
        type="text"
        :value="state.searchQuery"
        placeholder="搜索英雄或物品 (Ctrl+F)..."
        class="bg-dota-panel border-dota-border focus:border-dota-gold text-dota-text placeholder:text-dota-subtle text-xs px-8 outline-none border rounded h-7 w-full transition-colors"
        @input="onSearchInput"
      >
      <Search class="text-dota-subtle h-3.5 w-3.5 left-2.5 top-2 absolute" />
      <button
        v-if="state.searchQuery"
        class="text-dota-subtle hover:text-dota-text right-2.5 top-1.5 absolute"
        @click="setSearchQuery('')"
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
        title="刷新数据"
        class="hover:bg-dota-hover text-dota-muted rounded flex h-7 w-7 transition-colors items-center justify-center"
        @click="reloadPage"
      >
        <RefreshCw class="h-3.5 w-3.5" />
      </button>

      <!-- 仅在无边框 Wails 窗口下渲染窗口三键；若原生窗口自带标题栏，自动隐藏以杜绝双标题栏 -->
      <template v-if="isWailsFrameless">
        <div class="bg-dota-border mx-1 h-3.5 w-[1px]" />

        <button
          class="hover:bg-dota-hover text-dota-muted rounded flex h-7 w-7 transition-colors items-center justify-center"
          @click="minimizeWindow"
        >
          <Minus class="h-3.5 w-3.5" />
        </button>
        <button
          class="hover:bg-dota-hover text-dota-muted rounded flex h-7 w-7 transition-colors items-center justify-center"
          @click="maximizeWindow"
        >
          <Square class="h-3 w-3" />
        </button>
        <button
          class="hover:bg-dota-red/80 text-dota-muted rounded flex h-7 w-7 transition-colors items-center justify-center hover:text-white"
          @click="closeWindow"
        >
          <X class="h-3.5 w-3.5" />
        </button>
      </template>
    </div>
  </header>
</template>
