<script setup lang="ts">
/**
 * 英雄宏观技巧与推荐热门出装面板组件 (src/views/heroes/components/HeroBuildPanel.vue)
 * 职责：
 * 1. 呈现英雄分阶段（出门、前期、中期、后期）推荐出装与宏观打法技巧
 * 2. 集成 Dota 2 官方标准高对比度物品悬浮卡 (Item Tooltip Hover)，展示完整属性、主动/被动效果与冷却魔耗
 * 3. 使用 Teleport 挂载悬浮卡，自适应视口四边防遮挡防溢出
 */

import {
  AlertCircle,
  Database,
  RefreshCw,
  ShoppingBag,
  Sparkles,
  TrendingUp,
} from '@lucide/vue'
import { nextTick, onMounted, ref } from 'vue'
import ItemTooltipCard from '@/components/common/ItemTooltipCard.vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { gameDataService } from '@/services/gameDataService.ts'
import { getItemCdnUrl, getItemIconUrl } from '@/utils/assetUrl.ts'
import { cleanHtml } from '@/utils/htmlCleaner.ts'

defineOptions({ name: 'HeroBuildPanel' })

withDefaults(defineProps<{
  tips: HeroTip[]
  builds: HeroBuildStage[]
  currentPatch: string
  loading?: boolean
  error?: string | null
  source?: string | null
}>(), {
  loading: false,
  error: null,
  source: null,
})

const emit = defineEmits<{
  (e: 'retry'): void
}>()

export interface HeroBuildItem {
  name?: string
  dname: string
  cost: number
  img: string
}

export interface HeroBuildStage {
  stage: string
  items: HeroBuildItem[]
}

export interface HeroTip {
  title: string
  content: string
}

/** 完整物品元数据实体 */
interface DetailedItem {
  id?: string | number
  key: string
  dname: string
  cost: number
  tier: number
  category: string
  attrib: string
  desc: string
  cd?: any
  mc?: any
  notes?: string
  img: string
  fallbackImg?: string
}

const itemCatalogMap = ref<Map<string, any>>(new Map())
const hoveredItem = ref<DetailedItem | null>(null)
const tooltipRef = ref<HTMLElement | null>(null)
const tooltipPosition = ref<{ x: number, y: number }>({ x: 0, y: 0 })
const currentMousePos = { x: 0, y: 0 }

// 组件挂载时异步拉取全量官方物品元数据构建快查表
onMounted(async () => {
  try {
    const { officialData, constantsJson } = await gameDataService.getItemsData()
    const map = new Map<string, any>()

    // 索引 officialData (主数据源)
    if (officialData && typeof officialData === 'object') {
      for (const [key, raw] of Object.entries(officialData) as [string, any][]) {
        const cleanKey = key.replace(/^item_/, '').toLowerCase()
        const dname = raw.dname || key
        const entry = { ...raw, key, cleanKey }
        map.set(key.toLowerCase(), entry)
        map.set(cleanKey, entry)
        map.set(dname.trim().toLowerCase(), entry)
        // 兼容带括号简称如 "科勒的匕首(跳刀)"
        const pureDname = dname.replace(/[（(].*?[）)]/g, '').trim().toLowerCase()
        if (pureDname && !map.has(pureDname)) {
          map.set(pureDname, entry)
        }
      }
    }

    // 补充 constantsJson (OpenDota 补充字典)
    if (constantsJson && typeof constantsJson === 'object') {
      for (const [key, raw] of Object.entries(constantsJson) as [string, any][]) {
        const cleanKey = key.replace(/^item_/, '').toLowerCase()
        const dname = raw.dname || key
        const entry = { ...raw, key, cleanKey }
        if (!map.has(key.toLowerCase())) map.set(key.toLowerCase(), entry)
        if (!map.has(cleanKey)) map.set(cleanKey, entry)
        if (!map.has(dname.trim().toLowerCase())) map.set(dname.trim().toLowerCase(), entry)
      }
    }

    itemCatalogMap.value = map
  } catch (err) {
    console.warn('[HeroBuildPanel] 物品数据预热失败:', err)
  }
})

/**
 * 从 HeroBuildItem 解析得到完整的 DetailedItem 实体
 */
