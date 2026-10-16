<script setup lang="ts">
/**
 * 玩家身份档案与全景战术雷达大盘卡片 (src/views/analysis/PlayerProfileBanner.vue)
 * 职责：
 * 1. 顶部：选手身份信息 + 刷新按钮 + Account ID
 * 2. 下半部分三栏紧凑一体化（高能效尺寸收敛）：
 *    - 左侧 (5 列)：6 核心数据指标 (胜率、KDA、GPM、推塔伤害等)
 *    - 中间 (4 列)：高频使用与近期常用英雄池 (支持 Tab 切换与战绩列表联动筛选)
 *    - 右侧 (3 列)：实战五维战术雷达图
 */

import type { AnalysisStatsOverview } from './composables/useAnalysisStats'
import {
  Award,
  Check,
  Clock,
  Coins,
  Copy,
  Flame,
  RefreshCw,
  Sparkles,
  Swords,
  Trophy,
  User,
  X,
} from '@lucide/vue'
import { computed, ref } from 'vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { getHeroCdnUrl, getHeroIconUrl } from '@/utils/assetUrl.ts'
import RadarChart from '@/views/analysis/RadarChart.vue'

defineOptions({ name: 'PlayerProfileBanner' })

export interface HeroUsageItem {
  heroId: number
  name: string
  count: number
  winRate: number
  lastPlayedAt?: number
  lastWin?: boolean
}

const props = withDefaults(
  defineProps<{
    name: string
    avatar: string
    playerId: string
    loading: boolean
    statsOverview: AnalysisStatsOverview
    radarDimensions: { name: string, score: number }[]
    topHeroes?: HeroUsageItem[]
    recentHeroes?: HeroUsageItem[]
    filterHeroId?: number | null
  }>(),
  {
    topHeroes: () => [],
    recentHeroes: () => [],
    filterHeroId: null,
  },
)

const emit = defineEmits<{
  (e: 'refresh'): void
  (e: 'selectHero', heroId: number): void
  (e: 'clearFilter'): void
}>()

const idCopied = ref(false)
const heroTab = ref<'top' | 'recent'>('top')

const currentHeroList = computed(() => {
  return heroTab.value === 'top' ? props.topHeroes : props.recentHeroes
})

async function copyPlayerId(id: string) {
  if (!id) return
  try {
    await navigator.clipboard.writeText(id)
    idCopied.value = true
    setTimeout(() => {
      idCopied.value = false
    }, 2000)
  } catch {}
}
</script>

