<script setup lang="ts">
/**
 * 局内独立置顶经济雷达悬浮窗视图 (src/views/OverlayView.vue)
 * 遵循开工铁律第 8 条（零伪造原则）：
 * 1. 严禁在未建立真实对局连接时伪造任何虚拟英雄、经济差或战局时间；
 * 2. 当 GSI 未连接或英雄未就绪时，严格呈现“等待 GSI 4444 端口接入”真实空态守卫；
 * 3. 游戏建立连接后，全量呈现由后端派发的真实快照数据。
 */

import {
  Lock,
  Maximize2,
  Minimize2,
  Radio,
  Shield,
  Unlock,
  WifiOff,
  X,
} from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { overlayApi } from '../api'
import { HERO_CHINESE_NAMES } from '../data/heroNames'
import { useAppStore } from '../stores/app'
import { useBattleStore } from '../stores/battle'

const appStore = useAppStore()
const battleStore = useBattleStore()

/** 悬浮窗折叠状态 */
const isCollapsed = ref(false)

/** 悬浮窗位置锁定状态 (防止对局中鼠标误触拖拽，所有按钮依然可交互) */
const isPositionLocked = ref(false)

/** 悬浮窗透明度：由设置页面统一管理与持久化，支持跨窗口实时同步 */
const opacity = computed(() => appStore.settings.overlay_opacity ?? 90)

/** 鼠标穿透状态 (HUD 纯观战穿透模式，由 Alt+F3 触发) */
const isMouseThrough = ref(false)

/**
 * 触发 Wails 原生窗口拖拽
 */
function triggerNativeDrag(e: MouseEvent) {
  if (isPositionLocked.value) return
  if ((e.target as HTMLElement).closest('button, input, select')) return
  try {
    (window as any).chrome?.webview?.postMessage?.('wails:drag')
  } catch {
    // 忽略
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
  appStore.isOverlayRunning = false
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
  // 保持初始穿透为 false，确保用户打开后可立即拖动和点击交互
  try {
    await overlayApi.setIgnoreMouseEvents(false)
  } catch {
    // 忽略开发预览模式
  }

  // 跨窗口同步：监听主窗口设置页面更改透明度
  window.addEventListener('storage', (e: StorageEvent) => {
    if (e.key === 'aegis_coach_settings_v2' && e.newValue) {
      try {
        const parsed = JSON.parse(e.newValue)
        if (typeof parsed.overlay_opacity === 'number') {
          appStore.settings.overlay_opacity = parsed.overlay_opacity
        }
      } catch {
        // 忽略解析错误
      }
    }
  })

  // 监听快捷键或菜单触发的鼠标穿透事件
  const win = window as any
  if (win.wails?.Events?.On) {
    win.wails.Events.On('overlay_mouse_through_change', (payload: any) => {
      let active = false
      if (typeof payload === 'boolean') {
        active = payload
      } else if (payload && typeof payload === 'object') {
        const raw = payload.data ?? payload
        if (typeof raw === 'boolean') active = raw
        else if (Array.isArray(raw) && raw.length > 0 && typeof raw[0] === 'boolean') active = raw[0]
      }
      isMouseThrough.value = active
    })
  }
})
</script>

