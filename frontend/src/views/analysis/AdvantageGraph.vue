<script setup lang="ts">
/**
 * 团队经济与经验优势走势曲线图 (src/views/analysis/AdvantageGraph.vue)
 * 职责：
 * 1. 基于 OpenDota radiant_gold_adv 与 radiant_xp_adv 纯 SVG 绘制高保真折线面积图
 * 2. 0 轴基准线隔离双方阵营（上方天辉绿色优势，下方夜魇红色优势）
 * 3. 实时十字准星与鼠标 Hover 极值转折提示
 */

import { Coins, Sparkles, TrendingUp, Zap } from '@lucide/vue'
import { computed, ref } from 'vue'

defineOptions({ name: 'AdvantageGraph' })

const props = defineProps<{
  goldAdv?: number[]
  xpAdv?: number[]
  duration: number
}>()

const activeTab = ref<'gold' | 'xp'>('gold')
const hoverIndex = ref<number | null>(null)

const activeData = computed<number[]>(() => {
  if (activeTab.value === 'gold') {
    return props.goldAdv && props.goldAdv.length > 0 ? props.goldAdv : []
  }
  return props.xpAdv && props.xpAdv.length > 0 ? props.xpAdv : []
})

const hasData = computed(() => activeData.value.length >= 2)

// 计算极值
const stats = computed(() => {
  const data = activeData.value
  if (data.length === 0) return { maxRadiant: 0, maxDire: 0, finalAdv: 0, flips: 0 }

  let maxR = 0
  let maxD = 0
  let flips = 0
  for (let i = 0; i < data.length; i++) {
    const v = data[i]
    if (v > maxR) maxR = v
    if (v < maxD) maxD = v
    if (i > 0 && Math.sign(data[i]) !== Math.sign(data[i - 1]) && data[i] !== 0 && data[i - 1] !== 0) {
      flips++
    }
  }

  return {
    maxRadiant: maxR,
    maxDire: Math.abs(maxD),
    finalAdv: data[data.length - 1],
    flips,
  }
})

// SVG 画布参数
const viewBoxWidth = 800
const viewBoxHeight = 220
const padding = { top: 25, bottom: 30, left: 55, right: 30 }

const chartWidth = viewBoxWidth - padding.left - padding.right
const chartHeight = viewBoxHeight - padding.top - padding.bottom

// Y 轴范围对称计算，保证 0 轴处于正中心
const yMax = computed(() => {
  const data = activeData.value
  if (data.length === 0) return 10000
  let peak = 0
  for (const v of data) {
    const abs = Math.abs(v)
    if (abs > peak) peak = abs
  }
  // 向上对齐取整，至少保留 5000
  return Math.max(5000, Math.ceil(peak / 2000) * 2000)
})

const zeroY = computed(() => padding.top + chartHeight / 2)

// 计算点位坐标
const points = computed(() => {
  const data = activeData.value
  if (data.length === 0) return []
  const count = data.length

  return data.map((val, idx) => {
    const x = padding.left + (idx / Math.max(1, count - 1)) * chartWidth
    // val > 0 在 0 轴上方 (y 变小)，val < 0 在 0 轴下方 (y 变大)
    const y = zeroY.value - (val / yMax.value) * (chartHeight / 2)
    return { x, y, val, minute: idx }
  })
})

// 生成折线 path
const linePath = computed(() => {
  const pts = points.value
  if (pts.length === 0) return ''
  return pts.reduce((acc, p, idx) => {
    return idx === 0 ? `M ${p.x.toFixed(1)} ${p.y.toFixed(1)}` : `${acc} L ${p.x.toFixed(1)} ${p.y.toFixed(1)}`
  }, '')
})

// 生成天辉优势面积 path (截取在 zeroY 以上部分)
const radiantAreaPath = computed(() => {
  const pts = points.value
  if (pts.length === 0) return ''
  let d = `M ${pts[0].x.toFixed(1)} ${zeroY.value.toFixed(1)}`
  for (const p of pts) {
    const clampedY = Math.min(p.y, zeroY.value)
    d += ` L ${p.x.toFixed(1)} ${clampedY.toFixed(1)}`
  }
  d += ` L ${pts[pts.length - 1].x.toFixed(1)} ${zeroY.value.toFixed(1)} Z`
  return d
})

// 生成夜魇优势面积 path (截取在 zeroY 以下部分)
const direAreaPath = computed(() => {
  const pts = points.value
  if (pts.length === 0) return ''
  let d = `M ${pts[0].x.toFixed(1)} ${zeroY.value.toFixed(1)}`
  for (const p of pts) {
    const clampedY = Math.max(p.y, zeroY.value)
    d += ` L ${p.x.toFixed(1)} ${clampedY.toFixed(1)}`
  }
  d += ` L ${pts[pts.length - 1].x.toFixed(1)} ${zeroY.value.toFixed(1)} Z`
  return d
})

