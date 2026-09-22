<script setup lang="ts">
/**
 * 英雄数据库与出装对策视图 (src/views/HeroesView.vue)
 * 职责：
 * 1. 127 位 Dota 2 官方英雄全量展示，严格遵守 100% 静态常数中文名兜底规范
 * 2. 区分全才、力量、敏捷、智力四维属性标签，支持天梯 8 阶段位胜率/登场率动态筛选
 * 3. 严格实现 1 级基础属性公式推导、官方技能图解、魔晶神杖强化与天赋树呈现
 * 4. 融合对线期/核心期/备选期推荐出装树与实战进阶打法教学
 */
import type { HeroStat } from '@/types'
import type {
  OfficialAbility,
  OfficialHero,
} from '../utils/abilityTextResolver'
import { ArrowLeft, ChevronDown, Clock, Droplet, Shield, ShoppingBag, Sparkles } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { HERO_ALIASES } from '../data/aliases'
import { getHeroGuideData } from '../data/heroGuides'
import { HERO_CHINESE_NAMES } from '../data/heroNames'
import { dataSyncService } from '../services/dataSyncService'
import { useAppStore } from '@/stores'
import {
  isEffectiveScepterUpgrade,
  isEffectiveShardUpgrade,
  resolveAbilityText,
  resolveTalentText,
} from '../utils/abilityTextResolver'
import { calculateLevel1Stats } from '../utils/heroStats'
import { cleanHtml } from '../utils/htmlCleaner'
import {
  getAbilityCdnUrl,
  getAbilityIconUrl,
  getHeroCdnUrl,
  getHeroIconUrl,
  handleImageFallback,
} from '../utils/assetUrl'

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

const ATTR_TABS = [
  { id: 'all', label: '全部', icon: '/assets/icons/hero_universal.png' },
  { id: 'str', label: '力量', icon: '/assets/icons/hero_strength.png' },
  { id: 'agi', label: '敏捷', icon: '/assets/icons/hero_agility.png' },
  { id: 'int', label: '智力', icon: '/assets/icons/hero_intelligence.png' },
  { id: 'all_attr', label: '全才', icon: '/assets/icons/hero_universal.png' },
]

const ROLE_MAP: Record<string, string> = {
  Carry: '核心',
  Support: '辅助',
  Nuker: '爆发',
  Disabler: '控制',
  Jungler: '打野',
  Durable: '耐久',
  Escape: '逃生',
  Pusher: '推进',
  Initiator: '先手',
}

function getRoleChineseName(role: string): string {
  return ROLE_MAP[role] || role
}

const appStore = useAppStore()
const heroes = ref<HeroStat[]>([])
const officialDetails = ref<Record<number, OfficialHero>>({})
const selectedAttr = ref<string>('all')
const selectedRank = ref<number>(7)
const isRankDropdownOpen = ref<boolean>(false)
const sortMode = ref<'win_desc' | 'pick_desc' | 'id_asc'>('win_desc')
const activeHero = ref<HeroStat | null>(null)
const selectedAbilityName = ref<string | null>(null)
const selectedShowcaseTab = ref<'ability' | 'talents'>('ability')
const isBioExpanded = ref<boolean>(false)
const loading = ref(true)

const currentRankOption = computed(() => {
  return RANK_OPTIONS.find(r => r.id === selectedRank.value) || RANK_OPTIONS[1]
})

function selectRank(rankId: number) {
  selectedRank.value = rankId
  isRankDropdownOpen.value = false
}

function getAttrIcon(attr: string): string {
  switch (attr) {
    case 'str': return '/assets/icons/hero_strength.png'
    case 'agi': return '/assets/icons/hero_agility.png'
    case 'int': return '/assets/icons/hero_intelligence.png'
    case 'all':
    default: return '/assets/icons/hero_universal.png'
  }
}

function getHeroDisplayImg(hero: HeroStat): string {
  const official = officialDetails.value[hero.id]
  if (official && official.crops_img) {
    return official.crops_img
  }
  return getHeroIconUrl(hero.name)
}

function openHeroDetail(hero: HeroStat) {
  activeHero.value = hero
  selectedAbilityName.value = null
  selectedShowcaseTab.value = 'ability'
  isBioExpanded.value = false
  dataSyncService.markHeroAsSeen(hero.id)
}

function selectAbility(name: string) {
  selectedAbilityName.value = name
  selectedShowcaseTab.value = 'ability'
}

function selectTalents() {
  selectedShowcaseTab.value = 'talents'
}

onMounted(async () => {
  try {
    const [statsRes, officialRes] = await Promise.all([
      fetch('/assets/data/herostats.json'),
      fetch('/assets/data/heroes-details-official.json'),
    ])
    const statsData: HeroStat[] = await statsRes.json()
    const officialData = await officialRes.json()

    // 100% 静态常数纠错兜底英雄中文名
    const enriched = statsData.map(h => ({
      ...h,
      localized_name: HERO_CHINESE_NAMES[h.id] || h.localized_name,
    }))
    heroes.value = enriched

    if (officialData?.heroes) {
      const dict: Record<number, OfficialHero> = {}
      officialData.heroes.forEach((h: OfficialHero) => {
        dict[h.id] = h
      })
      officialDetails.value = dict
    }
  } catch (err) {
    console.error('加载英雄数据失败', err)
  } finally {
    loading.value = false
  }
})

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
  if (rankKey === 8 && pick === 0) {
    pick = (hero as any)['7_pick'] || 0
  }
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
      const nameMatch = h.localized_name.toLowerCase().includes(q) || h.name.toLowerCase().includes(q)
      const aliases = HERO_ALIASES[String(h.id)] || []
      const aliasMatch = aliases.some(a => a.toLowerCase().includes(q))
      return nameMatch || aliasMatch
    }
    return true
  })

  return list.sort((a, b) => {
    if (sortMode.value === 'win_desc') {
      const aRate = parseFloat(getWinRate(a))
      const bRate = parseFloat(getWinRate(b))
      return bRate - aRate
    }
    if (sortMode.value === 'pick_desc') {
      const aPick = getPickCount(a)
      const bPick = getPickCount(b)
      return bPick - aPick
    }
    return a.id - b.id
  })
})

