<script setup lang="ts">
/**
 * 赛后战局与 AI 深度复盘视图 (src/views/AnalysisView.vue)
 * 职责：
 * 1. 结构骨架容器与多级路由/视图状态切换 (比赛列表总览 ⇄ 单场全屏独立详情页)
 * 2. 遵守开工铁律第 8 条（零伪造原则）：未配置玩家 ID 呈现真实空态，严禁 Mock 假数据
 * 3. 驱动 OpenDota 战绩拉取、比赛详情加载与大模型深度复盘调度
 */

import type { ItemMeta, OpenDotaMatchDetail } from '@/api/types'
import type { MatchReviewResult } from '@/services/aiCoachService.ts'
import type { HeroUsageItem } from '@/views/analysis/TopHeroesPanel.vue'
import { Activity, User } from '@lucide/vue'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { playerApi } from '@/api'
import { getHeroChineseName } from '@/data/heroNames.ts'
import { aiCoachService } from '@/services/aiCoachService.ts'
import { gameDataService } from '@/services/gameDataService.ts'
import { fetchMatchDetails, fetchPlayerRecentMatchesList } from '@/services/playerProfileService.ts'
import { useAppStore } from '@/stores/app.ts'
import { useAnalysisStats } from '@/views/analysis/composables/useAnalysisStats.ts'
import MatchDetailPanel from '@/views/analysis/MatchDetailPanel.vue'
import MatchSearchBar from '@/views/analysis/MatchSearchBar.vue'
import PlayerProfileBanner from '@/views/analysis/PlayerProfileBanner.vue'
import RecentMatchList from '@/views/analysis/RecentMatchList.vue'
import TopHeroesPanel from '@/views/analysis/TopHeroesPanel.vue'

defineOptions({ name: 'AnalysisView' })

const props = defineProps<{
  id?: string
}>()

const appStore = useAppStore()
const route = useRoute()
const router = useRouter()

/** 搜索框输入的比赛数字编号 */
const searchMatchId = ref('')
/** 正在查询比赛数据中状态 */
const searching = ref(false)
/** 查询比赛报错信息 */
const searchError = ref('')
/** 当前正在分析的比赛原始数据实体 (非空时进入单场深度详情页) */
const currentMatch = ref<OpenDotaMatchDetail | null>(null)

/** 选定分析视角的玩家槽位 (若为 null 则自动取绑定账号或 0 号位) */
const selectedPlayerSlot = ref<number | null>(null)
/** AI 深度复盘分析报告实体 */
const reviewResult = ref<MatchReviewResult | null>(null)
/** 正在生成 AI 复盘报告中状态 */
const reviewing = ref(false)

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

// 五维雷达图与战绩 4 宫格数据计算 composable
const { statsOverview, radarDimensions } = useAnalysisStats(recentMatches)

/** 是否处于比赛独立详情页视图模式 */
const isDetailMode = computed(() => {
  return currentMatch.value !== null
})

onMounted(async () => {
  await appStore.ensureSettingsLoaded()
  itemDict.value = await gameDataService.getItemDict()
  if (appStore.settings.dota_player_id) {
    playerProfile.value.name = appStore.settings.dota_player_name || ''
    playerProfile.value.avatar = appStore.settings.dota_player_avatar || ''
    await Promise.all([
      fetchPlayerRecentMatches(appStore.settings.dota_player_id),
      syncPlayerProfile(appStore.settings.dota_player_id),
    ])
  }

  // 路由参数联动：如果 URL 带有比赛 ID，秒级载入比赛详情并打开新详情页
  const paramId = props.id || (route.params.id as string | undefined)
  if (paramId && paramId.trim()) {
    await handleSearchMatch(paramId.trim())
  }
})

