<script setup lang="ts">
/**
 * 英雄图鉴与全景展台视图 (src/views/HeroesView.vue)
 * 职责：
 * 1. 127 英雄官网网格展台与段位天梯数据环境切换
 * 2. 英雄详情路由化与全景展台：组合 HeroStatsCard、AbilityShowcase、HeroBuildPanel
 * 3. 严格契合开工铁律第 11 条（127 英雄权威常数与属性规范）及零伪造空态守卫
 */

import type { HeroMatchupRecommendation, RecommendedItem } from '@/api/types/player.ts'
import type { PatchInfo } from '@/services/gameDataService.ts'
import type { HeroStat } from '@/types'
import type { OfficialHero } from '@/utils/abilityTextResolver.ts'
import { ArrowLeft, Check, ChevronDown } from '@lucide/vue'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { playerApi } from '@/api/player.ts'
import SmartImage from '@/components/common/SmartImage.vue'
import { HERO_ALIASES } from '@/data/aliases.ts'
import { getHeroGuideData } from '@/data/heroGuides.ts'
import { HERO_CHINESE_NAMES } from '@/data/heroNames.ts'
import { dataSyncService } from '@/services/dataSyncService.ts'
import { gameDataService } from '@/services/gameDataService.ts'
import { useAppStore } from '@/stores'
import { getHeroCdnUrl, getHeroIconUrl, getItemIconUrl } from '@/utils/assetUrl.ts'
import { calculateLevel1Stats } from '@/utils/heroStats.ts'
import AbilityShowcase from '@/views/heroes/components/AbilityShowcase.vue'
import HeroBuildPanel from '@/views/heroes/components/HeroBuildPanel.vue'
import HeroStatsCard from '@/views/heroes/components/HeroStatsCard.vue'

defineOptions({ name: 'HeroesView' })

const RANK_OPTIONS = [
  { id: 8, label: '冠绝一世', icon: '/assets/icons/rank_icon_8.png' },
  { id: 7, label: '超凡入圣', icon: '/assets/icons/rank_icon_7.png' },
  { id: 6, label: '万古流芳', icon: '/assets/icons/rank_icon_6.png' },
  { id: 5, label: '传奇', icon: '/assets/icons/rank_icon_5.png' },
  { id: 4, label: '统帅', icon: '/assets/icons/rank_icon_4.png' },
  { id: 3, label: '中军', icon: '/assets/icons/rank_icon_3.png' },
  { id: 2, label: '卫士', icon: '/assets/icons/rank_icon_2.png' },
  { id: 1, label: '先锋', icon: '/assets/icons/rank_icon_1.png' },
]

const SORT_OPTIONS = [
  { id: 'win_desc', label: '按胜率最高' },
  { id: 'pick_desc', label: '按热度最高' },
  { id: 'id_asc', label: '按英雄编号' },
] as const

const ATTR_TABS = [
  { id: 'all', label: '全部', icon: '/assets/icons/hero_universal.png' },
  { id: 'str', label: '力量', icon: '/assets/icons/hero_strength.png' },
  { id: 'agi', label: '敏捷', icon: '/assets/icons/hero_agility.png' },
  { id: 'int', label: '智力', icon: '/assets/icons/hero_intelligence.png' },
  { id: 'all_attr', label: '全才', icon: '/assets/icons/hero_universal.png' },
]

const appStore = useAppStore()
const route = useRoute()
const router = useRouter()

const heroes = ref<HeroStat[]>([])
const officialDetails = ref<Record<number, OfficialHero>>({})
const patchInfo = ref<PatchInfo | null>(null)
const selectedAttr = ref<string>('all')
const isRankDropdownOpen = ref<boolean>(false)
const rankDropdownRef = ref<HTMLElement | null>(null)
const isSortDropdownOpen = ref<boolean>(false)
const sortDropdownRef = ref<HTMLElement | null>(null)
const activeHero = ref<HeroStat | null>(null)
const loading = ref(true)

