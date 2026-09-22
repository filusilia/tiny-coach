<script setup lang="ts">
/**
 * 赛后战局与 AI 深度复盘视图 (src/views/AnalysisView.vue)
 * 职责：
 * 1. 查询并解析 Dota 2 官方比赛详情 (Match ID) 与绑定玩家的近期 20 场天梯对局
 * 2. 渲染十人对战全景大盘 (双方阵容、KDA、56x42 物品栏、经济领先走势)
 * 3. 基于 Solution Hierarchy 思维链调度端侧 NPU/云端大模型生成专业复盘报告
 * 4. 拆解胜负分水岭、对线期得失、出装克制推演与下局致胜提升指南
 */

import type { MatchReviewResult } from '../services/aiCoachService'
import {
  Activity,
  Bot,
  Check,
  Clock,
  Copy,
  Cpu,
  Flame,
  Search,
  Sparkles,
  Trophy,
} from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { HERO_CHINESE_NAMES } from '../data/heroNames'
import { aiCoachService } from '../services/aiCoachService'
import { fetchMatchDetails, fetchPlayerRecentMatchesList } from '../services/playerProfileService'
import { useAppStore } from '../stores/app'

const appStore = useAppStore()

/** 搜索框输入的比赛数字编号 */
const searchMatchId = ref('')
/** 正在查询比赛数据中状态 */
const searching = ref(false)
/** 查询比赛报错信息 */
const searchError = ref('')
/** 当前正在分析的比赛原始数据实体 */
const currentMatch = ref<any | null>(null)

/** 选定分析视角的玩家槽位 (若为 null 则自动取绑定账号或 0 号位) */
const selectedPlayerSlot = ref<number | null>(null)
/** AI 深度复盘分析报告实体 */
const reviewResult = ref<MatchReviewResult | null>(null)
/** 正在生成 AI 复盘报告中状态 */
const reviewing = ref(false)
/** 是否已复制报告文本至剪贴板 */
const copied = ref(false)

/** 近期绑定的玩家对局列表快照 */
const recentMatches = ref<any[]>([])
/** 正在拉取近期对局状态 */
const loadingRecent = ref(false)

onMounted(async () => {
  if (appStore.settings.dota_player_id) {
    await fetchPlayerRecentMatches(appStore.settings.dota_player_id)
  }
})

/**
 * 拉取绑定玩家的近期对局历史
 * @param accountId 玩家 32 位数字 ID
 */
async function fetchPlayerRecentMatches(accountId: string) {
  loadingRecent.value = true
  try {
    const list = await fetchPlayerRecentMatchesList(accountId)
    if (Array.isArray(list)) {
      recentMatches.value = list.slice(0, 10)
    }
  } catch (e) {
    console.warn('获取玩家近期比赛失败:', e)
  } finally {
    loadingRecent.value = false
  }
}

/**
 * 依据比赛 ID 查询完整比赛大盘数据
 * @param targetId 指定比赛 ID（可选，未传则取输入框内容）
 */
async function handleSearchMatch(targetId?: string) {
  const mid = (targetId || searchMatchId.value).trim()
  if (!mid) {
    searchError.value = '请输入有效的 Dota 2 比赛 Match ID'
    return
  }

  searching.value = true
  searchError.value = ''
  currentMatch.value = null

  try {
    const data = await fetchMatchDetails(mid)
    currentMatch.value = data
    searchMatchId.value = mid
    reviewResult.value = null

    // 默认优先选中绑定玩家的槽位，否则选第一个玩家
    if (Array.isArray(data.players) && data.players.length > 0) {
      const bound = data.players.find((p: any) => isBoundPlayer(p))
      selectedPlayerSlot.value = bound ? bound.player_slot : data.players[0].player_slot
    } else {
      selectedPlayerSlot.value = null
    }
  } catch (err: any) {
    searchError.value = `查询比赛失败: ${err.message || '网络请求超时'}`
  } finally {
    searching.value = false
  }
}

/**
 * 格式化比赛时长为 MM:SS
 * @param seconds 时长秒数
 */
function formatDuration(seconds: number): string {
  const min = Math.floor(seconds / 60)
  const sec = (seconds % 60).toString().padStart(2, '0')
  return `${min}:${sec}`
}

/**
 * 根据官方英雄数字 ID 获取中文译名 (严格 100% 静态常数兜底)
 * @param heroId 英雄数字 ID
 */
function getHeroName(heroId: number): string {
  return HERO_CHINESE_NAMES[heroId] || `英雄 #${heroId}`
}

