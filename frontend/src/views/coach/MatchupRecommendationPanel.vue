<script setup lang="ts">
/**
 * 实时对局大数据针对性出装推荐面板 (src/views/coach/MatchupRecommendationPanel.vue)
 * 职责：
 * 1. 严格遵守开工铁律第 11 条：56x42 官网物品格、127 英雄中文名静态兜底、深色高对比悬浮卡
 * 2. 结合 Valve GSI 监测到的实时英雄与敌方阵容，消费 OpenDota 大数据针对性出装建议
 * 3. 动态识别本局敌方高危威胁克制源，呈现分阶段（出门/前期/中期/后期）与针对性质变装矩阵
 * 4. 自动匹配当前背包已购装备，高亮标记已拥有状态
 */

import type { HeroMatchupRecommendation } from '@/api/types'
import {
  AlertCircle,
  Check,
  Database,
  RefreshCw,
  ShieldAlert,
  Sparkles,
  Swords,
  TrendingUp,
} from '@lucide/vue'
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { playerApi } from '@/api'
import ItemTooltipCard from '@/components/common/ItemTooltipCard.vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { getHeroChineseName } from '@/data/heroNames.ts'
import { gameDataService } from '@/services/gameDataService.ts'
import {
  cleanItemKey,
  getHeroCdnUrl,
  getHeroIconUrl,
  getItemCdnUrl,
  getItemIconUrl,
} from '@/utils/assetUrl.ts'

defineOptions({ name: 'MatchupRecommendationPanel' })

const props = defineProps<{
  heroId: number
  heroName: string
  enemyHeroIds?: number[]
  currentItems?: string[]
}>()

const loading = ref(false)
const errorMessage = ref<string | null>(null)
const recommendation = ref<HeroMatchupRecommendation | null>(null)

// 悬浮物品卡片状态
const itemCatalogMap = ref<Map<string, any>>(new Map())
const hoveredItem = ref<any | null>(null)
const tooltipRef = ref<HTMLElement | null>(null)
const tooltipPosition = ref<{ x: number, y: number }>({ x: 0, y: 0 })
const currentMousePos = { x: 0, y: 0 }

// 规范化当前背包物品列表，便于快速判断是否“已拥有”
const ownedItemSet = computed(() => {
  const set = new Set<string>()
  if (!props.currentItems) return set
  for (const name of props.currentItems) {
    if (name) {
      const clean = cleanItemKey(name).toLowerCase()
      set.add(clean)
      set.add(`item_${clean}`)
    }
  }
  return set
})

function isItemOwned(itemKey: string): boolean {
  const clean = cleanItemKey(itemKey).toLowerCase()
  return ownedItemSet.value.has(clean) || ownedItemSet.value.has(`item_${clean}`)
}

async function loadRecommendations() {
  if (!props.heroId || props.heroId <= 0) {
    recommendation.value = null
    errorMessage.value = null
    return
  }

  loading.value = true
  errorMessage.value = null
  try {
    const res = await playerApi.getHeroMatchupRecommendations(props.heroId, props.enemyHeroIds || [])
    if (res && (res.startItems?.length || res.earlyItems?.length || res.midItems?.length || res.lateItems?.length)) {
      recommendation.value = res
    } else {
      recommendation.value = null
      errorMessage.value = '暂未检索到该英雄/阵容的大数据出装统计'
    }
  } catch (err: any) {
    console.warn('[MatchupRecommendationPanel] 拉取大数据出装失败 (零伪造):', err)
    recommendation.value = null
    errorMessage.value = '网络未连接或未能获取到 OpenDota 在线大数据出装'
  } finally {
    loading.value = false
  }
}

watch(
  () => [props.heroId, props.enemyHeroIds],
  () => {
    loadRecommendations()
  },
  { deep: true },
)

