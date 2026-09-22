<script setup lang="ts">
/**
 * 局内独立置顶经济雷达悬浮窗视图 (src/views/OverlayView.vue)
 * 遵循开工铁律第 8 条（零伪造原则）：
 * 1. 严禁在未建立真实对局连接时伪造任何虚拟英雄、经济差或战局时间；
 * 2. 当 GSI 未连接或英雄未就绪时，严格呈现“等待 GSI 4444 端口接入”真实空态守卫；
 * 3. 游戏建立连接后，全量呈现由后端派发的真实快照数据。
 */

import {
  Maximize2,
  Minimize2,
  Radio,
  Shield,
  WifiOff,
  X,
} from '@lucide/vue'
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { overlayApi } from '../api'
import { HERO_CHINESE_NAMES } from '../data/heroNames'
import { useAppStore } from '../stores/app'
import { useBattleStore } from '../stores/battle'

const appStore = useAppStore()
const battleStore = useBattleStore()

/** 悬浮窗折叠状态 */
const isCollapsed = ref(false)

/** 悬浮窗透明度 (30% ~ 100%) */
const opacity = ref(92)

/** 是否运行在独立置顶窗口进程中 */
const isStandalone = ref(false)

/** 悬浮窗拖拽位置 (默认靠右上) */
const position = reactive<{ x: number | null, y: number }>({
  x: null,
  y: 56,
})

let isDragging = false
let dragOffset = { x: 0, y: 0 }

/**
 * 启动窗口拖拽
 */
function startDrag(e: MouseEvent) {
  isDragging = true
  const target = (e.currentTarget as HTMLElement).closest('.overlay-container') as HTMLElement
  if (!target) return
  const rect = target.getBoundingClientRect()
  dragOffset = {
    x: e.clientX - rect.left,
    y: e.clientY - rect.top,
  }
  position.x = rect.left
  position.y = rect.top

  window.addEventListener('mousemove', onDrag)
  window.addEventListener('mouseup', stopDrag)
}

/**
 * 拖拽移动处理 (限制在可视窗口范围内)
 */
function onDrag(e: MouseEvent) {
  if (!isDragging) return
  const maxX = window.innerWidth - 280
  const maxY = window.innerHeight - 60
  position.x = Math.max(10, Math.min(maxX, e.clientX - dragOffset.x))
  position.y = Math.max(10, Math.min(maxY, e.clientY - dragOffset.y))
}

/**
 * 停止拖拽
 */
function stopDrag() {
  isDragging = false
  window.removeEventListener('mousemove', onDrag)
  window.removeEventListener('mouseup', stopDrag)
}

/** 鼠标移入浮窗：关闭穿透，支持点击与调节 */
async function onMouseEnter() {
  if (!isStandalone.value) return
  try {
    await overlayApi.setIgnoreMouseEvents(false)
  } catch {
    // 忽略开发环境模拟错误
  }
}

/** 鼠标移出浮窗：恢复穿透，游戏战斗点击直通底层 */
async function onMouseLeave() {
  if (!isStandalone.value || isDragging) return
  try {
    await overlayApi.setIgnoreMouseEvents(true)
  } catch {
    // 忽略开发环境模拟错误
  }
}

/**
 * 关闭悬浮窗：通知后端收起原生置顶窗口
 */
async function handleClose() {
  try {
    await overlayApi.toggleOverlayWindow()
  } catch {
    // 忽略
  }
  appStore.setIsOverlayOpen(false)
}

// 真实对局快照数据衍生计算
const isGsiActive = computed<boolean>(() => {
  return battleStore.gsiConnected && Boolean(battleStore.snapshot)
})

const heroName = computed<string>(() => {
  const raw = battleStore.snapshot?.hero?.name || ''
  const hid = battleStore.snapshot?.hero?.id
  if (hid && HERO_CHINESE_NAMES[hid]) {
    return HERO_CHINESE_NAMES[hid]
  }
  return raw.replace('npc_dota_hero_', '') || '等待英雄入场'
})

const heroLevel = computed<number>(() => {
  return battleStore.snapshot?.hero?.level || 1
})

const heroHpPercent = computed<number>(() => {
  return battleStore.snapshot?.hero?.healthPercent || 0
})

const heroManaPercent = computed<number>(() => {
  return battleStore.snapshot?.hero?.manaPercent || 0
})

const currentGold = computed<number>(() => {
  return battleStore.snapshot?.player?.gold || 0
})

const kdaString = computed<string>(() => {
  const p = battleStore.snapshot?.player
  if (!p) return '0 / 0 / 0'
  return `${p.kills ?? 0} / ${p.deaths ?? 0} / ${p.assists ?? 0}`
})