/**
 * 判断指定槽位玩家是否为当前主程序绑定的账号
 * @param player 玩家条目
 */
function isBoundPlayer(player: any): boolean {
  if (!appStore.settings.dota_player_id || !player.account_id) return false
  return String(player.account_id) === String(appStore.settings.dota_player_id)
}

/**
 * 调用 AI 推理服务生成结构化深度战局复盘
 */
async function handleGenerateReview() {
  if (!currentMatch.value || selectedPlayerSlot.value === null || reviewing.value) return

  reviewing.value = true
  reviewResult.value = null

  try {
    const res = await aiCoachService.generateMatchReview(
      currentMatch.value,
      selectedPlayerSlot.value,
      appStore.settings.user_context,
    )
    reviewResult.value = res
    appStore.showToast('AI 赛后深度复盘报告已成功生成！', 'success')
  } catch (err: any) {
    appStore.showToast(`生成复盘分析失败: ${err.message || '推理超时'}`, 'error')
  } finally {
    reviewing.value = false
  }
}

/**
 * 一键复制复盘报告内容到系统剪贴板
 */
async function handleCopyReview() {
  if (!reviewResult.value) return
  try {
    await navigator.clipboard.writeText(reviewResult.value.fullText)
    copied.value = true
    appStore.showToast('已将完整复盘报告复制到剪贴板！', 'success')
    setTimeout(() => {
      copied.value = false
    }, 2500)
  } catch {
    appStore.showToast('复制到剪贴板失败，请手动选取文本', 'warning')
  }
}
</script>