const selectedRank = computed<number>({
  get: () => appStore.settings.hero_rank_tier ?? 7,
  set: (val: number) => {
    appStore.updateSettings({ hero_rank_tier: val })
  },
})

const sortMode = computed<'win_desc' | 'pick_desc' | 'id_asc'>({
  get: () => appStore.settings.hero_sort_mode || 'win_desc',
  set: (val: 'win_desc' | 'pick_desc' | 'id_asc') => {
    appStore.updateSettings({ hero_sort_mode: val })
  },
})

const currentPatch = computed(
  () => dataSyncService.state.currentPatch || patchInfo.value?.version || '',
)

const currentRankOption = computed(() => {
  return RANK_OPTIONS.find(r => r.id === selectedRank.value) || RANK_OPTIONS[1]
})

const currentSortOption = computed(() => {
  return SORT_OPTIONS.find(s => s.id === sortMode.value) || SORT_OPTIONS[0]
})

function selectRank(rankId: number) {
  selectedRank.value = rankId
  isRankDropdownOpen.value = false
}

function selectSort(mode: 'win_desc' | 'pick_desc' | 'id_asc') {
  sortMode.value = mode
  isSortDropdownOpen.value = false
}

function handleClickOutside(e: MouseEvent) {
  if (rankDropdownRef.value && !rankDropdownRef.value.contains(e.target as Node)) {
    isRankDropdownOpen.value = false
  }
  if (sortDropdownRef.value && !sortDropdownRef.value.contains(e.target as Node)) {
    isSortDropdownOpen.value = false
  }
}

function getAttrIcon(attr: string): string {
  switch (attr) {
    case 'str':
      return '/assets/icons/hero_strength.png'
    case 'agi':
      return '/assets/icons/hero_agility.png'
    case 'int':
      return '/assets/icons/hero_intelligence.png'
    default:
      return '/assets/icons/hero_universal.png'
  }
}

function getHeroDisplayImg(hero: HeroStat): string {
  // 架构铁律：优先走本地高速缓存路由 (/cache/heroes/xxx.png)，秒级极速渲染；
  // 官方 crops_img 仅作为 SmartImage 的 cdn-src 兜底，杜绝 127 英雄直连海外 CDN 排队卡顿
  return getHeroIconUrl(hero.name)
}

function getWinRate(hero: HeroStat): string {
  const rankKey = selectedRank.value
  let pick = (hero as any)[`${rankKey}_pick`] || 0
  let win = (hero as any)[`${rankKey}_win`] || 0
  if (rankKey === 8 && pick === 0) {
    pick = (hero as any)['7_pick'] || 0
    win = (hero as any)['7_win'] || 0
  }
  if (pick <= 0) return '0.0'
  return ((win / pick) * 100).toFixed(1)
}

function getPickCount(hero: HeroStat): number {
  const rankKey = selectedRank.value
  let pick = (hero as any)[`${rankKey}_pick`] || 0
  if (rankKey === 8 && pick === 0) pick = (hero as any)['7_pick'] || 0
  return pick
}

const filteredHeroes = computed(() => {
  const list = heroes.value.filter((h) => {
    if (selectedAttr.value === 'str' && h.primary_attr !== 'str') return false
    if (selectedAttr.value === 'agi' && h.primary_attr !== 'agi') return false
    if (selectedAttr.value === 'int' && h.primary_attr !== 'int') return false
    if (selectedAttr.value === 'all_attr' && h.primary_attr !== 'all') return false

    if (appStore.searchQuery.trim()) {
      const q = appStore.searchQuery.toLowerCase().trim()
      const nameMatch
        = h.localized_name.toLowerCase().includes(q)
          || h.name.toLowerCase().includes(q)
      const aliases = HERO_ALIASES[String(h.id)] || []
      const aliasMatch = aliases.some(a => a.toLowerCase().includes(q))
      return nameMatch || aliasMatch
    }
    return true
  })

  return list.sort((a, b) => {
    if (sortMode.value === 'win_desc') {
      return parseFloat(getWinRate(b)) - parseFloat(getWinRate(a))
    } else if (sortMode.value === 'pick_desc') {
      return getPickCount(b) - getPickCount(a)
    }
    return a.id - b.id
  })
})

