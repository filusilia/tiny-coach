<script setup lang="ts">
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
  ShieldAlert,
  Sparkles,
  Trophy,
  UserCheck,
} from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { HERO_CHINESE_NAMES } from '../data/heroNames'
import { aiCoachService } from '../services/aiCoachService'
import { fetchMatchDetails, fetchPlayerRecentMatchesList } from '../services/playerProfileService'
import { useAppStore } from '../store'

const { state, showToast } = useAppStore()

const searchMatchId = ref('')
const searching = ref(false)
const searchError = ref('')
const currentMatch = ref<any | null>(null)

// 选定分析视角的玩家槽位 (若为 null 则自动取绑定账号或 0 号位)
const selectedPlayerSlot = ref<number | null>(null)
// AI 复盘分析报告
const reviewResult = ref<MatchReviewResult | null>(null)
const reviewing = ref(false)
const copied = ref(false)

// 近期绑定的对局列表
const recentMatches = ref<any[]>([])
const loadingRecent = ref(false)

onMounted(async () => {
  if (state.settings.dota_player_id) {
    await fetchPlayerRecentMatches(state.settings.dota_player_id)
  }
})

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

function formatDuration(seconds: number): string {
  const min = Math.floor(seconds / 60)
  const sec = (seconds % 60).toString().padStart(2, '0')
  return `${min}:${sec}`
}

function getHeroName(heroId: number): string {
  return HERO_CHINESE_NAMES[heroId] || `英雄 #${heroId}`
}

function isBoundPlayer(player: any): boolean {
  if (!state.settings.dota_player_id) return false
  return String(player?.account_id) === String(state.settings.dota_player_id)
}

function getSelectedPlayer(): any {
  if (!currentMatch.value || !Array.isArray(currentMatch.value.players)) return null
  if (selectedPlayerSlot.value !== null) {
    return currentMatch.value.players.find((p: any) => p.player_slot === selectedPlayerSlot.value) || currentMatch.value.players[0]
  }
  return currentMatch.value.players[0]
}

function selectPlayerPerspective(slot: number) {
  selectedPlayerSlot.value = slot
  // 若之前已经生成过复盘，自动清空提醒重新针对新视角复盘
  if (reviewResult.value) {
    showToast('已切换主复盘选手视角，请点击【重新生成复盘】', 'info')
  }
}

async function handleRunAiReview() {
  if (!currentMatch.value) return
  reviewing.value = true
  try {
    const res = await aiCoachService.generateMatchReview(currentMatch.value, selectedPlayerSlot.value ?? undefined)
    reviewResult.value = res
    showToast('AI 深度复盘推演完成！', 'success')
  } catch (e: any) {
    showToast(`复盘生成异常: ${e?.message || '请检查端点配置'}`, 'error')
  } finally {
    reviewing.value = false
  }
}

async function copyReviewText() {
  if (!reviewResult.value?.fullText) return
  try {
    await navigator.clipboard.writeText(reviewResult.value.fullText)
    copied.value = true
    showToast('复盘报告全文已复制到剪贴板', 'success')
    setTimeout(() => {
      copied.value = false
    }, 2000)
  } catch {
    showToast('复制失败，请手动划选文本', 'warning')
  }
}
</script>