<template>
  <div class="bg-dota-bg text-slate-100 flex flex-1 flex-col select-none overflow-hidden">
    <!-- 顶部状态栏 -->
    <header class="border-dota-border bg-dota-surface px-6 border-b flex shrink-0 h-14 items-center justify-between">
      <div class="flex items-center space-x-3">
        <Bot class="text-amber-400 h-5 w-5" />
        <h1 class="text-base text-amber-400 tracking-wide font-bold">
          赛后战局 AI 深度复盘与针对性打法研判
        </h1>
      </div>
      <div class="text-xs text-slate-400 font-mono flex items-center space-x-2">
        <Sparkles class="text-cyan-400 h-4 w-4" />
        <span>基于 Solution Hierarchy 思维链</span>
      </div>
    </header>

    <!-- 主视口内容区 -->
    <div class="p-6 flex-1 overflow-y-auto space-y-6">
      <!-- 1. 比赛搜索与近期战绩载入区 -->
      <section class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
        <div class="flex flex-col gap-3 items-center justify-between sm:flex-row">
          <div class="flex-1 w-full relative">
            <input
              v-model="searchMatchId"
              type="text"
              placeholder="输入 Dota 2 比赛编号 (Match ID) 检索比赛数据..."
              class="text-xs text-slate-100 font-mono px-4 py-2.5 pl-10 outline-none border border-slate-700 rounded-xl bg-slate-950 w-full focus:ring-1 focus:ring-amber-400"
              @keydown.enter="() => handleSearchMatch()"
            >
            <Search class="text-slate-400 h-4 w-4 left-3.5 top-3 absolute" />
          </div>
          <button
            class="text-xs text-slate-950 font-bold px-5 py-2.5 rounded-xl bg-amber-500 flex w-full cursor-pointer shadow-md transition-colors items-center justify-center space-x-1.5 hover:bg-amber-400 sm:w-auto"
            :disabled="searching"
            @click="() => handleSearchMatch()"
          >
            <Activity class="h-4 w-4" :class="{ 'animate-spin': searching }" />
            <span>{{ searching ? '正在检索大盘...' : '载入战局数据' }}</span>
          </button>
        </div>

        <div v-if="searchError" class="text-xs text-rose-400 font-mono">
          {{ searchError }}
        </div>

        <!-- 绑定玩家的近期对局快捷胶囊 -->
        <div v-if="recentMatches.length > 0" class="pt-2 border-t border-slate-800/80">
          <div class="text-xs text-slate-400 mb-2 flex items-center space-x-1.5">
            <Clock class="text-amber-400 h-3.5 w-3.5" />
            <span>您绑定的账号近期天梯对局 (点击即可秒级载入复盘)：</span>
          </div>
          <div class="flex flex-wrap gap-2">
            <button
              v-for="m in recentMatches"
              :key="m.match_id"
              class="text-xs font-mono px-2.5 py-1 border border-slate-700 rounded-lg bg-slate-950/80 flex cursor-pointer transition-colors items-center space-x-1.5 hover:border-amber-400/60 hover:bg-slate-800"
              :class="{ 'border-amber-400 text-amber-300': String(m.match_id) === String(searchMatchId) }"
              @click="() => handleSearchMatch(String(m.match_id))"
            >
              <span class="font-bold">{{ getHeroName(m.hero_id) }}</span>
              <span :class="m.radiant_win === (m.player_slot < 128) ? 'text-emerald-400' : 'text-rose-400'">
                {{ m.radiant_win === (m.player_slot < 128) ? '胜' : '负' }}
              </span>
              <span class="text-[10px] text-slate-500">#{{ m.match_id }}</span>
            </button>
          </div>
        </div>
      </section>

      <!-- 2. 比赛详情全景大盘 (当有比赛被载入时) -->
      <section v-if="currentMatch" class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-4">
        <!-- 比赛元信息头 -->
        <div class="pb-3 border-b border-slate-800 flex flex-col gap-3 items-start justify-between sm:flex-row sm:items-center">
          <div class="space-y-1">
            <div class="flex items-center space-x-2">
              <span class="text-sm text-slate-100 font-extrabold font-mono">比赛 #{{ currentMatch.match_id }}</span>
              <span
                class="text-xs font-bold px-2 py-0.5 rounded"
                :class="currentMatch.radiant_win ? 'bg-emerald-950 text-emerald-300 border border-emerald-700' : 'bg-rose-950 text-rose-300 border border-rose-700'"
              >
                {{ currentMatch.radiant_win ? '天辉获胜' : '夜魇获胜' }}
              </span>
            </div>
            <div class="text-xs text-slate-400 font-mono flex items-center space-x-3">
              <span>时长: {{ formatDuration(currentMatch.duration || 0) }}</span>
              <span>比分: <b class="text-emerald-400">{{ currentMatch.radiant_score ?? '-' }}</b> : <b class="text-rose-400">{{ currentMatch.dire_score ?? '-' }}</b></span>
            </div>
          </div>

          <!-- 一键生成 AI 深度复盘按钮 -->
          <button
            class="text-xs text-slate-950 font-bold px-5 py-2 rounded-xl flex cursor-pointer shadow-lg transition-all items-center from-amber-500 to-amber-600 bg-gradient-to-r space-x-2 disabled:opacity-50 hover:from-amber-400 hover:to-amber-500"
            :disabled="reviewing"
            @click="handleGenerateReview"
          >
            <Cpu class="h-4 w-4" :class="{ 'animate-spin': reviewing }" />
            <span>{{ reviewing ? 'AI 教练思维链推演中...' : '生成针对性 AI 复盘报告' }}</span>
          </button>
        </div>

        <!-- 选手视角选择提示 -->
        <div class="text-xs text-slate-400 flex items-center justify-between">
          <span>请点击选择下方任一选手作为复盘主视角 (默认已自动定位您绑定的账号)：</span>
          <span class="text-amber-400 font-bold">当前视角: 槽位 {{ selectedPlayerSlot ?? '0' }}</span>
        </div>

        <!-- 十人阵容卡片列表 -->
        <div class="gap-3 grid grid-cols-1 lg:grid-cols-2">
          <!-- 天辉阵营 -->
          <div class="p-3 border border-emerald-900/40 rounded-xl bg-emerald-950/20 space-y-1.5">
            <div class="text-xs text-emerald-400 font-bold pb-1 border-b border-emerald-900/30 flex items-center space-x-1.5">
              <Trophy class="h-3.5 w-3.5" />
              <span>天辉方 (The Radiant)</span>
            </div>
            <div
              v-for="p in currentMatch.players.slice(0, 5)"
              :key="p.player_slot"
              class="text-xs p-2 border rounded-lg flex cursor-pointer transition-all items-center justify-between"
              :class="[
                selectedPlayerSlot === p.player_slot
                  ? 'border-amber-400 bg-amber-500/15 shadow-sm'
                  : 'border-slate-800 bg-slate-950/60 hover:border-slate-700',
              ]"
              @click="selectedPlayerSlot = p.player_slot"
            >
              <div class="flex items-center space-x-2.5">
                <span class="text-slate-200 font-bold">{{ getHeroName(p.hero_id) }}</span>
                <span v-if="isBoundPlayer(p)" class="text-[9px] text-amber-300 font-mono px-1 py-0.2 border border-amber-500/40 rounded bg-amber-500/20">您操纵</span>
              </div>
              <div class="text-[11px] text-slate-400 font-mono space-x-3">
                <span class="text-slate-200 font-bold">{{ p.kills }}/{{ p.deaths }}/{{ p.assists }}</span>
                <span>净资产: <b class="text-amber-400">{{ p.net_worth || p.total_gold }}</b></span>
              </div>
            </div>
          </div>

          <!-- 夜魇阵营 -->
          <div class="p-3 border border-rose-900/40 rounded-xl bg-rose-950/20 space-y-1.5">
            <div class="text-xs text-rose-400 font-bold pb-1 border-b border-rose-900/30 flex items-center space-x-1.5">
              <Flame class="h-3.5 w-3.5" />
              <span>夜魇方 (The Dire)</span>
            </div>
            <div
              v-for="p in currentMatch.players.slice(5, 10)"
              :key="p.player_slot"
              class="text-xs p-2 border rounded-lg flex cursor-pointer transition-all items-center justify-between"
              :class="[
                selectedPlayerSlot === p.player_slot
                  ? 'border-amber-400 bg-amber-500/15 shadow-sm'
                  : 'border-slate-800 bg-slate-950/60 hover:border-slate-700',
              ]"
              @click="selectedPlayerSlot = p.player_slot"
            >
              <div class="flex items-center space-x-2.5">
                <span class="text-slate-200 font-bold">{{ getHeroName(p.hero_id) }}</span>
                <span v-if="isBoundPlayer(p)" class="text-[9px] text-amber-300 font-mono px-1 py-0.2 border border-amber-500/40 rounded bg-amber-500/20">您操纵</span>
              </div>
              <div class="text-[11px] text-slate-400 font-mono space-x-3">
                <span class="text-slate-200 font-bold">{{ p.kills }}/{{ p.deaths }}/{{ p.assists }}</span>
                <span>净资产: <b class="text-amber-400">{{ p.net_worth || p.total_gold }}</b></span>
              </div>
            </div>
          </div>
        </div>

        <!-- 3. AI 复盘分析报告卡片呈现 -->
        <div v-if="reviewResult" class="pt-4 border-t border-slate-800 space-y-4">
          <div class="flex items-center justify-between">
            <div class="flex items-center space-x-2">
              <Sparkles class="text-amber-400 h-5 w-5" />
              <h3 class="text-sm text-amber-400 font-bold">
                战术教练深度复盘分析报告
              </h3>
              <span class="text-xs text-slate-500 font-mono">模型: {{ reviewResult.modelUsed }}</span>
            </div>
            <button
              class="text-xs text-slate-200 px-3 py-1 border border-slate-700 rounded-lg bg-slate-800 flex cursor-pointer transition-colors items-center space-x-1.5 hover:bg-slate-700"
              @click="handleCopyReview"
            >
              <Check v-if="copied" class="text-emerald-400 h-3.5 w-3.5" />
              <Copy v-else class="h-3.5 w-3.5" />
              <span>{{ copied ? '已复制报告' : '一键复制报告' }}</span>
            </button>
          </div>

          <!-- 报告结构化要点 -->
          <div class="text-xs leading-relaxed gap-3 grid grid-cols-1 md:grid-cols-2">
            <div class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/70 space-y-1.5">
              <div class="text-slate-200 font-bold flex items-center space-x-1.5">
                <span class="rounded-full bg-amber-400 h-1.5 w-1.5" />
                <span>大盘战略与胜负分水岭</span>
              </div>
              <p class="text-slate-300">
                {{ reviewResult.overview }}
              </p>
            </div>

            <div class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/70 space-y-1.5">
              <div class="text-slate-200 font-bold flex items-center space-x-1.5">
                <span class="rounded-full bg-cyan-400 h-1.5 w-1.5" />
                <span>核心视角实战得失剖析</span>
              </div>
              <p class="text-slate-300">
                {{ reviewResult.keyTurningPoints }}
              </p>
            </div>

            <div class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/70 space-y-1.5">
              <div class="text-slate-200 font-bold flex items-center space-x-1.5">
                <span class="rounded-full bg-emerald-400 h-1.5 w-1.5" />
                <span>出装针对性推演与克制</span>
              </div>
              <p class="text-slate-300">
                {{ reviewResult.itemizationAnalysis }}
              </p>
            </div>

            <div class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/70 space-y-1.5">
              <div class="text-slate-200 font-bold flex items-center space-x-1.5">
                <span class="rounded-full bg-rose-400 h-1.5 w-1.5" />
                <span>下局致胜核心提升指南</span>
              </div>
              <ul class="text-slate-300 list-disc list-inside space-y-1">
                <li v-for="(act, idx) in reviewResult.nextMatchActions" :key="idx">
                  {{ act }}
                </li>
              </ul>
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>