onMounted(async () => {
  // 预热全量物品元数据，供给 Tooltip 悬浮卡
  try {
    const { officialData, constantsJson } = await gameDataService.getItemsData()
    const map = new Map<string, any>()
    if (officialData && typeof officialData === 'object') {
      for (const [key, raw] of Object.entries(officialData) as [string, any][]) {
        const cleanKey = cleanItemKey(key).toLowerCase()
        const entry = { ...raw, key, cleanKey }
        map.set(key.toLowerCase(), entry)
        map.set(cleanKey, entry)
        if (raw.dname) {
          map.set(raw.dname.trim().toLowerCase(), entry)
        }
      }
    }
    if (constantsJson && typeof constantsJson === 'object') {
      for (const [key, raw] of Object.entries(constantsJson) as [string, any][]) {
        const cleanKey = cleanItemKey(key).toLowerCase()
        const entry = { ...raw, key, cleanKey }
        if (!map.has(key.toLowerCase())) map.set(key.toLowerCase(), entry)
        if (!map.has(cleanKey)) map.set(cleanKey, entry)
      }
    }
    itemCatalogMap.value = map
  } catch (err) {
    console.warn('[MatchupRecommendationPanel] 物品数据字典预热失败:', err)
  }

  loadRecommendations()
})

function handleMouseEnter(itemKey: string, event: MouseEvent) {
  currentMousePos.x = event.clientX
  currentMousePos.y = event.clientY

  const clean = cleanItemKey(itemKey).toLowerCase()
  const map = itemCatalogMap.value
  const found = map.get(clean) || map.get(`item_${clean}`) || {
    key: `item_${clean}`,
    dname: clean,
    cost: 0,
    attrib: '',
    desc: '',
  }

  hoveredItem.value = found
  nextTick(() => {
    adjustTooltipPosition(currentMousePos.x, currentMousePos.y)
  })
}

function handleMouseMove(event: MouseEvent) {
  currentMousePos.x = event.clientX
  currentMousePos.y = event.clientY
  adjustTooltipPosition(event.clientX, event.clientY)
}

function handleMouseLeave() {
  hoveredItem.value = null
}

function adjustTooltipPosition(mouseX: number, mouseY: number) {
  const el = tooltipRef.value
  const tooltipWidth = el ? el.offsetWidth : 360
  const tooltipHeight = el ? el.offsetHeight : 240
  const padding = 16

  let x = mouseX + padding
  let y = mouseY - tooltipHeight / 2

  if (x + tooltipWidth > window.innerWidth - padding) {
    x = mouseX - tooltipWidth - padding
  }
  if (x < padding) x = padding

  if (y + tooltipHeight > window.innerHeight - padding) {
    y = window.innerHeight - tooltipHeight - padding
  }
  if (y < padding) y = padding

  tooltipPosition.value = { x, y }
}
</script>

