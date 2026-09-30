<script setup lang="ts">
/**
 * 战术能力五维纯 SVG 雷达图组件 (src/views/analysis/RadarChart.vue)
 * 核心优化：
 * 1. 背景 5 层多边形网格点与 5 条轴线由模块级常量单次预计算缓存，彻底杜绝渲染帧重复三角函数运算
 * 2. 接收 dimensions 响应式输入，纯展示组件，职责单一，零外部副作用
 */

import { computed } from 'vue'

defineOptions({ name: 'RadarChart' })

const props = defineProps<{
  dimensions: RadarDimension[]
}>()

export interface RadarDimension {
  name: string
  score: number
}

const RADAR_CENTER = { x: 120, y: 120 }
const RADAR_RADIUS = 78
const RADAR_ANGLES = [
  -Math.PI / 2,
  -Math.PI / 2 + (2 * Math.PI) / 5,
  -Math.PI / 2 + (4 * Math.PI) / 5,
  -Math.PI / 2 + (6 * Math.PI) / 5,
  -Math.PI / 2 + (8 * Math.PI) / 5,
]

// 静态背景网格 5 层多边形点 (单次预计算常量)
const RADAR_GRID_LEVELS = [0.2, 0.4, 0.6, 0.8, 1.0].map((scale) => {
  const r = RADAR_RADIUS * scale
  return RADAR_ANGLES.map((angle) => {
    const x = RADAR_CENTER.x + r * Math.cos(angle)
    const y = RADAR_CENTER.y + r * Math.sin(angle)
    return `${x.toFixed(1)},${y.toFixed(1)}`
  }).join(' ')
})

// 轴线终点预计算
const RADAR_AXIS_LINES = RADAR_ANGLES.map(angle => ({
  x2: RADAR_CENTER.x + RADAR_RADIUS * Math.cos(angle),
  y2: RADAR_CENTER.y + RADAR_RADIUS * Math.sin(angle),
}))

// 数据多边形
const radarDataPoints = computed(() => {
  const dims = props.dimensions
  return RADAR_ANGLES.map((angle, i) => {
    const score = dims[i]?.score ?? 50
    const r = RADAR_RADIUS * (score / 100)
    const x = RADAR_CENTER.x + r * Math.cos(angle)
    const y = RADAR_CENTER.y + r * Math.sin(angle)
    return `${x.toFixed(1)},${y.toFixed(1)}`
  }).join(' ')
})

// 数据顶点高亮圆点
const radarVertices = computed(() => {
  const dims = props.dimensions
  return RADAR_ANGLES.map((angle, i) => {
    const score = dims[i]?.score ?? 50
    const r = RADAR_RADIUS * (score / 100)
    return {
      x: RADAR_CENTER.x + r * Math.cos(angle),
      y: RADAR_CENTER.y + r * Math.sin(angle),
      name: dims[i]?.name || '',
      score,
    }
  })
})

// 标签位置
const radarLabelPositions = computed(() => {
  const labelRadius = RADAR_RADIUS + 22
  const dims = props.dimensions
  return RADAR_ANGLES.map((angle, i) => ({
    x: RADAR_CENTER.x + labelRadius * Math.cos(angle),
    y: RADAR_CENTER.y + labelRadius * Math.sin(angle) + 4,
    name: dims[i]?.name || '',
    score: dims[i]?.score || 50,
  }))
})
</script>

<template>
  <div class="flex h-64 w-64 select-none items-center justify-center relative">
    <svg viewBox="0 0 240 240" class="h-full w-full overflow-visible">
      <!-- 1. 背景五边形网格线 (5 层静态常量预计算) -->
      <polygon
        v-for="(points, idx) in RADAR_GRID_LEVELS"
        :key="idx"
        :points="points"
        fill="none"
        stroke="#334155"
        :stroke-width="idx === 4 ? '1.5' : '1'"
        stroke-dasharray="2,2"
        class="transition-all"
      />

      <!-- 2. 发散轴线 -->
      <line
        v-for="(axis, i) in RADAR_AXIS_LINES"
        :key="`axis-${i}`"
        :x1="RADAR_CENTER.x"
        :y1="RADAR_CENTER.y"
        :x2="axis.x2"
        :y2="axis.y2"
        stroke="#475569"
        stroke-width="1"
      />

      <!-- 3. 数据覆盖多边形 -->
      <polygon
        :points="radarDataPoints"
        fill="rgba(245, 158, 11, 0.25)"
        stroke="#f59e0b"
        stroke-width="2"
        class="transition-all duration-500 ease-out"
      />

      <!-- 4. 顶点高亮圆点 -->
      <circle
        v-for="(v, i) in radarVertices"
        :key="`vertex-${i}`"
        :cx="v.x"
        :cy="v.y"
        r="3.5"
        fill="#fbbf24"
        stroke="#0f172a"
        stroke-width="1.5"
      />

      <!-- 5. 外部维度文本标签 -->
      <text
        v-for="(pos, i) in radarLabelPositions"
        :key="`label-${i}`"
        :x="pos.x"
        :y="pos.y"
        fill="#94a3b8"
        font-size="10.5"
        font-family="system-ui, sans-serif"
        text-anchor="middle"
        dominant-baseline="central"
        class="tracking-tight font-medium pointer-events-none"
      >
        <tspan fill="#cbd5e1" font-weight="600">{{ pos.name }}</tspan>
        <tspan fill="#f59e0b" dx="3" font-size="9.5" font-family="monospace">{{ pos.score }}</tspan>
      </text>
    </svg>
  </div>
</template>
