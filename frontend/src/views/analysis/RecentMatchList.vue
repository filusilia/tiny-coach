<script setup lang="ts">
/**
 * 近期对局历史卡片列表 (src/views/analysis/RecentMatchList.vue)
 * 职责：
 * 1. 支持前端大样本智能分页 (每页 12 场，支持上一页/下一页/页码切换)
 * 2. 点击条目即时响应反馈与菊花 Loading 态，优化进入详情体验
 */

import { ChevronLeft, ChevronRight, Clock, Loader2 } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { getHeroChineseName } from '@/data/heroNames.ts'
import { getHeroCdnUrl, getHeroIconUrl } from '@/utils/assetUrl.ts'

defineOptions({ name: 'RecentMatchList' })

const props = defineProps<{
  matches: any[]
  totalMatchesCount: number
  selectedMatchId: string | number | null
  loadingDetail?: boolean
}>()

const emit = defineEmits<{
  (e: 'selectMatch', matchId: string): void
}>()

/** 每页呈现比赛场次 (12 场在 3 列网格下正好 4 行，排版极其整齐) */
const pageSize = 12
/** 当前激活分页页码 (1-indexed) */
const currentPage = ref(1)
/** 当前正在被点击载入中的比赛 ID */
const activeLoadingMatchId = ref<string | null>(null)

// 当外部比赛列表源发生变化时重置回第 1 页
watch(
  () => props.matches.length,
  () => {
    currentPage.value = 1
  },
)

// 当外部 loading 结束时，清空正在载入比赛 ID
watch(
  () => props.loadingDetail,
  (val) => {
    if (!val) {
      activeLoadingMatchId.value = null
    }
  },
)

const totalPages = computed(() => {
  return Math.max(1, Math.ceil(props.matches.length / pageSize))
})

const pagedMatches = computed(() => {
  const start = (currentPage.value - 1) * pageSize
  return props.matches.slice(start, start + pageSize)
})

function goToPage(page: number) {
  if (page >= 1 && page <= totalPages.value) {
    currentPage.value = page
  }
}

function handleMatchClick(matchId: number | string) {
  const strId = String(matchId)
  activeLoadingMatchId.value = strId
  emit('selectMatch', strId)
}

function formatDuration(sec: number): string {
  const m = Math.floor(sec / 60)
  const s = sec % 60
  return `${m}分${s.toString().padStart(2, '0')}秒`
}

function getHeroName(heroId: number): string {
  return getHeroChineseName(heroId) || `英雄 #${heroId}`
}
</script>