<template>
  <div
    class="p-1 bg-transparent flex flex-col h-full w-full pointer-events-none select-none items-center justify-start overflow-hidden"
  >
    <!-- 悬浮窗卡片主体 (零伪造真实状态呈现) -->
    <div
      class="border border-slate-700/80 rounded-xl bg-slate-950/95 flex flex-col w-full pointer-events-auto shadow-2xl transition-all duration-200 overflow-hidden backdrop-blur-md"
      :style="{
        opacity: opacity / 100,
        maxWidth: isCollapsed ? '240px' : '332px',
      }"
    >
      <!-- 全局鼠标穿透状态提示横幅 (Alt+F3 触发) -->
      <div
        v-if="isMouseThrough"
        class="px-2.5 py-1 bg-emerald-950/90 border-b border-emerald-700/60 text-emerald-300 text-xs flex items-center justify-between font-mono animate-pulse"
      >
        <span>已开启鼠标穿透 (点击直通游戏)</span>
        <span class="font-bold text-emerald-400">按 Alt+F3 解除</span>
      </div>

      <!-- 顶部把手与标题栏 (支持 Wails 原生窗口拖拽) -->
      <div
        class="px-3 py-2 border-b border-slate-800 bg-slate-900 flex select-none items-center justify-between"
        :class="isPositionLocked ? 'cursor-default' : 'cursor-move'"
        :style="isPositionLocked ? '--wails-draggable: none' : '--wails-draggable: drag'"
        @mousedown="triggerNativeDrag"
      >
        <div class="flex gap-2 pointer-events-none items-center">
          <!-- 状态呼吸指示灯：绿灯连接，黄灯等待 -->
          <div
            class="rounded-full h-2.5 w-2.5"
            :class="[
              isGsiActive ? 'bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.8)] animate-pulse' : 'bg-amber-500 animate-ping',
            ]"
          />
          <span class="text-xs text-slate-100 tracking-wider font-bold flex items-center space-x-1.5">
            <Shield class="text-amber-400 h-3.5 w-3.5" />
            <span>雷达</span>
          </span>
          <span
            v-if="isGsiActive"
            class="text-emerald-400 font-mono px-1 py-0.2 border border-emerald-800/60 rounded bg-emerald-950 text-2xs"
          >
            {{ formattedGameTime }}
          </span>
        </div>

        <div class="flex gap-1 items-center" style="--wails-draggable: none">
          <!-- 锁定/解锁悬浮窗位置 (防止对局误拖，所有按钮依然随时可用) -->
          <button
            class="p-1 rounded cursor-pointer transition-colors"
            :class="[
              isPositionLocked
                ? 'text-amber-400 bg-amber-950/80 border border-amber-600/50'
                : 'text-slate-400 hover:text-white hover:bg-slate-800',
            ]"
            :title="isPositionLocked ? '位置已锁定 (点击解锁以允许拖拽)' : '锁定位置 (防止误触拖拽)'"
            @click.stop="isPositionLocked = !isPositionLocked"
          >
            <Lock v-if="isPositionLocked" class="h-3.5 w-3.5" />
            <Unlock v-else class="h-3.5 w-3.5" />
          </button>

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
              <span>等待 GSI 接入</span>
            </div>
            <p class="text-slate-400 leading-relaxed text-xs">
              请启动 Dota 2 客户端并进入比赛，系统将自动开始追踪真实英雄经济与买活状态。
            </p>
          </div>
        </div>

        <!-- GSI 已连接：渲染真实快照数据 -->
        <div v-else class="space-y-2.5">
          <!-- 操纵英雄与血蓝状态 -->
          <div class="p-2.5 border border-slate-800 rounded-lg bg-slate-900/80 space-y-1.5">
            <div class="flex items-center justify-between">
              <div class="text-slate-100 font-bold flex items-center space-x-1.5">
                <span class="text-amber-400">{{ heroName }}</span>
                <span class="text-slate-400 font-mono text-xs">Lv.{{ heroLevel }}</span>
              </div>
              <span class="text-emerald-300 font-mono font-semibold text-xs">
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
              <div class="text-slate-400 text-xs">
                当前储备
              </div>
              <div class="text-sm text-amber-400 tracking-tight font-bold font-mono">
                {{ currentGold }}
              </div>
            </div>
            <div class="p-2 text-center border border-slate-800 rounded-lg bg-slate-900/80">
              <div class="text-slate-400 text-xs">
                GPM
              </div>
              <div class="text-sm text-slate-200 tracking-tight font-bold font-mono">
                {{ gpm }}
              </div>
            </div>
            <div class="p-2 text-center border border-slate-800 rounded-lg bg-slate-900/80">
              <div class="text-slate-400 text-xs">
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
              <span class="text-slate-300 font-medium text-xs">买活警戒评估</span>
              <span
                class="font-bold font-mono px-1.5 py-0.2 rounded text-xs"
                :class="[
                  canBuyback ? 'bg-emerald-950 text-emerald-300 border border-emerald-700' : 'bg-rose-950 text-rose-300 border border-rose-800',
                ]"
              >
                {{ canBuyback ? '买活充裕' : '无买活风险' }}
              </span>
            </div>
            <div class="text-slate-400 font-mono flex items-center justify-between text-xs">
              <span>买活成本: {{ buybackCost }}</span>
              <span v-if="buybackCooldown > 0" class="text-rose-400">冷却中 ({{ buybackCooldown }}s)</span>
              <span v-else class="text-emerald-400">冷却就绪</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