// 监听路由参数变化，支持前进后退
watch(
  () => route.params.id,
  async (newId) => {
    if (newId && typeof newId === 'string' && newId.trim()) {
      if (String(currentMatch.value?.match_id) !== newId.trim()) {
        await handleSearchMatch(newId.trim())
      }
    } else if (!newId && currentMatch.value) {
      currentMatch.value = null
      searchMatchId.value = ''
      reviewResult.value = null
    }
  },
)

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
 * 同步玩家个人档案
 */
async function syncPlayerProfile(accountId: string) {
  try {
    // 统一出口：经 Go 侧 PlayerService 代理 OpenDota，前端不直连第三方
    const raw = await playerApi.getPlayerCard(accountId)
    if (!raw) return
    const data = JSON.parse(raw)
    if (data?.profile) {
      playerProfile.value.name = data.profile.personaname || appStore.settings.dota_player_name || ''
      playerProfile.value.avatar = data.profile.avatarfull || data.profile.avatar || appStore.settings.dota_player_avatar || ''
      playerProfile.value.rankTier = data.rank_tier
      playerProfile.value.leaderboardRank = data.leaderboard_rank
      appStore.updateSettings({
        dota_player_name: playerProfile.value.name,
        dota_player_avatar: playerProfile.value.avatar,
      })
    }
  } catch (e) {
    console.warn('[AnalysisView] 获取玩家个人主页失败:', e)
  }
}

/**
 * 拉取绑定玩家的近期比赛列表
 */
async function fetchPlayerRecentMatches(accountId: string) {
  if (!accountId || !accountId.trim()) return
  loadingRecent.value = true
  try {
    const matches = await fetchPlayerRecentMatchesList(accountId.trim())
    recentMatches.value = matches || []
  } catch (err: any) {
    appStore.showToast(`拉取玩家战报失败: ${err.message || '网络连接异常'}`, 'warning')
  } finally {
    loadingRecent.value = false
  }
}

/**
 * 计算常用高频英雄池 (取前 6)
 */
const topHeroes = computed<HeroUsageItem[]>(() => {
  const counts: Record<number, { count: number, wins: number, lastWin?: boolean }> = {}
  for (const m of recentMatches.value) {
    const hid = m.hero_id
    if (!hid) continue
    const isWin = m.radiant_win === (m.player_slot < 128)
    if (!counts[hid]) {
      counts[hid] = { count: 0, wins: 0, lastWin: isWin }
    }
    counts[hid].count++
    if (isWin) counts[hid].wins++
  }

  const sorted = Object.entries(counts)
    .sort((a, b) => b[1].count - a[1].count)
    .slice(0, 6)

  return sorted.map(([hidStr, data]) => {
    const hid = Number(hidStr)
    return {
      heroId: hid,
      name: getHeroChineseName(hid) || `英雄 #${hid}`,
      count: data.count,
      winRate: Math.round((data.wins / data.count) * 100),
      lastWin: data.lastWin,
    }
  })
})

/**
 * 计算近期使用英雄序列 (按最近出场时间倒序去重，取前 6)
 */
const recentHeroes = computed<HeroUsageItem[]>(() => {
  const counts: Record<number, { count: number, wins: number, lastWin?: boolean, lastTime: number }> = {}
  const order: number[] = []

  for (const m of recentMatches.value) {
    const hid = m.hero_id
    if (!hid) continue
    const isWin = m.radiant_win === (m.player_slot < 128)
    if (!counts[hid]) {
      counts[hid] = { count: 0, wins: 0, lastWin: isWin, lastTime: m.start_time || 0 }
      order.push(hid)
    }
    counts[hid].count++
    if (isWin) counts[hid].wins++
  }

  return order.slice(0, 6).map((hid) => {
    const data = counts[hid]
    return {
      heroId: hid,
      name: getHeroChineseName(hid) || `英雄 #${hid}`,
      count: data.count,
      winRate: Math.round((data.wins / data.count) * 100),
      lastWin: data.lastWin,
      lastPlayedAt: data.lastTime,
    }
  })
})

