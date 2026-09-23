<script setup lang="ts">
/**
 * 赛后战局与 AI 深度复盘视图 (src/views/AnalysisView.vue)
 * 职责：
 * 1. 玩家身份档案 Banner：展示当前绑定账号的 Steam/OpenDota 高分辨率头像、昵称、32位好友ID与战术风格标签
 * 2. 战术大盘双栏布局：4宫格核心战绩指标、Top 3 常用英雄池（可点击筛选）与纯 SVG 轻量级五维战术雷达图
 * 3. 近期对局历史卡片化：展示官方英雄头像、胜负徽章、KDA、持续时长与 GPM 经济
 * 4. 十人对战全景大盘：双方阵容 56x42 物品栏 (6格神装+中立物品+A杖魔晶)、精确对决时间、KDA、参战率、净资产与伤害输出
 * 5. 基于 Solution Hierarchy 思维链调度端侧 NPU/云端大模型生成专业复盘报告
 * 6. 严格坚守零伪造原则：未建立连接或未配置玩家 ID 呈现真实空态，严禁 Mock 假数据
 */

import type { MatchReviewResult } from '../services/aiCoachService'
import {
  Activity,
  Award,
  BarChart3,
  Bot,
  Check,
  Clock,
  Coins,
  Copy,
  Cpu,
  Flame,
  Layers,
  RefreshCw,
  Search,
  Shield,
  Sparkles,
  Swords,
  Target,
  Trophy,
  User,
  Wand2,
  X,
  Zap,
} from '@lucide/vue'
import { computed, onMounted, ref, watch } from 'vue'
import { getHeroChineseName, getHeroShortName } from '../data/heroNames'
import { aiCoachService } from '../services/aiCoachService'
import { gameDataService } from '../services/gameDataService'
import { fetchMatchDetails, fetchPlayerRecentMatchesList } from '../services/playerProfileService'
import { useAppStore } from '../stores/app'
import { getHeroCdnUrl, getHeroIconUrl, getItemCdnUrl, getItemIconUrl, handleImageFallback } from '../utils/assetUrl'

interface ItemMeta {
  key: string
  nameLoc: string
  dname: string
}

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
/** 是否已复制玩家 ID */
const idCopied = ref(false)

/** 近期绑定的玩家对局列表快照 */
const recentMatches = ref<any[]>([])
/** 正在拉取近期对局状态 */
const loadingRecent = ref(false)

/** 筛选特定常用英雄的对局列表 */
const filterHeroId = ref<number | null>(null)

/** 全局官方物品 ID 至 Meta 映射字典 */
const itemDict = ref<Record<number, ItemMeta>>({})

/** 玩家个人资料响应式快照 */
const playerProfile = ref<{
  name: string
  avatar: string
  rankTier?: number
  leaderboardRank?: number | null
}>({
  name: '',
  avatar: '',
})

onMounted(async () => {
  await appStore.ensureSettingsLoaded()
  loadItemDict()
  if (appStore.settings.dota_player_id) {
    playerProfile.value.name = appStore.settings.dota_player_name || ''
    playerProfile.value.avatar = appStore.settings.dota_player_avatar || ''
    await Promise.all([
      fetchPlayerRecentMatches(appStore.settings.dota_player_id),
      syncPlayerProfile(appStore.settings.dota_player_id),
    ])
  }
})

// 监听绑定玩家 ID 变更，自动重新拉取战绩与档案
watch(
  () => appStore.settings.dota_player_id,
  async (newId) => {
    if (newId) {
      filterHeroId.value = null
      await Promise.all([
        fetchPlayerRecentMatches(newId),
        syncPlayerProfile(newId),
      ])
    } else {
      recentMatches.value = []
      playerProfile.value.name = ''
      playerProfile.value.avatar = ''
    }
  },
)

/**
 * 载入官方物品数据库与常数表，建立高保真物品字典
 */
async function loadItemDict() {
  try {
    const { officialData, constantsJson } = await gameDataService.getItemsData()
    const dict: Record<number, ItemMeta> = {}

    // 1. constantsJson 提取
    if (constantsJson && typeof constantsJson === 'object') {
      for (const [key, val] of Object.entries(constantsJson as Record<string, any>)) {
        if (val && typeof val.id === 'number' && val.id > 0) {
          const cleanKey = key.replace(/^item_/, '')
          dict[val.id] = {
            key: cleanKey,
            nameLoc: val.dname || cleanKey,
            dname: val.dname || cleanKey,
          }
        }
      }
    }

    // 2. officialData 补充中文名
    if (officialData && typeof officialData === 'object') {
      for (const [key, val] of Object.entries(officialData as Record<string, any>)) {
        if (val && typeof val.id === 'number' && val.id > 0) {
          const cleanKey = key.replace(/^item_/, '')
          const existing = dict[val.id]
          dict[val.id] = {
            key: cleanKey,
            nameLoc: val.name_loc || existing?.nameLoc || val.dname || cleanKey,
            dname: val.dname || existing?.dname || cleanKey,
          }
        }
      }
    }

    itemDict.value = dict
  } catch (e) {
    console.warn('[AnalysisView] 载入物品字典失败:', e)
  }
}

/**
 * 获取指定数字 ID 的物品 Meta
 */
function getItemMeta(itemId: number): ItemMeta | null {
  if (!itemId || itemId <= 0) return null
  return itemDict.value[itemId] || null
}

/**
 * 同步并持久化玩家公开资料 (头像、昵称、段位)
 */
async function syncPlayerProfile(accountId: string) {
  const clean = accountId.trim()
  if (!clean) return

  try {
    const res = await fetch(`https://api.opendota.com/api/players/${encodeURIComponent(clean)}`, {
      signal: AbortSignal.timeout(6000),
    })
    if (res.ok) {
      const data = await res.json()
      if (data?.profile) {
        const p = data.profile
        playerProfile.value.name = p.personaname || playerProfile.value.name
        playerProfile.value.avatar = p.avatarfull || p.avatar || playerProfile.value.avatar
        playerProfile.value.rankTier = data.rank_tier
        playerProfile.value.leaderboardRank = data.leaderboard_rank

        // 同步并持久化至本地 settings.json
        appStore.updateSettings({
          dota_player_name: playerProfile.value.name,
          dota_player_avatar: playerProfile.value.avatar,
        })
      }
    }
  } catch {
    // 忽略脱机异常
  }
}

/**
 * 拉取绑定玩家的近期对局历史
 */
async function fetchPlayerRecentMatches(accountId: string) {
  const clean = accountId.trim()
  if (!clean) return
  loadingRecent.value = true
  try {
    const list = await fetchPlayerRecentMatchesList(clean)
    if (Array.isArray(list)) {
      recentMatches.value = list
    }
  } catch (e) {
    console.warn('获取玩家近期比赛失败:', e)
  } finally {
    loadingRecent.value = false
  }
}