// X 轴时间刻度
const xTicks = computed(() => {
  const data = activeData.value
  const totalMin = data.length > 0 ? data.length - 1 : Math.ceil(props.duration / 60)
  if (totalMin <= 0) return []

  const step = totalMin > 45 ? 10 : 5
  const ticks: { x: number, text: string }[] = []
  for (let m = 0; m <= totalMin; m += step) {
    const x = padding.left + (m / Math.max(1, totalMin)) * chartWidth
    ticks.push({ x, text: `${m}m` })
  }
  return ticks
})

function formatVal(v: number): string {
  const abs = Math.abs(v)
  if (abs >= 10000) return `${(abs / 1000).toFixed(1)}k`
  if (abs >= 1000) return `${(abs / 1000).toFixed(1)}k`
  return String(abs)
}

function handleMouseMove(e: MouseEvent) {
  const svg = e.currentTarget as SVGSVGElement
  const rect = svg.getBoundingClientRect()
  const mouseX = e.clientX - rect.left
  const ratio = (mouseX / rect.width) * viewBoxWidth

  const chartX = ratio - padding.left
  if (chartX < 0 || chartX > chartWidth) {
    hoverIndex.value = null
    return
  }

  const count = activeData.value.length
  if (count <= 1) return

  const idx = Math.round((chartX / chartWidth) * (count - 1))
  hoverIndex.value = Math.max(0, Math.min(count - 1, idx))
}

function handleMouseLeave() {
  hoverIndex.value = null
}

const currentHoverPoint = computed(() => {
  if (hoverIndex.value === null) return null
  return points.value[hoverIndex.value] || null
})
</script>