/**
 * 依据筛选英雄过后的对局列表
 */
const displayedRecentMatches = computed(() => {
  if (!filterHeroId.value) return recentMatches.value
  return recentMatches.value.filter(m => m.hero_id === filterHeroId.value)
})

/**
 * 载入指定比赛详情并切换至独立比赛详情页
 */
async function handleSearchMatch(targetMatchId?: string) {
  const mid = (targetMatchId || searchMatchId.value).trim()
  if (!mid) {
    searchError.value = '请输入有效的比赛编号 (如 7987654321)'
    return
  }

  searchError.value = ''
  searching.value = true
  searchMatchId.value = mid

  try {
    const match = await fetchMatchDetails(mid)
    if (!match || !match.players || match.players.length === 0) {
      throw new Error('未检索到该比赛详细数据，请确认编号是否正确且对局已结算')
    }

    currentMatch.value = match as OpenDotaMatchDetail

    // 默认高亮选择当前已绑定的玩家操纵的槽位
    const boundId = appStore.settings.dota_player_id
    let foundSlot: number | null = null
    if (boundId) {
      const p = match.players.find((pl: any) => String(pl.account_id) === String(boundId))
      if (p) foundSlot = p.player_slot
    }
    selectedPlayerSlot.value = foundSlot !== null ? foundSlot : match.players[0]?.player_slot ?? 0

    // 重置之前的复盘报告
    reviewResult.value = null

    // 路由同步推入，确保 URL 可分享与刷新
    if (route.params.id !== mid) {
      router.push(`/analysis/match/${mid}`).catch(() => {})
    }

    appStore.showToast(`成功载入比赛 #${mid} 深度战报！`, 'success')
  } catch (err: any) {
    currentMatch.value = null
    searchError.value = err.message || '查询比赛数据失败，请稍后重试'
    appStore.showToast(searchError.value, 'error')
  } finally {
    searching.value = false
  }
}

/**
 * 从比赛详情页返回近期对局列表主视图
 */
function handleBackToList() {
  currentMatch.value = null
  searchMatchId.value = ''
  reviewResult.value = null
  if (route.path !== '/analysis') {
    router.push('/analysis').catch(() => {})
  }
}

/**
 * 触发针对选定玩家的 AI 赛后深度复盘生成
 */
