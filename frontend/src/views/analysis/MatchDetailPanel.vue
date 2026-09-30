<script setup lang="ts">
/**
 * 比赛十人对战全景大盘与深度复盘中心 (src/views/analysis/MatchDetailPanel.vue)
 * 职责：
 * 1. 紧凑单卡片顶部 Header (返回按钮 + 比赛ID双击复制 + 模式/时间/比分)
 * 2. 深度 AI 战术研判面板 (免去思维链字眼，专注绑定选手复盘，无十人视角切换干扰)
 * 3. 严格对齐 OpenDota 标准十人全景数据表格 (横向滚动条，列宽垂直对齐，防长ID错乱)
 * 4. 英雄头像放大气派，使用 scepter_0/1 与 shard_0/1 状态图标
 */

import type { ItemMeta, OpenDotaMatchDetail, OpenDotaPlayer } from '@/api/types'
import type { MatchReviewResult } from '@/services/aiCoachService.ts'
import {
  Activity,
  ArrowLeft,
  Check,
  Clock,
  Coins,
  Copy,
  Cpu,
  Eye,
  Flame,
  LayoutGrid,
  Shield,
  Sparkles,
  TrendingUp,
  Trophy,
} from '@lucide/vue'
import { computed, onMounted, onUnmounted, ref } from 'vue'
import ItemTooltipCard from '@/components/common/ItemTooltipCard.vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { getHeroChineseName } from '@/data/heroNames.ts'
import { aiCoachService } from '@/services/aiCoachService.ts'
import { gameDataService } from '@/services/gameDataService.ts'
import {
  getHeroCdnUrl,
  getHeroIconUrl,
  getItemCdnUrl,
  getItemIconUrl,
} from '@/utils/assetUrl.ts'
import AdvantageGraph from '@/views/analysis/AdvantageGraph.vue'

defineOptions({ name: 'MatchDetailPanel' })

const props = defineProps<{
  match: OpenDotaMatchDetail
  selectedPlayerSlot: number | null
  boundPlayerId: string
  itemDict: Record<number, ItemMeta>
  reviewing: boolean
  reviewResult?: MatchReviewResult | null
}>()

const emit = defineEmits<{
  (e: 'selectSlot', slot: number): void
  (e: 'generateReview'): void
  (e: 'backToList'): void
}>()

const aiReviewStatus = computed(() => aiCoachService.isAiReviewConfigured())

/** 当前激活的数据展示 Tab */
const activeTab = ref<'overview' | 'advantage' | 'advanced' | 'draft'>('overview')

/** 一键复制复盘报告状态 */
const copied = ref(false)

/** 比赛 ID 复制反馈状态 */
const matchIdCopied = ref(false)

const fullItemMap = ref<Map<string | number, any>>(new Map())
const hoveredItem = ref<any | null>(null)
const pinnedItem = ref<any | null>(null)
const activeTooltipItem = computed(() => pinnedItem.value || hoveredItem.value)
const tooltipPosition = ref<{ x: number, y: number }>({ x: 0, y: 0 })

onMounted(async () => {
  fullItemMap.value = await gameDataService.getFullItemCatalogMap()
  window.addEventListener('keydown', handleGlobalKeydown)
  window.addEventListener('click', handleGlobalClick)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleGlobalKeydown)
  window.removeEventListener('click', handleGlobalClick)
})

function getItemFull(itemId?: number) {
  if (!itemId) return null
  return fullItemMap.value.get(itemId) || null
}

function calculateTooltipPos(mouseX: number, mouseY: number) {
  const cardWidth = 368
  const cardHeight = 360
  const padding = 12

  let targetX = mouseX + 16
  let targetY = mouseY + 14

  if (targetX + cardWidth > window.innerWidth - padding) {
    targetX = mouseX - cardWidth - 14
  }
  if (targetX < padding) {
    targetX = padding
  }

  const viewportH = window.innerHeight
  if (targetY + cardHeight > viewportH - padding) {
    const flippedY = mouseY - cardHeight - 12
    if (flippedY >= padding) {
      targetY = flippedY
    } else {
      targetY = Math.max(padding, viewportH - cardHeight - padding)
    }
  }

  tooltipPosition.value = { x: targetX, y: targetY }
}

function onHoverItem(itemId?: number, event?: MouseEvent) {
  if (pinnedItem.value || !itemId) return
  const found = getItemFull(itemId)
  if (!found) return
  hoveredItem.value = found
  if (event) {
    calculateTooltipPos(event.clientX, event.clientY)
  }
}

function onLeaveItem() {
  if (pinnedItem.value) return
  hoveredItem.value = null
}

function onPinItem(itemId?: number, event?: MouseEvent) {
  if (event) event.stopPropagation()
  if (!itemId) return
  const found = getItemFull(itemId)
  if (!found) return
  if (pinnedItem.value?.key === found.key) {
    pinnedItem.value = null
  } else {
    pinnedItem.value = found
    hoveredItem.value = null
    if (event) {
      calculateTooltipPos(event.clientX, event.clientY)
    }
  }
}

function unpinItem() {
  pinnedItem.value = null
}

function handleGlobalKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && pinnedItem.value) {
    pinnedItem.value = null
  }
}

function handleGlobalClick() {
  if (pinnedItem.value) {
    pinnedItem.value = null
  }
}

function getHeroName(heroId: number): string {
  return getHeroChineseName(heroId) || `英雄 #${heroId}`
}

function getItemMeta(itemId?: number): ItemMeta | undefined {
  if (!itemId || itemId === 0) return undefined
  return props.itemDict[itemId]
}

function formatDuration(sec: number): string {
  const m = Math.floor(sec / 60)
  const s = sec % 60
  return `${m}分${s.toString().padStart(2, '0')}秒`
}

function formatMatchDateTime(epochSec: number): string {
  if (!epochSec) return '-'
  const d = new Date(epochSec * 1000)
  return d.toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}

function getMatchGameMode(gameMode: number, lobbyType: number): string {
  if (lobbyType === 7) return '天梯匹配'
  if (lobbyType === 0) return '普通匹配'
  if (gameMode === 22) return '全英雄选择'
  if (gameMode === 23) return '加速模式'
  if (gameMode === 2) return '队长模式'
  return '标准比赛模式'
}

function getPlayerKp(player: OpenDotaPlayer, match: OpenDotaMatchDetail): number {
  const isRadiant = player.player_slot < 128
  const teamKills = isRadiant ? match.radiant_score : match.dire_score
  if (!teamKills || teamKills <= 0) {
    const players = match.players || []
    const teamTotal = players
      .filter((p: OpenDotaPlayer) => (p.player_slot < 128) === isRadiant)
      .reduce((sum: number, p: OpenDotaPlayer) => sum + (p.kills || 0), 0)
    if (teamTotal <= 0) return 0
    return Math.round(((player.kills + player.assists) / teamTotal) * 100)
  }
  return Math.round(((player.kills + player.assists) / teamKills) * 100)
}