const attackTypeIcon = computed(() =>
  activeHero.value?.attack_type === 'Melee'
    ? '/assets/icons/melee.svg'
    : '/assets/icons/ranged.svg',
)

const attackTypeLabel = computed(() =>
  activeHero.value?.attack_type === 'Melee' ? '近战攻击' : '远程攻击',
)

const activeDetails = computed<OfficialHero | null>(() => {
  if (!activeHero.value) return null
  return officialDetails.value[activeHero.value.id] || null
})

const activeLevel1Stats = computed(() => {
  if (!activeHero.value) return null
  return calculateLevel1Stats(activeHero.value)
})

const dynamicRec = ref<HeroMatchupRecommendation | null>(null)
const loadingRec = ref(false)
const recError = ref<string | null>(null)

async function loadHeroBuildRecommendations(heroId: number) {
  dynamicRec.value = null
  recError.value = null
  loadingRec.value = true
  try {
    const rec = await playerApi.getHeroMatchupRecommendations(heroId, [])
    if (rec && (rec.startItems?.length || rec.earlyItems?.length || rec.midItems?.length || rec.lateItems?.length)) {
      dynamicRec.value = rec
    } else {
      recError.value = '暂未检索到该英雄的大数据出装统计'
    }
  } catch (err: any) {
    console.warn('[HeroesView] 获取英雄热门出装推荐失败 (零伪造，实事求是呈现空态):', err)
    recError.value = '网络未连接或未能获取到 OpenDota 大数据出装'
  } finally {
    loadingRec.value = false
  }
}

watch(
  () => activeHero.value?.id,
  (newId) => {
    if (newId) {
      loadHeroBuildRecommendations(newId)
    } else {
      dynamicRec.value = null
      recError.value = null
      loadingRec.value = false
    }
  },
  { immediate: true },
)

const heroTips = computed(() => {
  if (!activeHero.value) return []
  return getHeroGuideData(activeHero.value.id, activeHero.value.primary_attr).tips
})

const heroBuilds = computed(() => {
  if (!dynamicRec.value) return []

  const toBuildItems = (items: RecommendedItem[]) => items.map(it => ({
    name: it.itemKey,
    dname: it.itemName,
    cost: 0,
    img: getItemIconUrl(it.itemKey),
  }))

  return [
    { stage: '出门装备 (大数据热选)', items: toBuildItems(dynamicRec.value.startItems) },
    { stage: '前期核心 (对局前中期)', items: toBuildItems(dynamicRec.value.earlyItems) },
    { stage: '中期核心 (战局质变)', items: toBuildItems(dynamicRec.value.midItems) },
    { stage: '后期质变 (决战神装)', items: toBuildItems(dynamicRec.value.lateItems) },
  ].filter(s => s.items.length > 0)
})

const currentPatchNotesForHero = computed(() => {
  if (!activeHero.value || !patchInfo.value) return {}
  return patchInfo.value.heroPatchNotes?.[activeHero.value.id] || {}
})

// 路由与英雄激活联动
function syncHeroFromRoute() {
  const paramId = route.params.id
  if (paramId && heroes.value.length > 0) {
    const numId = Number(paramId)
    const found = heroes.value.find(h => h.id === numId)
    if (found) {
      activeHero.value = found
      dataSyncService.markHeroAsSeen(found.id)
      return
    }
  }
  if (!paramId) {
    activeHero.value = null
  }
}

watch(() => route.params.id, () => {
  syncHeroFromRoute()
})

function openHeroDetail(hero: HeroStat) {
  activeHero.value = hero
  dataSyncService.markHeroAsSeen(hero.id)
  router.push(`/heroes/${hero.id}`)
}

function handleBackToList() {
  activeHero.value = null
  router.push('/heroes')
}