const gpm = computed<number>(() => {
  return battleStore.snapshot?.player?.goldPerMin || 0
})

const xpm = computed<number>(() => {
  return battleStore.snapshot?.player?.xpPerMin || 0
})

const formattedGameTime = computed<string>(() => {
  const sec = battleStore.gameTime
  if (sec <= 0) return '00:00'
  const m = Math.floor(sec / 60)
  const s = sec % 60
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
})

const buybackCost = computed<number>(() => {
  return battleStore.snapshot?.hero?.buybackCost || 0
})

const buybackCooldown = computed<number>(() => {
  return battleStore.snapshot?.hero?.buybackCooldown || 0
})

const canBuyback = computed<boolean>(() => {
  return currentGold.value >= buybackCost.value && buybackCooldown.value === 0
})

onMounted(async () => {
  // 检测是否处于独立悬浮窗环境
  try {
    const standalone = await overlayApi.isOverlayMode()
    isStandalone.value = Boolean(standalone)
  } catch {
    const params = new URLSearchParams(window.location.search)
    isStandalone.value = params.get('window') === 'overlay' || window.location.hash.includes('overlay')
  }

  if (isStandalone.value) {
    try {
      await overlayApi.setIgnoreMouseEvents(true)
    } catch {
      // 忽略
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
    class="overlay-container select-none transition-all fixed z-50"
    :class="[
      isStandalone ? 'inset-0 pointer-events-none' : '',
    ]"
    :style="!isStandalone ? {
      right: position.x === null ? '24px' : 'auto',
      left: position.x !== null ? `${position.x}px` : 'auto',
      top: `${position.y}px`,
    } : {}"
    @mouseenter="onMouseEnter"
    @mouseleave="onMouseLeave"
  >
    <!-- 悬浮窗卡片主体 (零伪造真实状态呈现) -->
    <div
      class="border border-slate-700/80 rounded-xl bg-slate-950/95 flex flex-col pointer-events-auto shadow-2xl transition-all duration-200 overflow-hidden backdrop-blur-md"
      :style="{
        opacity: opacity / 100,
        width: isCollapsed ? '220px' : '330px',
      }"
    >
      <!-- 顶部把手与标题栏 -->
      <div
        class="px-3 py-2 border-b border-slate-800 bg-slate-900 flex cursor-move items-center justify-between"
        @mousedown="startDrag"
      >
        <div class="flex gap-2 items-center">
          <!-- 状态呼吸指示灯：绿灯连接，黄灯等待 -->
          <div
            class="rounded-full h-2.5 w-2.5"
            :class="[
              isGsiActive ? 'bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.8)] animate-pulse' : 'bg-amber-500 animate-ping',
            ]"
          />
          <span class="text-xs text-slate-100 tracking-wider font-bold flex items-center space-x-1.5">
            <Shield class="text-amber-400 h-3.5 w-3.5" />
            <span>神盾局内雷达</span>
          </span>
          <span
            v-if="isGsiActive"
            class="text-[9px] text-emerald-400 font-mono px-1 py-0.2 border border-emerald-800/60 rounded bg-emerald-950"
          >
            {{ formattedGameTime }}
          </span>
        </div>

        <div class="flex gap-1 items-center">
          <!-- 折叠/展开 -->
          <button
            class="text-slate-400 p-1 rounded cursor-pointer transition-colors hover:text-white hover:bg-slate-800"
            :title="isCollapsed ? '展开悬浮窗' : '折叠悬浮窗'"
            @click.stop="isCollapsed = !isCollapsed"
          >
            <Minimize2 v-if="!isCollapsed" class="h-3.5 w-3.5" />
            <Maximize2 v-else class="h-3.5 w-3.5" />
          </button>

          <!-- 关闭 -->
          <button
            class="text-slate-400 p-1 rounded cursor-pointer transition-colors hover:text-rose-400 hover:bg-rose-950/60"
            title="隐藏悬浮窗"
            @click.stop="handleClose"
          >
            <X class="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      <!-- 战况主体 (未折叠态) -->
      <div v-if="!isCollapsed" class="text-xs p-3 space-y-2.5">
        <!-- 严守开工铁律 8：GSI 未连接空态守卫，严禁任何假数据 -->
        <div
          v-if="!isGsiActive"
          class="p-3.5 text-center border border-amber-800/50 rounded-lg bg-amber-950/20 space-y-2"
        >
          <div class="flex justify-center">
            <WifiOff class="text-amber-400 h-6 w-6" />
          </div>
          <div class="space-y-0.5">
            <div class="text-xs text-amber-300 font-bold flex items-center justify-center space-x-1">
              <Radio class="h-3.5 w-3.5 animate-pulse" />
              <span>等待 GSI 4444 端口接入</span>
            </div>
            <p class="text-[11px] text-slate-400 leading-relaxed">
              请启动 Dota 2 客户端并进入比赛，系统将自动开始追踪真实英雄经济与买活状态。
            </p>
          </div>
          <div class="text-[10px] text-slate-500 font-mono pt-1 border-t border-amber-900/40">
            开工铁律红线：严禁渲染任何虚拟假数据
          </div>
        </div>

        <!-- GSI 已连接：渲染真实快照数据 -->
        <div v-else class="space-y-2.5">
          <!-- 操纵英雄与血蓝状态 -->
          <div class="p-2.5 border border-slate-800 rounded-lg bg-slate-900/80 space-y-1.5">
            <div class="flex items-center justify-between">
              <div class="text-slate-100 font-bold flex items-center space-x-1.5">
                <span class="text-amber-400">{{ heroName }}</span>
                <span class="text-[10px] text-slate-400 font-mono">Lv.{{ heroLevel }}</span>
              </div>
              <span class="text-[11px] text-emerald-300 font-mono font-semibold">
                {{ kdaString }}
              </span>
            </div>

            <!-- 生命值与魔法值条 -->
            <div class="space-y-1">
              <div class="rounded-full bg-slate-800 h-1.5 w-full overflow-hidden">
                <div
                  class="rounded-full bg-emerald-500 h-full transition-all duration-300"
                  :style="{ width: `${heroHpPercent}%` }"
                />
              </div>
              <div class="rounded-full bg-slate-800 h-1.5 w-full overflow-hidden">
                <div
                  class="rounded-full bg-cyan-500 h-full transition-all duration-300"
                  :style="{ width: `${heroManaPercent}%` }"
                />
              </div>
            </div>
          </div>

          <!-- 真实经济与打钱效率 -->
          <div class="gap-2 grid grid-cols-3">
            <div class="p-2 text-center border border-slate-800 rounded-lg bg-slate-900/80">
              <div class="text-[10px] text-slate-400">
                当前储备
              </div>
              <div class="text-sm text-amber-400 tracking-tight font-bold font-mono">
                {{ currentGold }}
              </div>
            </div>
            <div class="p-2 text-center border border-slate-800 rounded-lg bg-slate-900/80">
              <div class="text-[10px] text-slate-400">
                GPM
              </div>
              <div class="text-sm text-slate-200 tracking-tight font-bold font-mono">
                {{ gpm }}
              </div>
            </div>
            <div class="p-2 text-center border border-slate-800 rounded-lg bg-slate-900/80">
              <div class="text-[10px] text-slate-400">
                XPM
              </div>
              <div class="text-sm text-cyan-400 tracking-tight font-bold font-mono">
                {{ xpm }}
              </div>
            </div>
          </div>

          <!-- 真实买活监控 (严守 Valve 实际买活算法) -->
          <div
            class="p-2.5 border rounded-lg bg-slate-900/80 space-y-1" :class="[
              canBuyback ? 'border-emerald-700/60' : 'border-rose-900/50',
            ]"
          >
            <div class="flex items-center justify-between">
              <span class="text-[11px] text-slate-300 font-medium">买活警戒评估</span>
              <span
                class="text-[10px] font-bold font-mono px-1.5 py-0.2 rounded"
                :class="[
                  canBuyback ? 'bg-emerald-950 text-emerald-300 border border-emerald-700' : 'bg-rose-950 text-rose-300 border border-rose-800',
                ]"
              >
                {{ canBuyback ? '买活充裕' : '无买活风险' }}
              </span>
            </div>
            <div class="text-[11px] text-slate-400 font-mono flex items-center justify-between">
              <span>买活成本: {{ buybackCost }}</span>
              <span v-if="buybackCooldown > 0" class="text-rose-400">冷却中 ({{ buybackCooldown }}s)</span>
              <span v-else class="text-emerald-400">冷却就绪</span>
            </div>
          </div>
        </div>

        <!-- 悬浮窗透明度调节滑动条 -->
        <div class="text-[10px] text-slate-400 pt-2 border-t border-slate-800/80 flex gap-2 items-center">
          <span>透明度</span>
          <input
            v-model.number="opacity"
            type="range"
            min="30"
            max="100"
            step="5"
            class="appearance-none accent-amber-500 rounded-lg bg-slate-800 flex-1 h-1 cursor-pointer"
          >
          <span class="text-slate-300 font-mono text-right w-6">{{ opacity }}%</span>
        </div>
      </div>
    </div>
  </div>
</template>
