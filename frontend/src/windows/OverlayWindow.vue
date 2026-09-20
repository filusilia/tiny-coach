<script setup lang="ts">
import { Maximize2, Minimize2, X } from '@lucide/vue'
import { onMounted, onUnmounted, reactive, ref } from 'vue'
import { SetIgnoreMouseEvents } from '../../wailsjs/go/main/App'
import { useAppStore } from '../store'

const { state, setIsOverlayOpen } = useAppStore()

const isCollapsed = ref(false)
const opacity = ref(90)
const isStandalone = ref(false)

// 悬浮窗位置 (默认右上角)
const position = reactive<{ x: number | null, y: number }>({
  x: null,
  y: 56,
})

let isDragging = false
let dragOffset = { x: 0, y: 0 }

function startDrag(e: MouseEvent) {
  isDragging = true
  const rect = (e.currentTarget as HTMLElement).closest('.fixed')!.getBoundingClientRect()
  dragOffset = {
    x: e.clientX - rect.left,
    y: e.clientY - rect.top,
  }
  position.x = rect.left
  position.y = rect.top

  window.addEventListener('mousemove', onDrag)
  window.addEventListener('mouseup', stopDrag)
}

function onDrag(e: MouseEvent) {
  if (!isDragging) return
  const maxX = window.innerWidth - 300
  const maxY = window.innerHeight - 80
  position.x = Math.max(10, Math.min(maxX, e.clientX - dragOffset.x))
  position.y = Math.max(10, Math.min(maxY, e.clientY - dragOffset.y))
}

function stopDrag() {
  isDragging = false
  window.removeEventListener('mousemove', onDrag)
  window.removeEventListener('mouseup', stopDrag)
}

/** 鼠标移入浮窗卡片：关闭穿透，保证面板可点击、可拖拽、滑块可操作 */
async function onMouseEnter() {
  if (!isStandalone.value) return
  try {
    await SetIgnoreMouseEvents(false)
  } catch (e) {
    // 兼容浏览器测试环境
  }
}

/** 鼠标移出浮窗卡片：开启穿透，让游戏战斗点击完全直达底层 */
async function onMouseLeave() {
  if (!isStandalone.value || isDragging) return
  try {
    await SetIgnoreMouseEvents(true)
  } catch (e) {
    // 兼容浏览器测试环境
  }
}

/** 关闭悬浮窗：独立模式通知后端或关闭窗口，内嵌模式修改 Store 状态 */
async function handleClose() {
  if (isStandalone.value) {
    const appApi = (window as any).go?.main?.App
    if (appApi?.ToggleOverlayWindow) {
      try {
        await appApi.ToggleOverlayWindow()
        return
      } catch (e) {
        // 忽略
      }
    }
    try {
      window.close()
    } catch {}
    return
  }
  setIsOverlayOpen(false)
}

onMounted(async () => {
  if (typeof window !== 'undefined') {
    const params = new URLSearchParams(window.location.search)
    if (params.get('window') === 'overlay' || window.location.hash.includes('overlay')) {
      isStandalone.value = true
    }
  }

  const appApi = (window as any).go?.main?.App
  if (appApi?.IsOverlayMode) {
    try {
      const mode = await appApi.IsOverlayMode()
      if (mode) {
        isStandalone.value = true
      }
    } catch (e) {
      // 忽略非桌面环境错误
    }
  }

  if (isStandalone.value) {
    try {
      await SetIgnoreMouseEvents(true)
    } catch (e) {
      // 兼容浏览器测试环境
    }
  }
})

onUnmounted(() => {
  window.removeEventListener('mousemove', onDrag)
  window.removeEventListener('mouseup', stopDrag)
})
</script>

<template>
  <div
    ref="overlayRef"
    :style="isStandalone ? {
      opacity: opacity / 100,
    } : {
      left: position.x !== null ? `${position.x}px` : undefined,
      right: position.x === null ? '24px' : undefined,
      top: `${position.y}px`,
      opacity: opacity / 100,
    }"
    class="text-dota-text font-sans bg-[#121824]/95 flex flex-col select-none transition-shadow overflow-hidden backdrop-blur-md" :class="[
      isStandalone
        ? 'w-full h-full rounded-none border-none'
        : 'w-84 fixed z-50 rounded-xl border border-dota-gold/40 shadow-2xl',
    ]"
    @mouseenter="onMouseEnter"
    @mouseleave="onMouseLeave"
  >
    <!-- 悬浮窗顶栏 (支持自由拖拽移动) -->
    <div
      class="bg-dota-surface/90 border-dota-border px-3 border-b flex shrink-0 h-9 cursor-move items-center justify-between"
      @mousedown="startDrag"
    >
      <div class="flex items-center space-x-2">
        <div
          class="rounded-full h-2.5 w-2.5" :class="[
            state.gsiConnected ? 'bg-dota-green animate-pulse' : 'bg-dota-subtle',
          ]"
        />
        <span class="text-dota-goldLight text-xs font-bold">Aegis 局内经济天平</span>
        <span class="bg-dota-panel text-dota-cyan border-dota-border text-[10px] px-1 border rounded">置顶</span>
      </div>

      <div class="flex items-center space-x-1.5" @mousedown.stop>
        <!-- 透明度滑块 -->
        <div class="text-dota-subtle text-[10px] mr-1 flex items-center space-x-1">
          <span>透明</span>
          <input
            v-model="opacity"
            type="range"
            min="40"
            max="100"
            class="accent-dota-gold h-1 w-12 cursor-pointer"
          >
        </div>

        <!-- 最小化折叠 -->
        <button
          title="展开/收起"
          class="hover:bg-dota-hover text-dota-muted hover:text-dota-text p-1 rounded cursor-pointer transition-colors"
          @click="isCollapsed = !isCollapsed"
        >
          <component :is="isCollapsed ? Maximize2 : Minimize2" class="h-3.5 w-3.5" />
        </button>

        <!-- 关闭悬浮窗 -->
        <button
          title="关闭悬浮窗"
          class="hover:bg-dota-red/80 text-dota-muted p-1 rounded cursor-pointer transition-colors hover:text-white"
          @click="handleClose"
        >
          <X class="h-3.5 w-3.5" />
        </button>
      </div>
    </div>

    <!-- 展开内容区 (严格不包含任何 mock 假数据) -->
    <div v-if="!isCollapsed" class="text-xs p-3 space-y-2.5">
      <div v-if="!state.gsiConnected" class="bg-dota-panel/60 border-dota-border p-4 text-center border rounded-lg border-dashed space-y-2">
        <div class="text-dota-gold text-xs font-bold">
          等待 Dota 2 客户端对局接入
        </div>
        <p class="text-dota-subtle text-[11px] leading-relaxed">
          请启动 Dota 2 并进入对局，系统将通过 4444 端口实时解析敌我双方买活状态与经济差距。
        </p>
      </div>

      <div v-else class="space-y-2">
        <div class="border-dota-border text-dota-subtle text-[11px] font-mono pb-1.5 border-b flex items-center justify-between">
          <span class="text-dota-green">天辉方实时追踪</span>
          <span class="text-dota-red">夜魇方实时追踪</span>
        </div>
        <div class="text-dota-muted text-xs py-3 text-center">
          实时战况监测在线 · 正在持续监听 10Hz GSI 快照流
        </div>
      </div>
    </div>
  </div>
</template>