onMounted(async () => {
  document.addEventListener('click', handleClickOutside)
  try {
    await appStore.ensureSettingsLoaded()
    const [statsData, officialDict, pInfo] = await Promise.all([
      gameDataService.getHeroStats(),
      gameDataService.getOfficialHeroesDetails(),
      gameDataService.getPatchInfo(),
    ])
    patchInfo.value = pInfo
    heroes.value = statsData.map(h => ({
      ...h,
      localized_name: HERO_CHINESE_NAMES[h.id] || h.localized_name,
    }))
    officialDetails.value = officialDict
    syncHeroFromRoute()
  } catch (err) {
    console.error('加载英雄数据失败', err)
  } finally {
    loading.value = false
  }
})

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
})
</script>

<template>
  <div
    v-if="loading"
    class="text-base text-dota-muted bg-dota-bg flex flex-1 select-none items-center justify-center"
  >
    <div class="mr-3 border-dota-gold border-b-2 border-t-2 rounded-full h-8 w-8 animate-spin" />
    加载英雄权威数据中...
  </div>

  <!-- 视图 2：英雄全景详情展台 (支持 /heroes/:id 路由) -->
  <div
    v-else-if="activeHero"
    class="bg-dota-bg flex flex-1 flex-col select-none overflow-hidden"
  >
    <!-- 顶部导航条 -->
    <div class="px-5 border-b border-dota-border bg-dota-surface flex shrink-0 h-11 shadow-sm items-center justify-between z-10">
      <button
        class="text-xs text-dota-gold font-medium px-3 py-1 border border-dota-border rounded-lg bg-dota-panel flex cursor-pointer shadow-sm transition-all items-center space-x-2 hover:text-dota-goldLight hover:bg-dota-hover"
        @click="handleBackToList"
      >
        <ArrowLeft class="h-3.5 w-3.5" />
        <span>返回英雄列表</span>
      </button>

      <div class="text-xs flex items-center space-x-4">
        <div class="flex items-center space-x-1.5">
          <img :src="attackTypeIcon" alt="攻击类型" class="h-3.5 w-3.5 object-contain">
          <span class="text-dota-text font-medium">{{ attackTypeLabel }}</span>
        </div>
        <div class="bg-dota-border h-3 w-[1px]" />
        <div class="text-dota-gold flex items-center space-x-1">
          <span class="text-dota-subtle">上手难度:</span>
          <span class="tracking-wider font-bold font-mono">
            {{ '◆'.repeat(activeDetails?.complexity || 1) }}{{ '◇'.repeat(Math.max(0, 3 - (activeDetails?.complexity || 1))) }}
          </span>
        </div>
      </div>
    </div>

    <!-- 详情内容可滚动区 (英雄信息与技能信息 4:6 分栏紧凑布局) -->
    <div class="p-4 flex-1 overflow-y-auto">
      <div class="flex flex-row gap-4 w-full items-start">
        <!-- 左侧 40%：英雄信息 (基础属性、战术定位、实战战略与出装路线) -->
        <div style="flex: 4 4 0%; min-width: 0;" class="flex flex-col space-y-4">
          <!-- 1. 英雄基础属性与背景传记卡片 -->
          <HeroStatsCard
            :hero="activeHero"
            :official-details="activeDetails"
            :level1-stats="activeLevel1Stats"
          />

          <!-- 2. 宏观打法与分阶段推荐出装 (100% 真实大数据，绝无任何离线假出装兜底) -->
          <HeroBuildPanel
            :tips="heroTips"
            :builds="heroBuilds"
            :current-patch="currentPatch"
            :loading="loadingRec"
            :error="recError"
            :source="dynamicRec?.source"
            @retry="activeHero?.id && loadHeroBuildRecommendations(activeHero.id)"
          />
        </div>

        <!-- 右侧 60%：技能信息 (天赋树、先天技能与官网标准 AbilityRight 展台) -->
        <div style="flex: 6 6 0%; min-width: 0;" class="flex flex-col space-y-4">
          <AbilityShowcase
            :active-hero-id="activeHero.id"
            :active-hero-name="activeHero.name"
            :active-details="activeDetails"
            :current-patch="currentPatch"
            :patch-notes="currentPatchNotesForHero"
            :tips="heroTips"
          />
        </div>
      </div>
    </div>
  </div>

  <!-- 视图 1：全屏 127 英雄图鉴大图网格 (route: /heroes) -->
  <div v-else class="bg-dota-bg flex flex-1 flex-col select-none overflow-hidden">
    <!-- 顶部分类与段位过滤条 -->
    <div class="px-4 border-b border-dota-border bg-dota-surface flex shrink-0 gap-4 h-12 items-center justify-between">
      <div class="flex items-center space-x-1">
        <button
          v-for="tab in ATTR_TABS"
          :key="tab.id"
          class="text-xs font-medium px-3 py-1.5 rounded-lg flex cursor-pointer transition-all items-center space-x-1.5"
          :class="[selectedAttr === tab.id ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40 shadow-sm' : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text']"
          @click="selectedAttr = tab.id"
        >
          <img :src="tab.icon" :alt="tab.label" class="h-4 w-4 object-contain">
          <span>{{ tab.label }}</span>
        </button>
      </div>

      <!-- 中间段位选择 (带段位官方图标) -->
      <div class="text-xs flex items-center space-x-2">
        <span class="text-dota-subtle shrink-0">段位环境:</span>
        <div ref="rankDropdownRef" class="relative">
          <button
            type="button"
            class="group text-xs text-dota-gold px-2.5 border border-dota-border rounded-lg bg-dota-panel flex h-7.5 w-30 cursor-pointer shadow-sm transition-colors items-center justify-between hover:border-dota-gold/70"
            @click="isRankDropdownOpen = !isRankDropdownOpen"
          >
            <div class="flex min-w-0 items-center space-x-1.5">
              <img :src="currentRankOption.icon" :alt="currentRankOption.label" class="shrink-0 h-4.5 w-4.5 object-contain drop-shadow">
              <span class="font-bold text-left truncate">{{ currentRankOption.label }}</span>
            </div>
            <ChevronDown class="text-dota-muted ml-1 shrink-0 h-3 w-3 transition-transform group-hover:text-dota-gold" :class="{ 'rotate-180': isRankDropdownOpen }" />
          </button>

          <div
            v-if="isRankDropdownOpen"
            class="py-1 border border-dota-border rounded-xl bg-dota-surface w-36 shadow-2xl left-0 top-8.5 absolute z-50 overflow-hidden backdrop-blur-md"
          >
            <button
              v-for="r in RANK_OPTIONS"
              :key="r.id"
              class="text-xs px-2.5 py-1.5 flex w-full cursor-pointer transition-colors items-center space-x-2 hover:bg-dota-hover"
              :class="selectedRank === r.id ? 'text-dota-gold font-bold bg-dota-panel/60' : 'text-dota-text'"
              @click="selectRank(r.id)"
            >
              <img :src="r.icon" :alt="r.label" class="shrink-0 h-4 w-4 object-contain">
              <span class="truncate">{{ r.label }}</span>
            </button>
          </div>
        </div>
      </div>

      <!-- 右侧排序切换 (与段位环境完全统一的暗黑 HUD 下拉菜单) -->
      <div class="flex items-center space-x-2">
        <span class="text-xs text-dota-subtle">排序:</span>
        <div ref="sortDropdownRef" class="relative">
          <button
            type="button"
            class="group text-xs text-dota-gold px-2.5 border border-dota-border rounded-lg bg-dota-panel flex h-7.5 w-30 cursor-pointer shadow-sm transition-colors items-center justify-between hover:border-dota-gold/70"
            @click="isSortDropdownOpen = !isSortDropdownOpen"
          >
            <span class="font-bold text-left truncate">{{ currentSortOption.label }}</span>
            <ChevronDown class="text-dota-muted ml-1 shrink-0 h-3 w-3 transition-transform group-hover:text-dota-gold" :class="{ 'rotate-180': isSortDropdownOpen }" />
          </button>

          <div
            v-if="isSortDropdownOpen"
            class="py-1 border border-dota-border rounded-xl bg-dota-surface w-32 shadow-2xl right-0 top-8.5 absolute z-50 overflow-hidden backdrop-blur-md"
          >
            <button
              v-for="s in SORT_OPTIONS"
              :key="s.id"
              class="text-xs px-2.5 py-1.5 flex w-full cursor-pointer transition-colors items-center justify-between hover:bg-dota-hover"
              :class="sortMode === s.id ? 'text-dota-gold font-bold bg-dota-panel/60' : 'text-dota-text'"
              @click="selectSort(s.id)"
            >
              <span class="truncate">{{ s.label }}</span>
              <Check v-if="sortMode === s.id" class="text-dota-gold ml-1 shrink-0 h-3.5 w-3.5" />
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- 127 英雄高清大图网格展示 -->
    <div class="p-4 flex-1 [scrollbar-gutter:stable] overflow-y-auto">
      <div class="gap-4 grid grid-cols-2 2xl:grid-cols-7 lg:grid-cols-5 md:grid-cols-4 sm:grid-cols-3 xl:grid-cols-6">
        <div
          v-for="hero in filteredHeroes"
          :key="hero.id"
          class="group border border-dota-border/80 rounded-2xl bg-dota-surface flex flex-col cursor-pointer transition-all duration-300 relative overflow-hidden hover:border-dota-gold hover:shadow-[0_8px_25px_rgba(0,0,0,0.7)] hover:-translate-y-1"
          @click="openHeroDetail(hero)"
        >
          <!-- 英雄大图展示区域 (400x250 官方全貌图 / Steam CDN 高清大图) -->
          <div class="bg-dota-card aspect-[16/10] relative overflow-hidden">
            <SmartImage
              :local-src="getHeroDisplayImg(hero)"
              :cdn-src="officialDetails[hero.id]?.crops_img || getHeroCdnUrl(hero.name)"
              :alt="hero.localized_name"
              class="h-full w-full transition-transform duration-500 object-cover group-hover:scale-110"
              loading="lazy"
            />
            <!-- 渐变阴影底色 -->
            <div class="pointer-events-none inset-0 absolute from-dota-surface to-black/30 via-transparent bg-gradient-to-t" />

            <!-- 主属性图标徽章 (左上角) -->
            <div class="p-1 border border-white/10 rounded-lg bg-black/70 shadow left-2 top-2 absolute backdrop-blur-xs">
              <img
                :src="getAttrIcon(hero.primary_attr)"
                :alt="hero.primary_attr"
                class="h-3.5 w-3.5 object-contain"
              >
            </div>

            <!-- 增量更新徽章 (左上角属性右侧) -->
            <div
              v-if="dataSyncService.isHeroNew(hero.id)"
              class="text-[9px] text-slate-950 tracking-wider font-black px-1.5 py-0.5 border border-amber-300 rounded uppercase shadow-[0_0_10px_rgba(245,158,11,0.6)] left-9 top-2 absolute animate-pulse from-amber-500 to-rose-500 bg-gradient-to-r"
            >
              {{ currentPatch ? `v${currentPatch} 更新` : '新补丁更新' }}
            </div>

            <!-- 胜率徽章 (右上角) -->
            <div
              class="text-xs text-dota-gold font-bold font-mono px-2 py-0.5 border border-dota-gold/40 rounded-lg bg-black/80 shadow right-2 top-2 absolute backdrop-blur-xs"
            >
              {{ getWinRate(hero) }}%
            </div>

            <!-- 底部英雄名称与热度信息条 -->
            <div class="flex pointer-events-none items-center bottom-2 left-2.5 right-2.5 justify-between absolute">
              <span class="text-sm text-white tracking-wide font-extrabold truncate drop-shadow-md group-hover:text-dota-gold">
                {{ hero.localized_name }}
              </span>
              <span class="text-xs text-dota-subtle font-mono px-1.5 py-0.5 rounded bg-black/60 shrink-0 backdrop-blur-xs">
                {{ (getPickCount(hero) / 1000).toFixed(0) }}k场
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