<template>
  <div class="p-4 border border-slate-700/80 rounded-2xl bg-slate-900/90 select-none shadow-xl relative overflow-hidden space-y-3.5">
    <!-- 头部栏：标题、数据源标识与刷新按钮 -->
    <div class="pb-2.5 border-b border-slate-800/80 flex items-center justify-between">
      <div class="flex items-center space-x-2">
        <Sparkles class="text-amber-400 h-4 w-4" />
        <h2 class="text-xs text-amber-400 tracking-wide font-extrabold flex gap-1.5 items-center">
          大数据动态出装 · 实战针对推荐
        </h2>
        <span
          v-if="recommendation?.source === 'opendota'"
          class="text-2xs text-emerald-400 font-bold px-1.5 py-0.2 border border-emerald-500/40 rounded bg-emerald-500/10 flex items-center space-x-1"
        >
          <TrendingUp class="h-2.5 w-2.5" />
          <span>OpenDota 高分段大数据</span>
        </span>
        <span
          v-else-if="recommendation?.source === 'cache'"
          class="text-2xs text-cyan-400 font-bold px-1.5 py-0.2 border border-cyan-500/40 rounded bg-cyan-500/10 flex items-center space-x-1"
        >
          <Database class="h-2.5 w-2.5" />
          <span>本地真实历史缓存</span>
        </span>
      </div>

      <button
        :disabled="loading"
        class="text-xs text-slate-400 p-1 rounded cursor-pointer transition-colors hover:text-amber-400 hover:bg-slate-800 disabled:opacity-40"
        title="刷新出装数据"
        @click="loadRecommendations"
      >
        <RefreshCw class="h-3.5 w-3.5" :class="{ 'animate-spin': loading }" />
      </button>
    </div>

    <!-- 加载中态 -->
    <div v-if="loading" class="py-8 flex flex-col items-center justify-center space-y-2">
      <RefreshCw class="text-amber-400 h-6 w-6 animate-spin" />
      <span class="text-xs text-slate-400 font-mono">正在分析 OpenDota 实时阵容出装大数据...</span>
    </div>

    <!-- 真实空态 (未获取到数据，零伪造原则) -->
    <div
      v-else-if="!recommendation"
      class="px-4 py-8 text-center border border-slate-800 rounded-xl border-dashed bg-slate-950/40 flex flex-col items-center justify-center space-y-2.5"
    >
      <AlertCircle class="text-slate-500 h-6 w-6" />
      <div class="space-y-0.5">
        <p class="text-xs text-slate-300 font-bold">
          {{ errorMessage || '未获取到当前英雄/阵容的大数据出装建议' }}
        </p>
        <p class="text-2xs text-slate-500 font-mono max-w-md">
          网络未连接或实时数据接口暂未响应，请检查网络设置后重试。
        </p>
      </div>
      <button
        class="text-xs text-amber-400 px-3 py-1.5 border border-amber-500/40 rounded-lg bg-amber-500/10 cursor-pointer transition-colors hover:bg-amber-500/20"
        @click="loadRecommendations"
      >
        重新拉取
      </button>
    </div>

    <!-- 真实数据展示 -->
    <template v-else>
      <!-- 1. 本局敌方威胁克制分析条 (Threat Radar) -->
      <div v-if="recommendation?.threatHeroes && recommendation.threatHeroes.length > 0" class="p-2.5 border border-slate-800 rounded-xl bg-slate-950/80 space-y-1.5">
      <div class="text-xs flex items-center justify-between">
        <span class="text-slate-400 font-bold flex items-center space-x-1.5">
          <ShieldAlert class="text-rose-400 h-3.5 w-3.5" />
          <span>本局敌方高危威胁克制源</span>
        </span>
        <span class="text-2xs text-slate-500 font-mono">
          根据对抗大数据历史胜率裁定
        </span>
      </div>

      <div class="pt-0.5 flex flex-wrap gap-2">
        <div
          v-for="th in recommendation.threatHeroes"
          :key="th.heroId"
          class="px-2 py-1 border rounded-lg bg-slate-900/90 flex items-center space-x-2"
          :class="[
            th.threatLevel === 'extreme'
              ? 'border-rose-500/60 bg-rose-950/30 text-rose-200'
              : th.threatLevel === 'high'
                ? 'border-amber-500/50 bg-amber-950/20 text-amber-200'
                : 'border-slate-700 bg-slate-900 text-slate-300',
          ]"
        >
          <SmartImage
            :src="getHeroIconUrl(th.heroId)"
            :fallback-src="getHeroCdnUrl(th.heroId)"
            :alt="th.heroName"
            class="border border-slate-700 rounded h-5 w-5 object-cover"
          />
          <div class="text-xs leading-none">
            <div class="font-extrabold flex items-center space-x-1">
              <span>{{ getHeroChineseName(th.heroId) || th.heroName }}</span>
              <span class="text-2xs font-bold font-mono" :class="th.winRate < 45 ? 'text-rose-400' : 'text-amber-400'">
                {{ th.winRate }}% 胜率
              </span>
            </div>
            <div class="text-2xs text-slate-400 mt-0.5">
              {{ th.threatDesc }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 2. 本局针对性质变出装 (Counter Items 核心展示区 · 琥珀金色高亮) -->
    <div
      v-if="recommendation?.counterItems && recommendation.counterItems.length > 0"
      class="p-3 border border-amber-500/40 rounded-xl shadow-md from-amber-950/30 to-slate-950/90 via-slate-950/80 bg-gradient-to-r space-y-2"
    >
      <div class="flex items-center space-x-2">
        <Swords class="text-amber-400 h-4 w-4" />
        <span class="text-xs text-amber-300 font-extrabold">本局专属针对性质变装</span>
        <span class="text-2xs text-slate-400 font-medium">(针对本局敌方阵容特质动态加权)</span>
      </div>

      <div class="gap-2.5 grid grid-cols-1 md:grid-cols-2">
        <div
          v-for="it in recommendation.counterItems"
          :key="it.itemId"
          class="p-2 border rounded-lg bg-slate-900/90 flex transition-colors items-center space-x-2.5"
          :class="[
            isItemOwned(it.itemKey)
              ? 'border-emerald-500/60 bg-emerald-950/20'
              : 'border-slate-800 hover:border-amber-500/50',
          ]"
        >
          <!-- 56x42 官网规格物品格 -->
          <div
            class="border border-slate-700 rounded-sm bg-black shrink-0 h-[42px] w-[56px] cursor-help shadow relative overflow-hidden"
            @mouseenter="handleMouseEnter(it.itemKey, $event)"
            @mousemove="handleMouseMove"
            @mouseleave="handleMouseLeave"
          >
            <SmartImage
              :src="getItemIconUrl(it.itemKey)"
              :fallback-src="getItemCdnUrl(it.itemKey)"
              :alt="it.itemName"
              class="h-full w-full object-cover"
            />
            <div
              v-if="isItemOwned(it.itemKey)"
              class="border border-emerald-400 bg-emerald-950/70 flex items-center inset-0 justify-center absolute"
            >
              <Check class="text-emerald-300 h-4 w-4 stroke-[3]" />
            </div>
          </div>

          <div class="text-xs flex-1 min-w-0">
            <div class="flex items-center justify-between">
              <span class="text-slate-100 font-extrabold truncate">{{ it.itemName }}</span>
              <span
                v-if="isItemOwned(it.itemKey)"
                class="text-2xs text-emerald-400 font-extrabold px-1.5 py-0.2 border border-emerald-600/40 rounded bg-emerald-950"
              >
                已拥有
              </span>
            </div>
            <p class="text-2xs text-amber-200/90 leading-tight mt-0.5 line-clamp-2">
              {{ it.reason }}
            </p>
          </div>
        </div>
      </div>
    </div>

    <!-- 3. 大数据分阶段标准出装阶梯 (Start / Early / Mid / Late) -->
    <div class="pt-1 border-t border-slate-800/80 space-y-2">
      <div class="text-xs text-slate-400 font-bold flex items-center justify-between">
        <span>全服大数据出装阶梯</span>
        <span class="text-2xs text-slate-500">官网 56x42 规格 · 悬停查看详情</span>
      </div>

      <div class="gap-2 grid grid-cols-2 lg:grid-cols-4">
        <!-- 出门装 -->
        <div class="p-2 border border-slate-800 rounded-xl bg-slate-950/60 space-y-1.5">
          <div class="text-xs text-slate-300 font-bold flex items-center justify-between">
            <span>出门对线</span>
            <span class="text-2xs text-slate-500 font-mono">Start</span>
          </div>
          <div class="flex flex-wrap gap-1.5 items-center">
            <div
              v-for="it in recommendation?.startItems || []"
              :key="it.itemId"
              class="border border-slate-700/80 rounded-sm bg-black h-[42px] w-[56px] cursor-help shadow-sm relative overflow-hidden"
              :class="{ 'ring-2 ring-emerald-500': isItemOwned(it.itemKey) }"
              @mouseenter="handleMouseEnter(it.itemKey, $event)"
              @mousemove="handleMouseMove"
              @mouseleave="handleMouseLeave"
            >
              <SmartImage
                :src="getItemIconUrl(it.itemKey)"
                :fallback-src="getItemCdnUrl(it.itemKey)"
                :alt="it.itemName"
                class="h-full w-full object-cover"
              />
              <span
                v-if="isItemOwned(it.itemKey)"
                class="text-white p-0.5 rounded-tl bg-emerald-600 bottom-0 right-0 absolute"
              >
                <Check class="h-2.5 w-2.5 stroke-[3]" />
              </span>
            </div>
          </div>
        </div>

        <!-- 前期过渡 -->
        <div class="p-2 border border-slate-800 rounded-xl bg-slate-950/60 space-y-1.5">
          <div class="text-xs text-cyan-300 font-bold flex items-center justify-between">
            <span>前期过渡</span>
            <span class="text-2xs text-slate-500 font-mono">&lt; 15m</span>
          </div>
          <div class="flex flex-wrap gap-1.5 items-center">
            <div
              v-for="it in recommendation?.earlyItems || []"
              :key="it.itemId"
              class="border border-slate-700/80 rounded-sm bg-black h-[42px] w-[56px] cursor-help shadow-sm relative overflow-hidden"
              :class="{ 'ring-2 ring-emerald-500': isItemOwned(it.itemKey) }"
              @mouseenter="handleMouseEnter(it.itemKey, $event)"
              @mousemove="handleMouseMove"
              @mouseleave="handleMouseLeave"
            >
              <SmartImage
                :src="getItemIconUrl(it.itemKey)"
                :fallback-src="getItemCdnUrl(it.itemKey)"
                :alt="it.itemName"
                class="h-full w-full object-cover"
              />
              <span
                v-if="isItemOwned(it.itemKey)"
                class="text-white p-0.5 rounded-tl bg-emerald-600 bottom-0 right-0 absolute"
              >
                <Check class="h-2.5 w-2.5 stroke-[3]" />
              </span>
            </div>
          </div>
        </div>

        <!-- 中期节奏 -->
        <div class="p-2 border border-slate-800 rounded-xl bg-slate-950/60 space-y-1.5">
          <div class="text-xs text-amber-300 font-bold flex items-center justify-between">
            <span>中期核心</span>
            <span class="text-2xs text-slate-500 font-mono">15-30m</span>
          </div>
          <div class="flex flex-wrap gap-1.5 items-center">
            <div
              v-for="it in recommendation?.midItems || []"
              :key="it.itemId"
              class="border border-slate-700/80 rounded-sm bg-black h-[42px] w-[56px] cursor-help shadow-sm relative overflow-hidden"
              :class="{ 'ring-2 ring-emerald-500': isItemOwned(it.itemKey) }"
              @mouseenter="handleMouseEnter(it.itemKey, $event)"
              @mousemove="handleMouseMove"
              @mouseleave="handleMouseLeave"
            >
              <SmartImage
                :src="getItemIconUrl(it.itemKey)"
                :fallback-src="getItemCdnUrl(it.itemKey)"
                :alt="it.itemName"
                class="h-full w-full object-cover"
              />
              <span
                v-if="isItemOwned(it.itemKey)"
                class="text-white p-0.5 rounded-tl bg-emerald-600 bottom-0 right-0 absolute"
              >
                <Check class="h-2.5 w-2.5 stroke-[3]" />
              </span>
            </div>
          </div>
        </div>

        <!-- 后期神装 -->
        <div class="p-2 border border-slate-800 rounded-xl bg-slate-950/60 space-y-1.5">
          <div class="text-xs text-purple-300 font-bold flex items-center justify-between">
            <span>后期决胜</span>
            <span class="text-2xs text-slate-500 font-mono">&gt; 30m</span>
          </div>
          <div class="flex flex-wrap gap-1.5 items-center">
            <div
              v-for="it in recommendation?.lateItems || []"
              :key="it.itemId"
              class="border border-slate-700/80 rounded-sm bg-black h-[42px] w-[56px] cursor-help shadow-sm relative overflow-hidden"
              :class="{ 'ring-2 ring-emerald-500': isItemOwned(it.itemKey) }"
              @mouseenter="handleMouseEnter(it.itemKey, $event)"
              @mousemove="handleMouseMove"
              @mouseleave="handleMouseLeave"
            >
              <SmartImage
                :src="getItemIconUrl(it.itemKey)"
                :fallback-src="getItemCdnUrl(it.itemKey)"
                :alt="it.itemName"
                class="h-full w-full object-cover"
              />
              <span
                v-if="isItemOwned(it.itemKey)"
                class="text-white p-0.5 rounded-tl bg-emerald-600 bottom-0 right-0 absolute"
              >
                <Check class="h-2.5 w-2.5 stroke-[3]" />
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </template>

    <!-- 全局 Teleport 挂载官方高对比度物品属性悬浮卡 (ItemTooltipCard) -->
    <Teleport to="body">
      <div
        v-if="hoveredItem"
        ref="tooltipRef"
        class="pointer-events-none transition-opacity duration-150 fixed z-[9999]"
        :style="{ left: `${tooltipPosition.x}px`, top: `${tooltipPosition.y}px` }"
      >
        <ItemTooltipCard
          :item="hoveredItem"
          :position="tooltipPosition"
          :pinned="false"
        />
      </div>
    </Teleport>
  </div>
</template>