<template>
  <section class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 select-none shadow-xl space-y-4">
    <div class="pb-2 border-b border-slate-800 flex items-center justify-between">
      <div class="flex items-center space-x-2">
        <Clock class="text-amber-400 h-4 w-4" />
        <h3 class="text-xs text-slate-200 font-bold">
          近期天梯比赛历史 (点击比赛打开独立详情页与 AI 复盘)
        </h3>
      </div>
      <span class="text-2xs text-slate-400 font-mono">
        共 {{ totalMatchesCount }} 场 · 当前第 {{ currentPage }} / {{ totalPages }} 页
      </span>
    </div>

    <!-- 卡片列表网格 -->
    <div class="gap-2.5 grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3">
      <div
        v-for="m in pagedMatches"
        :key="m.match_id"
        class="group p-2.5 border rounded-xl flex cursor-pointer select-none transition-all items-center justify-between relative overflow-hidden"
        :class="[
          String(m.match_id) === String(selectedMatchId) || String(m.match_id) === activeLoadingMatchId
            ? 'border-amber-400 bg-amber-500/15 shadow-md shadow-amber-500/10'
            : 'border-slate-800 bg-slate-950/60 hover:border-amber-500/50 hover:bg-slate-900',
        ]"
        @click="handleMatchClick(m.match_id)"
      >
        <!-- 左侧：英雄头像与胜负徽章 -->
        <div class="flex items-center space-x-3">
          <!-- 英雄头像：使用 SmartImage 自动 CDN 平滑降级 (统一放大醒目) -->
          <div class="border border-slate-700/80 rounded-lg bg-slate-950 shrink-0 h-13 w-20 shadow-sm transition-colors relative overflow-hidden group-hover:border-amber-500/60">
            <SmartImage
              :local-src="getHeroIconUrl(String(m.hero_id))"
              :cdn-src="getHeroCdnUrl(String(m.hero_id))"
              :alt="getHeroName(m.hero_id)"
              class="h-full w-full object-cover"
            />
          </div>

          <!-- 英雄名与比赛 ID -->
          <div class="flex flex-col">
            <div class="flex items-center space-x-1.5">
              <span class="text-sm text-slate-100 font-extrabold transition-colors group-hover:text-amber-300">
                {{ getHeroName(m.hero_id) }}
              </span>
              <!-- 胜负标识徽章 (稍微调小，精致紧凑) -->
              <span
                class="text-[10px] font-bold font-mono px-1 py-0.5 rounded leading-none shrink-0"
                :class="m.radiant_win === (m.player_slot < 128)
                  ? 'bg-emerald-950/80 text-emerald-400 border border-emerald-700/50'
                  : 'bg-rose-950/80 text-rose-400 border border-rose-700/50'"
              >
                {{ m.radiant_win === (m.player_slot < 128) ? '胜利' : '失败' }}
              </span>
            </div>
            <div class="text-2xs text-slate-500 font-mono">
              #{{ m.match_id }}
            </div>
          </div>
        </div>

        <!-- 右侧：KDA 与时长 + 详情箭头或加载中菊花 -->
        <div class="flex items-center space-x-3">
          <div class="font-mono text-right space-y-0.5">
            <div class="text-xs text-slate-200 font-bold">
              <span class="text-emerald-400">{{ m.kills }}</span>
              <span class="text-slate-600">/</span>
              <span class="text-rose-400">{{ m.deaths }}</span>
              <span class="text-slate-600">/</span>
              <span class="text-amber-400">{{ m.assists }}</span>
            </div>
            <div class="text-2xs text-slate-400 flex items-center justify-end space-x-1">
              <span>{{ formatDuration(m.duration || 0) }}</span>
              <span v-if="m.gold_per_min" class="text-amber-400/80">· {{ m.gold_per_min }} GPM</span>
            </div>
          </div>

          <!-- 加载中动画或导航箭头 -->
          <div class="flex shrink-0 w-5 items-center justify-center">
            <Loader2
              v-if="String(m.match_id) === activeLoadingMatchId"
              class="text-amber-400 h-4 w-4 animate-spin"
            />
            <ChevronRight
              v-else
              class="text-slate-600 h-4 w-4 transition-transform group-hover:text-amber-400 group-hover:translate-x-0.5"
            />
          </div>
        </div>
      </div>
    </div>

    <!-- 底部精致分页栏 (支持多页切换) -->
    <div
      v-if="totalPages > 1"
      class="text-xs font-mono pt-3 border-t border-slate-800 flex items-center justify-between"
    >
      <div class="text-2xs text-slate-500">
        显示第 {{ (currentPage - 1) * pageSize + 1 }} ~ {{ Math.min(currentPage * pageSize, matches.length) }} 场，共 {{ matches.length }} 场比赛
      </div>

      <div class="flex items-center space-x-1.5">
        <button
          type="button"
          :disabled="currentPage === 1"
          class="text-slate-300 p-1 px-2 border border-slate-700/80 rounded-lg bg-slate-950 flex transition-colors items-center space-x-1 hover:text-amber-300 hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed"
          @click="goToPage(currentPage - 1)"
        >
          <ChevronLeft class="h-3.5 w-3.5" />
          <span>上一页</span>
        </button>

        <!-- 紧凑页码按钮 -->
        <div class="flex items-center space-x-1">
          <button
            v-for="p in totalPages"
            :key="`page-${p}`"
            type="button"
            class="font-bold border rounded-lg flex h-7 w-7 transition-colors items-center justify-center"
            :class="[
              currentPage === p
                ? 'border-amber-400 bg-amber-500 text-slate-950 shadow-sm'
                : 'border-slate-800 bg-slate-950 text-slate-400 hover:bg-slate-800 hover:text-slate-200',
            ]"
            @click="goToPage(p)"
          >
            {{ p }}
          </button>
        </div>

        <button
          type="button"
          :disabled="currentPage === totalPages"
          class="text-slate-300 p-1 px-2 border border-slate-700/80 rounded-lg bg-slate-950 flex transition-colors items-center space-x-1 hover:text-amber-300 hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed"
          @click="goToPage(currentPage + 1)"
        >
          <span>下一页</span>
          <ChevronRight class="h-3.5 w-3.5" />
        </button>
      </div>
    </div>
  </section>
</template>