<template>
  <div class="bg-dota-bg text-slate-100 p-6 flex-1 select-none overflow-y-auto space-y-6">
    <!-- 顶部标题 -->
    <div class="pb-3 border-b border-slate-700/80 flex items-center justify-between">
      <div>
        <h1 class="text-lg text-amber-400 tracking-wide font-bold flex items-center space-x-2">
          <Trophy class="text-amber-400 h-5 w-5" />
          <span>赛后战绩与 AI 复盘中枢</span>
        </h1>
        <p class="text-xs text-slate-300 font-normal mt-0.5">
          支持输入 Match ID 即时查询官方比赛详情，并基于绑定玩家账号深度复盘经济转化与团战表现
        </p>
      </div>

      <div v-if="state.settings.dota_player_id" class="text-xs px-3 py-1 border border-emerald-500/50 rounded-lg bg-slate-900 flex items-center space-x-2">
        <UserCheck class="text-emerald-400 h-4 w-4" />
        <span class="text-slate-300">当前主账号:</span>
        <strong class="text-amber-400 font-mono">{{ state.settings.dota_player_name || state.settings.dota_player_id }}</strong>
      </div>
    </div>

    <!-- Match ID 搜索栏 -->
    <div class="p-4 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-2">
      <label class="text-xs text-slate-200 font-bold block">
        输入比赛 Match ID 查询全场对局数据 (OpenDota 官方数据源)
      </label>
      <div class="flex items-center space-x-2">
        <div class="flex-1 relative">
          <input
            v-model="searchMatchId"
            type="text"
            placeholder="输入比赛编号 (例如: 7890123456)，支持任意官方比赛"
            class="text-xs text-amber-300 font-mono px-3.5 outline-none border border-slate-700 rounded-lg bg-slate-950 h-10 w-full transition-colors focus:border-amber-400"
            @keydown.enter="handleSearchMatch()"
          >
        </div>
        <button
          :disabled="searching || !searchMatchId.trim()"
          class="text-xs text-slate-950 font-bold px-6 rounded-lg bg-amber-500 flex h-10 shadow transition-all items-center space-x-2 hover:bg-amber-400 disabled:opacity-50"
          @click="handleSearchMatch()"
        >
          <Activity v-if="searching" class="h-4 w-4 animate-spin" />
          <Search v-else class="h-4 w-4" />
          <span>{{ searching ? '正在查询对局...' : '查询并复盘' }}</span>
        </button>
      </div>

      <div v-if="searchError" class="text-xs text-rose-300 p-2.5 border border-rose-800 rounded-lg bg-rose-950/40">
        {{ searchError }}
      </div>
    </div>

    <!-- 查询到的比赛详细复盘看板 -->
    <div v-if="currentMatch" class="space-y-4">
      <!-- 比赛比分与总揽卡片 -->
      <div class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/95 shadow-2xl space-y-4">
        <div class="pb-3 border-b border-slate-800 flex flex-wrap gap-4 items-center justify-between">
          <div class="flex items-center space-x-4">
            <div
              class="text-sm tracking-wider font-black px-3 py-1.5 rounded-lg uppercase" :class="[
                currentMatch.radiant_win
                  ? 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/40'
                  : 'bg-rose-500/20 text-rose-300 border border-rose-500/40',
              ]"
            >
              {{ currentMatch.radiant_win ? '天辉方 胜利 (Radiant Victory)' : '夜魇方 胜利 (Dire Victory)' }}
            </div>
            <div class="text-xs text-slate-300 font-mono flex items-center space-x-2">
              <Clock class="text-amber-400 h-3.5 w-3.5" />
              <span>对局时长: <strong>{{ formatDuration(currentMatch.duration) }}</strong></span>
            </div>
          </div>

          <div class="text-sm font-black font-mono flex items-center space-x-3">
            <span class="text-base text-emerald-400">{{ currentMatch.radiant_score }}</span>
            <span class="text-slate-600">:</span>
            <span class="text-base text-rose-400">{{ currentMatch.dire_score }}</span>
            <span class="text-[11px] text-slate-500 font-mono ml-2">Match #{{ currentMatch.match_id }}</span>
          </div>
        </div>

        <!-- 阵容与玩家列表对比 -->
        <div class="space-y-3">
          <h3 class="text-xs text-slate-200 font-bold flex items-center space-x-2">
            <Flame class="text-amber-400 h-4 w-4" />
            <span>双方全员实战表现数据</span>
          </h3>

          <div class="overflow-x-auto">
            <table class="text-xs font-mono text-left w-full">
              <thead>
                <tr class="text-[11px] text-slate-400 border-b border-slate-800">
                  <th class="px-3 py-2">
                    阵营 / 玩家
                  </th>
                  <th class="px-2 py-2">
                    英雄
                  </th>
                  <th class="px-2 py-2">
                    等级
                  </th>
                  <th class="px-2 py-2">
                    K / D / A
                  </th>
                  <th class="px-2 py-2">
                    GPM / XPM
                  </th>
                  <th class="px-2 py-2">
                    造成伤害
                  </th>
                  <th class="px-2 py-2">
                    正/反补
                  </th>
                </tr>
              </thead>
              <tbody class="divide-slate-800/60 divide-y">
                <tr
                  v-for="p in currentMatch.players"
                  :key="p.player_slot"
                  class="cursor-pointer transition-colors" :class="[
                    selectedPlayerSlot === p.player_slot
                      ? 'bg-amber-500/20 font-bold border-l-4 border-amber-400 shadow-sm'
                      : isBoundPlayer(p)
                        ? 'bg-amber-500/10 border-l-2 border-amber-500/50 hover:bg-slate-800/60'
                        : 'hover:bg-slate-800/40',
                  ]"
                  title="点击将此选手设为 AI 复盘主分析视角"
                  @click="selectPlayerPerspective(p.player_slot)"
                >
                  <td class="px-3 py-2.5 flex items-center space-x-2">
                    <span
                      class="text-[9px] font-bold px-1 py-0.2 rounded uppercase" :class="[
                        p.isRadiant ? 'bg-emerald-950 text-emerald-300 border border-emerald-700' : 'bg-rose-950 text-rose-300 border border-rose-700',
                      ]"
                    >
                      {{ p.isRadiant ? '天辉' : '夜魇' }}
                    </span>
                    <span class="text-slate-200 font-sans max-w-[120px] truncate" :title="p.personaname || `玩家 #${p.account_id || p.player_slot}`">
                      {{ p.personaname || `玩家 #${p.account_id || p.player_slot}` }}
                    </span>
                    <span v-if="selectedPlayerSlot === p.player_slot" class="text-[9px] text-slate-950 font-black px-1.5 py-0.2 rounded bg-amber-500">
                      复盘视角
                    </span>
                    <span v-else-if="isBoundPlayer(p)" class="text-[9px] text-emerald-300 font-bold px-1.5 py-0.2 border border-emerald-500/40 rounded bg-emerald-500/30">
                      主账号
                    </span>
                  </td>
                  <td class="text-slate-200 font-medium font-sans px-2 py-2.5">
                    {{ getHeroName(p.hero_id) }}
                  </td>
                  <td class="text-amber-400 font-bold px-2 py-2.5">
                    Lv.{{ p.level }}
                  </td>
                  <td class="text-slate-100 px-2 py-2.5">
                    <span class="text-emerald-400">{{ p.kills }}</span> /
                    <span class="text-rose-400">{{ p.deaths }}</span> /
                    <span class="text-cyan-400">{{ p.assists }}</span>
                  </td>
                  <td class="text-slate-300 px-2 py-2.5">
                    {{ p.gold_per_min }} / {{ p.xp_per_min }}
                  </td>
                  <td class="text-rose-300 px-2 py-2.5">
                    {{ p.hero_damage }}
                  </td>
                  <td class="text-slate-400 px-2 py-2.5">
                    {{ p.last_hits }} / {{ p.denies }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- AI 智能深度复盘卡片 (参考 BrightGir/dota-ai-coach 决策层级) -->
      <div class="p-5 border border-amber-500/40 rounded-2xl bg-slate-900/95 shadow-2xl space-y-4">
        <div class="pb-3 border-b border-slate-800 flex flex-wrap gap-3 items-center justify-between">
          <div class="flex items-center space-x-2.5">
            <div class="text-slate-950 rounded-lg flex h-8 w-8 shadow-md items-center justify-center from-amber-500 to-amber-600 bg-gradient-to-br">
              <Bot class="h-5 w-5" />
            </div>
            <div>
              <div class="flex items-center space-x-2">
                <h3 class="text-sm text-amber-300 tracking-wide font-extrabold">
                  AI 智能深度复盘 (职业战队分析师级)
                </h3>
                <span class="text-[10px] text-amber-300 font-bold font-mono px-2 py-0.5 border border-amber-500/40 rounded bg-amber-500/20">
                  Dota 2 决策层级推演
                </span>
              </div>
              <p class="text-xs text-slate-400 font-normal mt-0.5">
                宏观战局胜负手、主视角对线与团战走位、装备针对性与下局落地指南
              </p>
            </div>
          </div>

          <!-- 右侧当前分析视角与触发按钮 -->
          <div class="flex items-center space-x-3">
            <div v-if="getSelectedPlayer()" class="text-xs text-slate-300 px-3 py-1.5 border border-slate-800 rounded-lg bg-slate-950 flex items-center space-x-1.5">
              <span class="text-slate-500">当前主分析视角:</span>
              <span class="text-amber-400 font-bold font-sans">{{ getHeroName(getSelectedPlayer()?.hero_id) }}</span>
              <span class="text-[11px] text-slate-400">({{ getSelectedPlayer()?.personaname || `玩家#${getSelectedPlayer()?.player_slot}` }})</span>
            </div>

            <button
              :disabled="reviewing"
              class="text-xs text-slate-950 font-bold px-4 py-2 rounded-lg flex cursor-pointer shadow-lg transition-all items-center from-amber-500 to-amber-600 bg-gradient-to-r space-x-2 disabled:opacity-50 hover:from-amber-400 hover:to-amber-500"
              @click="handleRunAiReview()"
            >
              <Activity v-if="reviewing" class="text-slate-950 h-4 w-4 animate-spin" />
              <Sparkles v-else class="text-slate-950 h-4 w-4" />
              <span>{{ reviewing ? '正在推演战局得失...' : (reviewResult ? '重新生成复盘' : '一键生成 AI 深度复盘') }}</span>
            </button>
          </div>
        </div>

        <!-- 1. 未生成且未在加载中时的欢迎态 -->
        <div v-if="!reviewResult && !reviewing" class="p-6 text-center border border-slate-800/80 rounded-xl bg-slate-950/60 space-y-2">
          <div class="text-xs text-slate-300 font-medium">
            点击上方【一键生成 AI 深度复盘】，系统将基于双方客观战绩数据与阵容克制关系，按职业战队标准生成结构化战术复盘。
          </div>
          <div class="text-[11px] text-slate-500">
            提示：您可以在上方战绩表格中点击任意一行玩家，将其设为主复盘分析视角。
          </div>
        </div>

        <!-- 2. 推演 Loading 动效 -->
        <div v-else-if="reviewing" class="p-8 text-center border border-slate-800 rounded-xl bg-slate-950/80 space-y-3">
          <div class="text-amber-400 p-3 border border-amber-500/30 rounded-full bg-amber-500/10 inline-flex animate-pulse">
            <Cpu class="h-6 w-6" />
          </div>
          <div class="text-sm text-amber-300 font-bold">
            战队 AI 教练正在深入推演本场对局关键分水岭...
          </div>
          <div class="text-xs text-slate-400 leading-relaxed mx-auto max-w-md">
            正在对比双方阵容强势窗口期、计算主视角经济转化率、评估针对性出装克制并提炼下局行动指南...
          </div>
        </div>

        <!-- 3. 复盘结果结构化卡片展示 -->
        <div v-else-if="reviewResult" class="space-y-3.5">
          <!-- 顶部状态栏: 使用模型 + 复制按钮 -->
          <div class="text-xs px-1 flex items-center justify-between">
            <div class="text-[11px] text-slate-400 font-mono flex items-center space-x-2">
              <span class="rounded-full bg-emerald-400 h-2 w-2 animate-pulse" />
              <span>推理引擎: {{ reviewResult.modelUsed }}</span>
            </div>
            <button
              class="text-xs text-slate-200 px-2.5 py-1 border border-slate-700 rounded-md bg-slate-800 flex cursor-pointer transition-colors items-center space-x-1.5 hover:bg-slate-700"
              @click="copyReviewText()"
            >
              <Check v-if="copied" class="text-emerald-400 h-3.5 w-3.5" />
              <Copy v-else class="h-3.5 w-3.5" />
              <span>{{ copied ? '已复制' : '复制复盘' }}</span>
            </button>
          </div>

          <div class="gap-3 grid grid-cols-1 md:grid-cols-2">
            <!-- 模块 1: 大盘与胜负定局复盘 -->
            <div class="p-4 border border-slate-800 rounded-xl bg-slate-950 space-y-2">
              <div class="text-xs text-amber-400 font-bold flex items-center space-x-2">
                <Flame class="text-amber-400 h-4 w-4" />
                <span>1. 大盘与胜负定局复盘</span>
              </div>
              <p class="text-xs text-slate-200 leading-relaxed font-normal whitespace-pre-line">
                {{ reviewResult.overview }}
              </p>
            </div>

            <!-- 模块 2: 主视角实战得失剖析 -->
            <div class="p-4 border border-slate-800 rounded-xl bg-slate-950 space-y-2">
              <div class="text-xs text-cyan-400 font-bold flex items-center space-x-2">
                <Trophy class="text-cyan-400 h-4 w-4" />
                <span>2. 主视角核心实战得失剖析</span>
              </div>
              <p class="text-xs text-slate-200 leading-relaxed font-normal whitespace-pre-line">
                {{ reviewResult.keyTurningPoints }}
              </p>
            </div>

            <!-- 模块 3: 装备针对性与克制推演 -->
            <div class="p-4 border border-slate-800 rounded-xl bg-slate-950 space-y-2">
              <div class="text-xs text-emerald-400 font-bold flex items-center space-x-2">
                <ShieldAlert class="text-emerald-400 h-4 w-4" />
                <span>3. 装备针对性与克制推演</span>
              </div>
              <p class="text-xs text-slate-200 leading-relaxed font-normal whitespace-pre-line">
                {{ reviewResult.itemizationAnalysis }}
              </p>
            </div>

            <!-- 模块 4: 下局致胜核心提升指南 -->
            <div class="p-4 border border-slate-800 rounded-xl bg-slate-950 space-y-2">
              <div class="text-xs text-amber-300 font-bold flex items-center space-x-2">
                <Sparkles class="text-amber-300 h-4 w-4" />
                <span>4. 下局致胜核心提升指南</span>
              </div>
              <div class="pt-0.5 space-y-1.5">
                <div
                  v-for="(action, idx) in reviewResult.nextMatchActions"
                  :key="idx"
                  class="text-xs text-slate-200 leading-relaxed flex items-start space-x-2"
                >
                  <span class="text-[10px] text-amber-400 font-bold font-mono mt-0.5 rounded-full bg-amber-500/20 flex shrink-0 h-4 w-4 items-center justify-center">
                    {{ idx + 1 }}
                  </span>
                  <span>{{ action }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 近期已绑定账号对局列表 (若已绑定玩家 ID) -->
    <div v-if="state.settings.dota_player_id" class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-3">
      <div class="pb-2 border-b border-slate-800 flex items-center justify-between">
        <h2 class="text-xs text-slate-100 font-bold flex items-center space-x-2">
          <Clock class="text-amber-400 h-4 w-4" />
          <span>我的近期对局历史 (点击任意一场直接载入复盘)</span>
        </h2>
        <span class="text-[11px] text-slate-400 font-mono">
          账号: {{ state.settings.dota_player_id }}
        </span>
      </div>

      <div v-if="loadingRecent" class="text-xs text-slate-400 py-6 text-center flex items-center justify-center space-x-2">
        <Activity class="text-amber-400 h-4 w-4 animate-spin" />
        <span>正在从 OpenDota 同步最近战绩...</span>
      </div>

      <div v-else-if="recentMatches.length === 0" class="text-xs text-slate-400 py-6 text-center">
        暂未查询到此账号近期对战，您可在上方手动输入 Match ID 进行复盘。
      </div>

      <div v-else class="gap-2.5 grid grid-cols-1 md:grid-cols-2">
        <div
          v-for="m in recentMatches"
          :key="m.match_id"
          class="p-3 border border-slate-800 rounded-xl bg-slate-950/80 flex cursor-pointer transition-all items-center justify-between hover:border-amber-500/50"
          @click="handleSearchMatch(String(m.match_id))"
        >
          <div class="flex items-center space-x-3">
            <span
              class="text-xs font-bold font-mono rounded-lg flex h-8 w-8 items-center justify-center" :class="[
                (m.radiant_win && m.player_slot < 128) || (!m.radiant_win && m.player_slot >= 128)
                  ? 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/40'
                  : 'bg-rose-500/20 text-rose-400 border border-rose-500/40',
              ]"
            >
              {{ (m.radiant_win && m.player_slot < 128) || (!m.radiant_win && m.player_slot >= 128) ? '胜' : '负' }}
            </span>
            <div>
              <div class="text-xs text-slate-100 font-bold font-sans">
                {{ getHeroName(m.hero_id) }}
              </div>
              <div class="text-[10px] text-slate-400 font-mono">
                Match #{{ m.match_id }} · {{ formatDuration(m.duration) }}
              </div>
            </div>
          </div>

          <div class="text-xs font-mono text-right">
            <div class="text-slate-200">
              <span class="text-emerald-400">{{ m.kills }}</span> /
              <span class="text-rose-400">{{ m.deaths }}</span> /
              <span class="text-cyan-400">{{ m.assists }}</span>
            </div>
            <div class="text-[10px] text-amber-400">
              点击复盘 →
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 未绑定账号时的指引提示 -->
    <div v-else class="p-6 text-center border border-slate-700 rounded-2xl border-dashed bg-slate-900/40 space-y-2">
      <p class="text-xs text-slate-300 font-medium">
        您尚未绑定 Dota 2 玩家账号。在设置中绑定您的数字 ID 后，此处将自动为您呈现最近 10 场天梯排位并一键复盘。
      </p>
    </div>
  </div>
</template>