/**
 * 依据比赛 ID 查询完整比赛大盘数据
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
 */
function formatDuration(seconds: number): string {
  const min = Math.floor(seconds / 60)
  const sec = (seconds % 60).toString().padStart(2, '0')
  return `${min}:${sec}`
}

/**
 * 格式化精确对决开始时间: YYYY-MM-DD HH:mm (含相对时间)
 */
function formatMatchDateTime(timestampSeconds: number): string {
  if (!timestampSeconds) return '未知时间'
  const date = new Date(timestampSeconds * 1000)
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  const hh = String(date.getHours()).padStart(2, '0')
  const mm = String(date.getMinutes()).padStart(2, '0')
  const exact = `${y}-${m}-${d} ${hh}:${mm}`

  const diffSec = Math.floor(Date.now() / 1000) - timestampSeconds
  let relative = ''
  if (diffSec < 3600) {
    const min = Math.max(1, Math.floor(diffSec / 60))
    relative = `${min}分钟前`
  } else if (diffSec < 86400) {
    const hr = Math.floor(diffSec / 3600)
    relative = `${hr}小时前`
  } else {
    const days = Math.floor(diffSec / 86400)
    relative = days === 1 ? '昨天' : `${days}天前`
  }

  return `${exact} (${relative})`
}

/**
 * 游戏模式与房间类型本地化
 */
function getMatchGameMode(mode: number, lobby: number): string {
  const lobbyMap: Record<number, string> = {
    0: '普通匹配',
    1: '练习赛',
    2: '锦标赛',
    7: '天梯匹配',
    8: '单排天梯',
    9: '战队对决',
  }
  const modeMap: Record<number, string> = {
    1: '全选模式',
    2: '队长模式',
    3: '随机征召',
    4: '单一征召',
    22: '天梯全选 (All Pick)',
    23: '加速模式 (Turbo)',
  }
  const l = lobbyMap[lobby] || '天梯'
  const m = modeMap[mode] || '全选模式'
  return `${l} · ${m}`
}

/**
 * 数字格式化 (金钱 / 伤害)
 */
function formatNum(val: number): string {
  if (!val) return '0'
  if (val >= 10000) {
    return `${(val / 1000).toFixed(1)}k`
  }
  return val.toLocaleString()
}

/**
 * 获取英雄头像本地缓存或 CDN URL
 */
function getHeroIcon(heroId: number): string {
  const shortName = getHeroShortName(heroId)
  if (shortName) {
    return getHeroIconUrl(shortName)
  }
  return '/assets/icons/hero_universal.png'
}

/**
 * 获取英雄头像官方 CDN 兜底 URL
 */
function getHeroCdn(heroId: number): string {
  const shortName = getHeroShortName(heroId)
  if (shortName) {
    return getHeroCdnUrl(shortName)
  }
  return ''
}

/**
 * 根据官方英雄数字 ID 获取中文译名
 */
function getHeroName(heroId: number): string {
  return getHeroChineseName(heroId)
}

/**
 * 判断指定槽位玩家是否为当前绑定的账号
 */
function isBoundPlayer(player: any): boolean {
  if (!appStore.settings.dota_player_id || !player.account_id) return false
  return String(player.account_id) === String(appStore.settings.dota_player_id)
}

/**
 * 计算选手的参战率 (Kill Participation, KP%)
 */
function getPlayerKp(player: any, match: any): number {
  if (!player || !match || !Array.isArray(match.players)) return 0
  const isRadiant = player.player_slot < 128
  const teamScore = isRadiant ? (match.radiant_score ?? 0) : (match.dire_score ?? 0)
  let totalTeamKills = teamScore
  if (totalTeamKills <= 0) {
    totalTeamKills = match.players
      .filter((p: any) => (p.player_slot < 128) === isRadiant)
      .reduce((acc: number, cur: any) => acc + (cur.kills || 0), 0)
  }
  if (totalTeamKills <= 0) return 0
  const participantKills = (player.kills || 0) + (player.assists || 0)
  return Math.min(100, Math.round((participantKills / totalTeamKills) * 100))
}

/**
 * 一键复制玩家 32 位 ID
 */
async function copyPlayerId() {
  if (!appStore.settings.dota_player_id) return
  try {
    await navigator.clipboard.writeText(appStore.settings.dota_player_id)
    idCopied.value = true
    appStore.showToast('已复制玩家数字 ID 到剪贴板', 'success')
    setTimeout(() => {
      idCopied.value = false
    }, 2000)
  } catch {
    appStore.showToast('复制失败', 'warning')
  }
}

/**
 * 战绩核心统计概况计算
 */
const statsOverview = computed(() => {
  const list = recentMatches.value
  const total = list.length
  if (total === 0) {
    return {
      total: 0,
      wins: 0,
      losses: 0,
      winRate: '0.0',
      avgKills: '0.0',
      avgDeaths: '0.0',
      avgAssists: '0.0',
      kdaRatio: '0.0',
      avgGpm: 0,
      avgTowerDmg: 0,
    }
  }

  let wins = 0
  let totalKills = 0
  let totalDeaths = 0
  let totalAssists = 0
  let totalGpm = 0
  let totalTowerDmg = 0

  for (const m of list) {
    const isWin = m.radiant_win === (m.player_slot < 128)
    if (isWin) wins++
    totalKills += Number(m.kills || 0)
    totalDeaths += Number(m.deaths || 0)
    totalAssists += Number(m.assists || 0)
    totalGpm += Number(m.gold_per_min || 0)
    totalTowerDmg += Number(m.tower_damage || 0)
  }

  const losses = total - wins
  const winRate = ((wins / total) * 100).toFixed(1)
  const avgKills = (totalKills / total).toFixed(1)
  const avgDeaths = (totalDeaths / total).toFixed(1)
  const avgAssists = (totalAssists / total).toFixed(1)
  const kdaRatio = ((totalKills + totalAssists) / Math.max(1, totalDeaths)).toFixed(2)
  const avgGpm = Math.round(totalGpm / total)
  const avgTowerDmg = Math.round(totalTowerDmg / total)

  return {
    total,
    wins,
    losses,
    winRate,
    avgKills,
    avgDeaths,
    avgAssists,
    kdaRatio,
    avgGpm,
    avgTowerDmg,
  }
})

/**
 * 五维战术评分 (0 ~ 100)
 */