const activeDetails = computed(() => {
  return activeHero.value ? officialDetails.value[activeHero.value.id] : null
})

const activeLevel1Stats = computed(() => {
  return activeHero.value ? calculateLevel1Stats(activeHero.value) : null
})

// 先天技能 (放在天赋树旁边)
const innateAbility = computed(() => {
  const abilities = activeDetails.value?.abilities || []
  return abilities.find(a => a.ability_is_innate || a.is_innate) || null
})

// 常规与升级技能列表 (排除纯先天，展示在右侧)
const nonInnateAbilities = computed(() => {
  const abilities = activeDetails.value?.abilities || []
  return abilities.filter(a => !a.ability_is_innate && !a.is_innate)
})

// 当前选中的技能 (支持点击先天技能联动展示)
const activeSelectedAbility = computed<OfficialAbility | null>(() => {
  if (selectedAbilityName.value) {
    if (innateAbility.value && selectedAbilityName.value === innateAbility.value.name) {
      return innateAbility.value
    }
    const found = nonInnateAbilities.value.find(a => a.name === selectedAbilityName.value)
    if (found) return found
  }
  return nonInnateAbilities.value[0] || innateAbility.value || null
})

// 英雄实战机制技巧与出装
const heroGuide = computed(() => {
  if (!activeHero.value) {
    return { tips: [], builds: [] }
  }
  return getHeroGuideData(activeHero.value.id, activeHero.value.primary_attr)
})
</script>

