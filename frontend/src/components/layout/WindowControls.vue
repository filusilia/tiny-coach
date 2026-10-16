<script setup lang="ts">
import { Minus, Square, X } from '@lucide/vue'
import { computed } from 'vue'

defineOptions({ name: 'WindowControls' })

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
</script>

<template>
  <div v-if="isWailsFrameless" class="flex items-center space-x-1" style="--wails-draggable: no-drag">
    <div class="mx-1 bg-slate-800 h-3.5 w-[1px]" />
    <button
      title="最小化"
      type="button"
      class="text-slate-400 rounded flex h-7 w-7 transition-colors items-center justify-center hover:bg-slate-800 hover:text-slate-200"
      @click="minimizeWindow"
    >
      <Minus class="h-3.5 w-3.5" />
    </button>
    <button
      title="最大化"
      type="button"
      class="text-slate-400 rounded flex h-7 w-7 transition-colors items-center justify-center hover:bg-slate-800 hover:text-slate-200"
      @click="maximizeWindow"
    >
      <Square class="h-3 w-3" />
    </button>
    <button
      title="关闭"
      type="button"
      class="text-slate-400 rounded flex h-7 w-7 transition-colors items-center justify-center hover:text-white hover:bg-rose-600/80"
      @click="closeWindow"
    >
      <X class="h-3.5 w-3.5" />
    </button>
  </div>
</template>