<template>
  <div class="p-4 border border-slate-800 rounded-xl bg-slate-950/80 space-y-4">
    <!-- 顶部控制条与统计速览 -->
    <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <div class="flex items-center space-x-2">
        <TrendingUp class="text-amber-400 h-4 w-4" />
        <h4 class="text-xs text-slate-200 font-bold">
          全场团队优势动态走势曲线
        </h4>
        <span class="text-2xs text-slate-500 font-mono">(每分钟战局切片)</span>
      </div>

      <!-- 经济 / 经验模式切换 Tab -->
      <div class="p-0.5 border border-slate-700/80 rounded-lg bg-slate-900/90 flex items-center space-x-1">
        <button
          class="text-2xs font-bold px-2.5 py-1 rounded flex transition-colors items-center space-x-1"
          :class="activeTab === 'gold' ? 'bg-amber-500 text-slate-950 shadow-sm' : 'text-slate-400 hover:text-slate-200'"
          @click="activeTab = 'gold'"
        >
          <Coins class="h-3 w-3" />
          <span>团队经济走势 (Gold)</span>
        </button>
        <button
          class="text-2xs font-bold px-2.5 py-1 rounded flex transition-colors items-center space-x-1"
          :class="activeTab === 'xp' ? 'bg-cyan-500 text-slate-950 shadow-sm' : 'text-slate-400 hover:text-slate-200'"
          @click="activeTab = 'xp'"
        >
          <Zap class="h-3 w-3" />
          <span>团队经验走势 (XP)</span>
        </button>
      </div>
    </div>

    <!-- 数据真实存在时的 SVG 图表 -->
    <div v-if="hasData" class="space-y-3">
      <div class="border border-slate-800/80 rounded-xl bg-slate-900/60 relative overflow-hidden">
        <!-- 悬浮数据浮层 Tooltip -->
        <div
          v-if="currentHoverPoint"
          class="text-2xs font-mono p-2 border border-slate-700 rounded-lg bg-slate-900/95 pointer-events-none shadow-xl top-2 absolute z-10 space-y-0.5"
          :style="{
            left: `${Math.min(80, Math.max(10, (currentHoverPoint.x / viewBoxWidth) * 100))}%`,
            transform: 'translateX(-50%)',
          }"
        >
          <div class="text-slate-400 font-bold flex items-center space-x-1">
            <span>比赛时钟: {{ currentHoverPoint.minute }}:00</span>
          </div>
          <div class="flex items-center space-x-1.5">
            <span
              class="rounded-full h-2 w-2"
              :class="currentHoverPoint.val >= 0 ? 'bg-emerald-400' : 'bg-rose-400'"
            />
            <span :class="currentHoverPoint.val >= 0 ? 'text-emerald-300' : 'text-rose-300'" class="font-extrabold">
              {{ currentHoverPoint.val >= 0 ? '天辉领先' : '夜魇领先' }}
              {{ formatVal(currentHoverPoint.val) }}
              {{ activeTab === 'gold' ? '经济' : '经验' }}
            </span>
          </div>
        </div>

        <svg
          class="h-48 w-full select-none"
          :viewBox="`0 0 ${viewBoxWidth} ${viewBoxHeight}`"
          preserveAspectRatio="none"
          @mousemove="handleMouseMove"
          @mouseleave="handleMouseLeave"
        >
          <defs>
            <!-- 天辉翡翠绿渐变 -->
            <linearGradient id="radiantGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stop-color="#10b981" stop-opacity="0.35" />
              <stop offset="100%" stop-color="#10b981" stop-opacity="0.02" />
            </linearGradient>
            <!-- 夜魇绯红渐变 -->
            <linearGradient id="direGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stop-color="#f43f5e" stop-opacity="0.02" />
              <stop offset="100%" stop-color="#f43f5e" stop-opacity="0.35" />
            </linearGradient>
          </defs>

          <!-- 0 轴基准背景线 -->
          <line
            :x1="padding.left"
            :y1="zeroY"
            :x2="padding.left + chartWidth"
            :y2="zeroY"
            stroke="#475569"
            stroke-width="1.2"
            stroke-dasharray="4 3"
          />

          <!-- 天辉优势面积与夜魇优势面积 -->
          <path :d="radiantAreaPath" fill="url(#radiantGrad)" />
          <path :d="direAreaPath" fill="url(#direGrad)" />

          <!-- 主走势折线 -->
          <path
            :d="linePath"
            fill="none"
            stroke="#38bdf8"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          />

          <!-- Y 轴刻度文本 -->
          <text
            :x="padding.left - 8"
            :y="padding.top + 5"
            fill="#34d399"
            font-size="10"
            font-family="monospace"
            text-anchor="end"
          >
            +{{ formatVal(yMax) }}
          </text>
          <text
            :x="padding.left - 8"
            :y="zeroY + 3"
            fill="#94a3b8"
            font-size="10"
            font-family="monospace"
            text-anchor="end"
          >
            0
          </text>
          <text
            :x="padding.left - 8"
            :y="viewBoxHeight - padding.bottom"
            fill="#fb7185"
            font-size="10"
            font-family="monospace"
            text-anchor="end"
          >
            -{{ formatVal(yMax) }}
          </text>

          <!-- X 轴刻度线与时间标签 -->
          <g v-for="tick in xTicks" :key="tick.text">
            <line
              :x1="tick.x"
              :y1="zeroY - 3"
              :x2="tick.x"
              :y2="zeroY + 3"
              stroke="#64748b"
              stroke-width="1"
            />
            <text
              :x="tick.x"
              :y="viewBoxHeight - 12"
              fill="#94a3b8"
              font-size="9.5"
              font-family="monospace"
              text-anchor="middle"
            >
              {{ tick.text }}
            </text>
          </g>

          <!-- 鼠标 Hover 十字准星与圆点 -->
          <g v-if="currentHoverPoint">
            <line
              :x1="currentHoverPoint.x"
              :y1="padding.top"
              :x2="currentHoverPoint.x"
              :y2="viewBoxHeight - padding.bottom"
              stroke="#f59e0b"
              stroke-width="1.2"
              stroke-dasharray="2 2"
            />
            <circle
              :cx="currentHoverPoint.x"
              :cy="currentHoverPoint.y"
              r="4.5"
              fill="#fbbf24"
              stroke="#0f172a"
              stroke-width="2"
            />
          </g>
        </svg>
      </div>

      <!-- 走势统计 4 宫格卡片 -->
      <div class="text-xs font-mono gap-2.5 grid grid-cols-2 lg:grid-cols-4">
        <div class="p-2 border border-emerald-950/60 rounded-lg bg-emerald-950/20 flex flex-col justify-between">
          <span class="text-2xs text-emerald-400">天辉最大峰值领先</span>
          <span class="text-emerald-300 font-extrabold">+{{ formatVal(stats.maxRadiant) }}</span>
        </div>
        <div class="p-2 border border-rose-950/60 rounded-lg bg-rose-950/20 flex flex-col justify-between">
          <span class="text-2xs text-rose-400">夜魇最大峰值领先</span>
          <span class="text-rose-300 font-extrabold">-{{ formatVal(stats.maxDire) }}</span>
        </div>
        <div class="p-2 border border-slate-800 rounded-lg bg-slate-900/60 flex flex-col justify-between">
          <span class="text-2xs text-slate-400">终局分差</span>
          <span
            class="font-extrabold"
            :class="stats.finalAdv >= 0 ? 'text-emerald-400' : 'text-rose-400'"
          >
            {{ stats.finalAdv >= 0 ? '+' : '' }}{{ formatVal(stats.finalAdv) }}
          </span>
        </div>
        <div class="p-2 border border-slate-800 rounded-lg bg-slate-900/60 flex flex-col justify-between">
          <span class="text-2xs text-slate-400">局势翻转/反超次数</span>
          <span class="text-amber-400 font-extrabold">{{ stats.flips }} 次拉扯</span>
        </div>
      </div>
    </div>

    <!-- 无曲线数据时的真实空态守卫 (零伪造规范) -->
    <div
      v-else
      class="text-xs text-slate-500 font-mono py-8 text-center border border-slate-800/60 rounded-xl bg-slate-900/30 flex flex-col items-center justify-center space-y-1.5"
    >
      <Sparkles class="text-slate-600 h-6 w-6" />
      <p>该场比赛未在 OpenDota 记录分钟级详细优势走势数据</p>
      <p class="text-2xs text-slate-600">非天梯普通局或极速自订对局可能缺少分钟级时间戳切片</p>
    </div>
  </div>
</template>