function resolveDetailedItem(item: HeroBuildItem): DetailedItem {
  const map = itemCatalogMap.value
  let found: any = null

  // 1. 尝试从 item.name 匹配
  if (item.name) {
    const k = item.name.toLowerCase()
    found = map.get(k) || map.get(k.replace(/^item_/, ''))
  }

  // 2. 尝试从 item.img 提取英文 key 匹配 (如 "/cache/items/blink.png" -> "blink")
  if (!found && item.img) {
    const filenameMatch = item.img.match(/\/([^/?#]+)\.(?:png|jpg|webp)$/i)
    if (filenameMatch) {
      const imgKey = filenameMatch[1].toLowerCase().replace(/^item_/, '')
      found = map.get(imgKey) || map.get(`item_${imgKey}`)
    }
  }

  // 3. 尝试从 item.dname 精确与模糊匹配
  if (!found && item.dname) {
    const rawName = item.dname.trim().toLowerCase()
    found = map.get(rawName)
    if (!found) {
      // 去掉括号匹配，如 "科勒的匕首(跳刀)" -> "科勒的匕首"
      const pure = rawName.replace(/[（(].*?[）)]/g, '').trim()
      found = map.get(pure)
    }
    if (!found) {
      // 提取括号内别名，如 "科勒的匕首(跳刀)" -> "跳刀"
      const aliasMatch = item.dname.match(/[（(](.*?)[）)]/)
      if (aliasMatch) {
        found = map.get(aliasMatch[1].trim().toLowerCase())
      }
    }
  }

  const key = found?.key || item.name || 'item_unknown'
  const cleanKey = key.replace(/^item_/, '')

  return {
    id: found?.id,
    key,
    dname: found?.dname || item.dname,
    cost: Number(found?.cost ?? item.cost) || 0,
    tier: Number(found?.tier) || 0,
    category: found?.category || '常用装备',
    attrib: found?.attrib || '',
    desc: found?.desc || '',
    cd: found?.cd,
    mc: found?.mc,
    notes: found?.notes || '',
    img: getItemIconUrl(cleanKey),
    fallbackImg: getItemCdnUrl(cleanKey),
  }
}

/** 计算自适应 Hover 卡片位置 (确保不超出窗口上下左右边界) */
function calculateTooltipPos(mouseX: number, mouseY: number) {
  const cardWidth = 340 // 悬浮窗宽度
  const cardHeight = tooltipRef.value ? tooltipRef.value.offsetHeight : 340
  const padding = 14

  let targetX = mouseX + 16
  let targetY = mouseY + 16

  // 1. 水平边界判定：如果右侧超出，翻转至鼠标左侧
  if (targetX + cardWidth > window.innerWidth - padding) {
    targetX = mouseX - cardWidth - 14
  }
  if (targetX < padding) {
    targetX = padding
  }

  // 2. 垂直边界判定：如果底部超出，翻转至鼠标上方
  if (targetY + cardHeight > window.innerHeight - padding) {
    targetY = mouseY - cardHeight - 14
  }
  if (targetY < padding) {
    targetY = padding
  }

  tooltipPosition.value = { x: targetX, y: targetY }
}

function handleMouseEnter(item: HeroBuildItem, e: MouseEvent) {
  currentMousePos.x = e.clientX
  currentMousePos.y = e.clientY
  hoveredItem.value = resolveDetailedItem(item)
  nextTick(() => {
    calculateTooltipPos(e.clientX, e.clientY)
  })
}

function handleMouseMove(e: MouseEvent) {
  currentMousePos.x = e.clientX
  currentMousePos.y = e.clientY
  if (hoveredItem.value) {
    calculateTooltipPos(e.clientX, e.clientY)
  }
}

function handleMouseLeave() {
  hoveredItem.value = null
}
</script>

<template>
  <div class="flex flex-col gap-3.5 select-none">
    <!-- 推荐出装路线 (100% 真实大数据，绝无任何离线假出装兜底) -->
    <div class="p-3.5 border border-dota-border rounded-xl bg-dota-panel shadow-md space-y-2.5">
      <div class="pb-2 border-b border-dota-border/60 flex items-center justify-between">
        <div class="flex items-center space-x-2">
          <ShoppingBag class="text-dota-gold h-4 w-4" />
          <h3 class="text-xs text-dota-goldLight font-bold">
            推荐与热门出装路线
          </h3>
          <span
            v-if="source === 'opendota'"
            class="text-3xs text-emerald-400 font-bold px-1.5 py-0.5 border border-emerald-500/40 rounded bg-emerald-500/10 flex items-center space-x-1"
          >
            <TrendingUp class="h-2.5 w-2.5" />
            <span>OpenDota 大数据</span>
          </span>
          <span
            v-else-if="source === 'cache'"
            class="text-3xs text-cyan-400 font-bold px-1.5 py-0.5 border border-cyan-500/40 rounded bg-cyan-500/10 flex items-center space-x-1"
          >
            <Database class="h-2.5 w-2.5" />
            <span>真实历史缓存</span>
          </span>
        </div>

        <button
          :disabled="loading"
          class="text-xs text-slate-400 p-1 rounded cursor-pointer transition-colors hover:text-amber-400 hover:bg-slate-800 disabled:opacity-40"
          title="刷新大数据出装"
          @click="emit('retry')"
        >
          <RefreshCw class="h-3.5 w-3.5" :class="{ 'animate-spin': loading }" />
        </button>
      </div>

      <!-- 加载中态 -->
      <div v-if="loading" class="py-6 flex flex-col items-center justify-center space-y-2">
        <RefreshCw class="text-amber-400 h-5 w-5 animate-spin" />
        <span class="text-2xs text-slate-400 font-mono">正在拉取 OpenDota 实时大数据出装...</span>
      </div>

      <!-- 真实大数据展示态 -->
      <div v-else-if="builds && builds.length > 0" class="space-y-2.5">
        <div
          v-for="b in builds"
          :key="b.stage"
          class="p-2 border border-dota-border/50 rounded-lg bg-dota-surface/60 space-y-1.5"
        >
          <div class="text-2xs text-dota-subtle font-bold flex items-center justify-between">
            <span class="text-dota-gold font-medium">{{ b.stage }}</span>
          </div>
          <div class="flex flex-wrap gap-1.5 items-center">
            <div
              v-for="item in b.items"
              :key="item.name || item.dname"
              class="group p-1 border border-dota-border/80 rounded-lg bg-black/60 flex flex-col cursor-pointer shadow-sm transition-all duration-150 items-center relative hover:border-dota-gold/90 hover:shadow-[0_0_8px_rgba(229,193,88,0.25)] hover:scale-105"
              @mouseenter="handleMouseEnter(item, $event)"
              @mousemove="handleMouseMove"
              @mouseleave="handleMouseLeave"
            >
              <div class="border border-white/5 rounded-md bg-slate-950 flex shrink-0 h-9 w-12 items-center justify-center relative overflow-hidden">
                <SmartImage
                  :src="item.img"
                  :alt="item.dname"
                  class="h-full w-full transition-transform object-cover group-hover:scale-105"
                />
              </div>
              <div class="leading-none pt-1 text-center w-full">
                <div class="text-3xs text-amber-300 tracking-tight font-bold font-mono">
                  {{ item.cost > 0 ? `${item.cost}金` : '' }}
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 真实空态 (未获取到数据，零伪造原则) -->
      <div v-else class="px-3 py-5 text-center border border-slate-800 rounded-lg border-dashed bg-slate-950/40 flex flex-col items-center justify-center space-y-2">
        <AlertCircle class="text-slate-500 h-5 w-5" />
        <div class="space-y-0.5">
          <p class="text-xs text-slate-400 font-bold">
            {{ error || '未检索到该英雄的大数据出装推荐' }}
          </p>
          <p class="text-2xs text-slate-500 font-mono">
            网络未连接或数据源暂未响应，请检查网络设置后重试。
          </p>
        </div>
        <button
          class="text-2xs text-amber-400 px-2.5 py-1 border border-amber-500/40 rounded bg-amber-500/10 cursor-pointer transition-colors hover:bg-amber-500/20"
          @click="emit('retry')"
        >
          重新拉取
        </button>
      </div>
    </div>

    <!-- 宏观打法与技巧 -->
    <div
      v-if="tips.length > 0"
      class="p-3.5 border border-dota-border rounded-xl bg-dota-panel shadow-md space-y-2.5"
    >
      <div class="pb-2 border-b border-dota-border/60 flex items-center space-x-2">
        <Sparkles class="text-dota-gold h-4 w-4" />
        <h3 class="text-xs text-dota-goldLight font-bold">英雄宏观打法与实战战略</h3>
      </div>
      <div class="space-y-2">
        <div
          v-for="(tip, idx) in tips"
          :key="idx"
          class="p-2.5 border border-dota-border/50 rounded-lg bg-dota-surface/60 space-y-1"
        >
          <div class="text-xs text-dota-gold font-bold flex items-center space-x-1.5">
            <span class="rounded-full bg-dota-gold h-1.5 w-1.5" />
            <span>{{ tip.title }}</span>
          </div>
          <p class="text-xs text-dota-text leading-relaxed pl-2.5">
            {{ cleanHtml(tip.content) }}
          </p>
        </div>
      </div>
    </div>

    <!-- 悬浮高保真深色高对比度物品详情卡片 (完全复用 ItemTooltipCard 统一组件并通过 Teleport 挂载到 body) -->
    <Teleport to="body">
      <ItemTooltipCard
        :item="hoveredItem"
        :position="tooltipPosition"
      />
    </Teleport>
  </div>
</template>