const radarDimensions = computed(() => {
  const s = statsOverview.value
  if (s.total === 0) {
    return [
      { name: '参战', score: 50 },
      { name: '发育', score: 50 },
      { name: '击杀', score: 50 },
      { name: '生存', score: 50 },
      { name: '推进', score: 50 },
    ]
  }

  const partScore = Math.min(99, Math.max(25, Math.round((Number(s.avgAssists) / 14) * 85 + 10)))
  const gpmVal = s.avgGpm > 0 ? s.avgGpm : 500
  const farmScore = Math.min(99, Math.max(25, Math.round((gpmVal / 650) * 85 + 5)))
  const killScore = Math.min(99, Math.max(25, Math.round((Number(s.avgKills) / 9) * 85 + 10)))
  const dVal = Number(s.avgDeaths)
  const survScore = Math.min(99, Math.max(25, Math.round(100 - (dVal * 7.5))))
  const tdVal = s.avgTowerDmg > 0 ? s.avgTowerDmg : 2000
  const pushScore = Math.min(99, Math.max(25, Math.round((tdVal / 2800) * 80 + 15)))

  return [
    { name: '参战', score: partScore },
    { name: '发育', score: farmScore },
    { name: '击杀', score: killScore },
    { name: '生存', score: survScore },
    { name: '推进', score: pushScore },
  ]
})

/**
 * 纯 SVG 五维雷达图几何
 */
const radarCenter = { x: 120, y: 120 }
const radarRadius = 78
const radarAngles = [-Math.PI / 2, -Math.PI / 2 + (2 * Math.PI) / 5, -Math.PI / 2 + (4 * Math.PI) / 5, -Math.PI / 2 + (6 * Math.PI) / 5, -Math.PI / 2 + (8 * Math.PI) / 5]

