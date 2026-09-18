<template>
  <header class="h-11 bg-dota-surface border-b border-dota-border flex items-center justify-between px-3 select-none z-50">
    <!-- 1. 左侧 Logo 与名称 -->
    <div class="flex items-center space-x-2.5 min-w-[200px]">
      <img src="/assets/icons/app_256.png" alt="AegisCoach" class="w-6 h-6 object-contain" />
      <span class="text-base font-bold tracking-wide text-dota-goldLight">神盾教练</span>
    </div>

    <!-- 2. 搜索框 -->
    <div class="relative w-80 max-w-sm">
      <input
        type="text"
        :value="state.searchQuery"
        @input="onSearchInput"
        placeholder="搜索英雄或物品 (Ctrl+F)..."
        class="w-full h-7 bg-dota-panel border border-dota-border focus:border-dota-gold rounded px-8 text-xs text-dota-text placeholder:text-dota-subtle outline-none transition-colors"
      />
      <Search class="w-3.5 h-3.5 text-dota-subtle absolute left-2.5 top-2" />
      <button
        v-if="state.searchQuery"
        @click="setSearchQuery('')"
        class="absolute right-2.5 top-1.5 text-dota-subtle hover:text-dota-text"
      >
        <X class="w-3.5 h-3.5" />
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
        @click="setIsOverlayOpen(!state.isOverlayOpen)"
        title="打开/隐藏局内经济悬浮窗"
        :class="[
          'h-7 px-2.5 rounded text-xs flex items-center space-x-1.5 transition-colors',
          state.isOverlayOpen
            ? 'bg-dota-gold/20 text-dota-gold border border-dota-gold/40'
            : 'hover:bg-dota-hover text-dota-muted'
        ]"
      >
        <Layers class="w-3.5 h-3.5" />
        <span class="hidden sm:inline font-medium">经济悬浮窗</span>
      </button>

      <button
        @click="reloadPage"
        title="刷新数据"
        class="h-7 w-7 rounded flex items-center justify-center hover:bg-dota-hover text-dota-muted transition-colors"
      >
        <RefreshCw class="w-3.5 h-3.5" />
      </button>

      <!-- 仅在无边框 Wails 窗口下渲染窗口三键；若原生窗口自带标题栏，自动隐藏以杜绝双标题栏 -->
      <template v-if="isWailsFrameless">
        <div class="w-[1px] h-3.5 bg-dota-border mx-1" />

        <button
          @click="minimizeWindow"
          class="h-7 w-7 rounded flex items-center justify-center hover:bg-dota-hover text-dota-muted transition-colors"
        >
          <Minus class="w-3.5 h-3.5" />
        </button>
        <button
          @click="maximizeWindow"
          class="h-7 w-7 rounded flex items-center justify-center hover:bg-dota-hover text-dota-muted transition-colors"
        >
          <Square class="w-3 h-3" />
        </button>
        <button
          @click="closeWindow"
          class="h-7 w-7 rounded flex items-center justify-center hover:bg-dota-red/80 hover:text-white text-dota-muted transition-colors"
        >
          <X class="w-3.5 h-3.5" />
        </button>
      </template>
    </div>
  </header>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { Search, Layers, Minus, Square, X, RefreshCw } from 'lucide-vue-next';
import { useAppStore } from '../../store';

const { state, setSearchQuery, setIsOverlayOpen } = useAppStore();

const onSearchInput = (e: Event) => {
  setSearchQuery((e.target as HTMLInputElement).value);
};

// 检查是否存在 Wails 原生 runtime API 且为无边框模式
const isWailsFrameless = computed(() => {
  return typeof window !== 'undefined' && Boolean((window as any).runtime?.WindowMinimise && (window as any).__WAILS_FRAMELESS__);
});

const minimizeWindow = () => {
  (window as any).runtime?.WindowMinimise();
};

const maximizeWindow = () => {
  (window as any).runtime?.WindowToggleMaximise();
};

const closeWindow = () => {
  (window as any).runtime?.Quit();
};

const reloadPage = () => {
  window.location.reload();
};
</script>
