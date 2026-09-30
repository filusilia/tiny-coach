<script setup lang="ts">
/**
 * 玩家身份档案与全景战术雷达大盘卡片 (src/views/analysis/PlayerProfileBanner.vue)
 * 职责：
 * 1. 左侧：刷新图标 + 头像 + 身份信息 + 胜率 / KDA 等核心指标 + 近期高频英雄
 * 2. 右侧：实战五维雷达图
 * 3. 彻底移除冗余生硬的能量进度条，雷达图独立成列
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
} from '@lucide/vue'
import { ref } from 'vue'
import RadarChart from '@/views/analysis/RadarChart.vue'

defineOptions({ name: 'PlayerProfileBanner' })

defineProps<{
  name: string
  avatar: string
  playerId: string
  loading: boolean
  statsOverview: AnalysisStatsOverview
  radarDimensions: { name: string, score: number }[]
}>()

const emit = defineEmits<{
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
  <div class="p-4 border border-slate-700/80 rounded-2xl bg-slate-900/90 select-none shadow-xl space-y-4 sm:p-5">
    <!-- 1. 顶部：刷新图标(置于头像左侧) + 选手身份信息 -->
    <div class="pb-3 border-b border-slate-800 flex gap-3 items-center justify-between">
      <div class="flex min-w-0 items-center space-x-3.5">
        <!-- 刷新战绩图标按钮 -->
        <button
          v-if="playerId"
          type="button"
          class="text-slate-300 border border-slate-700 rounded-xl bg-slate-950 flex shrink-0 h-9 w-9 cursor-pointer shadow-sm transition-all items-center justify-center hover:text-amber-300 hover:border-amber-500/50 disabled:opacity-40 disabled:cursor-not-allowed"
          :disabled="loading"
          :title="loading ? '刷新中...' : '刷新战绩'"
          @click="emit('refresh')"
        >
          <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': loading }" />
        </button>

        <!-- 玩家高清头像 (带微光渐变外圈) -->
        <div class="p-0.5 rounded-full shrink-0 h-13 w-13 ring-2 ring-amber-400/50 shadow-md relative from-amber-600 to-amber-200 via-amber-400 bg-gradient-to-tr">
          <div class="rounded-full bg-slate-950 flex h-full w-full items-center justify-center overflow-hidden">
            <img v-if="avatar" :src="avatar" :alt="name || '玩家头像'" class="h-full w-full object-cover">
            <User v-else class="text-amber-400/80 h-6 w-6" />
          </div>
          <span class="border-2 border-slate-900 rounded-full bg-emerald-500 h-3.5 w-3.5 bottom-0 right-0 absolute" />
        </div>

        <!-- 选手昵称与好友 ID -->
        <div class="min-w-0">
          <div class="flex items-center space-x-2">
            <h2 class="text-base text-slate-100 tracking-wide font-extrabold truncate">
              {{ name || '已绑定选手' }}
            </h2>
          </div>

          <div class="text-xs text-slate-400 font-mono mt-0.5 flex items-center space-x-2">
            <span v-if="playerId" class="px-2 py-0.5 border border-slate-800 rounded bg-slate-950 flex items-center space-x-1">
              <span class="text-slate-500">Account ID:</span>
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
            <span v-else class="text-2xs text-amber-400/90">未配置 Account ID</span>
          </div>
        </div>
      </div>
    </div>

    <!-- 2. 下半部分核心整合：左侧指标大盘 + 右侧雷达图 -->
    <div class="gap-4 grid grid-cols-1 items-stretch lg:grid-cols-12">
      <!-- 2.1 左侧：胜率 / KDA 等核心大盘 + 近期高频英雄 (占 7 列) -->
      <div class="flex flex-col gap-3 lg:col-span-7">
        <!-- 6 宫格紧凑排布 -->
        <div class="gap-2.5 grid grid-cols-2 sm:grid-cols-3">
          <!-- 1. 综合胜率 -->
          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between">
            <span class="text-2xs text-slate-400 flex items-center space-x-1">
              <Trophy class="text-amber-400 h-3 w-3" />
              <span>综合胜率</span>
            </span>
            <div class="mt-1 flex items-baseline space-x-1">
              <span
                class="text-xl font-extrabold font-mono"
                :class="Number(statsOverview.winRate) >= 50 ? 'text-emerald-400' : 'text-rose-400'"
              >
                {{ statsOverview.winRate }}%
              </span>
              <span class="text-3xs text-slate-500">
                ({{ statsOverview.wins }}胜/{{ statsOverview.losses }}负)
              </span>
            </div>
          </div>

          <!-- 2. 场均 K/D/A -->
          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between">
            <span class="text-2xs text-slate-400 flex items-center space-x-1">
              <Swords class="text-cyan-400 h-3 w-3" />
              <span>场均 K / D / A</span>
            </span>
            <div class="text-sm text-slate-200 font-bold font-mono mt-1">
              <span class="text-emerald-400">{{ statsOverview.avgKills }}</span>
              <span class="text-slate-600 mx-0.5">/</span>
              <span class="text-rose-400">{{ statsOverview.avgDeaths }}</span>
              <span class="text-slate-600 mx-0.5">/</span>
              <span class="text-amber-400">{{ statsOverview.avgAssists }}</span>
            </div>
          </div>

          <!-- 3. KDA 战损比 -->
          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between">
            <span class="text-2xs text-slate-400 flex items-center space-x-1">
              <Award class="text-purple-400 h-3 w-3" />
              <span>战损比率 (KDA)</span>
            </span>
            <div class="mt-1">
              <span class="text-xl text-purple-300 font-extrabold font-mono">{{ statsOverview.kdaRatio }}</span>
              <span class="text-3xs text-slate-500 ml-1">KDA</span>
            </div>
          </div>

          <!-- 4. 场均经济 (GPM) -->
          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between">
            <span class="text-2xs text-slate-400 flex items-center space-x-1">
              <Coins class="text-amber-400 h-3 w-3" />
              <span>场均打钱 (GPM)</span>
            </span>
            <div class="mt-1">
              <span class="text-xl text-amber-400 font-extrabold font-mono">{{ statsOverview.avgGpm || '-' }}</span>
              <span class="text-3xs text-slate-500 ml-1">金币/分</span>
            </div>
          </div>

          <!-- 5. 场均建筑伤害 -->
          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between">
            <span class="text-2xs text-slate-400 flex items-center space-x-1">
              <Flame class="text-orange-400 h-3 w-3" />
              <span>场均推塔伤害</span>
            </span>
            <div class="mt-1">
              <span class="text-xl text-orange-300 font-extrabold font-mono">{{ statsOverview.avgTowerDmg ? `${(statsOverview.avgTowerDmg / 1000).toFixed(1)}k` : '-' }}</span>
              <span class="text-3xs text-slate-500 ml-1">建筑伤害</span>
            </div>
          </div>

          <!-- 6. 样本总场次 -->
          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between">
            <span class="text-2xs text-slate-400 flex items-center space-x-1">
              <Clock class="text-slate-400 h-3 w-3" />
              <span>近期样本场次</span>
            </span>
            <div class="mt-1">
              <span class="text-xl text-slate-100 font-extrabold font-mono">{{ statsOverview.total }}</span>
              <span class="text-3xs text-slate-500 ml-1">场比赛</span>
            </div>
          </div>
        </div>
</div>

      <!-- 2.2 右侧：实战能力五维雷达图 (占 5 列) -->
      <div class="p-3 border border-slate-800/80 rounded-xl bg-slate-950/60 flex flex-col items-center justify-center lg:col-span-5">
        <div class="mb-1 pb-1.5 border-b border-slate-800/60 flex w-full items-center justify-between">
          <span class="text-xs text-slate-200 font-bold flex items-center space-x-1.5">
            <Sparkles class="text-cyan-400 h-3.5 w-3.5" />
            <span>最近评价</span>
          </span>
        </div>
        <div class="py-1 flex flex-1 items-center justify-center">
          <RadarChart :dimensions="radarDimensions" />
        </div>
      </div>
    </div>
  </div>
</template>