async function handleGenerateReview() {
  if (!currentMatch.value || selectedPlayerSlot.value === null || reviewing.value) return

  const reviewStatus = aiCoachService.isAiReviewConfigured()
  if (!reviewStatus.available) {
    appStore.showToast(reviewStatus.reason || 'AI 引擎未配置，无法生成复盘报告', 'warning')
    return
  }

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

const handleRunAiReview = handleGenerateReview

defineExpose({
  handleRunAiReview,
  handleBackToList,
})
</script>

<template>
  <div class="text-slate-100 bg-dota-bg flex flex-1 flex-col select-none overflow-hidden">
    <!-- 主视口内容区 (紧凑布局，无顶部冗余标题) -->
    <div class="p-4 flex-1 overflow-y-auto space-y-4 sm:p-5">
      <!-- 模式 A：比赛全景独立详情页 (点击比赛列表或搜索后打开新详情页) -->
      <div v-if="isDetailMode && currentMatch" class="space-y-4">
        <MatchDetailPanel
          :match="currentMatch"
          :selected-player-slot="selectedPlayerSlot"
          :bound-player-id="appStore.settings.dota_player_id"
          :item-dict="itemDict"
          :reviewing="reviewing"
          :review-result="reviewResult"
          @select-slot="(slot) => (selectedPlayerSlot = slot)"
          @generate-review="handleGenerateReview"
          @back-to-list="handleBackToList"
        />
      </div>

      <!-- 检索比赛详情加载中骨架反馈 -->
      <div
        v-else-if="searching && !currentMatch"
        class="p-12 text-center border border-slate-800 rounded-2xl bg-slate-900/80 flex flex-col items-center justify-center space-y-3"
      >
        <Activity class="text-amber-400 h-8 w-8 animate-spin" />
        <div class="text-sm text-slate-200 font-bold">正在解析比赛 #{{ searchMatchId }} 数据...</div>
      </div>

      <!-- 模式 B：近期战绩历史列表与玩家大盘主视图 -->
      <div v-else class="space-y-4">
        <!-- 1. 比赛搜索输入栏组件 (极简紧凑无外框卡片) -->
        <MatchSearchBar
          v-model="searchMatchId"
          :searching="searching"
          :search-error="searchError"
          @search="handleSearchMatch()"
        />

        <!-- 2. 玩家战术身份档案、雷达大盘与场均胜率 (单卡片紧凑一体化) -->
        <PlayerProfileBanner
          v-if="appStore.settings.dota_player_id"
          :name="playerProfile.name || appStore.settings.dota_player_name || ''"
          :avatar="playerProfile.avatar || appStore.settings.dota_player_avatar || ''"
          :player-id="appStore.settings.dota_player_id"
          :loading="loadingRecent"
          :stats-overview="statsOverview"
          :radar-dimensions="radarDimensions"
          @refresh="fetchPlayerRecentMatches(appStore.settings.dota_player_id)"
        />

        <!-- 2.1 未绑定账号的真实空态守卫 (零伪造规范) -->
        <div
          v-else
          class="text-slate-400 p-8 text-center border border-slate-700/80 rounded-2xl bg-slate-900/90 flex flex-col shadow-xl items-center justify-center space-y-2"
        >
          <User class="text-slate-600 mb-1 h-10 w-10" />
          <p class="text-xs text-slate-300 font-medium">尚未配置绑定的 Dota 2 玩家数字 ID</p>
          <p class="text-2xs text-slate-500">前往【基础设置】绑定您的 32 位好友 ID，即可在此自动生成近期胜率、战术五维雷达图与常用英雄池</p>
        </div>

        <!-- 战绩载入中状态 -->
        <div
          v-if="loadingRecent && recentMatches.length === 0 && appStore.settings.dota_player_id"
          class="text-slate-400 py-8 flex flex-col items-center justify-center space-y-2"
        >
          <Activity class="text-amber-400 h-8 w-8 animate-spin" />
          <p class="text-xs font-mono">正在从 OpenDota 同步玩家近期真实天梯战报...</p>
        </div>

        <!-- 3. 下半区：常用英雄池 (左) + 近期对局历史列表 (右) 双栏紧凑对称排布 -->
        <div v-else-if="recentMatches.length > 0" class="gap-4 grid grid-cols-1 items-start xl:grid-cols-12">
          <!-- 常用英雄池与近期使用合并面板 (占 4 列) -->
          <div class="flex flex-col xl:col-span-4">
            <TopHeroesPanel
              :top-heroes="topHeroes"
              :recent-heroes="recentHeroes"
              :filter-hero-id="filterHeroId"
              @select-hero="(hid) => (filterHeroId = filterHeroId === hid ? null : hid)"
              @clear-filter="filterHeroId = null"
            />
          </div>

          <!-- 近期对局历史卡片列表 (占 8 列) -->
          <div class="flex flex-col xl:col-span-8">
            <RecentMatchList
              :matches="displayedRecentMatches"
              :total-matches-count="recentMatches.length"
              :selected-match-id="searchMatchId"
              :loading-detail="searching"
              @select-match="handleSearchMatch"
            />
          </div>
        </div>

        <!-- 战绩无公开数据的真实兜底 -->
        <div
          v-else-if="appStore.settings.dota_player_id && !loadingRecent"
          class="text-xs text-slate-500 font-mono py-6 text-center"
        >
          未检索到该玩家公开天梯战绩，请确保在 Dota 2 客户端中勾选了【公开比赛数据】
        </div>
      </div>
    </div>
  </div>
</template>