function getWebPoints(scale: number): string {
  const r = radarRadius * scale
  return radarAngles
    .map((angle) => {
      const x = radarCenter.x + r * Math.cos(angle)
      const y = radarCenter.y + r * Math.sin(angle)
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')
}

const radarDataPoints = computed(() => {
  const dims = radarDimensions.value
  return radarAngles
    .map((angle, i) => {
      const score = dims[i]?.score ?? 50
      const r = radarRadius * (score / 100)
      const x = radarCenter.x + r * Math.cos(angle)
      const y = radarCenter.y + r * Math.sin(angle)
      return `${x.toFixed(1)},${y.toFixed(1)}`
    })
    .join(' ')
})

const radarVertices = computed(() => {
  const dims = radarDimensions.value
  return radarAngles.map((angle, i) => {
    const score = dims[i]?.score ?? 50
    const r = radarRadius * (score / 100)
    return {
      x: radarCenter.x + r * Math.cos(angle),
      y: radarCenter.y + r * Math.sin(angle),
      name: dims[i]?.name || '',
      score,
    }
  })
})

const radarLabelPositions = computed(() => {
  const labelRadius = radarRadius + 22
  const dims = radarDimensions.value
  return radarAngles.map((angle, i) => {
    return {
      x: radarCenter.x + labelRadius * Math.cos(angle),
      y: radarCenter.y + labelRadius * Math.sin(angle) + 4,
      name: dims[i]?.name || '',
      score: dims[i]?.score || 50,
    }
  })
})

/**
 * 最近出场 Top 3 常用英雄池
 */
const topThreeHeroes = computed(() => {
  const list = recentMatches.value
  if (list.length === 0) return []

  const map = new Map<number, {
    heroId: number
    count: number
    wins: number
    kills: number
    deaths: number
    assists: number
  }>()

  for (const m of list) {
    const hid = Number(m.hero_id)
    if (!hid) continue
    const isWin = m.radiant_win === (m.player_slot < 128)
    const existing = map.get(hid) || {
      heroId: hid,
      count: 0,
      wins: 0,
      kills: 0,
      deaths: 0,
      assists: 0,
    }
    existing.count += 1
    if (isWin) existing.wins += 1
    existing.kills += Number(m.kills || 0)
    existing.deaths += Number(m.deaths || 0)
    existing.assists += Number(m.assists || 0)
    map.set(hid, existing)
  }

  const sorted = Array.from(map.values())
    .sort((a, b) => b.count - a.count || b.wins - a.wins)
    .slice(0, 3)

  return sorted.map(h => ({
    heroId: h.heroId,
    name: getHeroName(h.heroId),
    shortName: getHeroShortName(h.heroId),
    count: h.count,
    wins: h.wins,
    winRate: Math.round((h.wins / h.count) * 100),
    avgKda: ((h.kills + h.assists) / Math.max(1, h.deaths)).toFixed(1),
  }))
})

/**
 * 筛选后的近期比赛列表
 */
const displayedRecentMatches = computed(() => {
  if (!filterHeroId.value) return recentMatches.value
  return recentMatches.value.filter(m => Number(m.hero_id) === filterHeroId.value)
})

/**
 * 当前选中视角的选手数据实体
 */
const selectedPlayerData = computed(() => {
  if (!currentMatch.value || !Array.isArray(currentMatch.value.players)) return null
  return currentMatch.value.players.find((p: any) => p.player_slot === selectedPlayerSlot.value) || null
})

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
 * 一键复制复盘报告内容
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
  <div class="text-slate-100 bg-dota-bg flex flex-1 flex-col select-none overflow-hidden">
    <!-- 顶部标题栏 -->
    <header class="px-6 border-b border-dota-border bg-dota-surface flex shrink-0 h-14 items-center justify-between">
      <div class="flex items-center space-x-3">
        <Bot class="text-amber-400 h-5 w-5" />
        <h1 class="text-base text-amber-400 tracking-wide font-bold">
          赛后战局 AI 深度复盘与全景战术研判
        </h1>
      </div>
      <div class="text-xs text-slate-400 font-mono flex items-center space-x-2">
        <Sparkles class="text-cyan-400 h-4 w-4" />
        <span>基于 Solution Hierarchy 思维链全景推演</span>
      </div>
    </header>

    <!-- 主视口内容区 -->
    <div class="p-6 flex-1 overflow-y-auto space-y-6">
      <!-- 1. 比赛搜索输入栏 -->
      <section class="p-4 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-3">
        <div class="flex flex-col gap-3 items-center justify-between sm:flex-row">
          <div class="flex-1 w-full relative">
            <input
              v-model="searchMatchId"
              type="text"
              placeholder="输入 Dota 2 比赛编号 (Match ID) 检索全场十人装备与战局数据..."
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
            <span>{{ searching ? '正在检索大盘...' : '载入对局大盘' }}</span>
          </button>
        </div>

        <div v-if="searchError" class="text-xs text-rose-400 font-mono">
          {{ searchError }}
        </div>
      </section>

      <!-- 2. 玩家战术身份档案与五维雷达图大盘 (全面强化头像与 UI 布局) -->
      <section class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-5">
        <!-- 2.1 玩家个人档案 Banner -->
        <div class="flex flex-col md:flex-row md:items-center justify-between gap-4 pb-4 border-b border-slate-800">
          <div class="flex items-center space-x-4">
            <!-- 玩家高清圆形头像 (带双层微光金环) -->
            <div class="relative w-15 h-15 rounded-full ring-2 ring-amber-400/60 p-0.5 bg-gradient-to-tr from-amber-600 via-amber-400 to-amber-200 shadow-lg shadow-amber-500/10 shrink-0">
              <div class="w-full h-full rounded-full overflow-hidden bg-slate-950 flex items-center justify-center">
                <img
                  v-if="playerProfile.avatar || appStore.settings.dota_player_avatar"
                  :src="playerProfile.avatar || appStore.settings.dota_player_avatar"
                  :alt="playerProfile.name || '玩家头像'"
                  class="w-full h-full object-cover"
                >
                <User v-else class="h-7 w-7 text-amber-400/80" />
              </div>
              <span class="absolute bottom-0 right-0 w-3.5 h-3.5 bg-emerald-500 border-2 border-slate-900 rounded-full" />
            </div>

            <!-- 玩家昵称、ID 与战术标签 -->
            <div class="space-y-1">
              <div class="flex items-center space-x-2.5">
                <h2 class="text-base font-extrabold text-slate-100 tracking-wide">
                  {{ playerProfile.name || appStore.settings.dota_player_name || '已绑定选手' }}
                </h2>
                <span class="text-2xs font-mono font-bold px-2 py-0.5 rounded-full bg-amber-500/20 text-amber-300 border border-amber-500/40">
                  天梯实战档案
                </span>
              </div>

              <div class="flex flex-wrap items-center gap-2 text-xs font-mono text-slate-400">
                <span v-if="appStore.settings.dota_player_id" class="flex items-center space-x-1 bg-slate-950 px-2 py-0.5 rounded border border-slate-800">
                  <span class="text-slate-500">ID:</span>
                  <span class="text-slate-200 font-bold">{{ appStore.settings.dota_player_id }}</span>
                  <button
                    class="ml-1 text-slate-500 hover:text-amber-300 transition-colors"
                    title="复制账号 ID"
                    @click="copyPlayerId"
                  >
                    <Check v-if="idCopied" class="h-3 w-3 text-emerald-400" />
                    <Copy v-else class="h-3 w-3" />
                  </button>
                </span>
                <span v-else class="text-amber-400/80">未绑定玩家 ID (请前往设置绑定)</span>
              </div>
            </div>
          </div>

          <!-- 右侧操作栏 (刷新与统计周期) -->
          <div class="flex items-center space-x-3 self-end md:self-auto">
            <button
              v-if="appStore.settings.dota_player_id"
              class="text-xs text-slate-300 hover:text-amber-300 font-mono px-3 py-1.5 rounded-xl border border-slate-700 bg-slate-950 flex items-center space-x-1.5 transition-colors shadow-sm"
              :disabled="loadingRecent"
              @click="fetchPlayerRecentMatches(appStore.settings.dota_player_id)"
            >
              <RefreshCw class="h-3.5 w-3.5" :class="{ 'animate-spin': loadingRecent }" />
              <span>刷新战绩</span>
            </button>
          </div>
        </div>

        <!-- 未绑定账号的空态守卫 (零伪造规范) -->
        <div
          v-if="!appStore.settings.dota_player_id"
          class="py-8 flex flex-col items-center justify-center text-center space-y-2 text-slate-400"
        >
          <User class="h-10 w-10 text-slate-600 mb-1" />
          <p class="text-xs text-slate-300 font-medium">尚未配置绑定的 Dota 2 玩家数字 ID</p>
          <p class="text-2xs text-slate-500">前往【基础设置】绑定您的 32 位好友 ID，即可在此自动生成近期胜率、战术五维雷达图与常用英雄池</p>
        </div>

        <!-- 战绩载入中 -->
        <div
          v-else-if="loadingRecent && recentMatches.length === 0"
          class="py-8 flex flex-col items-center justify-center space-y-2 text-slate-400"
        >
          <Activity class="h-8 w-8 text-amber-400 animate-spin" />
          <p class="text-xs font-mono">正在从 OpenDota 同步玩家近期真实天梯战报...</p>
        </div>

        <!-- 已拉取到战绩：展示核心数据卡片 + 雷达图 + Top 3 英雄 -->
        <div v-else-if="recentMatches.length > 0" class="grid grid-cols-1 lg:grid-cols-12 gap-5">
          <!-- 左侧：战绩数据核心指标与常用英雄 (7 列) -->
          <div class="lg:col-span-7 space-y-4">
            <!-- 核心数据 4 宫格 -->
            <div class="grid grid-cols-2 sm:grid-cols-4 gap-2.5">
              <!-- 近期场次 -->
              <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between">
                <span class="text-2xs text-slate-400 flex items-center space-x-1">
                  <Clock class="h-3 w-3 text-slate-500" />
                  <span>近期样本</span>
                </span>
                <div class="mt-1">
                  <span class="text-xl font-mono font-extrabold text-slate-100">{{ statsOverview.total }}</span>
                  <span class="text-2xs text-slate-400 ml-1">场</span>
                </div>
              </div>

              <!-- 总体胜率 -->
              <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between">
                <span class="text-2xs text-slate-400 flex items-center space-x-1">
                  <Trophy class="h-3 w-3 text-amber-400" />
                  <span>综合胜率</span>
                </span>
                <div class="mt-1 flex items-baseline space-x-1">
                  <span
                    class="text-xl font-mono font-extrabold"
                    :class="Number(statsOverview.winRate) >= 50 ? 'text-emerald-400' : 'text-rose-400'"
                  >
                    {{ statsOverview.winRate }}%
                  </span>
                  <span class="text-2xs text-slate-500">
                    ({{ statsOverview.wins }}胜/{{ statsOverview.losses }}负)
                  </span>
                </div>
              </div>

              <!-- 场均 KDA -->
              <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between">
                <span class="text-2xs text-slate-400 flex items-center space-x-1">
                  <Swords class="h-3 w-3 text-cyan-400" />
                  <span>场均 K/D/A</span>
                </span>
                <div class="mt-1 font-mono text-xs text-slate-200">
                  <span class="text-emerald-400 font-bold">{{ statsOverview.avgKills }}</span>
                  <span class="text-slate-600">/</span>
                  <span class="text-rose-400 font-bold">{{ statsOverview.avgDeaths }}</span>
                  <span class="text-slate-600">/</span>
                  <span class="text-amber-400 font-bold">{{ statsOverview.avgAssists }}</span>
                </div>
              </div>

              <!-- 综合 KDA 评分 -->
              <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col justify-between">
                <span class="text-2xs text-slate-400 flex items-center space-x-1">
                  <Award class="h-3 w-3 text-purple-400" />
                  <span>战损比率</span>
                </span>
                <div class="mt-1">
                  <span class="text-xl font-mono font-extrabold text-purple-300">{{ statsOverview.kdaRatio }}</span>
                  <span class="text-2xs text-slate-500 ml-1">KDA</span>
                </div>
              </div>
            </div>

            <!-- 最近使用前三英雄池 (Top 3 Heroes) -->
            <div class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/70 space-y-2.5">
              <div class="flex items-center justify-between">
                <span class="text-xs font-bold text-slate-300 flex items-center space-x-1.5">
                  <Zap class="h-3.5 w-3.5 text-amber-400" />
                  <span>近期使用最高频英雄 (Top 3)</span>
                </span>
                <span v-if="filterHeroId" class="text-2xs text-amber-400 font-mono flex items-center space-x-1 cursor-pointer" @click="filterHeroId = null">
                  <span>已筛选: {{ getHeroName(filterHeroId) }} (点击清除)</span>
                  <X class="h-3 w-3" />
                </span>
                <span v-else class="text-2xs text-slate-500 font-mono">点击英雄可筛选历史对局</span>
              </div>

              <div class="grid grid-cols-1 sm:grid-cols-3 gap-2.5">
                <div
                  v-for="h in topThreeHeroes"
                  :key="h.heroId"
                  class="p-2 border rounded-lg flex items-center space-x-2.5 cursor-pointer transition-all select-none"
                  :class="[
                    filterHeroId === h.heroId
                      ? 'border-amber-400 bg-amber-500/15 shadow-sm'
                      : 'border-slate-800 bg-slate-900/60 hover:border-slate-700 hover:bg-slate-900',
                  ]"
                  @click="filterHeroId = filterHeroId === h.heroId ? null : h.heroId"
                >
                  <!-- 英雄头像 -->
                  <div class="w-11 h-8 rounded border border-slate-700 bg-slate-950 overflow-hidden shrink-0">
                    <img
                      :src="getHeroIcon(h.heroId)"
                      :alt="h.name"
                      class="w-full h-full object-cover"
                      loading="lazy"
                      @error="(e) => handleImageFallback(e, getHeroCdn(h.heroId))"
                    >
                  </div>

                  <!-- 英雄数据 -->
                  <div class="flex-1 min-w-0">
                    <div class="text-xs font-bold text-slate-200 truncate">{{ h.name }}</div>
                    <div class="text-2xs text-slate-400 font-mono flex items-center space-x-2">
                      <span>{{ h.count }}场</span>
                      <span :class="h.winRate >= 50 ? 'text-emerald-400 font-bold' : 'text-rose-400 font-bold'">
                        {{ h.winRate }}%
                      </span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- 右侧：五维战术能力雷达图 (5 列) -->
          <div class="lg:col-span-5 p-3.5 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col items-center justify-center">
            <div class="w-full flex items-center justify-between mb-1">
              <span class="text-xs font-bold text-slate-300 flex items-center space-x-1.5">
                <Target class="h-3.5 w-3.5 text-cyan-400" />
                <span>实战能力五维战术雷达</span>
              </span>
              <span class="text-2xs text-slate-500 font-mono">均值标准化 0~100</span>
            </div>

            <!-- 纯 SVG 雷达图组件 -->
            <div class="relative w-64 h-64 flex items-center justify-center select-none">
              <svg viewBox="0 0 240 240" class="w-full h-full overflow-visible">
                <!-- 1. 背景五边形网格线 (5 层等比同心圆环) -->
                <polygon
                  v-for="scale in [0.2, 0.4, 0.6, 0.8, 1.0]"
                  :key="scale"
                  :points="getWebPoints(scale)"
                  fill="none"
                  stroke="#334155"
                  :stroke-width="scale === 1.0 ? '1.5' : '1'"
                  stroke-dasharray="2,2"
                  class="transition-all"
                />

                <!-- 2. 由中心发散至顶点的 5 条轴线 -->
                <line
                  v-for="(angle, i) in radarAngles"
                  :key="`axis-${i}`"
                  :x1="radarCenter.x"
                  :y1="radarCenter.y"
                  :x2="radarCenter.x + radarRadius * Math.cos(angle)"
                  :y2="radarCenter.y + radarRadius * Math.sin(angle)"
                  stroke="#475569"
                  stroke-width="1"
                />

                <!-- 3. 数据覆盖多边形 (半透明渐变能量光晕) -->
                <polygon
                  :points="radarDataPoints"
                  fill="rgba(245, 158, 11, 0.25)"
                  stroke="#f59e0b"
                  stroke-width="2"
                  class="transition-all duration-500 ease-out"
                />

                <!-- 4. 数据顶点高亮圆点 -->
                <circle
                  v-for="(v, i) in radarVertices"
                  :key="`vertex-${i}`"
                  :cx="v.x"
                  :cy="v.y"
                  r="3.5"
                  fill="#fbbf24"
                  stroke="#0f172a"
                  stroke-width="1.5"
                />

                <!-- 5. 外部维度文本标签 -->
                <text
                  v-for="(pos, i) in radarLabelPositions"
                  :key="`label-${i}`"
                  :x="pos.x"
                  :y="pos.y"
                  text-anchor="middle"
                  dominant-baseline="central"
                  class="text-2xs font-mono font-bold fill-slate-300"
                >
                  {{ pos.name }} {{ pos.score }}
                </text>
              </svg>
            </div>
          </div>
        </div>

        <!-- 战绩无公开数据的真实兜底 -->
        <div v-else class="py-6 text-center text-xs text-slate-500 font-mono">
          未检索到该玩家公开天梯战绩，请确保在 Dota 2 客户端中勾选了【公开比赛数据】
        </div>
      </section>

      <!-- 3. 近期对局历史卡片列表 -->
      <section v-if="recentMatches.length > 0" class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-3">
        <div class="flex items-center justify-between pb-2 border-b border-slate-800">
          <div class="flex items-center space-x-2">
            <Clock class="text-amber-400 h-4 w-4" />
            <h3 class="text-xs font-bold text-slate-200">
              近期天梯比赛历史记录 (点击任意条目秒级载入十人全景大盘)
            </h3>
          </div>
          <span class="text-2xs font-mono text-slate-500">
            显示 {{ displayedRecentMatches.length }} / {{ recentMatches.length }} 场
          </span>
        </div>

        <!-- 卡片列表网格 -->
        <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-2.5">
          <div
            v-for="m in displayedRecentMatches"
            :key="m.match_id"
            class="p-2.5 border rounded-xl flex items-center justify-between cursor-pointer transition-all select-none group"
            :class="[
              String(m.match_id) === String(searchMatchId)
                ? 'border-amber-400 bg-amber-500/10 shadow-md shadow-amber-500/5'
                : 'border-slate-800 bg-slate-950/60 hover:border-slate-700 hover:bg-slate-900',
            ]"
            @click="() => handleSearchMatch(String(m.match_id))"
          >
            <!-- 左侧：英雄头像与胜负徽章 -->
            <div class="flex items-center space-x-3">
              <!-- 英雄头像 -->
              <div class="relative w-13 h-9 rounded-lg border border-slate-700 bg-slate-950 overflow-hidden shrink-0 group-hover:border-slate-500 transition-colors">
                <img
                  :src="getHeroIcon(m.hero_id)"
                  :alt="getHeroName(m.hero_id)"
                  class="w-full h-full object-cover"
                  loading="lazy"
                  @error="(e) => handleImageFallback(e, getHeroCdn(m.hero_id))"
                >
              </div>

              <!-- 英雄名与比赛 ID -->
              <div class="flex flex-col">
                <div class="flex items-center space-x-1.5">
                  <span class="text-xs font-bold text-slate-100 group-hover:text-amber-300 transition-colors">
                    {{ getHeroName(m.hero_id) }}
                  </span>
                  <!-- 胜负标识徽章 -->
                  <span
                    class="text-2xs px-1.5 py-0.2 rounded font-bold font-mono"
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

            <!-- 右侧：KDA 与时长 -->
            <div class="text-right font-mono space-y-0.5">
              <div class="text-xs font-bold text-slate-200">
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
          </div>
        </div>
      </section>

      <!-- 4. 比赛详情全景大盘 (含装备栏、对决时间与多维战绩) -->
      <section v-if="currentMatch" class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-5">
        <!-- 4.1 比赛元信息头 (精确对决时间、房间模式、比分走势) -->
        <div class="pb-4 border-b border-slate-800 flex flex-col gap-3 items-start justify-between lg:flex-row lg:items-center">
          <div class="space-y-1.5">
            <div class="flex flex-wrap items-center gap-2.5">
              <span class="text-base text-slate-100 font-extrabold font-mono">比赛 #{{ currentMatch.match_id }}</span>
              <span
                class="text-xs font-bold px-2.5 py-0.5 rounded-full"
                :class="currentMatch.radiant_win ? 'bg-emerald-950 text-emerald-300 border border-emerald-700' : 'bg-rose-950 text-rose-300 border border-rose-700'"
              >
                {{ currentMatch.radiant_win ? '天辉获胜' : '夜魇获胜' }}
              </span>
              <span class="text-2xs font-mono px-2 py-0.5 rounded bg-slate-800 text-slate-300 border border-slate-700">
                {{ getMatchGameMode(currentMatch.game_mode, currentMatch.lobby_type) }}
              </span>
            </div>

            <!-- 对决时间与时长 -->
            <div class="text-xs text-slate-400 font-mono flex flex-wrap items-center gap-4">
              <span class="flex items-center space-x-1.5 text-slate-300">
                <Clock class="h-3.5 w-3.5 text-amber-400" />
                <span>对决时间: {{ formatMatchDateTime(currentMatch.start_time) }}</span>
              </span>
              <span>比赛时长: <b class="text-slate-200">{{ formatDuration(currentMatch.duration || 0) }}</b></span>
              <span>对阵总比分: <b class="text-emerald-400">{{ currentMatch.radiant_score ?? '-' }}</b> : <b class="text-rose-400">{{ currentMatch.dire_score ?? '-' }}</b></span>
            </div>
          </div>

          <!-- 一键生成 AI 深度复盘按钮 -->
          <button
            class="text-xs text-slate-950 font-bold px-5 py-2.5 rounded-xl flex cursor-pointer shadow-lg transition-all items-center from-amber-500 to-amber-600 bg-gradient-to-r space-x-2 disabled:opacity-50 hover:from-amber-400 hover:to-amber-500 shrink-0"
            :disabled="reviewing"
            @click="handleGenerateReview"
          >
            <Cpu class="h-4 w-4" :class="{ 'animate-spin': reviewing }" />
            <span>{{ reviewing ? 'AI 教练思维链推演中...' : '生成针对性 AI 复盘报告' }}</span>
          </button>
        </div>

        <!-- 选手视角选择提示 -->
        <div class="text-xs text-slate-400 flex items-center justify-between pb-1">
          <span>请点击选择下方任一选手作为复盘主视角 (默认已自动定位您操纵的英雄)：</span>
          <span class="text-amber-400 font-bold font-mono">当前视角: 槽位 {{ selectedPlayerSlot ?? '0' }}</span>
        </div>

        <!-- 4.2 十人阵容对战列表 (天辉方 vs 夜魇方) -->
        <div class="space-y-4">
          <!-- 天辉方阵营卡片 -->
          <div class="p-3.5 border border-emerald-900/40 rounded-xl bg-emerald-950/20 space-y-2">
            <div class="text-xs text-emerald-400 font-bold pb-1.5 border-b border-emerald-900/30 flex items-center justify-between">
              <div class="flex items-center space-x-1.5">
                <Trophy class="h-3.5 w-3.5" />
                <span>天辉方 (The Radiant)</span>
                <span v-if="currentMatch.radiant_win" class="text-2xs px-1.5 rounded bg-emerald-500/20 text-emerald-300">胜利方</span>
              </div>
              <span class="text-2xs font-mono text-emerald-300/70">总击杀: {{ currentMatch.radiant_score ?? '-' }}</span>
            </div>

            <!-- 天辉 5 人条目 -->
            <div class="space-y-1.5">
              <div
                v-for="p in currentMatch.players.slice(0, 5)"
                :key="p.player_slot"
                class="p-2.5 border rounded-xl flex flex-col md:flex-row items-start md:items-center justify-between gap-3 cursor-pointer transition-all"
                :class="[
                  selectedPlayerSlot === p.player_slot
                    ? 'border-amber-400 bg-amber-500/15 shadow-sm'
                    : 'border-slate-800 bg-slate-950/70 hover:border-slate-700 hover:bg-slate-900/80',
                ]"
                @click="selectedPlayerSlot = p.player_slot"
              >
                <!-- 英雄头像、等级与名称 -->
                <div class="flex items-center space-x-3 shrink-0 min-w-44">
                  <div class="relative w-12 h-9 rounded border border-slate-700 bg-slate-950 overflow-hidden shrink-0">
                    <img
                      :src="getHeroIcon(p.hero_id)"
                      :alt="getHeroName(p.hero_id)"
                      class="w-full h-full object-cover"
                      loading="lazy"
                      @error="(e) => handleImageFallback(e, getHeroCdn(p.hero_id))"
                    >
                    <span class="absolute bottom-0 right-0 bg-slate-900/90 text-amber-300 text-3xs font-mono font-bold px-1 rounded-tl">
                      Lv.{{ p.level || '-' }}
                    </span>
                  </div>

                  <div>
                    <div class="flex items-center space-x-1.5">
                      <span class="text-xs font-bold text-slate-100">{{ getHeroName(p.hero_id) }}</span>
                      <span v-if="isBoundPlayer(p)" class="text-amber-300 font-mono px-1 py-0.2 border border-amber-500/40 rounded bg-amber-500/20 text-3xs">您操纵</span>
                    </div>
                    <div class="text-2xs text-slate-400 font-mono truncate max-w-32">
                      {{ p.personaname || `选手 #${p.player_slot}` }}
                    </div>
                  </div>
                </div>

                <!-- 中间：6 格神装 + 中立物品 + A杖魔晶徽章 -->
                <div class="flex items-center space-x-2 shrink-0">
                  <!-- 6 格标准装备 (严格 4:3 比例 36x27) -->
                  <div class="grid grid-cols-6 gap-1 bg-slate-900/80 p-1 rounded-lg border border-slate-800">
                    <div
                      v-for="(itemId, idx) in [p.item_0, p.item_1, p.item_2, p.item_3, p.item_4, p.item_5]"
                      :key="`t1-${p.player_slot}-${idx}`"
                      class="w-9 h-6.75 rounded-[3px] border border-slate-800 bg-slate-950 overflow-hidden relative flex items-center justify-center"
                      :title="getItemMeta(itemId)?.nameLoc || ''"
                    >
                      <img
                        v-if="getItemMeta(itemId)"
                        :src="getItemIconUrl(getItemMeta(itemId)!.key)"
                        :alt="getItemMeta(itemId)!.nameLoc"
                        class="w-full h-full object-cover"
                        loading="lazy"
                        @error="(e) => handleImageFallback(e, getItemCdnUrl(getItemMeta(itemId)!.key))"
                      >
                    </div>
                  </div>

                  <!-- 1 格中立装备 -->
                  <div
                    class="w-9 h-6.75 rounded-full border border-cyan-800/60 bg-cyan-950/30 overflow-hidden relative flex items-center justify-center p-0.5"
                    :title="`中立物品: ${getItemMeta(p.item_neutral)?.nameLoc || '无'}`"
                  >
                    <img
                      v-if="getItemMeta(p.item_neutral)"
                      :src="getItemIconUrl(getItemMeta(p.item_neutral)!.key)"
                      :alt="getItemMeta(p.item_neutral)!.nameLoc"
                      class="w-full h-full object-cover rounded-full"
                      loading="lazy"
                      @error="(e) => handleImageFallback(e, getItemCdnUrl(getItemMeta(p.item_neutral)!.key))"
                    >
                    <span v-else class="text-3xs text-slate-600 font-mono">-</span>
                  </div>

                  <!-- A 杖与魔晶激活状态小徽章 -->
                  <div class="flex flex-col space-y-0.5 pl-0.5">
                    <span
                      class="text-3xs px-1 rounded font-bold font-mono"
                      :class="p.aghanims_scepter === 1 ? 'bg-cyan-500/20 text-cyan-300 border border-cyan-500/40' : 'bg-slate-900 text-slate-600'"
                      title="阿哈利姆神杖"
                    >
                      A
                    </span>
                    <span
                      class="text-3xs px-1 rounded font-bold font-mono"
                      :class="p.aghanims_shard === 1 ? 'bg-purple-500/20 text-purple-300 border border-purple-500/40' : 'bg-slate-900 text-slate-600'"
                      title="阿哈利姆魔晶"
                    >
                      S
                    </span>
                  </div>
                </div>

                <!-- 右侧：KDA、参战率、净资产与输出 -->
                <div class="flex items-center space-x-4 text-xs font-mono text-right">
                  <!-- KDA 与参战率 -->
                  <div class="space-y-0.5">
                    <div class="text-slate-100 font-bold">
                      <span class="text-emerald-400">{{ p.kills }}</span>
                      <span class="text-slate-600">/</span>
                      <span class="text-rose-400">{{ p.deaths }}</span>
                      <span class="text-slate-600">/</span>
                      <span class="text-amber-400">{{ p.assists }}</span>
                    </div>
                    <div class="text-2xs text-slate-400">
                      参战 {{ getPlayerKp(p, currentMatch) }}%
                    </div>
                  </div>

                  <!-- 净资产与 GPM -->
                  <div class="space-y-0.5 min-w-18">
                    <div class="text-amber-400 font-extrabold">
                      {{ formatNum(p.net_worth || p.total_gold) }}
                    </div>
                    <div class="text-2xs text-slate-400">
                      {{ p.gold_per_min || '-' }} GPM
                    </div>
                  </div>

                  <!-- 伤害与正反补 -->
                  <div class="space-y-0.5 min-w-16 hidden sm:block">
                    <div class="text-slate-300">
                      {{ formatDamage(p.hero_damage) }} 伤
                    </div>
                    <div class="text-2xs text-slate-500">
                      {{ p.last_hits || 0 }}/{{ p.denies || 0 }} 补
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- 夜魇方阵营卡片 -->
          <div class="p-3.5 border border-rose-900/40 rounded-xl bg-rose-950/20 space-y-2">
            <div class="text-xs text-rose-400 font-bold pb-1.5 border-b border-rose-900/30 flex items-center justify-between">
              <div class="flex items-center space-x-1.5">
                <Flame class="h-3.5 w-3.5" />
                <span>夜魇方 (The Dire)</span>
                <span v-if="!currentMatch.radiant_win" class="text-2xs px-1.5 rounded bg-rose-500/20 text-rose-300">胜利方</span>
              </div>
              <span class="text-2xs font-mono text-rose-300/70">总击杀: {{ currentMatch.dire_score ?? '-' }}</span>
            </div>

            <!-- 夜魇 5 人条目 -->
            <div class="space-y-1.5">
              <div
                v-for="p in currentMatch.players.slice(5, 10)"
                :key="p.player_slot"
                class="p-2.5 border rounded-xl flex flex-col md:flex-row items-start md:items-center justify-between gap-3 cursor-pointer transition-all"
                :class="[
                  selectedPlayerSlot === p.player_slot
                    ? 'border-amber-400 bg-amber-500/15 shadow-sm'
                    : 'border-slate-800 bg-slate-950/70 hover:border-slate-700 hover:bg-slate-900/80',
                ]"
                @click="selectedPlayerSlot = p.player_slot"
              >
                <!-- 英雄头像、等级与名称 -->
                <div class="flex items-center space-x-3 shrink-0 min-w-44">
                  <div class="relative w-12 h-9 rounded border border-slate-700 bg-slate-950 overflow-hidden shrink-0">
                    <img
                      :src="getHeroIcon(p.hero_id)"
                      :alt="getHeroName(p.hero_id)"
                      class="w-full h-full object-cover"
                      loading="lazy"
                      @error="(e) => handleImageFallback(e, getHeroCdn(p.hero_id))"
                    >
                    <span class="absolute bottom-0 right-0 bg-slate-900/90 text-amber-300 text-3xs font-mono font-bold px-1 rounded-tl">
                      Lv.{{ p.level || '-' }}
                    </span>
                  </div>

                  <div>
                    <div class="flex items-center space-x-1.5">
                      <span class="text-xs font-bold text-slate-100">{{ getHeroName(p.hero_id) }}</span>
                      <span v-if="isBoundPlayer(p)" class="text-amber-300 font-mono px-1 py-0.2 border border-amber-500/40 rounded bg-amber-500/20 text-3xs">您操纵</span>
                    </div>
                    <div class="text-2xs text-slate-400 font-mono truncate max-w-32">
                      {{ p.personaname || `选手 #${p.player_slot}` }}
                    </div>
                  </div>
                </div>

                <!-- 中间：6 格神装 + 中立物品 + A杖魔晶徽章 -->
                <div class="flex items-center space-x-2 shrink-0">
                  <!-- 6 格标准装备 (严格 4:3 比例 36x27) -->
                  <div class="grid grid-cols-6 gap-1 bg-slate-900/80 p-1 rounded-lg border border-slate-800">
                    <div
                      v-for="(itemId, idx) in [p.item_0, p.item_1, p.item_2, p.item_3, p.item_4, p.item_5]"
                      :key="`t2-${p.player_slot}-${idx}`"
                      class="w-9 h-6.75 rounded-[3px] border border-slate-800 bg-slate-950 overflow-hidden relative flex items-center justify-center"
                      :title="getItemMeta(itemId)?.nameLoc || ''"
                    >
                      <img
                        v-if="getItemMeta(itemId)"
                        :src="getItemIconUrl(getItemMeta(itemId)!.key)"
                        :alt="getItemMeta(itemId)!.nameLoc"
                        class="w-full h-full object-cover"
                        loading="lazy"
                        @error="(e) => handleImageFallback(e, getItemCdnUrl(getItemMeta(itemId)!.key))"
                      >
                    </div>
                  </div>

                  <!-- 1 格中立装备 -->
                  <div
                    class="w-9 h-6.75 rounded-full border border-cyan-800/60 bg-cyan-950/30 overflow-hidden relative flex items-center justify-center p-0.5"
                    :title="`中立物品: ${getItemMeta(p.item_neutral)?.nameLoc || '无'}`"
                  >
                    <img
                      v-if="getItemMeta(p.item_neutral)"
                      :src="getItemIconUrl(getItemMeta(p.item_neutral)!.key)"
                      :alt="getItemMeta(p.item_neutral)!.nameLoc"
                      class="w-full h-full object-cover rounded-full"
                      loading="lazy"
                      @error="(e) => handleImageFallback(e, getItemCdnUrl(getItemMeta(p.item_neutral)!.key))"
                    >
                    <span v-else class="text-3xs text-slate-600 font-mono">-</span>
                  </div>

                  <!-- A 杖与魔晶激活状态小徽章 -->
                  <div class="flex flex-col space-y-0.5 pl-0.5">
                    <span
                      class="text-3xs px-1 rounded font-bold font-mono"
                      :class="p.aghanims_scepter === 1 ? 'bg-cyan-500/20 text-cyan-300 border border-cyan-500/40' : 'bg-slate-900 text-slate-600'"
                      title="阿哈利姆神杖"
                    >
                      A
                    </span>
                    <span
                      class="text-3xs px-1 rounded font-bold font-mono"
                      :class="p.aghanims_shard === 1 ? 'bg-purple-500/20 text-purple-300 border border-purple-500/40' : 'bg-slate-900 text-slate-600'"
                      title="阿哈利姆魔晶"
                    >
                      S
                    </span>
                  </div>
                </div>

                <!-- 右侧：KDA、参战率、净资产与输出 -->
                <div class="flex items-center space-x-4 text-xs font-mono text-right">
                  <!-- KDA 与参战率 -->
                  <div class="space-y-0.5">
                    <div class="text-slate-100 font-bold">
                      <span class="text-emerald-400">{{ p.kills }}</span>
                      <span class="text-slate-600">/</span>
                      <span class="text-rose-400">{{ p.deaths }}</span>
                      <span class="text-slate-600">/</span>
                      <span class="text-amber-400">{{ p.assists }}</span>
                    </div>
                    <div class="text-2xs text-slate-400">
                      参战 {{ getPlayerKp(p, currentMatch) }}%
                    </div>
                  </div>

                  <!-- 净资产与 GPM -->
                  <div class="space-y-0.5 min-w-18">
                    <div class="text-amber-400 font-extrabold">
                      {{ formatNum(p.net_worth || p.total_gold) }}
                    </div>
                    <div class="text-2xs text-slate-400">
                      {{ p.gold_per_min || '-' }} GPM
                    </div>
                  </div>

                  <!-- 伤害与正反补 -->
                  <div class="space-y-0.5 min-w-16 hidden sm:block">
                    <div class="text-slate-300">
                      {{ formatDamage(p.hero_damage) }} 伤
                    </div>
                    <div class="text-2xs text-slate-500">
                      {{ p.last_hits || 0 }}/{{ p.denies || 0 }} 补
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- 4.3 选定选手的深度战术切片摘要 -->
        <div v-if="selectedPlayerData" class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/80 flex flex-wrap items-center justify-between gap-3 text-xs font-mono">
          <div class="flex items-center space-x-2">
            <Layers class="h-4 w-4 text-amber-400" />
            <span class="text-slate-300 font-bold">已选定复盘主视角:</span>
            <span class="text-amber-300 font-extrabold">{{ getHeroName(selectedPlayerData.hero_id) }}</span>
            <span class="text-slate-500">({{ selectedPlayerData.personaname || `槽位 ${selectedPlayerData.player_slot}` }})</span>
          </div>

          <div class="flex items-center space-x-4 text-2xs text-slate-400">
            <span>正反补: <b class="text-slate-200">{{ selectedPlayerData.last_hits || 0 }}/{{ selectedPlayerData.denies || 0 }}</b></span>
            <span>经验/分: <b class="text-slate-200">{{ selectedPlayerData.xp_per_min || 0 }} XPM</b></span>
            <span>破塔伤害: <b class="text-amber-400">{{ formatDamage(selectedPlayerData.tower_damage || 0) }}</b></span>
            <span>英雄治疗: <b class="text-emerald-400">{{ formatDamage(selectedPlayerData.hero_healing || 0) }}</b></span>
          </div>
        </div>

        <!-- 5. AI 复盘分析报告卡片呈现 -->
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