function formatNum(num?: number): string {
  if (!num) return '0'
  if (num >= 10000) return `${(num / 1000).toFixed(1)}k`
  return String(num)
}

function formatDamage(dmg?: number): string {
  if (!dmg) return '0'
  if (dmg >= 1000) return `${(dmg / 1000).toFixed(1)}k`
  return String(dmg)
}

function isBoundPlayer(player: OpenDotaPlayer): boolean {
  if (!props.boundPlayerId) return false
  return String(player.account_id) === String(props.boundPlayerId)
}

/** 复制比赛 ID 到剪贴板 (双击或单击图标) */
async function copyMatchId() {
  if (!props.match.match_id) return
  try {
    await navigator.clipboard.writeText(String(props.match.match_id))
    matchIdCopied.value = true
    setTimeout(() => {
      matchIdCopied.value = false
    }, 2000)
  } catch {
    // 降级使用传统模式
    const ta = document.createElement('textarea')
    ta.value = String(props.match.match_id)
    document.body.appendChild(ta)
    ta.select()
    document.execCommand('copy')
    document.body.removeChild(ta)
    matchIdCopied.value = true
    setTimeout(() => {
      matchIdCopied.value = false
    }, 2000)
  }
}

/** 团队总伤害，用于计算伤害占比 */
const radiantTotalDmg = computed(() => {
  return (props.match.players || [])
    .filter(p => p.player_slot < 128)
    .reduce((sum, p) => sum + (p.hero_damage || 0), 0) || 1
})

const direTotalDmg = computed(() => {
  return (props.match.players || [])
    .filter(p => p.player_slot >= 128)
    .reduce((sum, p) => sum + (p.hero_damage || 0), 0) || 1
})

function getDamageShare(p: OpenDotaPlayer): number {
  const isRadiant = p.player_slot < 128
  const total = isRadiant ? radiantTotalDmg.value : direTotalDmg.value
  return Math.min(100, Math.round(((p.hero_damage || 0) / total) * 100))
}

/** 复制复盘报告 */
async function handleCopyReview() {
  if (!props.reviewResult?.fullText) return
  try {
    await navigator.clipboard.writeText(props.reviewResult.fullText)
    copied.value = true
    setTimeout(() => {
      copied.value = false
    }, 2500)
  } catch {
    // 忽略
  }
}
</script>

