<script setup lang="ts">
/**
 * 英雄出场战绩综合面板 (src/views/analysis/TopHeroesPanel.vue)
 * 职责：
 * 1. 统一合并展示【高频常用】与【近期使用】英雄池
 * 2. 分段切换控制 (Segmented Control)，直观展示出场数与胜率
 * 3. 联动右侧比赛列表历史筛选
 */

import { Clock, Flame, X } from '@lucide/vue'
import { computed, ref } from 'vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { getHeroCdnUrl, getHeroIconUrl } from '@/utils/assetUrl.ts'

defineOptions({ name: 'TopHeroesPanel' })

const props = withDefaults(
  defineProps<{
    topHeroes?: HeroUsageItem[]
    recentHeroes?: HeroUsageItem[]
    // 兼容旧属性 heroes
    heroes?: HeroUsageItem[]
    filterHeroId: number | null
  }>(),
  {
    topHeroes: () => [],
    recentHeroes: () => [],
    heroes: () => [],
  },
)

const emit = defineEmits<{
  (e: 'selectHero', heroId: number): void
  (e: 'clearFilter'): void
}>()

export interface HeroUsageItem {
  heroId: number
  name: string
  count: number
  winRate: number
  lastPlayedAt?: number
  lastWin?: boolean
}

// 保持向下兼容 TopHeroItem 别名
export type TopHeroItem = HeroUsageItem

const activeTab = ref<'top' | 'recent'>('top')

const effectiveTopHeroes = computed(() => {
  if (props.topHeroes && props.topHeroes.length > 0) return props.topHeroes
  return props.heroes || []
})

const effectiveRecentHeroes = computed(() => props.recentHeroes || [])

const currentList = computed(() => {
  return activeTab.value === 'top' ? effectiveTopHeroes.value : effectiveRecentHeroes.value
})
</script>

<template>
  <div class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/70 select-none space-y-3">
    <!-- 顶部操作栏：分段 Tab 切换 + 筛选提示 -->
    <div class="flex flex-wrap gap-2 items-center justify-between">
      <!-- 分段切换器 -->
      <div class="p-0.5 border border-slate-800 rounded-lg bg-slate-900/90 flex items-center space-x-1">
        <button
          type="button"
          class="text-xs px-2.5 py-1 rounded-md flex cursor-pointer transition-all items-center space-x-1.5"
          :class="[
            activeTab === 'top'
              ? 'bg-amber-500/20 text-amber-300 font-bold border border-amber-500/30 shadow-sm'
              : 'text-slate-400 hover:text-slate-200 border border-transparent',
          ]"
          @click="activeTab = 'top'"
        >
          <Flame class="h-3.5 w-3.5" :class="activeTab === 'top' ? 'text-amber-400' : 'text-slate-500'" />
          <span>高频英雄</span>
          <span class="text-3xs font-mono ml-0.5 opacity-80">({{ effectiveTopHeroes.length }})</span>
        </button>

        <button
          type="button"
          class="text-xs px-2.5 py-1 rounded-md flex cursor-pointer transition-all items-center space-x-1.5"
          :class="[
            activeTab === 'recent'
              ? 'bg-amber-500/20 text-amber-300 font-bold border border-amber-500/30 shadow-sm'
              : 'text-slate-400 hover:text-slate-200 border border-transparent',
          ]"
          @click="activeTab = 'recent'"
        >
          <Clock class="h-3.5 w-3.5" :class="activeTab === 'recent' ? 'text-amber-400' : 'text-slate-500'" />
          <span>近期使用</span>
          <span class="text-3xs font-mono ml-0.5 opacity-80">({{ effectiveRecentHeroes.length }})</span>
        </button>
      </div>

      <!-- 右侧筛选状态反馈 -->
      <div class="flex items-center space-x-2">
        <span
          v-if="filterHeroId"
          class="text-2xs text-amber-400 font-mono flex cursor-pointer items-center space-x-1 hover:underline"
          title="点击取消筛选"
          @click="emit('clearFilter')"
        >
          <span>已筛选 (点击清除)</span>
          <X class="h-3 w-3" />
        </span>
        <span v-else class="text-3xs text-slate-500 font-mono hidden sm:inline">
          点击英雄联动筛选历史
        </span>
      </div>
    </div>

    <!-- 英雄卡片列表 -->
    <div v-if="currentList.length > 0" class="gap-2.5 grid grid-cols-1 md:grid-cols-3 sm:grid-cols-2">
      <div
        v-for="h in currentList"
        :key="h.heroId"
        class="p-2 border rounded-lg flex cursor-pointer select-none transition-all items-center space-x-2.5"
        :class="[
          filterHeroId === h.heroId
            ? 'border-amber-400 bg-amber-500/15 shadow-sm ring-1 ring-amber-400/40'
            : 'border-slate-800 bg-slate-900/60 hover:border-slate-700 hover:bg-slate-900',
        ]"
        @click="emit('selectHero', h.heroId)"
      >
        <!-- 英雄头像：使用 SmartImage 自动 CDN 平滑降级 -->
        <div class="border border-slate-700/80 rounded-lg bg-slate-950 shrink-0 h-11 w-16 shadow-sm relative overflow-hidden">
          <SmartImage
            :local-src="getHeroIconUrl(String(h.heroId))"
            :cdn-src="getHeroCdnUrl(String(h.heroId))"
            :alt="h.name"
            class="h-full w-full object-cover"
          />
          <!-- 若有最近胜负状态标签 (例如在近期使用 Tab) -->
          <span
            v-if="h.lastWin !== undefined"
            class="text-4xs font-bold font-mono px-1 py-0.2 border-l border-t rounded-tl bottom-0 right-0 absolute z-10"
            :class="h.lastWin ? 'bg-emerald-950/90 text-emerald-400 border-emerald-600/60' : 'bg-rose-950/90 text-rose-400 border-rose-600/60'"
          >
            {{ h.lastWin ? '胜' : '负' }}
          </span>
        </div>

        <!-- 英雄数据 -->
        <div class="flex-1 min-w-0">
          <div class="text-xs text-slate-100 font-bold truncate">{{ h.name }}</div>
          <div class="text-2xs text-slate-400 font-mono mt-0.5 flex flex-wrap gap-x-2 items-center">
            <span>{{ h.count }} 场</span>
            <span :class="h.winRate >= 50 ? 'text-emerald-400 font-bold' : 'text-rose-400 font-bold'">
              {{ h.winRate }}%
            </span>
          </div>
        </div>
      </div>
    </div>

    <!-- 空数据提示 -->
    <div v-else class="text-xs text-slate-500 font-mono py-6 text-center">
      {{ activeTab === 'top' ? '暂无高频英雄记录' : '暂无近期使用英雄记录' }}
    </div>
  </div>
</template>
