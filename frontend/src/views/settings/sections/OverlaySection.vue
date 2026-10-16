<script setup lang="ts">
/**
 * 局内独立置顶经济悬浮窗设置组件 (src/views/settings/sections/OverlaySection.vue)
 */

import { Shield } from '@lucide/vue'
import { useAppStore } from '@/stores'

defineOptions({ name: 'OverlaySection' })

const appStore = useAppStore()

function onOpacityInput(e: Event) {
  const val = Number((e.target as HTMLInputElement).value)
  appStore.updateSettings({ overlay_opacity: val })
  // 跨窗口实时广播透明度变更 (Wails Event 总线)
  const win = window as any
  if (win.wails?.Events?.Emit) {
    win.wails.Events.Emit('overlay_opacity_changed', val)
  }
}

function onAutoOverlayChange(e: Event) {
  const checked = (e.target as HTMLInputElement).checked
  appStore.updateSettings({ auto_overlay_on_game_start: checked })
}
</script>

<template>
  <section class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 select-none shadow-xl space-y-4">
    <div class="flex items-center space-x-3">
      <Shield class="text-amber-400 h-5 w-5" />
      <div>
        <h2 class="text-sm text-slate-100 font-bold">
          局内独立置顶经济悬浮窗
        </h2>
      </div>
    </div>

    <div class="p-4 border border-slate-800 rounded-xl bg-slate-950/60 space-y-4">
      <!-- 悬浮窗透明度调节滑动条 -->
      <div class="space-y-2">
        <div class="flex items-center justify-between">
          <label class="text-xs text-slate-200 font-bold">悬浮窗不透明度 (Opacity)</label>
          <span class="text-xs text-amber-400 font-bold font-mono">{{ appStore.settings.overlay_opacity ?? 90 }}%</span>
        </div>
        <div class="flex gap-3 items-center">
          <span class="text-xs text-slate-400 font-mono">30%</span>
          <input
            :value="appStore.settings.overlay_opacity ?? 90"
            type="range"
            min="30"
            max="100"
            step="5"
            class="appearance-none accent-amber-500 rounded-lg bg-slate-800 flex-1 h-1.5 cursor-pointer"
            @input="onOpacityInput"
          >
          <span class="text-xs text-slate-400 font-mono">100%</span>
        </div>
        <p class="text-xs text-slate-400">
          调节悬浮窗底板与信息整体透明度，确保既能洞察战局经济，又不遮挡英雄血量与小地图。
        </p>
      </div>

      <div class="pt-3 border-t border-slate-800/80 gap-3 grid grid-cols-1 sm:grid-cols-2">
        <!-- 自动呼出开关 -->
        <div class="p-3 border border-slate-800 rounded-lg bg-slate-900/60 flex items-center justify-between">
          <div>
            <div class="text-xs text-slate-200 font-bold">对局开始自动呼出</div>
            <div class="text-xs text-slate-400">建立真实 GSI 连接并选定英雄后自动开启</div>
          </div>
          <input
            type="checkbox"
            class="accent-amber-500 rounded h-4 w-4 cursor-pointer"
            :checked="appStore.settings.auto_overlay_on_game_start"
            @change="onAutoOverlayChange"
          >
        </div>

        <!-- 快捷键提示与位置重置 -->
        <div class="p-3 border border-slate-800 rounded-lg bg-slate-900/60 flex items-center justify-between">
          <div>
            <div class="text-xs text-slate-200 font-bold">交互与穿透快捷键</div>
            <div class="text-xs text-slate-400">
              <kbd class="text-xs text-slate-300 font-mono px-1 py-0.5 border border-slate-700 rounded bg-slate-800">Alt+F2</kbd> 显隐 |
              <kbd class="text-xs text-slate-300 font-mono px-1 py-0.5 border border-slate-700 rounded bg-slate-800">Alt+F3</kbd> 穿透 (顶部锁仅锁定拖拽位置，游戏内点击穿透请按 Alt+F3)
            </div>
          </div>
          <button
            class="text-xs text-slate-300 px-2.5 py-1.5 border border-slate-700 rounded-lg bg-slate-800 shrink-0 cursor-pointer transition-colors hover:bg-slate-700"
            title="清除记忆并重置悬浮窗至主显示器中央"
            @click="appStore.resetOverlayPosition"
          >
            重置位置
          </button>
        </div>
      </div>
    </div>
  </section>
</template>