<template>
  <div class="p-3.5 border border-slate-700/80 rounded-2xl bg-slate-900/90 select-none shadow-xl space-y-3 sm:p-4">
    <!-- 1. 顶部：紧凑选手身份栏 (头像、昵称、Account ID、刷新按钮) -->
    <div class="pb-2.5 border-b border-slate-800 flex gap-3 items-center justify-between">
      <div class="flex min-w-0 items-center space-x-3">
        <!-- 刷新战绩图标按钮 -->
        <button
          v-if="playerId"
          type="button"
          class="text-slate-300 border border-slate-700 rounded-lg bg-slate-950 flex shrink-0 h-8 w-8 cursor-pointer shadow-sm transition-all items-center justify-center hover:text-amber-300 hover:border-amber-500/50 disabled:opacity-40 disabled:cursor-not-allowed"
          :disabled="loading"
          :title="loading ? '刷新中...' : '刷新战绩'"
          @click="emit('refresh')"
        >
          <RefreshCw class="h-3.5 w-3.5" :class="{ 'animate-spin': loading }" />
        </button>

        <!-- 玩家头像 (缩小紧凑，微光渐变外圈) -->
        <div class="p-0.5 rounded-full shrink-0 h-10 w-10 ring-2 ring-amber-400/50 shadow relative from-amber-600 to-amber-200 via-amber-400 bg-gradient-to-tr">
          <div class="rounded-full bg-slate-950 flex h-full w-full items-center justify-center overflow-hidden">
            <img v-if="avatar" :src="avatar" :alt="name || '玩家头像'" class="h-full w-full object-cover">
            <User v-else class="text-amber-400/80 h-5 w-5" />
          </div>
          <span class="border-2 border-slate-900 rounded-full bg-emerald-500 h-2.5 w-2.5 bottom-0 right-0 absolute" />
        </div>

        <!-- 选手昵称与好友 ID -->
        <div class="min-w-0">
          <div class="flex items-center space-x-2">
            <h2 class="text-sm text-slate-100 tracking-wide font-extrabold truncate">
              {{ name || '已绑定选手' }}
            </h2>
          </div>

          <div class="text-xs text-slate-400 font-mono mt-0.5 flex items-center space-x-2">
            <span v-if="playerId" class="px-1.5 py-0.2 border border-slate-800 rounded bg-slate-950 flex items-center space-x-1">
              <span class="text-slate-500">ID:</span>
              <span class="text-slate-200 font-bold">{{ playerId }}</span>
              <button
                class="text-slate-500 ml-1 cursor-pointer transition-colors hover:text-amber-300"
                title="复制 Account ID"
                @click="copyPlayerId(playerId)"
              >
                <Check v-if="idCopied" class="text-emerald-400 h-3 w-3" />
                <Copy v-else class="h-3 w-3" />
              </button>
            </span>
            <span v-else class="text-xs text-amber-400/90">未配置 Account ID</span>
          </div>
        </div>
      </div>
    </div>

    <!-- 2. 下半部分核心三栏整合：左侧指标 (5列) + 中间高频英雄 (4列) + 右侧雷达图 (3列) -->
    <div class="gap-3 grid grid-cols-1 items-stretch lg:grid-cols-12">
      <!-- 2.1 左侧：核心 6 宫格紧凑指标大盘 (占 5 列) -->
      <div class="flex flex-col justify-between lg:col-span-5">
        <div class="gap-2 grid grid-cols-2 sm:grid-cols-3">
          <!-- 1. 综合胜率 -->
          <div class="p-2 border border-slate-800 rounded-lg bg-slate-950/70 flex flex-col justify-between">
            <span class="text-xs text-slate-400 flex items-center space-x-1">
              <Trophy class="text-amber-400 h-3 w-3" />
              <span>综合胜率</span>
            </span>
            <div class="mt-0.5 flex items-baseline space-x-1">
              <span
                class="text-base font-extrabold font-mono"
                :class="Number(statsOverview.winRate) >= 50 ? 'text-emerald-400' : 'text-rose-400'"
              >
                {{ statsOverview.winRate }}%
              </span>
              <span class="text-[10px] text-slate-500">
                ({{ statsOverview.wins }}胜/{{ statsOverview.losses }}负)
              </span>
            </div>
          </div>

          <!-- 2. 场均 K/D/A -->
          <div class="p-2 border border-slate-800 rounded-lg bg-slate-950/70 flex flex-col justify-between">
            <span class="text-xs text-slate-400 flex items-center space-x-1">
              <Swords class="text-cyan-400 h-3 w-3" />
              <span>场均 KDA</span>
            </span>
            <div class="text-xs text-slate-200 font-bold font-mono mt-0.5">
              <span class="text-emerald-400">{{ statsOverview.avgKills }}</span>
              <span class="text-slate-600 mx-0.5">/</span>
              <span class="text-rose-400">{{ statsOverview.avgDeaths }}</span>
              <span class="text-slate-600 mx-0.5">/</span>
              <span class="text-amber-400">{{ statsOverview.avgAssists }}</span>
            </div>
          </div>

          <!-- 3. KDA 战损比 -->
          <div class="p-2 border border-slate-800 rounded-lg bg-slate-950/70 flex flex-col justify-between">
            <span class="text-xs text-slate-400 flex items-center space-x-1">
              <Award class="text-purple-400 h-3 w-3" />
              <span>战损比率</span>
            </span>
            <div class="mt-0.5">
              <span class="text-base text-purple-300 font-extrabold font-mono">{{ statsOverview.kdaRatio }}</span>
            </div>
          </div>

          <!-- 4. 场均打钱 (GPM) -->
          <div class="p-2 border border-slate-800 rounded-lg bg-slate-950/70 flex flex-col justify-between">
            <span class="text-xs text-slate-400 flex items-center space-x-1">
              <Coins class="text-amber-400 h-3 w-3" />
              <span>场均打钱</span>
            </span>
            <div class="mt-0.5">
              <span class="text-base text-amber-400 font-extrabold font-mono">{{ statsOverview.avgGpm || '-' }}</span>
              <span class="text-[10px] text-slate-500 ml-0.5">GPM</span>
            </div>
          </div>

          <!-- 5. 场均推塔伤害 -->
          <div class="p-2 border border-slate-800 rounded-lg bg-slate-950/70 flex flex-col justify-between">
            <span class="text-xs text-slate-400 flex items-center space-x-1">
              <Flame class="text-orange-400 h-3 w-3" />
              <span>推塔伤害</span>
            </span>
            <div class="mt-0.5">
              <span class="text-base text-orange-300 font-extrabold font-mono">{{ statsOverview.avgTowerDmg ? `${(statsOverview.avgTowerDmg / 1000).toFixed(1)}k` : '-' }}</span>
            </div>
          </div>

          <!-- 6. 样本总场次 -->
          <div class="p-2 border border-slate-800 rounded-lg bg-slate-950/70 flex flex-col justify-between">
            <span class="text-xs text-slate-400 flex items-center space-x-1">
              <Clock class="text-slate-400 h-3 w-3" />
              <span>样本场次</span>
            </span>
            <div class="mt-0.5">
              <span class="text-base text-slate-100 font-extrabold font-mono">{{ statsOverview.total }}</span>
              <span class="text-[10px] text-slate-500 ml-0.5">场</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 2.2 中间：高频常用与近期英雄池 (已合并入顶部，占 4 列) -->
      <div class="p-2.5 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between lg:col-span-4 space-y-2">
        <div class="flex items-center justify-between pb-1.5 border-b border-slate-800/80">
          <!-- 分段切换器 -->
          <div class="p-0.5 border border-slate-800 rounded-md bg-slate-900/90 flex items-center space-x-1">
            <button
              type="button"
              class="text-xs px-2 py-0.5 rounded cursor-pointer transition-all flex items-center space-x-1"
              :class="[
                heroTab === 'top'
                  ? 'bg-amber-500/20 text-amber-300 font-bold border border-amber-500/30'
                  : 'text-slate-400 hover:text-slate-200 border border-transparent',
              ]"
              @click="heroTab = 'top'"
            >
              <Flame class="h-3 w-3" :class="heroTab === 'top' ? 'text-amber-400' : 'text-slate-500'" />
              <span>高频</span>
              <span class="font-mono text-[10px] opacity-80">({{ topHeroes.length }})</span>
            </button>

            <button
              type="button"
              class="text-xs px-2 py-0.5 rounded cursor-pointer transition-all flex items-center space-x-1"
              :class="[
                heroTab === 'recent'
                  ? 'bg-amber-500/20 text-amber-300 font-bold border border-amber-500/30'
                  : 'text-slate-400 hover:text-slate-200 border border-transparent',
              ]"
              @click="heroTab = 'recent'"
            >
              <Clock class="h-3 w-3" :class="heroTab === 'recent' ? 'text-amber-400' : 'text-slate-500'" />
              <span>近期</span>
              <span class="font-mono text-[10px] opacity-80">({{ recentHeroes.length }})</span>
            </button>
          </div>

          <!-- 联动筛选状态 -->
          <span
            v-if="filterHeroId"
            class="text-xs text-amber-400 font-mono flex cursor-pointer items-center space-x-1 hover:underline"
            title="点击清除筛选"
            @click="emit('clearFilter')"
          >
            <span>清除筛选</span>
            <X class="h-3 w-3" />
          </span>
          <span v-else class="text-[10px] text-slate-500 font-mono">
            点击英雄筛选
          </span>
        </div>

        <!-- 英雄微型列表 (2 列紧凑排布) -->
        <div v-if="currentHeroList.length > 0" class="gap-1.5 grid grid-cols-2">
          <div
            v-for="h in currentHeroList.slice(0, 6)"
            :key="h.heroId"
            class="p-1 border rounded-lg flex cursor-pointer transition-all items-center space-x-1.5"
            :class="[
              filterHeroId === h.heroId
                ? 'border-amber-400 bg-amber-500/20 ring-1 ring-amber-400/50'
                : 'border-slate-800/80 bg-slate-900/60 hover:border-slate-700 hover:bg-slate-900',
            ]"
            @click="emit('selectHero', h.heroId)"
          >
            <div class="border border-slate-700/80 rounded bg-slate-950 shrink-0 h-6 w-9 shadow-sm relative overflow-hidden">
              <SmartImage
                :local-src="getHeroIconUrl(String(h.heroId))"
                :cdn-src="getHeroCdnUrl(String(h.heroId))"
                :alt="h.name"
                class="h-full w-full object-cover"
              />
              <span
                v-if="h.lastWin !== undefined"
                class="text-[8px] font-bold font-mono px-0.5 leading-none bottom-0 right-0 absolute z-10"
                :class="h.lastWin ? 'bg-emerald-950 text-emerald-400' : 'bg-rose-950 text-rose-400'"
              >
                {{ h.lastWin ? '胜' : '负' }}
              </span>
            </div>

            <div class="flex-1 min-w-0">
              <div class="text-xs text-slate-100 font-bold truncate leading-tight">{{ h.name }}</div>
              <div class="text-[10px] text-slate-400 font-mono flex items-center justify-between">
                <span>{{ h.count }}场</span>
                <span :class="h.winRate >= 50 ? 'text-emerald-400' : 'text-rose-400'">{{ h.winRate }}%</span>
              </div>
            </div>
          </div>
        </div>

        <div v-else class="py-3 text-center text-xs text-slate-500 font-mono">
          暂无英雄出场数据
        </div>
      </div>

      <!-- 2.3 右侧：实战能力五维雷达图 (占 3 列) -->
      <div class="p-2 border border-slate-800/80 rounded-xl bg-slate-950/60 flex flex-col items-center justify-center lg:col-span-3">
        <div class="mb-0.5 pb-1 border-b border-slate-800/60 flex w-full items-center justify-between">
          <span class="text-xs text-slate-200 font-bold flex items-center space-x-1">
            <Sparkles class="text-cyan-400 h-3 w-3" />
            <span>战术五维</span>
          </span>
        </div>
        <div class="py-0.5 flex flex-1 items-center justify-center scale-90 sm:scale-95">
          <RadarChart :dimensions="radarDimensions" />
        </div>
      </div>
    </div>
  </div>
</template>