<template>
  <div v-if="loading" class="text-dota-muted bg-dota-bg text-base flex flex-1 items-center justify-center">
    <div class="border-dota-gold mr-3 border-b-2 border-t-2 rounded-full h-8 w-8 animate-spin" />
    正在加载 127 英雄官方图鉴与技能数据库...
  </div>

  <!-- 视图 2：单独英雄全屏官方风格详情视图 (紧凑顶部 + 天赋树在左技能在右 + 遮罩图标 + 下方机制技巧与热门出装) -->
  <div v-else-if="activeHero" class="bg-dota-bg flex flex-1 flex-col select-none overflow-hidden">
    <!-- 顶部导航条 (紧凑化：无英雄编号、无英文代号) -->
    <div class="border-dota-border bg-dota-surface px-5 border-b flex shrink-0 h-11 shadow-sm items-center justify-between z-10">
      <button
        class="bg-dota-panel hover:bg-dota-hover border-dota-border text-dota-gold hover:text-dota-goldLight text-xs font-medium px-3 py-1 border rounded-lg flex cursor-pointer shadow-sm transition-all items-center space-x-2"
        @click="activeHero = null"
      >
        <ArrowLeft class="h-3.5 w-3.5" />
        <span>返回英雄列表</span>
      </button>

      <div class="text-xs flex items-center space-x-4">
        <div class="flex items-center space-x-1.5">
          <img
            :src="activeHero.attack_type === 'Melee' ? '/assets/icons/melee.svg' : '/assets/icons/ranged.svg'"
            alt="攻击类型"
            class="h-3.5 w-3.5 object-contain"
          >
          <span class="text-dota-text font-medium">
            {{ activeHero.attack_type === 'Melee' ? '近战攻击' : '远程攻击' }}
          </span>
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

    <!-- 详情内容可滚动大画幅区 -->
    <div class="p-5 flex-1 overflow-y-auto space-y-5">
      <!-- 1. 英雄资料顶部：缩小紧凑布局 (无编号/无英文/无HTML标签) -->
      <div class="from-dota-panel via-dota-panel/90 to-dota-surface border-dota-border p-5 border rounded-2xl flex flex-col gap-5 shadow-xl items-start justify-between relative overflow-hidden bg-gradient-to-r lg:flex-row">
        <!-- 背景半透光效与立绘 -->
        <div
          v-if="activeHero.img"
          class="opacity-15 w-1/2 pointer-events-none bottom-0 right-0 top-0 absolute bg-cover bg-right bg-no-repeat"
          :style="{ backgroundImage: `url(${getHeroIconUrl(activeHero.name)})` }"
        />

        <!-- 左侧：紧凑立绘头像、中文名称、核心定位、纯中文介绍与折叠官方背景传记 -->
        <div class="flex max-w-xl items-start relative z-10 space-x-4">
          <img
            :src="getHeroIconUrl(activeHero.name)"
            :alt="activeHero.localized_name"
            class="border-dota-border/80 border-2 rounded-xl shrink-0 h-16 w-24 shadow-xl object-cover"
            @error="(e) => handleImageFallback(e, getHeroCdnUrl(activeHero?.name || ''))"
          >
          <div class="space-y-1">
            <div class="flex items-center space-x-2.5">
              <h1 class="text-dota-gold text-2xl tracking-wide font-extrabold">
                {{ activeHero.localized_name }}
              </h1>
              <span
                v-if="dataSyncService.isHeroNew(activeHero.id)"
                class="text-[10px] text-slate-950 tracking-wider font-black px-2 py-0.5 border border-amber-300 rounded uppercase shadow animate-pulse from-amber-500 to-rose-500 bg-gradient-to-r"
              >
                官方更新
              </span>
            </div>

            <div class="text-dota-muted text-xs flex flex-wrap gap-y-1 items-center space-x-2">
              <span class="text-dota-gold font-medium">核心定位:</span>
              <span>{{ activeHero.roles.map(getRoleChineseName).join(' · ') }}</span>
            </div>

            <!-- 背景简述 (彻底清理 HTML 标签) -->
            <p v-if="activeDetails?.hype_loc" class="text-dota-subtle text-xs leading-relaxed pt-0.5 line-clamp-2">
              {{ cleanHtml(activeDetails.hype_loc) }}
            </p>

            <!-- 官方背景传记故事 (移至英雄介绍中，默认折叠隐藏) -->
            <div v-if="activeDetails?.bio_loc" class="pt-1">
              <button
                type="button"
                class="text-dota-subtle hover:text-dota-gold text-xs flex cursor-pointer transition-colors items-center space-x-1"
                @click="isBioExpanded = !isBioExpanded"
              >
                <ChevronDown class="h-3.5 w-3.5 transition-transform duration-200" :class="{ 'rotate-180': isBioExpanded }" />
                <span>{{ isBioExpanded ? '收起背景故事' : '展开背景故事...' }}</span>
              </button>
              <div v-show="isBioExpanded" class="border-dota-border/60 text-dota-muted text-xs leading-relaxed mt-2 p-3 border rounded-xl bg-black/50 max-h-52 whitespace-pre-line shadow-inner overflow-y-auto">
                {{ cleanHtml(activeDetails.bio_loc) }}
              </div>
            </div>
          </div>
        </div>

        <!-- 右侧：属性卡片 + 生命魔法条 + 8格衍生属性网格 -->
        <div class="font-sans flex shrink-0 flex-col w-full relative z-10 space-y-2.5 lg:w-auto">
          <!-- 三大属性横排卡片 (力量、敏捷、智力) -->
          <div class="text-xs gap-2 grid grid-cols-3">
            <!-- 力量卡片 -->
            <div
              class="p-2 border rounded-xl flex transition-all items-center space-x-2" :class="[
                activeHero.primary_attr === 'str'
                  ? 'bg-red-950/40 border-dota-red/80 shadow-[0_0_12px_rgba(239,68,68,0.25)]'
                  : 'bg-dota-surface/70 border-dota-border/60',
              ]"
            >
              <img src="/assets/icons/hero_strength.png" alt="力量" class="h-5 w-5 object-contain">
              <div>
                <div class="text-dota-subtle text-[10px] font-medium flex items-center space-x-1">
                  <span>力量</span>
                  <span v-if="activeHero.primary_attr === 'str'" class="bg-dota-red/30 text-dota-red text-[8px] font-bold px-1 rounded">主属性</span>
                </div>
                <div class="text-dota-text text-xs font-bold font-mono">
                  {{ activeDetails?.str_base || activeHero.base_str }}
                  <span class="text-dota-subtle text-[10px] font-normal">+{{ activeDetails?.str_gain?.toFixed(1) || activeHero.str_gain?.toFixed(1) }}</span>
                </div>
              </div>
            </div>

            <!-- 敏捷卡片 -->
            <div
              class="p-2 border rounded-xl flex transition-all items-center space-x-2" :class="[
                activeHero.primary_attr === 'agi'
                  ? 'bg-emerald-950/40 border-dota-green/80 shadow-[0_0_12px_rgba(16,185,129,0.25)]'
                  : 'bg-dota-surface/70 border-dota-border/60',
              ]"
            >
              <img src="/assets/icons/hero_agility.png" alt="敏捷" class="h-5 w-5 object-contain">
              <div>
                <div class="text-dota-subtle text-[10px] font-medium flex items-center space-x-1">
                  <span>敏捷</span>
                  <span v-if="activeHero.primary_attr === 'agi'" class="bg-dota-green/30 text-dota-green text-[8px] font-bold px-1 rounded">主属性</span>
                </div>
                <div class="text-dota-text text-xs font-bold font-mono">
                  {{ activeDetails?.agi_base || activeHero.base_agi }}
                  <span class="text-dota-subtle text-[10px] font-normal">+{{ activeDetails?.agi_gain?.toFixed(1) || activeHero.agi_gain?.toFixed(1) }}</span>
                </div>
              </div>
            </div>

            <!-- 智力卡片 -->
            <div
              class="p-2 border rounded-xl flex transition-all items-center space-x-2" :class="[
                activeHero.primary_attr === 'int'
                  ? 'bg-blue-950/40 border-dota-cyan/80 shadow-[0_0_12px_rgba(56,189,248,0.25)]'
                  : 'bg-dota-surface/70 border-dota-border/60',
              ]"
            >
              <img src="/assets/icons/hero_intelligence.png" alt="智力" class="h-5 w-5 object-contain">
              <div>
                <div class="text-dota-subtle text-[10px] font-medium flex items-center space-x-1">
                  <span>智力</span>
                  <span v-if="activeHero.primary_attr === 'int'" class="bg-dota-cyan/30 text-dota-cyan text-[8px] font-bold px-1 rounded">主属性</span>
                </div>
                <div class="text-dota-text text-xs font-bold font-mono">
                  {{ activeDetails?.int_base || activeHero.base_int }}
                  <span class="text-dota-subtle text-[10px] font-normal">+{{ activeDetails?.int_gain?.toFixed(1) || activeHero.int_gain?.toFixed(1) }}</span>
                </div>
              </div>
            </div>
          </div>

          <!-- 生命槽与魔法槽 -->
          <div v-if="activeLevel1Stats" class="text-xs font-mono space-y-1">
            <div class="text-dota-green text-[11px] font-bold px-2.5 border border-emerald-800/60 rounded-lg bg-emerald-950/60 flex h-5 shadow-inner items-center justify-between">
              <span>生命值 {{ activeLevel1Stats.hp }}</span>
              <span class="text-[10px] text-emerald-400 font-normal">+{{ activeLevel1Stats.hpRegen }}/s</span>
            </div>
            <div class="text-dota-cyan text-[11px] font-bold px-2.5 border border-blue-800/60 rounded-lg bg-blue-950/60 flex h-5 shadow-inner items-center justify-between">
              <span>魔法值 {{ activeLevel1Stats.mana }}</span>
              <span class="text-[10px] text-cyan-300 font-normal">+{{ activeLevel1Stats.manaRegen }}/s</span>
            </div>
          </div>

          <!-- 官方 8 大衍生属性网格 -->
          <div v-if="activeLevel1Stats" class="text-xs font-mono gap-1.5 grid grid-cols-4">
            <div class="bg-dota-surface/80 border-dota-border/60 text-[11px] p-1 border rounded-lg flex items-center space-x-1" title="基础攻击力">
              <img src="/assets/icons/icon_damage.png" alt="攻击" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeLevel1Stats.attackMin }}-{{ activeLevel1Stats.attackMax }}</span>
            </div>
            <div class="bg-dota-surface/80 border-dota-border/60 text-[11px] p-1 border rounded-lg flex items-center space-x-1" title="物理护甲">
              <img src="/assets/icons/icon_armor.png" alt="护甲" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeLevel1Stats.armor }}</span>
            </div>
            <div class="bg-dota-surface/80 border-dota-border/60 text-[11px] p-1 border rounded-lg flex items-center space-x-1" title="基础移动速度">
              <img src="/assets/icons/icon_movement_speed.png" alt="移速" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeLevel1Stats.moveSpeed }}</span>
            </div>
            <div class="bg-dota-surface/80 border-dota-border/60 text-[11px] p-1 border rounded-lg flex items-center space-x-1" title="基础攻击间隔 (BAT)">
              <img src="/assets/icons/icon_attack_time.png" alt="攻速" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeDetails?.attack_rate || 1.7 }}s</span>
            </div>
            <div class="bg-dota-surface/80 border-dota-border/60 text-[11px] p-1 border rounded-lg flex items-center space-x-1" title="攻击距离">
              <img src="/assets/icons/icon_attack_range.png" alt="射程" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeDetails?.attack_range || activeHero.base_attack_min }}</span>
            </div>
            <div class="bg-dota-surface/80 border-dota-border/60 text-[11px] p-1 border rounded-lg flex items-center space-x-1" title="基础魔法抗性">
              <img src="/assets/icons/icon_magic_resist.png" alt="魔抗" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeDetails?.magic_resistance || 25 }}%</span>
            </div>
            <div class="bg-dota-surface/80 border-dota-border/60 text-[11px] p-1 border rounded-lg flex items-center space-x-1" title="转身速率">
              <img src="/assets/icons/icon_turn_rate.png" alt="转身" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeDetails?.turn_rate || 0.6 }}</span>
            </div>
            <div class="bg-dota-surface/80 border-dota-border/60 text-[11px] p-1 border rounded-lg flex items-center space-x-1" title="视野范围">
              <img src="/assets/icons/icon_vision.png" alt="视野" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeDetails?.sight_range_day || 1800 }}/{{ activeDetails?.sight_range_night || 800 }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 2. 核心区：天赋树、先天技能、技能列表一体化联动展台 -->
      <div class="bg-dota-panel border-dota-border/90 p-5 border rounded-2xl shadow-xl space-y-4">
        <div class="border-dota-border/60 pb-3 border-b flex flex-wrap gap-3 items-center">
          <!-- 左侧：天赋树入口 + 技能列表 -->
          <div class="flex flex-wrap gap-2 items-center">

            <!-- 英雄天赋树入口（最左，仅一个图标，与技能图标同尺寸） -->
            <button
              type="button"
              class="group p-1 border rounded-xl flex flex-col cursor-pointer transition-all duration-200 items-center relative"
              :class="[
          selectedShowcaseTab === 'talents'
            ? 'bg-dota-surface border-dota-gold text-dota-gold shadow-[0_0_12px_rgba(229,193,88,0.35)] scale-105'
            : 'bg-dota-surface/50 border-dota-border/60 text-dota-muted hover:border-dota-gold/60 hover:text-dota-text',
        ]"
              @click="selectTalents"
            >
              <div class="bg-dota-card border border-dota-gold/40 rounded-lg h-9 w-9 relative overflow-hidden flex items-center justify-center">
                <Shield class="text-dota-gold h-5 w-5" />
              </div>
              <span class="text-[9px] font-medium mt-0.5 text-center max-w-[50px] truncate">
          天赋树
        </span>
            </button>

            <!-- 分隔线 + 先天技能 + 常规技能 -->
            <div class="border-dota-border/50 pl-1 border-l flex items-center space-x-1.5">

              <!-- 先天技能入口：与常规技能完全一致的展示方式，文字在图标下方 -->
              <button
                v-if="innateAbility"
                type="button"
                class="group p-1 border rounded-xl flex flex-col cursor-pointer transition-all duration-200 items-center relative"
                :class="[
            selectedShowcaseTab === 'ability' && activeSelectedAbility?.name === innateAbility.name
              ? 'bg-dota-surface border-emerald-400 text-emerald-300 shadow-[0_0_12px_rgba(16,185,129,0.35)] scale-105'
              : 'bg-dota-surface/50 border-dota-border/60 text-dota-muted hover:border-emerald-500/60 hover:text-emerald-300',
          ]"
                @click="selectAbility(innateAbility.name)"
              >
                <div class="bg-dota-card rounded-lg h-9 w-9 relative overflow-hidden">
                  <img
                    :src="getAbilityIconUrl(innateAbility.name)"
                    :alt="innateAbility.name_loc"
                    class="h-full w-full object-cover"
                    @error="(e) => handleImageFallback(e, getAbilityCdnUrl(innateAbility?.name || ''))"
                  >
                  <img
                    src="/assets/icons/innate_icon.png"
                    alt="先天遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute z-10 object-contain"
                  >
                </div>
                <span class="text-[9px] font-medium mt-0.5 text-center max-w-[50px] truncate">
            {{ innateAbility.name_loc || innateAbility.name }}
          </span>
              </button>

              <!-- 常规技能列表入口 -->
              <button
                v-for="ability in nonInnateAbilities"
                :key="ability.id"
                type="button"
                class="group p-1 border rounded-xl flex flex-col cursor-pointer transition-all duration-200 items-center relative"
                :class="[
            selectedShowcaseTab === 'ability' && activeSelectedAbility?.name === ability.name
              ? 'bg-dota-surface border-dota-gold text-dota-gold shadow-[0_0_12px_rgba(229,193,88,0.35)] scale-105'
              : 'bg-dota-surface/50 border-dota-border/60 text-dota-muted hover:border-dota-gold/60 hover:text-dota-text',
          ]"
                @click="selectAbility(ability.name)"
              >
                <div class="bg-dota-card rounded-lg h-9 w-9 relative overflow-hidden">
                  <img
                    :src="getAbilityIconUrl(ability.name)"
                    :alt="ability.name_loc || ability.name"
                    class="h-full w-full object-cover"
                    @error="(e) => handleImageFallback(e, getAbilityCdnUrl(ability.name))"
                  >
                  <!-- 神杖遮罩 -->
                  <img
                    v-if="ability.ability_has_scepter || ability.ability_is_granted_by_scepter"
                    src="/assets/icons/aghs_scepter.png"
                    alt="神杖遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute z-10 object-contain"
                  >
                  <!-- 魔晶遮罩 -->
                  <img
                    v-else-if="ability.ability_has_shard || ability.ability_is_granted_by_shard"
                    src="/assets/icons/aghs_shard.png"
                    alt="魔晶遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute z-10 object-contain"
                  >
                </div>
                <span class="text-[9px] font-medium mt-0.5 text-center max-w-[50px] truncate">
            {{ ability.name_loc || ability.name }}
          </span>
              </button>
            </div>
          </div>
        </div>

        <!-- 展台全景详细内容区 (切换展示技能详情 或 天赋树分支) -->
        <div class="bg-dota-surface/90 border-dota-border/70 p-5 border rounded-xl shadow-lg">
          <!-- 模式 A：技能详细全景展示 (先天技能或常规技能) -->
          <div v-if="selectedShowcaseTab === 'ability' && activeSelectedAbility" class="space-y-4">
            <!-- 技能顶栏：大图 + 标题 + 属性 + 冷却魔耗 -->
            <div class="border-dota-border/50 pb-3.5 border-b flex flex-col gap-3 justify-between sm:flex-row sm:items-center">
              <div class="flex items-center space-x-3.5">
                <div class="bg-dota-card border-dota-border border-2 rounded-xl shrink-0 h-15 w-15 shadow-md relative overflow-hidden">
                  <img
                    :src="getAbilityIconUrl(activeSelectedAbility.name)"
                    :alt="activeSelectedAbility.name_loc"
                    class="h-full w-full object-cover"
                    @error="(e) => handleImageFallback(e, getAbilityCdnUrl(activeSelectedAbility?.name || ''))"
                  >
                  <!-- 先天/神杖/魔晶遮罩 -->
                  <img
                    v-if="activeSelectedAbility.ability_is_innate"
                    src="/assets/icons/innate_icon.png"
                    alt="先天遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute z-10 object-contain"
                  >
                  <img
                    v-else-if="activeSelectedAbility.ability_has_scepter || activeSelectedAbility.ability_is_granted_by_scepter"
                    src="/assets/icons/aghs_scepter.png"
                    alt="神杖遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute z-10 object-contain"
                  >
                  <img
                    v-else-if="activeSelectedAbility.ability_has_shard || activeSelectedAbility.ability_is_granted_by_shard"
                    src="/assets/icons/aghs_shard.png"
                    alt="魔晶遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute z-10 object-contain"
                  >
                </div>

                <div class="space-y-1">
                  <div class="flex flex-wrap gap-y-1 items-center space-x-2">
                    <h3 class="text-dota-goldLight text-lg font-extrabold">
                      {{ activeSelectedAbility.name_loc || activeSelectedAbility.name }}
                    </h3>

                    <!-- 先天技能标签 (纯中文) -->
                    <span v-if="activeSelectedAbility.ability_is_innate" class="text-[11px] text-emerald-300 font-medium px-2 py-0.5 border border-emerald-700/60 rounded bg-emerald-900/60">
                先天技能
              </span>

                    <!-- 伤害类型 -->
                    <span v-if="activeSelectedAbility.damage === 1" class="text-[11px] text-orange-400 font-medium px-2 py-0.5 border border-orange-800/60 rounded bg-orange-950/60">
                物理伤害
              </span>
                    <span v-else-if="activeSelectedAbility.damage === 2" class="text-[11px] text-blue-400 font-medium px-2 py-0.5 border border-blue-800/60 rounded bg-blue-950/60">
                魔法伤害
              </span>
                    <span v-else-if="activeSelectedAbility.damage === 3 || activeSelectedAbility.damage === 4" class="text-[11px] text-amber-300 font-medium px-2 py-0.5 border border-amber-700/60 rounded bg-amber-950/60">
                纯粹伤害
              </span>

                    <!-- 减益免疫状态 -->
                    <span v-if="activeSelectedAbility.immunity === 1 || activeSelectedAbility.immunity === 3" class="text-[11px] text-emerald-400 font-medium px-2 py-0.5 border border-emerald-800/50 rounded bg-emerald-950/50">
                无视减益免疫
              </span>
                  </div>
                </div>
              </div>

              <!-- 冷却与魔法消耗 -->
              <div class="bg-dota-panel/90 border-dota-border/60 text-xs font-mono px-3.5 py-1.5 border rounded-lg flex shrink-0 items-center self-start space-x-3 sm:self-auto">
                <div v-if="activeSelectedAbility.cooldowns && activeSelectedAbility.cooldowns.length > 0 && activeSelectedAbility.cooldowns[0] > 0" class="text-dota-gold flex items-center space-x-1">
                  <Clock class="h-3.5 w-3.5" />
                  <span class="font-bold">{{ activeSelectedAbility.cooldowns.join(' / ') }}s</span>
                </div>
                <div v-if="activeSelectedAbility.cooldowns?.length && activeSelectedAbility.mana_costs?.length" class="bg-dota-border h-3 w-[1px]" />
                <div v-if="activeSelectedAbility.mana_costs && activeSelectedAbility.mana_costs.length > 0 && activeSelectedAbility.mana_costs[0] > 0" class="text-dota-cyan flex items-center space-x-1">
                  <Droplet class="h-3.5 w-3.5 fill-current" />
                  <span class="font-bold">{{ activeSelectedAbility.mana_costs.join(' / ') }}</span>
                </div>
              </div>
            </div>

            <!-- 技能机制描述 -->
            <div class="text-dota-text bg-dota-panel/60 border-dota-border/40 text-xs leading-relaxed p-4 border rounded-xl whitespace-pre-line">
              {{ cleanHtml(resolveAbilityText(activeSelectedAbility.desc_loc, activeSelectedAbility, 'none', activeDetails || undefined)) }}
            </div>

            <!-- 神杖/魔晶强化卡片 (去英文标签，纯中文高质感卡片) -->
            <div class="pt-1 gap-3.5 grid grid-cols-1 md:grid-cols-2">
              <!-- 阿哈利姆神杖强化卡片 -->
              <div
                v-if="isEffectiveScepterUpgrade(activeSelectedAbility, activeDetails || undefined)"
                class="from-dota-gold/15 to-dota-panel/80 border-dota-gold/40 text-dota-goldLight text-xs p-3.5 border rounded-xl flex shadow-md items-start bg-gradient-to-r space-x-3"
              >
                <div class="border-dota-gold/50 p-1 border rounded-lg bg-black/60 shrink-0 shadow">
                  <img
                    src="/assets/icons/ultimate_scepter.png"
                    alt="阿哈利姆神杖"
                    class="h-8 w-11 object-contain"
                  >
                </div>
                <div class="flex-1 space-y-1">
                  <div class="text-dota-gold text-xs font-bold flex items-center space-x-1.5">
                    <span>阿哈利姆神杖强化效果</span>
                    <span class="bg-dota-gold/20 text-dota-gold border-dota-gold/40 text-[9px] px-1.5 py-0.2 border rounded">神杖加成</span>
                  </div>
                  <p class="text-dota-text text-[11px] leading-relaxed">
                    {{ cleanHtml(resolveAbilityText(activeSelectedAbility.scepter_loc, activeSelectedAbility, 'scepter', activeDetails || undefined)) }}
                  </p>
                </div>
              </div>

              <!-- 阿哈利姆魔晶强化卡片 -->
              <div
                v-if="isEffectiveShardUpgrade(activeSelectedAbility, activeDetails || undefined)"
                class="from-dota-cyan/15 to-dota-panel/80 border-dota-cyan/40 text-dota-cyan text-xs p-3.5 border rounded-xl flex shadow-md items-start bg-gradient-to-r space-x-3"
              >
                <div class="border-dota-cyan/50 p-1 border rounded-lg bg-black/60 shrink-0 shadow">
                  <img
                    src="/assets/icons/aghanims_shard.png"
                    alt="阿哈利姆魔晶"
                    class="h-8 w-11 object-contain"
                  >
                </div>
                <div class="flex-1 space-y-1">
                  <div class="text-dota-cyan text-xs font-bold flex items-center space-x-1.5">
                    <span>阿哈利姆魔晶</span>
                    <span class="bg-dota-cyan/20 text-dota-cyan border-dota-cyan/40 text-[9px] px-1.5 py-0.2 border rounded" />
                  </div>
                  <p class="text-dota-text text-[11px] leading-relaxed">
                    {{ cleanHtml(resolveAbilityText(activeSelectedAbility.shard_loc, activeSelectedAbility, 'shard', activeDetails || undefined)) }}
                  </p>
                </div>
              </div>
            </div>
          </div>

          <!-- 模式 B：英雄天赋树全景展示 (居中大画幅左右分支) -->
          <div v-else-if="selectedShowcaseTab === 'talents'" class="py-2 space-y-4">
            <div class="border-dota-border/50 pb-2.5 border-b flex items-center justify-between">
              <div class="flex items-center space-x-2">
                <Shield class="text-dota-gold h-4.5 w-4.5" />
                <span class="text-dota-gold text-sm font-bold">{{ activeHero.localized_name }} 完整天赋树</span>
              </div>
              <span class="text-dota-subtle text-xs">对局每升 5 级可选择一项左侧或右侧天赋</span>
            </div>

            <div class="mx-auto py-2 max-w-3xl space-y-3">
              <div v-for="(lvl, index) in [25, 20, 15, 10]" :key="lvl" class="flex items-center space-x-2">
                <!-- 左翼天赋 -->
                <div
                  class="bg-dota-panel hover:bg-dota-hover border-dota-border/60 hover:border-dota-gold/50 text-dota-text text-xs leading-relaxed p-3 text-right border rounded-xl flex-1 shadow-sm transition-all"
                  :title="cleanHtml(resolveTalentText(activeDetails?.talents?.[index * 2 + 1], activeDetails || undefined))"
                >
                  {{ cleanHtml(resolveTalentText(activeDetails?.talents?.[index * 2 + 1], activeDetails || undefined)) }}
                </div>

                <!-- 居中等级徽章 -->
                <div class="text-xs text-slate-950 font-black font-mono border-2 border-amber-300 rounded-full flex shrink-0 h-9 w-9 shadow-lg items-center justify-center from-amber-400 to-amber-600 bg-gradient-to-b">
                  {{ lvl }}
                </div>

                <!-- 右翼天赋 -->
                <div
                  class="bg-dota-panel hover:bg-dota-hover border-dota-border/60 hover:border-dota-gold/50 text-dota-text text-xs leading-relaxed p-3 text-left border rounded-xl flex-1 shadow-sm transition-all"
                  :title="cleanHtml(resolveTalentText(activeDetails?.talents?.[index * 2], activeDetails || undefined))"
                >
                  {{ cleanHtml(resolveTalentText(activeDetails?.talents?.[index * 2], activeDetails || undefined)) }}
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 3. 页面下方：英雄技巧和热门出装 (放在页面下方) -->
      <div class="pt-1 gap-5 grid grid-cols-1 lg:grid-cols-2">
        <!-- 英雄核心机制技巧 (Mechanic Tips) -->
        <div class="bg-dota-panel border-dota-border p-4 border rounded-xl shadow-md space-y-3">
          <div class="border-dota-border/60 pb-2.5 border-b flex items-center space-x-2">
            <Sparkles class="text-dota-gold h-4 w-4" />
            <h3 class="text-dota-goldLight text-xs font-bold">
              英雄实战技巧与机制解析
            </h3>
          </div>

          <div class="space-y-2.5">
            <div
              v-for="(tip, idx) in heroGuide.tips"
              :key="idx"
              class="bg-dota-surface/60 border-dota-border/50 p-3 border rounded-lg space-y-1"
            >
              <div class="text-dota-gold text-xs font-bold flex items-center space-x-1.5">
                <span class="bg-dota-gold rounded-full h-1.5 w-1.5" />
                <span>{{ tip.title }}</span>
              </div>
              <p class="text-dota-text text-[11px] leading-relaxed pl-3">
                {{ cleanHtml(tip.content) }}
              </p>
            </div>
          </div>
        </div>

        <!-- 热门实战出装推荐 (Item Builds) -->
        <div class="bg-dota-panel border-dota-border p-4 border rounded-xl shadow-md space-y-3">
          <div class="border-dota-border/60 pb-2.5 border-b flex items-center space-x-2">
            <ShoppingBag class="text-dota-gold h-4 w-4" />
            <h3 class="text-dota-goldLight text-xs font-bold">
              推荐与热门出装路线
            </h3>
          </div>

          <div class="space-y-3">
            <div
              v-for="b in heroGuide.builds"
              :key="b.stage"
              class="bg-dota-surface/60 border-dota-border/50 p-2.5 border rounded-lg space-y-2"
            >
              <div class="text-dota-subtle text-[11px] font-bold flex items-center justify-between">
                <span>{{ b.stage }}</span>
                <span class="text-dota-muted text-[10px]">当前 7.41e 热门</span>
              </div>

              <!-- 官网 56x42 规格物品列表 -->
              <div class="flex flex-wrap gap-2 items-center space-x-2">
                <div
                  v-for="item in b.items"
                  :key="item.name"
                  class="bg-dota-panel border-dota-border/80 p-1.5 border rounded flex items-center space-x-1.5"
                  :title="`${item.dname} (${item.cost} 金币)`"
                >
                  <img
                    :src="item.img"
                    :alt="item.dname"
                    class="border-dota-border/60 border rounded-sm shrink-0 h-5 w-7 object-cover"
                    @error="(e) => ((e.target as HTMLImageElement).src = '/assets/icons/aghs_scepter.png')"
                  >
                  <div class="text-[10px] leading-tight">
                    <div class="text-dota-text font-medium">
                      {{ item.dname }}
                    </div>
                    <div class="text-dota-gold text-[9px] font-mono">
                      {{ item.cost }}金
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>

  <!-- 视图 1：全屏铺满 127 英雄大图鉴网格 -->
  <div v-else class="bg-dota-bg flex flex-1 flex-col select-none overflow-hidden">
    <!-- 顶部单行高密度控制栏 -->
    <div class="border-dota-border bg-dota-surface px-4 border-b flex shrink-0 gap-4 h-12 items-center justify-between">
      <!-- 左侧属性分类 Tabs -->
      <div class="flex items-center space-x-1">
        <button
          v-for="tab in ATTR_TABS"
          :key="tab.id"
          class="text-xs font-medium px-3 py-1.5 rounded-lg flex cursor-pointer transition-all items-center space-x-1.5" :class="[
            selectedAttr === tab.id
              ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40 shadow-sm'
              : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text',
          ]"
          @click="selectedAttr = tab.id"
        >
          <img :src="tab.icon" :alt="tab.label" class="h-4 w-4 object-contain">
          <span>{{ tab.label }}</span>
        </button>
      </div>

      <!-- 中间段位选择 (带段位官方图标) -->
      <div class="text-xs flex items-center relative space-x-2">
        <span class="text-dota-subtle">段位环境:</span>
        <button
          type="button"
          class="bg-dota-panel border-dota-border hover:border-dota-gold/70 text-dota-gold group text-xs px-2 border rounded-lg flex h-7.5 cursor-pointer shadow-sm transition-all items-center space-x-1.5"
          @click="isRankDropdownOpen = !isRankDropdownOpen"
        >
          <img
            :src="currentRankOption.icon"
            :alt="currentRankOption.label"
            class="shrink-0 h-4.5 w-4.5 object-contain drop-shadow"
          >
          <span class="group-hover:text-dota-goldLight font-bold">{{ currentRankOption.label }}</span>
          <ChevronDown class="text-dota-muted group-hover:text-dota-gold h-3.5 w-3.5 transition-transform duration-200" :class="{ 'rotate-180': isRankDropdownOpen }" />
        </button>

        <!-- 背景遮罩 (点击外部关闭) -->
        <div v-if="isRankDropdownOpen" class="inset-0 fixed z-40" @click="isRankDropdownOpen = false" />

        <!-- 段位下拉弹窗列表 (带图标) -->
        <div
          v-if="isRankDropdownOpen"
          class="bg-dota-surface border-dota-gold/40 mt-1 py-1 border rounded-xl max-h-72 w-40 shadow-2xl left-16 top-full absolute z-50 overflow-y-auto backdrop-blur-md"
        >
          <div
            v-for="rank in RANK_OPTIONS"
            :key="rank.id"
            class="text-xs px-3 py-1.5 flex cursor-pointer transition-colors items-center space-x-2.5" :class="[
              selectedRank === rank.id
                ? 'bg-dota-panel text-dota-gold font-bold'
                : 'text-dota-text hover:bg-dota-hover hover:text-dota-goldLight',
            ]"
            @click="selectRank(rank.id)"
          >
            <img :src="rank.icon" :alt="rank.label" class="shrink-0 h-4.5 w-4.5 object-contain">
            <span class="flex-1">{{ rank.label }}</span>
            <span v-if="selectedRank === rank.id" class="text-dota-gold text-[10px]">✓</span>
          </div>
        </div>
      </div>

      <!-- 右侧排序规则 -->
      <div class="text-xs flex items-center space-x-1">
        <button
          class="px-2.5 py-1 rounded transition-colors" :class="[
            sortMode === 'win_desc'
              ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40'
              : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text',
          ]"
          @click="sortMode = 'win_desc'"
        >
          胜率最高
        </button>
        <button
          class="px-2.5 py-1 rounded transition-colors" :class="[
            sortMode === 'pick_desc'
              ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40'
              : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text',
          ]"
          @click="sortMode = 'pick_desc'"
        >
          出场最多
        </button>
      </div>
    </div>

    <!-- 127 英雄首页大图鉴卡片网格 (首页使用大图) -->
    <div class="p-4 flex-1 overflow-y-auto">
      <div class="gap-4 grid grid-cols-2 2xl:grid-cols-7 lg:grid-cols-5 md:grid-cols-4 sm:grid-cols-3 xl:grid-cols-6">
        <div
          v-for="hero in filteredHeroes"
          :key="hero.id"
          class="bg-dota-surface border-dota-border/80 hover:border-dota-gold group border rounded-2xl flex flex-col cursor-pointer transition-all duration-300 relative overflow-hidden hover:shadow-[0_8px_25px_rgba(0,0,0,0.7)] hover:-translate-y-1"
          @click="openHeroDetail(hero)"
        >
          <!-- 英雄大图展示区域 (400x250 官方全貌图 / Steam CDN 高清大图) -->
          <div class="bg-dota-card aspect-[16/10] relative overflow-hidden">
            <img
              :src="getHeroDisplayImg(hero)"
              :alt="hero.localized_name"
              class="h-full w-full transition-transform duration-500 object-cover group-hover:scale-110"
              loading="lazy"
              @error="(e) => handleImageFallback(e, getHeroCdnUrl(hero.name))"
            >
            <!-- 渐变阴影底色 -->
            <div class="from-dota-surface pointer-events-none inset-0 absolute to-black/30 via-transparent bg-gradient-to-t" />

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
              官方更新
            </div>

            <!-- 胜率徽章 (右上角) -->
            <div class="text-dota-gold border-dota-gold/40 text-[11px] font-bold font-mono px-2 py-0.5 border rounded-lg bg-black/80 shadow right-2 top-2 absolute backdrop-blur-xs">
              {{ getWinRate(hero) }}%
            </div>

            <!-- 底部英雄名称与出场频次 -->
            <div class="flex pointer-events-none items-center bottom-2 left-2.5 right-2.5 justify-between absolute">
              <span class="group-hover:text-dota-gold text-sm text-white tracking-wide font-extrabold truncate drop-shadow-md">
                {{ hero.localized_name }}
              </span>
              <span class="text-dota-subtle text-[10px] font-mono px-1.5 py-0.5 rounded bg-black/60 shrink-0 backdrop-blur-xs">
                {{ (getPickCount(hero) / 1000).toFixed(0) }}k场
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
