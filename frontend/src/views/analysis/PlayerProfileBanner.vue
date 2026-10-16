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
  Check,
  Clock,
  Coins,
  Copy,
  RefreshCw,
  Sparkles,
  Swords,
  Trophy,
  User,
  X,
} from '@lucide/vue'
import { ref } from 'vue'
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
  (e: 'selectHero', heroId: number): void
  (e: 'clearFilter'): void
  (e: 'refresh'): void
}>()

const idCopied = ref(false)

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
  <div class="p-4 border border-slate-700/80 rounded-2xl bg-slate-900/90 select-none shadow-xl space-y-4">
    <!-- 1. 顶部：选手身份栏 (头像、昵称、Account ID) - 彻底移除多余的刷新按钮 -->
    <div class="pb-3 border-b border-slate-800 flex items-center justify-between">
      <div class="flex min-w-0 items-center space-x-3">
        <!-- 玩家头像 (微光渐变外圈) -->
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

      <!-- 右侧：刷新战绩按钮 -->
      <button
        type="button"
        class="px-2.5 py-1.5 border border-slate-700/80 rounded-lg bg-slate-800/80 text-xs text-slate-300 font-medium flex items-center space-x-1.5 transition-all hover:bg-slate-700 hover:text-slate-100 hover:border-slate-600 active:scale-95 disabled:opacity-50"
        :disabled="loading"
        title="重新拉取并同步最新对局数据"
        @click="emit('refresh')"
      >
        <RefreshCw class="h-3.5 w-3.5 text-amber-400" :class="{ 'animate-spin': loading }" />
        <span>刷新战绩</span>
      </button>
    </div>

    <!-- 2. 下半部分：精简后的 3 大指标 + 放大版近期英雄 (8列) + 战术五维雷达图 (4列) -->
    <div class="gap-4 grid grid-cols-1 items-stretch lg:grid-cols-12">
      <!-- 2.1 左侧核心大盘：3 核心指标 + 放大后的近期英雄列表 (占 8 列) -->
      <div class="flex flex-col justify-between lg:col-span-8 space-y-3">
        <!-- 精简后的 3 大战绩核心指标 (去掉推塔伤害、样本、战损) -->
        <div class="gap-2.5 grid grid-cols-3">
          <!-- 1. 综合胜率 -->
          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between shadow-sm">
            <span class="text-xs text-slate-400 font-medium flex items-center space-x-1.5">
              <Trophy class="text-amber-400 h-3.5 w-3.5" />
              <span>综合胜率</span>
            </span>
            <div class="mt-1 flex items-baseline space-x-1.5">
              <span
                class="text-lg font-extrabold font-mono"
                :class="Number(statsOverview.winRate) >= 50 ? 'text-emerald-400' : 'text-rose-400'"
              >
                {{ statsOverview.winRate }}%
              </span>
              <span class="text-xs text-slate-500 font-mono">
                ({{ statsOverview.wins }}胜/{{ statsOverview.losses }}负)
              </span>
            </div>
          </div>

          <!-- 2. 场均 KDA -->
          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between shadow-sm">
            <span class="text-xs text-slate-400 font-medium flex items-center space-x-1.5">
              <Swords class="text-cyan-400 h-3.5 w-3.5" />
              <span>场均 KDA</span>
            </span>
            <div class="text-sm text-slate-200 font-bold font-mono mt-1 flex items-center">
              <span class="text-emerald-400 font-extrabold">{{ statsOverview.avgKills }}</span>
              <span class="text-slate-600 mx-1">/</span>
              <span class="text-rose-400 font-extrabold">{{ statsOverview.avgDeaths }}</span>
              <span class="text-slate-600 mx-1">/</span>
              <span class="text-amber-400 font-extrabold">{{ statsOverview.avgAssists }}</span>
            </div>
          </div>

          <!-- 3. 场均打钱 (GPM) -->
          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between shadow-sm">
            <span class="text-xs text-slate-400 font-medium flex items-center space-x-1.5">
              <Coins class="text-amber-400 h-3.5 w-3.5" />
              <span>场均打钱</span>
            </span>
            <div class="mt-1 flex items-baseline space-x-1">
              <span class="text-lg text-amber-400 font-extrabold font-mono">{{ statsOverview.avgGpm || '-' }}</span>
              <span class="text-xs text-slate-500 font-mono">GPM</span>
            </div>
          </div>
        </div>

        <!-- 放大后的近期英雄卡片展示区 (去除微型局促感，采用舒适大号网格卡片) -->
        <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 space-y-2 flex-1">
          <div class="flex items-center justify-between pb-1.5 border-b border-slate-800/80">
            <div class="flex items-center space-x-2">
              <Clock class="h-3.5 w-3.5 text-amber-400" />
              <span class="text-xs text-slate-200 font-bold">近期英雄</span>
              <span class="font-mono text-xs text-slate-400">({{ recentHeroes.length }})</span>
            </div>

            <!-- 联动筛选状态 -->
            <button
              v-if="filterHeroId"
              type="button"
              class="text-xs text-amber-400 font-mono flex cursor-pointer items-center space-x-1 hover:underline"
              title="点击清除筛选"
              @click="emit('clearFilter')"
            >
              <span>清除筛选</span>
              <X class="h-3.5 w-3.5" />
            </button>
            <span v-else class="text-xs text-slate-500 font-mono">
              点击卡片筛选
            </span>
          </div>

          <!-- 放大版英雄卡片 (3 列排布，大幅提升辨识度与点击面积) -->
          <div v-if="recentHeroes.length > 0" class="gap-2.5 grid grid-cols-2 sm:grid-cols-3">
            <div
              v-for="h in recentHeroes.slice(0, 6)"
              :key="h.heroId"
              class="p-2 border rounded-xl flex cursor-pointer transition-all items-center space-x-3 shadow-sm"
              :class="[
                filterHeroId === h.heroId
                  ? 'border-amber-400 bg-amber-500/20 ring-2 ring-amber-400/50'
                  : 'border-slate-800/90 bg-slate-900/80 hover:border-slate-700 hover:bg-slate-900',
              ]"
              @click="emit('selectHero', h.heroId)"
            >
              <!-- 大号英雄头像 (56x32) -->
              <div class="border border-slate-700 rounded-lg bg-slate-950 shrink-0 h-9 w-14 shadow relative overflow-hidden">
                <SmartImage
                  :local-src="getHeroIconUrl(String(h.heroId))"
                  :cdn-src="getHeroCdnUrl(String(h.heroId))"
                  :alt="h.name"
                  class="h-full w-full object-cover"
                />
                <span
                  v-if="h.lastWin !== undefined"
                  class="text-[9px] font-bold font-mono px-1 py-0.2 leading-none bottom-0 right-0 absolute z-10 rounded-tl"
                  :class="h.lastWin ? 'bg-emerald-950/90 text-emerald-400 border-t border-l border-emerald-600/50' : 'bg-rose-950/90 text-rose-400 border-t border-l border-rose-600/50'"
                >
                  {{ h.lastWin ? '胜' : '负' }}
                </span>
              </div>

              <!-- 英雄名字与战绩指标 -->
              <div class="flex-1 min-w-0 space-y-0.5">
                <div class="text-xs text-slate-100 font-extrabold truncate">{{ h.name }}</div>
                <div class="text-xs font-mono flex items-center justify-between">
                  <span class="text-slate-400">{{ h.count }} 场</span>
                  <span
                    class="font-bold"
                    :class="h.winRate >= 50 ? 'text-emerald-400' : 'text-rose-400'"
                  >
                    {{ h.winRate }}%
                  </span>
                </div>
              </div>
            </div>
          </div>

          <div v-else class="py-6 text-center text-xs text-slate-500 font-mono">
            暂无近期英雄记录
          </div>
        </div>
      </div>

      <!-- 2.2 右侧：实战能力五维雷达图 (占 4 列) -->
      <div class="p-3 border border-slate-800/80 rounded-xl bg-slate-950/60 flex flex-col items-center justify-center lg:col-span-4">
        <div class="mb-1 pb-1.5 border-b border-slate-800/60 flex w-full items-center justify-between">
          <span class="text-xs text-slate-200 font-bold flex items-center space-x-1.5">
            <Sparkles class="text-cyan-400 h-3.5 w-3.5" />
            <span>战术五维</span>
          </span>
        </div>
        <div class="py-1 flex flex-1 items-center justify-center w-full">
          <RadarChart :dimensions="radarDimensions" />
        </div>
      </div>
    </div>
  </div>
</template>