<template>
  <div class="select-none space-y-4">
    <!-- 1. 紧凑型比赛战报 Header (返回按钮 + 比赛ID双击复制 + 模式/时间/比分统合卡片) -->
    <div class="p-3.5 border border-slate-700/80 rounded-xl bg-slate-900/90 flex flex-wrap gap-4 shadow-lg items-center justify-between">
      <!-- 左侧：返回按钮 + 比赛ID (双击复制) + 胜负/模式 Badge -->
      <div class="flex flex-wrap gap-3 items-center">
        <button
          class="text-xs text-slate-300 font-bold px-3 py-1.5 border border-slate-700 rounded-lg bg-slate-800/90 flex cursor-pointer transition-all items-center space-x-1.5 hover:text-amber-300 hover:border-amber-500/50 hover:bg-slate-800"
          title="返回近期比赛列表"
          @click="emit('backToList')"
        >
          <ArrowLeft class="h-4 w-4" />
          <span>返回列表</span>
        </button>

        <div class="bg-slate-800 h-4 w-px hidden sm:block" />

        <!-- 比赛 ID (支持双击复制与单击图标复制，绝对定位防抖动气泡) -->
        <div
          class="group px-2.5 py-1 border border-slate-800 rounded-lg bg-slate-950/80 flex cursor-pointer transition-all items-center relative space-x-1.5 hover:border-amber-500/50"
          title="双击或点击复制比赛 ID"
          @dblclick="copyMatchId"
          @click="copyMatchId"
        >
          <span class="text-xs text-slate-400 font-mono">比赛 #</span>
          <span class="text-sm text-slate-100 font-bold font-mono group-hover:text-amber-300">
            {{ match.match_id }}
          </span>
          <Check v-if="matchIdCopied" class="text-emerald-400 ml-0.5 shrink-0 h-3.5 w-3.5" />
          <Copy v-else class="text-slate-500 ml-0.5 shrink-0 h-3.5 w-3.5 group-hover:text-amber-400" />

          <!-- 悬浮无尺寸开销的复制成功气泡，杜绝父容器重排抖动 -->
          <transition
            enter-active-class="transition duration-150 ease-out"
            enter-from-class="opacity-0 -translate-y-1"
            enter-to-class="opacity-100 translate-y-0"
            leave-active-class="transition duration-150 ease-in"
            leave-from-class="opacity-100"
            leave-to-class="opacity-0"
          >
            <div
              v-if="matchIdCopied"
              class="text-3xs text-emerald-300 font-mono px-2 py-0.5 border border-emerald-500 rounded bg-emerald-950 flex gap-1 pointer-events-none whitespace-nowrap shadow-lg items-center left-1/2 absolute z-30 -translate-x-1/2 -top-7"
            >
              <span>已复制</span>
            </div>
          </transition>
        </div>

        <!-- 获胜方 Badge -->
        <span
          class="text-xs font-black px-2.5 py-1 rounded-full shadow-sm"
          :class="match.radiant_win
            ? 'bg-emerald-950 text-emerald-300 border border-emerald-600 shadow-emerald-500/10'
            : 'bg-rose-950 text-rose-300 border border-rose-600 shadow-rose-500/10'"
        >
          {{ match.radiant_win ? '天辉获胜' : '夜魇获胜' }}
        </span>

        <!-- 比赛模式 -->
        <span class="text-2xs text-slate-300 font-mono px-2 py-0.5 border border-slate-700 rounded bg-slate-800">
          {{ getMatchGameMode(match.game_mode, match.lobby_type) }}
        </span>
      </div>

      <!-- 右侧：总比分、开赛时间与比赛时长 (紧凑整洁) -->
      <div class="text-xs text-slate-400 font-mono flex flex-wrap gap-4 items-center">
        <!-- 总比分 -->
        <div class="px-2.5 py-1 border border-slate-800 rounded-lg bg-slate-950/60 flex items-center space-x-1">
          <span class="text-2xs text-slate-500">总比分:</span>
          <b class="text-sm text-emerald-400 font-bold">{{ match.radiant_score ?? '-' }}</b>
          <span class="text-slate-600 mx-0.5">:</span>
          <b class="text-sm text-rose-400 font-bold">{{ match.dire_score ?? '-' }}</b>
        </div>

        <!-- 开赛时间 -->
        <span class="text-slate-400 hidden items-center space-x-1 md:inline-flex">
          <span class="text-slate-500">时间:</span>
          <span class="text-slate-300">{{ formatMatchDateTime(match.start_time) }}</span>
        </span>

        <!-- 比赛时长 -->
        <span class="text-slate-300 flex items-center space-x-1">
          <Clock class="text-amber-400 h-3.5 w-3.5" />
          <span class="font-bold">{{ formatDuration(match.duration || 0) }}</span>
        </span>

        <!-- 首杀产生时间 -->
        <span v-if="match.first_blood_time" class="text-slate-400 hidden lg:inline">
          首杀: <b class="text-amber-400">{{ formatDuration(match.first_blood_time) }}</b>
        </span>
      </div>
    </div>

    <!-- 2. AI 赛后战术深度复盘中心 (无思维链概念，专注复盘已绑定选手) -->
    <div class="p-4 border border-amber-500/30 rounded-2xl shadow-xl from-slate-900 to-slate-950 via-slate-900 bg-gradient-to-b space-y-3.5">
      <div class="flex flex-col gap-3 justify-between sm:flex-row sm:items-center">
        <div class="flex items-center space-x-3">
          <div class="p-2 border border-amber-500/40 rounded-xl bg-amber-500/10">
            <Cpu class="text-amber-400 h-5 w-5" />
          </div>
          <div>
            <div class="flex items-center space-x-2">
              <h3 class="text-sm text-amber-400 font-extrabold">
                AI 赛后战术研判与深度复盘
              </h3>
              <span class="text-3xs text-emerald-400 font-mono px-1.5 py-0.2 border border-emerald-500/40 rounded bg-emerald-500/10">
                专属选手战备视角
              </span>
            </div>
            <p class="text-2xs text-slate-400 mt-0.5">
              自动针对您绑定的选手账号进行专属实战研判，剖析胜负分水岭、出装克制与下局提升路线
            </p>
          </div>
        </div>

        <!-- 启动 AI 复盘按钮 (未配置 AI 时直接不可用，绝不规则伪造) -->
        <div class="flex flex-col items-end">
          <button
            class="text-xs font-black px-5 py-2.5 rounded-xl flex shrink-0 shadow-lg transition-all items-center justify-center space-x-2 active:scale-98"
            :class="[
              reviewing || !aiReviewStatus.available
                ? 'bg-slate-800 text-slate-400 border border-slate-700/80 cursor-not-allowed opacity-75'
                : 'text-slate-950 from-amber-400 to-amber-600 via-amber-500 bg-gradient-to-r cursor-pointer hover:from-amber-300 hover:to-amber-500 shadow-amber-500/20',
            ]"
            :disabled="reviewing || !aiReviewStatus.available"
            :title="!aiReviewStatus.available ? (aiReviewStatus.reason || 'AI 引擎未配置') : '生成针对性 AI 复盘报告'"
            @click="emit('generateReview')"
          >
            <Activity v-if="reviewing" class="h-4 w-4 animate-spin" />
            <Sparkles v-else class="h-4 w-4" />
            <span>
              {{
                reviewing
                  ? 'AI 正在深度解析战报...'
                  : !aiReviewStatus.available
                    ? 'AI 复盘不可用 (未配置 AI 引擎)'
                    : '生成针对性 AI 复盘报告'
              }}
            </span>
          </button>
          <span v-if="!aiReviewStatus.available" class="text-3xs text-slate-500 font-mono mt-1 text-right">
            {{ aiReviewStatus.reason || '请在系统设置中启用并配置大模型' }}
          </span>
        </div>
      </div>

      <!-- AI 复盘报告结果展示区 -->
      <div v-if="reviewResult" class="pt-3 border-t border-slate-800/80 space-y-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center space-x-2">
            <Sparkles class="text-amber-400 h-4 w-4" />
            <h4 class="text-xs text-amber-400 font-bold">
              AI 智能深度复盘 · 战术评估报告
            </h4>
            <span class="text-2xs text-slate-500 font-mono">模型: {{ reviewResult.modelUsed }}</span>
          </div>

          <button
            class="text-2xs text-slate-200 px-2.5 py-1 border border-slate-700 rounded-lg bg-slate-800 flex cursor-pointer transition-colors items-center space-x-1 hover:bg-slate-700"
            @click="handleCopyReview"
          >
            <Check v-if="copied" class="text-emerald-400 h-3 w-3" />
            <Copy v-else class="h-3 w-3" />
            <span>{{ copied ? '已复制报告' : '复制复盘文本' }}</span>
          </button>
        </div>

        <!-- 4 宫格结构化复盘卡片 -->
        <div class="text-xs leading-relaxed gap-3 grid grid-cols-1 md:grid-cols-2">
          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/80 space-y-1">
            <div class="text-slate-200 font-bold flex items-center space-x-1.5">
              <span class="rounded-full bg-amber-400 h-1.5 w-1.5" />
              <span>大盘战略与胜负分水岭</span>
            </div>
            <p class="text-slate-300">{{ reviewResult.overview }}</p>
          </div>

          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/80 space-y-1">
            <div class="text-slate-200 font-bold flex items-center space-x-1.5">
              <span class="rounded-full bg-cyan-400 h-1.5 w-1.5" />
              <span>核心视角实战得失剖析</span>
            </div>
            <p class="text-slate-300">{{ reviewResult.keyTurningPoints }}</p>
          </div>

          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/80 space-y-1">
            <div class="text-slate-200 font-bold flex items-center space-x-1.5">
              <span class="rounded-full bg-emerald-400 h-1.5 w-1.5" />
              <span>出装针对性推演与克制</span>
            </div>
            <p class="text-slate-300">{{ reviewResult.itemizationAnalysis }}</p>
          </div>

          <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/80 space-y-1">
            <div class="text-slate-200 font-bold flex items-center space-x-1.5">
              <span class="rounded-full bg-rose-400 h-1.5 w-1.5" />
              <span>下局致胜核心提升指南</span>
            </div>
            <ul class="text-slate-300 list-disc list-inside space-y-0.5">
              <li v-for="(act, idx) in reviewResult.nextMatchActions" :key="idx">
                {{ act }}
              </li>
            </ul>
          </div>
        </div>
      </div>
    </div>

    <!-- 3. 多维战况数据库选项卡 (4 大专业 Tab) -->
    <div class="space-y-4">
      <!-- Tab 切换按钮组 -->
      <div class="pb-2 border-b border-slate-800 flex flex-wrap gap-2 items-center justify-between">
        <div class="flex flex-wrap gap-1.5 items-center">
          <button
            class="text-xs font-bold px-3 py-1.5 rounded-lg flex cursor-pointer transition-all items-center space-x-1.5"
            :class="activeTab === 'overview'
              ? 'bg-amber-500 text-slate-950 shadow-md'
              : 'text-slate-400 bg-slate-900 border border-slate-800 hover:text-slate-200 hover:bg-slate-800'"
            @click="activeTab = 'overview'"
          >
            <LayoutGrid class="h-3.5 w-3.5" />
            <span>阵容与装备大盘</span>
          </button>

          <button
            class="text-xs font-bold px-3 py-1.5 rounded-lg flex cursor-pointer transition-all items-center space-x-1.5"
            :class="activeTab === 'advantage'
              ? 'bg-cyan-500 text-slate-950 shadow-md'
              : 'text-slate-400 bg-slate-900 border border-slate-800 hover:text-slate-200 hover:bg-slate-800'"
            @click="activeTab = 'advantage'"
          >
            <TrendingUp class="h-3.5 w-3.5" />
            <span>团队战力与经济走势</span>
          </button>

          <button
            class="text-xs font-bold px-3 py-1.5 rounded-lg flex cursor-pointer transition-all items-center space-x-1.5"
            :class="activeTab === 'advanced'
              ? 'bg-emerald-500 text-slate-950 shadow-md'
              : 'text-slate-400 bg-slate-900 border border-slate-800 hover:text-slate-200 hover:bg-slate-800'"
            @click="activeTab = 'advanced'"
          >
            <Eye class="h-3.5 w-3.5" />
            <span>高阶攻防与视野贡献</span>
          </button>

          <button
            class="text-xs font-bold px-3 py-1.5 rounded-lg flex cursor-pointer transition-all items-center space-x-1.5"
            :class="activeTab === 'draft'
              ? 'bg-purple-500 text-slate-950 shadow-md'
              : 'text-slate-400 bg-slate-900 border border-slate-800 hover:text-slate-200 hover:bg-slate-800'"
            @click="activeTab = 'draft'"
          >
            <Shield class="h-3.5 w-3.5" />
            <span>BP 禁选与加点时序</span>
          </button>
        </div>

        <span class="text-2xs text-slate-500 font-mono">
          十人全景数据已对齐官方协议 · 支持横向滚动浏览
        </span>
      </div>

      <!-- Tab 1: 【阵容与装备大盘】(对齐 OpenDota 表格规范，防长昵称错乱，头像统一放大) -->
      <div v-show="activeTab === 'overview'" class="space-y-4">
        <!-- 天辉方阵营卡片 -->
        <div class="border border-emerald-900/40 rounded-xl bg-emerald-950/15 shadow-lg overflow-hidden">
          <div class="px-4 py-2 border-b border-emerald-900/30 bg-emerald-950/40 flex items-center justify-between">
            <div class="flex items-center space-x-2">
              <Trophy class="text-emerald-400 h-4 w-4" />
              <span class="text-xs text-emerald-400 tracking-wide font-extrabold">天辉阵营 (The Radiant)</span>
              <span v-if="match.radiant_win" class="text-2xs text-emerald-300 font-bold px-1.5 py-0.2 border border-emerald-500/40 rounded bg-emerald-500/20">获胜方</span>
            </div>
            <span class="text-2xs text-emerald-300/80 font-mono">总比分: {{ match.radiant_score ?? '-' }} 击杀</span>
          </div>

          <!-- 横向滚动表格容器 (对齐 OpenDota，单行不换行) -->
          <div class="overflow-x-auto">
            <table class="font-mono text-left min-w-[1260px] w-full whitespace-nowrap border-collapse">
              <thead>
                <tr class="text-2xs text-slate-400 border-b border-emerald-900/20 bg-slate-950/40">
                  <th class="font-bold px-3 py-2.5 w-56">选手 / 英雄</th>
                  <th class="font-bold px-2 py-2.5 text-center w-14">等级</th>
                  <th class="font-bold px-3 py-2.5 text-center w-36">K / D / A (KP%)</th>
                  <th class="font-bold px-3 py-2.5 text-right w-28">净资产 (NET)</th>
                  <th class="font-bold px-3 py-2.5 text-right w-32">GPM / XPM</th>
                  <th class="font-bold px-2 py-2.5 text-right w-28">正 / 反补</th>
                  <th class="font-bold px-3 py-2.5 text-right w-32">英雄伤害</th>
                  <th class="font-bold px-3 py-2.5 text-right w-32">推塔 / 治疗</th>
                  <th class="font-bold px-3 py-2.5 min-w-[380px]">装备与物品体系 (悬浮查看)</th>
                </tr>
              </thead>
              <tbody class="text-xs divide-slate-800/40 divide-y">
                <tr
                  v-for="p in match.players.slice(0, 5)"
                  :key="`rad-row-${p.player_slot}`"
                  class="cursor-pointer transition-colors hover:bg-emerald-950/30"
                  :class="selectedPlayerSlot === p.player_slot ? 'bg-amber-500/15' : 'bg-slate-950/30'"
                  @click="emit('selectSlot', p.player_slot)"
                >
                  <!-- 1. 英雄大头像与选手昵称 (固定宽度，防长ID撑开) -->
                  <td class="px-3 py-2">
                    <div class="flex w-52 items-center space-x-3">
                      <!-- 英雄大头像 (放大至 h-13 w-20 更加醒目) -->
                      <div class="border border-slate-700/80 rounded-lg bg-slate-950 shrink-0 h-13 w-20 shadow-sm relative overflow-hidden">
                        <SmartImage
                          :local-src="getHeroIconUrl(String(p.hero_id))"
                          :cdn-src="getHeroCdnUrl(String(p.hero_id))"
                          :alt="getHeroName(p.hero_id)"
                          class="h-full w-full object-cover"
                        />
                        <span class="text-3xs text-amber-300 font-bold font-mono px-1 rounded-tl bg-slate-950/90 bottom-0 right-0 absolute">
                          Lv.{{ p.level || '-' }}
                        </span>
                      </div>

                      <div class="flex-1 min-w-0">
                        <div class="flex items-center space-x-1.5">
                          <span class="text-xs text-slate-100 font-bold truncate">{{ getHeroName(p.hero_id) }}</span>
                          <span v-if="isBoundPlayer(p)" class="text-3xs text-amber-300 font-mono px-1 py-0.2 border border-amber-500/40 rounded bg-amber-500/20 shrink-0">您操纵</span>
                        </div>
                        <div class="text-2xs text-slate-400 font-mono mt-0.5 max-w-32 truncate" :title="p.personaname || `选手 #${p.player_slot}`">
                          {{ p.personaname || `选手 #${p.player_slot}` }}
                        </div>
                      </div>
                    </div>
                  </td>

                  <!-- 2. 等级 -->
                  <td class="text-amber-300 font-bold px-2 py-2 text-center">
                    {{ p.level || '-' }}
                  </td>

                  <!-- 3. K / D / A 与参战率 -->
                  <td class="px-3 py-2 text-center">
                    <div class="font-bold">
                      <span class="text-emerald-400">{{ p.kills }}</span>
                      <span class="text-slate-600 mx-0.5">/</span>
                      <span class="text-rose-400">{{ p.deaths }}</span>
                      <span class="text-slate-600 mx-0.5">/</span>
                      <span class="text-amber-400">{{ p.assists }}</span>
                    </div>
                    <div class="text-3xs text-slate-400">
                      参战率 <b class="text-slate-300">{{ getPlayerKp(p, match) }}%</b>
                    </div>
                  </td>

                  <!-- 4. 净资产 (NET) -->
                  <td class="px-3 py-2 text-right">
                    <div class="text-amber-400 font-extrabold flex items-center justify-end space-x-1">
                      <Coins class="text-amber-400 h-3 w-3" />
                      <span>{{ formatNum(p.net_worth || p.total_gold) }}</span>
                    </div>
                  </td>

                  <!-- 5. GPM / XPM -->
                  <td class="px-3 py-2 text-right">
                    <div>
                      <span class="text-amber-300/90 font-bold">{{ p.gold_per_min || 0 }}</span>
                      <span class="text-3xs text-slate-500 mx-1">G</span>
                      <span class="text-cyan-300/90 font-bold">{{ p.xp_per_min || 0 }}</span>
                      <span class="text-3xs text-slate-500 ml-1">X</span>
                    </div>
                  </td>

                  <!-- 6. 正/反补 -->
                  <td class="text-slate-300 px-2 py-2 text-right">
                    {{ p.last_hits || 0 }} / {{ p.denies || 0 }}
                  </td>

                  <!-- 7. 英雄伤害 -->
                  <td class="px-3 py-2 text-right">
                    <div class="text-slate-100 font-bold">
                      {{ formatDamage(p.hero_damage) }}
                    </div>
                    <div class="text-3xs text-slate-400">
                      占比 {{ getDamageShare(p) }}%
                    </div>
                  </td>

                  <!-- 8. 推塔与治疗 -->
                  <td class="px-3 py-2 text-right">
                    <div class="text-amber-400/90" title="推进防御塔伤害">
                      {{ formatDamage(p.tower_damage) }}
                    </div>
                    <div v-if="p.hero_healing" class="text-3xs text-emerald-400" title="英雄治疗量">
                      +{{ formatDamage(p.hero_healing) }}
                    </div>
                  </td>

                  <!-- 9. 装备与物品体系 (6 主装 + 3 背包 + 1 中立 + A杖魔晶，支持悬浮卡片) -->
                  <td class="px-3 py-2">
                    <div class="flex items-center space-x-2">
                      <!-- 6 格主装 -->
                      <div class="p-0.5 border border-slate-800 rounded bg-slate-950/80 shrink-0 gap-1 grid grid-cols-6">
                        <div
                          v-for="(itemId, idx) in [p.item_0, p.item_1, p.item_2, p.item_3, p.item_4, p.item_5]"
                          :key="`rad-m-${p.player_slot}-${idx}`"
                          class="border rounded-[2px] bg-slate-900 flex h-6 w-8 cursor-pointer transition-all items-center justify-center relative overflow-hidden hover:border-amber-400"
                          :class="[
                            pinnedItem && getItemFull(itemId) && pinnedItem.key === getItemFull(itemId).key
                              ? 'border-amber-400 ring-1 ring-amber-400/60 shadow-[0_0_8px_rgba(251,191,36,0.6)]'
                              : 'border-slate-800/80',
                          ]"
                          @mouseenter="onHoverItem(itemId, $event)"
                          @mouseleave="onLeaveItem"
                          @click="onPinItem(itemId, $event)"
                        >
                          <SmartImage
                            v-if="getItemMeta(itemId)"
                            :local-src="getItemIconUrl(getItemMeta(itemId)!.key)"
                            :cdn-src="getItemCdnUrl(getItemMeta(itemId)!.key)"
                            :alt="getItemMeta(itemId)!.nameLoc"
                            class="h-full w-full object-cover"
                          />
                        </div>
                      </div>

                      <!-- 3 格背包物品 (Backpack) -->
                      <div class="p-0.5 border border-slate-800/80 rounded bg-slate-950/60 shrink-0 gap-0.5 grid grid-cols-3" title="背包物品 (3格)">
                        <div
                          v-for="(bpId, bIdx) in [p.backpack_0, p.backpack_1, p.backpack_2]"
                          :key="`rad-bp-${p.player_slot}-${bIdx}`"
                          class="border rounded-[2px] bg-slate-900/50 flex h-5.5 w-6.5 cursor-pointer transition-all items-center justify-center relative overflow-hidden hover:border-amber-400"
                          :class="[
                            pinnedItem && getItemFull(bpId) && pinnedItem.key === getItemFull(bpId).key
                              ? 'border-amber-400 ring-1 ring-amber-400/60 shadow-[0_0_8px_rgba(251,191,36,0.6)]'
                              : 'border-slate-800/50',
                          ]"
                          @mouseenter="onHoverItem(bpId, $event)"
                          @mouseleave="onLeaveItem"
                          @click="onPinItem(bpId, $event)"
                        >
                          <SmartImage
                            v-if="getItemMeta(bpId)"
                            :local-src="getItemIconUrl(getItemMeta(bpId)!.key)"
                            :cdn-src="getItemCdnUrl(getItemMeta(bpId)!.key)"
                            :alt="getItemMeta(bpId)!.nameLoc"
                            class="opacity-70 h-full w-full object-cover"
                          />
                          <span v-else class="text-3xs text-slate-700">·</span>
                        </div>
                      </div>

                      <!-- 1 格中立装备 (带属性加成卡片) -->
                      <div
                        class="p-0.5 border rounded-full bg-cyan-950/30 flex shrink-0 h-6 w-8 cursor-pointer transition-all items-center justify-center relative overflow-hidden hover:border-cyan-400"
                        :class="[
                          pinnedItem && getItemFull(p.item_neutral) && pinnedItem.key === getItemFull(p.item_neutral).key
                            ? 'border-cyan-400 ring-1 ring-cyan-400/60 shadow-[0_0_8px_rgba(34,211,238,0.6)]'
                            : 'border-cyan-800/60',
                        ]"
                        @mouseenter="onHoverItem(p.item_neutral, $event)"
                        @mouseleave="onLeaveItem"
                        @click="onPinItem(p.item_neutral, $event)"
                      >
                        <SmartImage
                          v-if="getItemMeta(p.item_neutral)"
                          :local-src="getItemIconUrl(getItemMeta(p.item_neutral)!.key)"
                          :cdn-src="getItemCdnUrl(getItemMeta(p.item_neutral)!.key)"
                          :alt="getItemMeta(p.item_neutral)!.nameLoc"
                          class="rounded-full h-full w-full object-cover"
                        />
                        <span v-else class="text-3xs text-slate-600 font-mono">-</span>
                      </div>

                      <!-- A杖与魔晶状态图标 (使用用户增加的 scepter_0/1 与 shard_0/1) -->
                      <div class="pl-1 flex shrink-0 items-center space-x-1.5">
                        <img
                          :src="p.aghanims_scepter === 1 ? '/assets/icons/scepter_1.png' : '/assets/icons/scepter_0.png'"
                          alt="A杖"
                          class="h-5 w-5 object-contain"
                          :title="p.aghanims_scepter === 1 ? '阿哈利姆神杖: 已购买' : '阿哈利姆神杖: 未购买'"
                        />
                        <img
                          :src="p.aghanims_shard === 1 ? '/assets/icons/shard_1.png' : '/assets/icons/shard_0.png'"
                          alt="魔晶"
                          class="h-5 w-5 object-contain"
                          :title="p.aghanims_shard === 1 ? '阿哈利姆魔晶: 已购买' : '阿哈利姆魔晶: 未购买'"
                        />
                      </div>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- 夜魇方阵营卡片 -->
        <div class="border border-rose-900/40 rounded-xl bg-rose-950/15 shadow-lg overflow-hidden">
          <div class="px-4 py-2 border-b border-rose-900/30 bg-rose-950/40 flex items-center justify-between">
            <div class="flex items-center space-x-2">
              <Flame class="text-rose-400 h-4 w-4" />
              <span class="text-xs text-rose-400 tracking-wide font-extrabold">夜魇阵营 (The Dire)</span>
              <span v-if="!match.radiant_win" class="text-2xs text-rose-300 font-bold px-1.5 py-0.2 border border-rose-500/40 rounded bg-rose-500/20">获胜方</span>
            </div>
            <span class="text-2xs text-rose-300/80 font-mono">总比分: {{ match.dire_score ?? '-' }} 击杀</span>
          </div>

          <!-- 横向滚动表格容器 (对齐 OpenDota，单行不换行) -->
          <div class="overflow-x-auto">
            <table class="font-mono text-left min-w-[1260px] w-full whitespace-nowrap border-collapse">
              <thead>
                <tr class="text-2xs text-slate-400 border-b border-rose-900/20 bg-slate-950/40">
                  <th class="font-bold px-3 py-2.5 w-56">选手 / 英雄</th>
                  <th class="font-bold px-2 py-2.5 text-center w-14">等级</th>
                  <th class="font-bold px-3 py-2.5 text-center w-36">K / D / A (KP%)</th>
                  <th class="font-bold px-3 py-2.5 text-right w-28">净资产 (NET)</th>
                  <th class="font-bold px-3 py-2.5 text-right w-32">GPM / XPM</th>
                  <th class="font-bold px-2 py-2.5 text-right w-28">正 / 反补</th>
                  <th class="font-bold px-3 py-2.5 text-right w-32">英雄伤害</th>
                  <th class="font-bold px-3 py-2.5 text-right w-32">推塔 / 治疗</th>
                  <th class="font-bold px-3 py-2.5 min-w-[380px]">装备与物品体系 (悬浮查看)</th>
                </tr>
              </thead>
              <tbody class="text-xs divide-slate-800/40 divide-y">
                <tr
                  v-for="p in match.players.slice(5, 10)"
                  :key="`dire-row-${p.player_slot}`"
                  class="cursor-pointer transition-colors hover:bg-rose-950/30"
                  :class="selectedPlayerSlot === p.player_slot ? 'bg-amber-500/15' : 'bg-slate-950/30'"
                  @click="emit('selectSlot', p.player_slot)"
                >
                  <!-- 1. 英雄大头像与选手昵称 (固定宽度，防长ID撑开) -->
                  <td class="px-3 py-2">
                    <div class="flex w-52 items-center space-x-3">
                      <!-- 英雄大头像 (放大至 h-13 w-20 更加醒目) -->
                      <div class="border border-slate-700/80 rounded-lg bg-slate-950 shrink-0 h-13 w-20 shadow-sm relative overflow-hidden">
                        <SmartImage
                          :local-src="getHeroIconUrl(String(p.hero_id))"
                          :cdn-src="getHeroCdnUrl(String(p.hero_id))"
                          :alt="getHeroName(p.hero_id)"
                          class="h-full w-full object-cover"
                        />
                        <span class="text-3xs text-amber-300 font-bold font-mono px-1 rounded-tl bg-slate-950/90 bottom-0 right-0 absolute">
                          Lv.{{ p.level || '-' }}
                        </span>
                      </div>

                      <div class="flex-1 min-w-0">
                        <div class="flex items-center space-x-1.5">
                          <span class="text-xs text-slate-100 font-bold truncate">{{ getHeroName(p.hero_id) }}</span>
                          <span v-if="isBoundPlayer(p)" class="text-3xs text-amber-300 font-mono px-1 py-0.2 border border-amber-500/40 rounded bg-amber-500/20 shrink-0">您操纵</span>
                        </div>
                        <div class="text-2xs text-slate-400 font-mono mt-0.5 max-w-32 truncate" :title="p.personaname || `选手 #${p.player_slot}`">
                          {{ p.personaname || `选手 #${p.player_slot}` }}
                        </div>
                      </div>
                    </div>
                  </td>

                  <!-- 2. 等级 -->
                  <td class="text-amber-300 font-bold px-2 py-2 text-center">
                    {{ p.level || '-' }}
                  </td>

                  <!-- 3. K / D / A 与参战率 -->
                  <td class="px-3 py-2 text-center">
                    <div class="font-bold">
                      <span class="text-emerald-400">{{ p.kills }}</span>
                      <span class="text-slate-600 mx-0.5">/</span>
                      <span class="text-rose-400">{{ p.deaths }}</span>
                      <span class="text-slate-600 mx-0.5">/</span>
                      <span class="text-amber-400">{{ p.assists }}</span>
                    </div>
                    <div class="text-3xs text-slate-400">
                      参战率 <b class="text-slate-300">{{ getPlayerKp(p, match) }}%</b>
                    </div>
                  </td>

                  <!-- 4. 净资产 (NET) -->
                  <td class="px-3 py-2 text-right">
                    <div class="text-amber-400 font-extrabold flex items-center justify-end space-x-1">
                      <Coins class="text-amber-400 h-3 w-3" />
                      <span>{{ formatNum(p.net_worth || p.total_gold) }}</span>
                    </div>
                  </td>

                  <!-- 5. GPM / XPM -->
                  <td class="px-3 py-2 text-right">
                    <div>
                      <span class="text-amber-300/90 font-bold">{{ p.gold_per_min || 0 }}</span>
                      <span class="text-3xs text-slate-500 mx-1">G</span>
                      <span class="text-cyan-300/90 font-bold">{{ p.xp_per_min || 0 }}</span>
                      <span class="text-3xs text-slate-500 ml-1">X</span>
                    </div>
                  </td>

                  <!-- 6. 正/反补 -->
                  <td class="text-slate-300 px-2 py-2 text-right">
                    {{ p.last_hits || 0 }} / {{ p.denies || 0 }}
                  </td>

                  <!-- 7. 英雄伤害 -->
                  <td class="px-3 py-2 text-right">
                    <div class="text-slate-100 font-bold">
                      {{ formatDamage(p.hero_damage) }}
                    </div>
                    <div class="text-3xs text-slate-400">
                      占比 {{ getDamageShare(p) }}%
                    </div>
                  </td>

                  <!-- 8. 推塔与治疗 -->
                  <td class="px-3 py-2 text-right">
                    <div class="text-amber-400/90" title="推进防御塔伤害">
                      {{ formatDamage(p.tower_damage) }}
                    </div>
                    <div v-if="p.hero_healing" class="text-3xs text-emerald-400" title="英雄治疗量">
                      +{{ formatDamage(p.hero_healing) }}
                    </div>
                  </td>

                  <!-- 9. 装备与物品体系 (6 主装 + 3 背包 + 1 中立 + A杖魔晶，支持悬浮卡片) -->
                  <td class="px-3 py-2">
                    <div class="flex items-center space-x-2">
                      <!-- 6 格主装 -->
                      <div class="p-0.5 border border-slate-800 rounded bg-slate-950/80 shrink-0 gap-1 grid grid-cols-6">
                        <div
                          v-for="(itemId, idx) in [p.item_0, p.item_1, p.item_2, p.item_3, p.item_4, p.item_5]"
                          :key="`dire-m-${p.player_slot}-${idx}`"
                          class="border rounded-[2px] bg-slate-900 flex h-6 w-8 cursor-pointer transition-all items-center justify-center relative overflow-hidden hover:border-amber-400"
                          :class="[
                            pinnedItem && getItemFull(itemId) && pinnedItem.key === getItemFull(itemId).key
                              ? 'border-amber-400 ring-1 ring-amber-400/60 shadow-[0_0_8px_rgba(251,191,36,0.6)]'
                              : 'border-slate-800/80',
                          ]"
                          @mouseenter="onHoverItem(itemId, $event)"
                          @mouseleave="onLeaveItem"
                          @click="onPinItem(itemId, $event)"
                        >
                          <SmartImage
                            v-if="getItemMeta(itemId)"
                            :local-src="getItemIconUrl(getItemMeta(itemId)!.key)"
                            :cdn-src="getItemCdnUrl(getItemMeta(itemId)!.key)"
                            :alt="getItemMeta(itemId)!.nameLoc"
                            class="h-full w-full object-cover"
                          />
                        </div>
                      </div>

                      <!-- 3 格背包物品 (Backpack) -->
                      <div class="p-0.5 border border-slate-800/80 rounded bg-slate-950/60 shrink-0 gap-0.5 grid grid-cols-3" title="背包物品 (3格)">
                        <div
                          v-for="(bpId, bIdx) in [p.backpack_0, p.backpack_1, p.backpack_2]"
                          :key="`dire-bp-${p.player_slot}-${bIdx}`"
                          class="border rounded-[2px] bg-slate-900/50 flex h-5.5 w-6.5 cursor-pointer transition-all items-center justify-center relative overflow-hidden hover:border-amber-400"
                          :class="[
                            pinnedItem && getItemFull(bpId) && pinnedItem.key === getItemFull(bpId).key
                              ? 'border-amber-400 ring-1 ring-amber-400/60 shadow-[0_0_8px_rgba(251,191,36,0.6)]'
                              : 'border-slate-800/50',
                          ]"
                          @mouseenter="onHoverItem(bpId, $event)"
                          @mouseleave="onLeaveItem"
                          @click="onPinItem(bpId, $event)"
                        >
                          <SmartImage
                            v-if="getItemMeta(bpId)"
                            :local-src="getItemIconUrl(getItemMeta(bpId)!.key)"
                            :cdn-src="getItemCdnUrl(getItemMeta(bpId)!.key)"
                            :alt="getItemMeta(bpId)!.nameLoc"
                            class="opacity-70 h-full w-full object-cover"
                          />
                          <span v-else class="text-3xs text-slate-700">·</span>
                        </div>
                      </div>

                      <!-- 1 格中立装备 (带属性加成卡片) -->
                      <div
                        class="p-0.5 border rounded-full bg-cyan-950/30 flex shrink-0 h-6 w-8 cursor-pointer transition-all items-center justify-center relative overflow-hidden hover:border-cyan-400"
                        :class="[
                          pinnedItem && getItemFull(p.item_neutral) && pinnedItem.key === getItemFull(p.item_neutral).key
                            ? 'border-cyan-400 ring-1 ring-cyan-400/60 shadow-[0_0_8px_rgba(34,211,238,0.6)]'
                            : 'border-cyan-800/60',
                        ]"
                        @mouseenter="onHoverItem(p.item_neutral, $event)"
                        @mouseleave="onLeaveItem"
                        @click="onPinItem(p.item_neutral, $event)"
                      >
                        <SmartImage
                          v-if="getItemMeta(p.item_neutral)"
                          :local-src="getItemIconUrl(getItemMeta(p.item_neutral)!.key)"
                          :cdn-src="getItemCdnUrl(getItemMeta(p.item_neutral)!.key)"
                          :alt="getItemMeta(p.item_neutral)!.nameLoc"
                          class="rounded-full h-full w-full object-cover"
                        />
                        <span v-else class="text-3xs text-slate-600 font-mono">-</span>
                      </div>

                      <!-- A杖与魔晶状态图标 (使用用户增加的 scepter_0/1 与 shard_0/1) -->
                      <div class="pl-1 flex shrink-0 items-center space-x-1.5">
                        <img
                          :src="p.aghanims_scepter === 1 ? '/assets/icons/scepter_1.png' : '/assets/icons/scepter_0.png'"
                          alt="A杖"
                          class="h-5 w-5 object-contain"
                          :title="p.aghanims_scepter === 1 ? '阿哈利姆神杖: 已购买' : '阿哈利姆神杖: 未购买'"
                        />
                        <img
                          :src="p.aghanims_shard === 1 ? '/assets/icons/shard_1.png' : '/assets/icons/shard_0.png'"
                          alt="魔晶"
                          class="h-5 w-5 object-contain"
                          :title="p.aghanims_shard === 1 ? '阿哈利姆魔晶: 已购买' : '阿哈利姆魔晶: 未购买'"
                        />
                      </div>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- Tab 2: 【团队战力与经济走势】 -->
      <div v-show="activeTab === 'advantage'" class="space-y-4">
        <AdvantageGraph
          :gold-adv="match.radiant_gold_adv"
          :xp-adv="match.radiant_xp_adv"
          :duration="match.duration"
        />
      </div>

      <!-- Tab 3: 【高阶攻防与视野贡献】 -->
      <div v-show="activeTab === 'advanced'" class="p-4 border border-slate-800 rounded-xl bg-slate-950/80 space-y-4">
        <div class="flex items-center space-x-2">
          <Eye class="text-emerald-400 h-4 w-4" />
          <h4 class="text-xs text-slate-200 font-bold">
            全队视野布控、野区资源与生存买活数据库
          </h4>
        </div>

        <!-- 详细十人攻防数据对比表 -->
        <div class="border border-slate-800 rounded-lg overflow-x-auto">
          <table class="text-xs text-slate-300 font-mono text-left min-w-[760px] w-full">
            <thead class="text-2xs text-slate-400 border-b border-slate-800 bg-slate-900/90">
              <tr>
                <th class="p-2.5">选手 / 英雄</th>
                <th class="p-2.5 text-center">阵营</th>
                <th class="p-2.5 text-right">真眼插眼</th>
                <th class="p-2.5 text-right">假眼插眼</th>
                <th class="p-2.5 text-right">拉野堆叠</th>
                <th class="p-2.5 text-right">控符数</th>
                <th class="p-2.5 text-right">买活次数</th>
                <th class="p-2.5 text-right">推塔推进</th>
                <th class="p-2.5 text-right">英雄治疗</th>
              </tr>
            </thead>
            <tbody class="divide-slate-800/60 divide-y">
              <tr
                v-for="p in match.players"
                :key="`adv-table-${p.player_slot}`"
                class="transition-colors hover:bg-slate-900/60"
                :class="selectedPlayerSlot === p.player_slot ? 'bg-amber-500/10' : ''"
              >
                <td class="p-2.5 flex items-center space-x-2.5">
                  <div class="border border-slate-700 rounded bg-slate-950 shrink-0 h-8 w-12 relative overflow-hidden">
                    <SmartImage
                      :local-src="getHeroIconUrl(String(p.hero_id))"
                      :cdn-src="getHeroCdnUrl(String(p.hero_id))"
                      :alt="getHeroName(p.hero_id)"
                      class="h-full w-full object-cover"
                    />
                  </div>
                  <div>
                    <div class="text-slate-100 font-bold">{{ getHeroName(p.hero_id) }}</div>
                    <div class="text-3xs text-slate-500 max-w-28 truncate">{{ p.personaname || `#${p.player_slot}` }}</div>
                  </div>
                </td>
                <td class="p-2.5 text-center">
                  <span
                    class="text-3xs font-bold px-1.5 py-0.5 rounded"
                    :class="p.player_slot < 128 ? 'bg-emerald-950 text-emerald-400 border border-emerald-800' : 'bg-rose-950 text-rose-400 border border-rose-800'"
                  >
                    {{ p.player_slot < 128 ? '天辉' : '夜魇' }}
                  </span>
                </td>
                <td class="text-cyan-300 font-bold p-2.5 text-right">{{ p.sen_placed || 0 }}</td>
                <td class="text-amber-300 font-bold p-2.5 text-right">{{ p.obs_placed || 0 }}</td>
                <td class="text-slate-200 p-2.5 text-right">{{ p.camps_stacked || 0 }} 次</td>
                <td class="text-slate-200 p-2.5 text-right">{{ p.rune_pickups || 0 }} 个</td>
                <td class="p-2.5 text-right">
                  <span :class="(p.buyback_count || 0) > 0 ? 'text-amber-400 font-bold' : 'text-slate-600'">
                    {{ p.buyback_count || 0 }} 次
                  </span>
                </td>
                <td class="text-amber-300 p-2.5 text-right">{{ formatDamage(p.tower_damage) }}</td>
                <td class="text-emerald-400 p-2.5 text-right">{{ formatDamage(p.hero_healing) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Tab 4: 【BP 禁选与加点序列】 -->
      <div v-show="activeTab === 'draft'" class="p-4 border border-slate-800 rounded-xl bg-slate-950/80 space-y-4">
        <div class="flex items-center space-x-2">
          <Shield class="text-purple-400 h-4 w-4" />
          <h4 class="text-xs text-slate-200 font-bold">
            天梯比赛阵容禁选 (BP Picks & Bans) 与加点时序
          </h4>
        </div>

        <!-- 禁选时序序列 -->
        <div v-if="match.picks_bans && match.picks_bans.length > 0" class="space-y-3">
          <div class="text-2xs text-slate-400">官方禁选时间轴序列 (按顺位排序)：</div>
          <div class="flex flex-wrap gap-2 items-center">
            <div
              v-for="pb in match.picks_bans"
              :key="`pb-${pb.order}`"
              class="p-1 border rounded-lg flex flex-col items-center relative overflow-hidden"
              :class="[
                pb.is_pick
                  ? (pb.team === 0 ? 'border-emerald-700 bg-emerald-950/40' : 'border-rose-700 bg-rose-950/40')
                  : 'border-slate-800 bg-slate-900/60 opacity-60',
              ]"
            >
              <div class="border border-slate-700 rounded bg-slate-950 h-8 w-12 relative overflow-hidden">
                <SmartImage
                  :local-src="getHeroIconUrl(String(pb.hero_id))"
                  :cdn-src="getHeroCdnUrl(String(pb.hero_id))"
                  :alt="getHeroName(pb.hero_id)"
                  class="h-full w-full object-cover"
                />
                <!-- 禁选标记 -->
                <span
                  class="text-3xs font-black px-1 rounded-br left-0 top-0 absolute"
                  :class="pb.is_pick ? 'bg-amber-500 text-slate-950' : 'bg-slate-700 text-slate-300'"
                >
                  {{ pb.is_pick ? 'P' : 'B' }}
                </span>
              </div>
              <span class="text-3xs text-slate-300 font-mono mt-0.5 max-w-12 truncate">
                {{ getHeroName(pb.hero_id) }}
              </span>
            </div>
          </div>
        </div>

        <div v-else class="text-xs text-slate-500 font-mono py-6 text-center">
          该对局未包含天梯 BP 禁选数据 (可能为全英雄选择或普通匹配对局)
        </div>
      </div>
    </div>

    <!-- 全局高保真深色物品详情卡片 (6主装 + 3背包 + 1中立，支持悬浮预览与点击Pin锁定并通过 Teleport 挂载到 body) -->
    <Teleport to="body">
      <ItemTooltipCard
        :item="activeTooltipItem"
        :position="tooltipPosition"
        :pinned="Boolean(pinnedItem)"
        @unpin="unpinItem"
      />
    </Teleport>
  </div>
</template>
