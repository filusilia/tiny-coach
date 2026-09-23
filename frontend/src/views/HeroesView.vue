<script setup lang="ts">
import type { PatchInfo } from '../services/gameDataService'
import type { OfficialAbility, OfficialHero } from '../utils/abilityTextResolver'
import type { HeroStat } from '@/types'
import { ArrowLeft, ChevronDown, Clock, Droplet, Shield, ShoppingBag, Sparkles } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { useAppStore } from '@/stores'
import { resolveAbilityLore } from '../data/abilityLore'
import { HERO_ALIASES } from '../data/aliases'
import { getHeroGuideData } from '../data/heroGuides'
import { HERO_CHINESE_NAMES } from '../data/heroNames'
import { dataSyncService } from '../services/dataSyncService'
import { gameDataService } from '../services/gameDataService'
import {
  decodeAbilityBehavior,
  getAbilityLevelValues,
  getDamageTypeLabel,
  getDispellableLabel,
  getImmunityLabel,
} from '../utils/abilityMeta'
import {
  isEffectiveScepterUpgrade,
  isEffectiveShardUpgrade,
  resolveAbilityText,
  resolveTalentText,
} from '../utils/abilityTextResolver'
import {
  getAbilityCdnUrl,
  getAbilityIconUrl,
  getHeroCdnUrl,
  getHeroIconUrl,
  handleImageFallback,
} from '../utils/assetUrl'
import { calculateLevel1Stats } from '../utils/heroStats'
import { cleanHtml } from '../utils/htmlCleaner'

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
const patchInfo = ref<PatchInfo | null>(null)
const selectedAttr = ref<string>('all')
const isRankDropdownOpen = ref<boolean>(false)

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

const activeHero = ref<HeroStat | null>(null)
const selectedAbilityName = ref<string | null>(null)
const selectedShowcaseTab = ref<'ability' | 'talents'>('ability')
const isBioExpanded = ref<boolean>(false)
const loading = ref(true)

const currentPatch = computed(
  () =>
    dataSyncService.state.currentPatch || patchInfo.value?.version || '7.41f',
)

const currentRankOption = computed(() => {
  return (
    RANK_OPTIONS.find(r => r.id === selectedRank.value) || RANK_OPTIONS[1]
  )
})

function selectRank(rankId: number) {
  selectedRank.value = rankId
  isRankDropdownOpen.value = false
}

function getAttrIcon(attr: string): string {
  switch (attr) {
    case 'str':
      return '/assets/icons/hero_strength.png'
    case 'agi':
      return '/assets/icons/hero_agility.png'
    case 'int':
      return '/assets/icons/hero_intelligence.png'
    case 'all':
    default:
      return '/assets/icons/hero_universal.png'
  }
}

function getHeroDisplayImg(hero: HeroStat): string {
  const official = officialDetails.value[hero.id]
  if (official && official.crops_img) return official.crops_img
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
  if (rankKey === 8 && pick === 0) pick = (hero as any)['7_pick'] || 0
  return pick
}

const filteredHeroes = computed(() => {
  const list = heroes.value.filter((h) => {
    if (selectedAttr.value === 'str' && h.primary_attr !== 'str') return false
    if (selectedAttr.value === 'agi' && h.primary_attr !== 'agi') return false
    if (selectedAttr.value === 'int' && h.primary_attr !== 'int') return false
    if (selectedAttr.value === 'all_attr' && h.primary_attr !== 'all')
      return false

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

// 先天技能 (7.36+)
const innateAbility = computed<OfficialAbility | null>(() => {
  if (!activeDetails.value) return null
  const abilities = activeDetails.value.abilities || []
  return abilities.find(a => a.ability_is_innate || a.is_innate) || null
})

// 常规技能列表
const nonInnateAbilities = computed<OfficialAbility[]>(() => {
  if (!activeDetails.value) return []
  const abilities = activeDetails.value.abilities || []
  return abilities.filter(a => !a.ability_is_innate && !a.is_innate)
})

// 当前选中的技能
const activeSelectedAbility = computed<OfficialAbility | null>(() => {
  if (selectedAbilityName.value) {
    if (
      innateAbility.value
      && selectedAbilityName.value === innateAbility.value.name
    ) {
      return innateAbility.value
    }
    const found = nonInnateAbilities.value.find(
      a => a.name === selectedAbilityName.value,
    )
    if (found) return found
  }
  return nonInnateAbilities.value[0] || innateAbility.value || null
})

// 当前技能的所有标签（先天 / 伤害类型 / 减益免疫）
const activeAbilityTags = computed<{ text: string, cls: string }[]>(() => {
  const a = activeSelectedAbility.value
  if (!a) return []
  const tags: { text: string, cls: string }[] = []

  // 技能类型（由官网 behavior 64 位标志解码，未知时显式提示「资料待同步」）
  const behavior = decodeAbilityBehavior(a.behavior)
  if (behavior.primary) {
    tags.push({
      text: behavior.modifiers.length
        ? `${behavior.primary} · ${behavior.modifiers.join(' · ')}`
        : behavior.primary,
      cls: 'text-slate-300 border-slate-600/60 bg-slate-800/60',
    })
  }

  if (a.ability_is_innate) {
    tags.push({
      text: '先天技能',
      cls: 'text-emerald-300 border-emerald-700/60 bg-emerald-900/60',
    })
  }
  if (a.damage === 1) {
    tags.push({
      text: '物理伤害',
      cls: 'text-orange-400 border-orange-800/60 bg-orange-950/60',
    })
  } else if (a.damage === 2) {
    tags.push({
      text: '魔法伤害',
      cls: 'text-blue-400 border-blue-800/60 bg-blue-950/60',
    })
  } else if (a.damage === 3 || a.damage === 4) {
    tags.push({
      text: '纯粹伤害',
      cls: 'text-amber-300 border-amber-700/60 bg-amber-950/60',
    })
  }
  if (a.immunity === 1 || a.immunity === 3) {
    tags.push({
      text: '无视减益免疫',
      cls: 'text-emerald-400 border-emerald-800/50 bg-emerald-950/50',
    })
  }
  return tags
})

// 技能结构化属性面板（严格对齐官网「技能详情」口径：技能类型/伤害类型/无视减益免疫/可以驱散）
const activeAbilityMetaRows = computed<{ label: string, value: string }[]>(() => {
  const a = activeSelectedAbility.value
  if (!a) return []
  const behavior = decodeAbilityBehavior(a.behavior)
  return [
    {
      label: '技能类型',
      value: behavior.modifiers.length
        ? `${behavior.primary} · ${behavior.modifiers.join(' · ')}`
        : behavior.primary,
    },
    { label: '伤害类型', value: getDamageTypeLabel(a.damage) },
    { label: '无视减益免疫', value: getImmunityLabel(a.immunity) },
    { label: '可以驱散', value: getDispellableLabel(a.dispellable) },
  ]
})

// 技能各等级数值（仅取官网带中文标签的 special_values）
const activeAbilityLevelValues = computed(() =>
  getAbilityLevelValues(activeSelectedAbility.value || undefined),
)

// 技能背景介绍（官网原文优先，官网缺失时使用项目补充文案并标注来源）
const activeAbilityLore = computed(() =>
  resolveAbilityLore(activeSelectedAbility.value || undefined),
)

// 英雄实战机制技巧与出装
const heroGuide = computed(() => {
  if (!activeHero.value) return { tips: [], builds: [] }
  return getHeroGuideData(activeHero.value.id, activeHero.value.primary_attr)
})

function isTipRelatedToAbility(
  tip: { title: string, content: string },
  ability: OfficialAbility,
): boolean {
  if (!ability) return false
  const nameLoc = ability.name_loc || ''
  const nameEng = ability.name || ''
  if (nameLoc && (tip.title.includes(nameLoc) || tip.content.includes(nameLoc)))
    return true
  const keyWords = nameEng.replace(/^npc_dota_hero_/, '').split('_')
  for (const kw of keyWords) {
    if (
      kw.length > 3
      && (tip.title.toLowerCase().includes(kw)
        || tip.content.toLowerCase().includes(kw))
    ) {
      return true
    }
  }
  return false
}

const currentAbilityTips = computed(() => {
  if (!activeSelectedAbility.value) return []
  return heroGuide.value.tips.filter(tip =>
    isTipRelatedToAbility(tip, activeSelectedAbility.value!),
  )
})

const generalHeroTips = computed(() => {
  const allAbilities = [
    ...(nonInnateAbilities.value || []),
    ...(innateAbility.value ? [innateAbility.value] : []),
  ]
  return heroGuide.value.tips.filter(
    tip => !allAbilities.some(a => isTipRelatedToAbility(tip, a)),
  )
})

const currentAbilityNotes = computed(() => {
  return activeSelectedAbility.value?.notes_loc || []
})

const currentAbilityPatchNotes = computed(() => {
  if (!activeHero.value || !activeSelectedAbility.value || !patchInfo.value)
    return []
  return (
    patchInfo.value.heroPatchNotes?.[activeHero.value.id]?.[
      activeSelectedAbility.value.id
    ] || []
  )
})
</script>

<template>
  <div
    v-if="loading"
    class="text-base text-dota-muted bg-dota-bg flex flex-1 items-center justify-center"
  >
    <div
      class="mr-3 border-dota-gold border-b-2 border-t-2 rounded-full h-8 w-8 animate-spin"
    />
    加载中...
  </div>

  <!-- 视图 2：单独英雄全屏详情 -->
  <div
    v-else-if="activeHero"
    class="bg-dota-bg flex flex-1 flex-col select-none overflow-hidden"
  >
    <!-- 顶部导航条 -->
    <div
      class="px-5 border-b border-dota-border bg-dota-surface flex shrink-0 h-11 shadow-sm items-center justify-between z-10"
    >
      <button
        class="text-xs text-dota-gold font-medium px-3 py-1 border border-dota-border rounded-lg bg-dota-panel flex cursor-pointer shadow-sm transition-all items-center space-x-2 hover:text-dota-goldLight hover:bg-dota-hover"
        @click="activeHero = null"
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

    <!-- 详情内容可滚动区 -->
    <div class="p-5 flex-1 overflow-y-auto space-y-5">
      <!-- 1. 英雄资料顶部 -->
      <div
        class="p-5 border border-dota-border rounded-2xl flex flex-col gap-5 shadow-xl items-start justify-between relative overflow-hidden from-dota-panel to-dota-surface via-dota-panel/90 bg-gradient-to-r lg:flex-row"
      >
        <div
          v-if="activeHero.img"
          class="opacity-15 w-1/2 pointer-events-none bottom-0 right-0 top-0 absolute bg-cover bg-right bg-no-repeat"
          :style="{ backgroundImage: `url(${getHeroIconUrl(activeHero.name)})` }"
        />

        <div class="flex max-w-xl items-start relative z-10 space-x-4">
          <img
            :src="getHeroIconUrl(activeHero.name)"
            :alt="activeHero.localized_name"
            class="border-2 border-dota-border/80 rounded-xl shrink-0 h-16 w-24 shadow-xl object-cover"
            @error="(e) => handleImageFallback(e, getHeroCdnUrl(activeHero?.name || ''))"
          >
          <div class="space-y-1">
            <div class="flex items-center space-x-2.5">
              <h1 class="text-2xl text-dota-gold tracking-wide font-extrabold">
                {{ activeHero.localized_name }}
              </h1>
              <span
                v-if="dataSyncService.isHeroNew(activeHero.id)"
                class="text-xs text-slate-950 tracking-wider font-black px-2 py-0.5 border border-amber-300 rounded uppercase shadow animate-pulse from-amber-500 to-rose-500 bg-gradient-to-r"
              >
                官方更新
              </span>
            </div>

            <div class="text-xs text-dota-muted flex flex-wrap gap-y-1 items-center space-x-2">
              <span class="text-dota-gold font-medium">核心定位:</span>
              <span>{{ activeHero.roles.map(getRoleChineseName).join(" · ") }}</span>
            </div>

            <p v-if="activeDetails?.hype_loc" class="text-xs text-dota-subtle leading-relaxed pt-0.5 line-clamp-2">
              {{ cleanHtml(activeDetails.hype_loc) }}
            </p>

            <div v-if="activeDetails?.bio_loc" class="pt-1">
              <button
                type="button"
                class="text-xs text-dota-subtle flex cursor-pointer transition-colors items-center space-x-1 hover:text-dota-gold"
                @click="isBioExpanded = !isBioExpanded"
              >
                <ChevronDown class="h-3.5 w-3.5 transition-transform duration-200" :class="{ 'rotate-180': isBioExpanded }" />
                <span>{{ isBioExpanded ? "收起背景故事" : "展开背景故事..." }}</span>
              </button>
              <div
                v-show="isBioExpanded"
                class="text-xs text-dota-muted leading-relaxed mt-2 p-3 border border-dota-border/60 rounded-xl bg-black/50 max-h-52 whitespace-pre-line shadow-inner overflow-y-auto"
              >
                {{ cleanHtml(activeDetails.bio_loc) }}
              </div>
            </div>
          </div>
        </div>

        <!-- 右侧属性卡片 -->
        <div
          class="font-sans flex shrink-0 flex-col w-full relative z-10 space-y-2.5 lg:w-auto"
        >
          <div class="text-xs gap-2 grid grid-cols-3">
            <!-- 力量卡片 -->
            <div
              class="p-2 border rounded-xl flex transition-all items-center space-x-2"
              :class="[
                activeHero.primary_attr === 'str'
                  ? 'bg-red-950/40 border-dota-red/80 shadow-[0_0_12px_rgba(239,68,68,0.25)]'
                  : 'bg-dota-surface/70 border-dota-border/60',
              ]"
            >
              <img src="/assets/icons/hero_strength.png" alt="力量" class="h-5 w-5 object-contain">
              <div>
                <div class="text-xs text-dota-subtle font-medium flex items-center space-x-1">
                  <span>力量</span>
                  <span v-if="activeHero.primary_attr === 'str'" class="text-2xs text-dota-red font-bold px-1 rounded bg-dota-red/30">主属性</span>
                </div>
                <div class="text-xs text-dota-text font-bold font-mono">
                  {{ activeDetails?.str_base || activeHero.base_str }}
                  <span class="text-xs text-dota-subtle font-normal">+{{ activeDetails?.str_gain?.toFixed(1) || activeHero.str_gain?.toFixed(1) }}</span>
                </div>
              </div>
            </div>

            <!-- 敏捷卡片 -->
            <div
              class="p-2 border rounded-xl flex transition-all items-center space-x-2"
              :class="[
                activeHero.primary_attr === 'agi'
                  ? 'bg-emerald-950/40 border-dota-green/80 shadow-[0_0_12px_rgba(16,185,129,0.25)]'
                  : 'bg-dota-surface/70 border-dota-border/60',
              ]"
            >
              <img src="/assets/icons/hero_agility.png" alt="敏捷" class="h-5 w-5 object-contain">
              <div>
                <div class="text-xs text-dota-subtle font-medium flex items-center space-x-1">
                  <span>敏捷</span>
                  <span v-if="activeHero.primary_attr === 'agi'" class="text-2xs text-dota-green font-bold px-1 rounded bg-dota-green/30">主属性</span>
                </div>
                <div class="text-xs text-dota-text font-bold font-mono">
                  {{ activeDetails?.agi_base || activeHero.base_agi }}
                  <span class="text-xs text-dota-subtle font-normal">+{{ activeDetails?.agi_gain?.toFixed(1) || activeHero.agi_gain?.toFixed(1) }}</span>
                </div>
              </div>
            </div>

            <!-- 智力卡片 -->
            <div
              class="p-2 border rounded-xl flex transition-all items-center space-x-2"
              :class="[
                activeHero.primary_attr === 'int'
                  ? 'bg-blue-950/40 border-dota-cyan/80 shadow-[0_0_12px_rgba(56,189,248,0.25)]'
                  : 'bg-dota-surface/70 border-dota-border/60',
              ]"
            >
              <img src="/assets/icons/hero_intelligence.png" alt="智力" class="h-5 w-5 object-contain">
              <div>
                <div class="text-xs text-dota-subtle font-medium flex items-center space-x-1">
                  <span>智力</span>
                  <span v-if="activeHero.primary_attr === 'int'" class="text-2xs text-dota-cyan font-bold px-1 rounded bg-dota-cyan/30">主属性</span>
                </div>
                <div class="text-xs text-dota-text font-bold font-mono">
                  {{ activeDetails?.int_base || activeHero.base_int }}
                  <span class="text-xs text-dota-subtle font-normal">+{{ activeDetails?.int_gain?.toFixed(1) || activeHero.int_gain?.toFixed(1) }}</span>
                </div>
              </div>
            </div>
          </div>

          <div v-if="activeLevel1Stats" class="text-xs font-mono space-y-1">
            <div class="text-xs text-dota-green font-bold px-2.5 border border-emerald-800/60 rounded-lg bg-emerald-950/60 flex h-5 shadow-inner items-center justify-between">
              <span>生命值 {{ activeLevel1Stats.hp }}</span>
              <span class="text-xs text-emerald-400 font-normal">+{{ activeLevel1Stats.hpRegen }}/s</span>
            </div>
            <div class="text-xs text-dota-cyan font-bold px-2.5 border border-blue-800/60 rounded-lg bg-blue-950/60 flex h-5 shadow-inner items-center justify-between">
              <span>魔法值 {{ activeLevel1Stats.mana }}</span>
              <span class="text-xs text-cyan-300 font-normal">+{{ activeLevel1Stats.manaRegen }}/s</span>
            </div>
          </div>

          <div v-if="activeLevel1Stats" class="text-xs font-mono gap-1.5 grid grid-cols-4">
            <div class="text-xs p-1 border border-dota-border/60 rounded-lg bg-dota-surface/80 flex items-center space-x-1" title="基础攻击力">
              <img src="/assets/icons/icon_damage.png" alt="攻击" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeLevel1Stats.attackMin }}-{{ activeLevel1Stats.attackMax }}</span>
            </div>
            <div class="text-xs p-1 border border-dota-border/60 rounded-lg bg-dota-surface/80 flex items-center space-x-1" title="物理护甲">
              <img src="/assets/icons/icon_armor.png" alt="护甲" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeLevel1Stats.armor }}</span>
            </div>
            <div class="text-xs p-1 border border-dota-border/60 rounded-lg bg-dota-surface/80 flex items-center space-x-1" title="基础移动速度">
              <img src="/assets/icons/icon_movement_speed.png" alt="移速" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeLevel1Stats.moveSpeed }}</span>
            </div>
            <div class="text-xs p-1 border border-dota-border/60 rounded-lg bg-dota-surface/80 flex items-center space-x-1" title="基础攻击间隔 (BAT)">
              <img src="/assets/icons/icon_attack_time.png" alt="攻速" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeDetails?.attack_rate || 1.7 }}s</span>
            </div>
            <div class="text-xs p-1 border border-dota-border/60 rounded-lg bg-dota-surface/80 flex items-center space-x-1" title="攻击距离">
              <img src="/assets/icons/icon_attack_range.png" alt="射程" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeDetails?.attack_range || activeHero.base_attack_min }}</span>
            </div>
            <div class="text-xs p-1 border border-dota-border/60 rounded-lg bg-dota-surface/80 flex items-center space-x-1" title="基础魔法抗性">
              <img src="/assets/icons/icon_magic_resist.png" alt="魔抗" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeDetails?.magic_resistance || 25 }}%</span>
            </div>
            <div class="text-xs p-1 border border-dota-border/60 rounded-lg bg-dota-surface/80 flex items-center space-x-1" title="转身速率">
              <img src="/assets/icons/icon_turn_rate.png" alt="转身" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{
                activeDetails?.turn_rate || 0.6
              }}</span>
            </div>
            <div class="text-xs p-1 border border-dota-border/60 rounded-lg bg-dota-surface/80 flex items-center space-x-1" title="视野范围">
              <img src="/assets/icons/icon_vision.png" alt="视野" class="shrink-0 h-3.5 w-3.5 object-contain">
              <span class="text-dota-text font-bold">{{ activeDetails?.sight_range_day || 1800 }}/{{ activeDetails?.sight_range_night || 800 }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- ========== 2. 核心区：天赋树、先天技能、技能列表一体化联动展台 ========== -->
      <div class="p-5 border border-dota-border/90 rounded-2xl bg-dota-panel shadow-xl space-y-4">
        <!-- 顶部切换栏 -->
        <div class="pb-3 border-b border-dota-border/60 flex flex-wrap gap-3 items-center">
          <div class="flex flex-wrap gap-2 items-center">
            <!-- 天赋树入口 -->
            <button
              type="button"
              class="group p-0.5 border rounded-xl flex flex-col cursor-pointer transition-all duration-200 items-center relative"
              :class="[
                selectedShowcaseTab === 'talents'
                  ? 'bg-dota-surface border-dota-gold text-dota-gold shadow-[0_0_12px_rgba(229,193,88,0.35)] scale-105'
                  : 'bg-dota-surface/50 border-dota-border/60 text-dota-muted hover:border-dota-gold/60 hover:text-dota-text',
              ]"
              @click="selectTalents"
            >
              <span class="border border-dota-gold/40 rounded-lg bg-dota-card inline-flex h-12 w-12 items-center justify-center relative overflow-hidden">
                <Shield class="text-dota-gold h-6 w-6" />
              </span>
              <span class="text-xs font-medium mt-0.5 text-center max-w-14 truncate">天赋树</span>
            </button>

            <!-- 先天技能 + 常规技能 -->
            <div class="pl-1 border-l border-dota-border/50 flex items-center space-x-1.5">
              <!-- 先天技能入口 -->
              <button
                v-if="innateAbility"
                type="button"
                class="group p-0.5 border rounded-xl flex flex-col cursor-pointer transition-all duration-200 items-center relative"
                :class="[
                  selectedShowcaseTab === 'ability'
                    && activeSelectedAbility?.name === innateAbility.name
                    ? 'bg-dota-surface border-emerald-400 text-emerald-300 shadow-[0_0_12px_rgba(16,185,129,0.35)] scale-105'
                    : 'bg-dota-surface/50 border-dota-border/60 text-dota-muted hover:border-emerald-500/60 hover:text-emerald-300',
                ]"
                @click="selectAbility(innateAbility.name)"
              >
                <span class="rounded-lg bg-dota-card inline-flex h-12 w-12 relative overflow-hidden">
                  <img
                    :src="getAbilityIconUrl(innateAbility.name)"
                    :alt="innateAbility.name_loc"
                    class="h-full w-full object-cover"
                    @error="(e) => handleImageFallback(e, getAbilityCdnUrl(innateAbility?.name || ''))
                    "
                  >
                  <img src="/assets/icons/innate_icon.png" alt="先天遮罩" class="h-full w-full pointer-events-none inset-0 absolute object-contain">
                </span>
                <span class="text-xs font-medium mt-0.5 text-center max-w-14 truncate">{{ innateAbility.name_loc || innateAbility.name }}</span>
              </button>

              <!-- 常规技能列表 -->
              <button
                v-for="ability in nonInnateAbilities"
                :key="ability.id"
                type="button"
                class="group p-0.5 border rounded-xl flex flex-col cursor-pointer transition-all duration-200 items-center relative"
                :class="[
                  selectedShowcaseTab === 'ability'
                    && activeSelectedAbility?.name === ability.name
                    ? 'bg-dota-surface border-dota-gold text-dota-gold shadow-[0_0_12px_rgba(229,193,88,0.35)] scale-105'
                    : 'bg-dota-surface/50 border-dota-border/60 text-dota-muted hover:border-dota-gold/60 hover:text-dota-text',
                ]"
                @click="selectAbility(ability.name)"
              >
                <span class="rounded-lg bg-dota-card inline-flex h-12 w-12 relative overflow-hidden">
                  <img
                    :src="getAbilityIconUrl(ability.name)"
                    :alt="ability.name_loc || ability.name"
                    class="h-full w-full object-cover"
                    @error="(e) => handleImageFallback(e, getAbilityCdnUrl(ability.name))"
                  >
                  <img
                    v-if="ability.ability_has_scepter || ability.ability_is_granted_by_scepter"
                    src="/assets/icons/aghs_scepter.png"
                    alt="神杖遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute object-contain"
                  >
                  <img
                    v-if="ability.ability_has_shard || ability.ability_is_granted_by_shard"
                    src="/assets/icons/aghs_shard.png"
                    alt="魔晶遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute object-contain"
                  >
                </span>
                <span class="text-xs font-medium mt-0.5 text-center max-w-14 truncate">{{ ability.name_loc || ability.name }}</span>
              </button>
            </div>
          </div>
        </div>

        <!-- 展台详细内容区 -->
        <div class="p-5 border border-dota-border/70 rounded-xl bg-dota-surface/90 shadow-lg">
          <!-- 模式 A：技能详细全景展示 -->
          <div v-if="selectedShowcaseTab === 'ability' && activeSelectedAbility" class="space-y-2.5">
            <div class="pb-2.5 border-b border-dota-border/50 flex flex-col gap-2.5 justify-between sm:flex-row sm:items-center">
              <div class="flex items-center space-x-3">
                <span class="border-2 border-dota-border rounded-xl bg-dota-card inline-flex shrink-0 h-20 w-20 shadow-md relative overflow-hidden">
                  <img
                    :src="getAbilityIconUrl(activeSelectedAbility.name)"
                    :alt="activeSelectedAbility.name_loc"
                    class="h-full w-full object-cover"
                    @error=" (e) => handleImageFallback(e, getAbilityCdnUrl(activeSelectedAbility?.name || '')) "
                  >
                  <img
                    v-if="activeSelectedAbility.ability_is_innate"
                    src="/assets/icons/innate_icon.png"
                    alt="先天遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute object-contain"
                  >
                  <img
                    v-if="activeSelectedAbility.ability_has_scepter || activeSelectedAbility.ability_is_granted_by_scepter"
                    src="/assets/icons/aghs_scepter.png"
                    alt="神杖遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute object-contain"
                  >
                  <img
                    v-if="activeSelectedAbility.ability_has_shard || activeSelectedAbility.ability_is_granted_by_shard"
                    src="/assets/icons/aghs_shard.png"
                    alt="魔晶遮罩"
                    class="h-full w-full pointer-events-none inset-0 absolute object-contain"
                  >
                </span>

                <div class="space-y-1">
                  <div class="flex flex-wrap gap-y-1 items-center space-x-2">
                    <h3 class="text-lg text-dota-goldLight font-extrabold">{{ activeSelectedAbility.name_loc || activeSelectedAbility.name }}</h3>
                    <span v-for="tag in activeAbilityTags" :key="tag.text" class="text-xs font-medium px-2 py-0.5 border rounded" :class="tag.cls">
                      {{ tag.text }}
                    </span>
                  </div>
                </div>
              </div>

              <div class="text-xs font-mono px-3.5 py-1.5 border border-dota-border/60 rounded-lg bg-dota-panel/90 flex shrink-0 items-center self-start space-x-3 sm:self-auto">
                <div v-if="activeSelectedAbility.cooldowns?.length && activeSelectedAbility.cooldowns[0] > 0 " class="text-dota-gold flex items-center space-x-1">
                  <Clock class="h-3.5 w-3.5" />
                  <span class="font-bold">{{ activeSelectedAbility.cooldowns.join(" / ") }}s</span>
                </div>
                <div v-if=" activeSelectedAbility.cooldowns?.length && activeSelectedAbility.mana_costs?.length " class="bg-dota-border h-3 w-px" />
                <div v-if="activeSelectedAbility.mana_costs?.length && activeSelectedAbility.mana_costs[0] > 0" class="text-dota-cyan flex items-center space-x-1">
                  <Droplet class="h-3.5 w-3.5 fill-current" />
                  <span class="font-bold">{{ activeSelectedAbility.mana_costs.join(" / ") }}</span>
                </div>
              </div>
            </div>

            <!-- 技能描述 -->
            <div class="text-xs text-dota-text leading-relaxed px-3 py-2.5 border border-dota-border/40 rounded-lg bg-dota-panel/60 whitespace-pre-line">
              {{ cleanHtml(resolveAbilityText(activeSelectedAbility.desc_loc, activeSelectedAbility, "none", activeDetails || undefined)) }}
            </div>

            <!-- 技能结构化属性条（对齐官网技能详情口径，紧凑单行排布） -->
            <div class="px-1 flex flex-wrap gap-x-3.5 gap-y-1">
              <span
                v-for="row in activeAbilityMetaRows"
                :key="row.label"
                class="text-2xs flex gap-1 items-baseline"
              >
                <span class="text-dota-subtle">{{ row.label }}</span>
                <span class="text-dota-text font-semibold">{{ row.value }}</span>
              </span>
            </div>

            <!-- 各等级数值（模仿官网：中文标签在上、数值在下） -->
            <div
              v-if="activeAbilityLevelValues.length > 0"
              class="gap-x-2.5 gap-y-1.5 grid grid-cols-2 sm:grid-cols-3"
            >
              <div
                v-for="v in activeAbilityLevelValues"
                :key="v.label"
                class="px-2 py-1 border border-dota-border/25 rounded bg-black/25"
              >
                <div class="text-2xs text-dota-muted truncate" :title="cleanHtml(v.label)">
                  {{ cleanHtml(v.label) }}
                </div>
                <div class="text-xs text-dota-goldLight font-bold font-mono truncate" :title="v.display">
                  {{ v.display }}
                </div>
              </div>
            </div>

            <!-- 技能背景介绍：官网 lore 原文优先，缺失时用项目补充文案并内联标注；不显示「背景介绍」标题 -->
            <p
              v-if="activeAbilityLore"
              class="text-xs text-dota-text/85 leading-relaxed pl-2.5 border-dota-gold/40 border-l-2 italic"
            >
              {{ activeAbilityLore.text }}<span
                v-if="activeAbilityLore.source === 'project'"
                class="text-2xs text-dota-muted not-italic"
              >（项目补充）</span>
            </p>

            <div class="gap-3 grid grid-cols-1 md:grid-cols-2">
              <div
                v-if=" isEffectiveScepterUpgrade(activeSelectedAbility, activeDetails || undefined) "
                class="text-xs text-dota-goldLight p-3.5 border border-dota-gold/40 rounded-xl flex shadow-md items-start from-dota-gold/15 to-dota-panel/80 bg-gradient-to-r space-x-3"
              >
                <div class="p-1 border border-dota-gold/50 rounded-lg bg-black/60 shrink-0 shadow">
                  <img src="/assets/icons/ultimate_scepter.png" alt="阿哈利姆神杖" class="h-8 w-11 object-contain">
                </div>
                <div class="flex-1 space-y-1">
                  <div class="text-xs text-dota-gold font-bold flex items-center space-x-1.5">
                    <span>阿哈利姆神杖</span>
                  </div>
                  <p class="text-xs text-dota-text leading-relaxed">
                    {{ cleanHtml(resolveAbilityText(activeSelectedAbility.scepter_loc, activeSelectedAbility, "scepter", activeDetails || undefined)) }}
                  </p>
                </div>
              </div>

              <div
                v-if="isEffectiveShardUpgrade(activeSelectedAbility, activeDetails || undefined)"
                class="text-xs text-dota-cyan p-3.5 border border-dota-cyan/40 rounded-xl flex shadow-md items-start from-dota-cyan/15 to-dota-panel/80 bg-gradient-to-r space-x-3"
              >
                <div class="p-1 border border-dota-cyan/50 rounded-lg bg-black/60 shrink-0 shadow">
                  <img src="/assets/icons/aghanims_shard.png" alt="阿哈利姆魔晶" class="h-8 w-11 object-contain">
                </div>
                <div class="flex-1 space-y-1">
                  <div class="text-xs text-dota-cyan font-bold flex items-center space-x-1.5">
                    <span>阿哈利姆魔晶</span>
                  </div>
                  <p class="text-xs text-dota-text leading-relaxed">
                    {{ cleanHtml(resolveAbilityText(activeSelectedAbility.shard_loc, activeSelectedAbility, "shard", activeDetails || undefined)) }}
                  </p>
                </div>
              </div>
            </div>

            <div
              v-if="currentAbilityTips.length > 0 || currentAbilityNotes.length > 0 || currentAbilityPatchNotes.length > 0 "
              class="p-3.5 border border-amber-500/30 rounded-xl shadow-md from-amber-950/25 to-dota-panel/90 bg-gradient-to-br space-y-2.5"
            >
              <div class="pb-1.5 border-b border-amber-500/20 flex items-center justify-between">
                <div class="text-xs text-amber-300 font-bold flex items-center space-x-2">
                  <Sparkles class="text-amber-400 h-3.5 w-3.5" />
                  <span>{{ activeSelectedAbility.name_loc || activeSelectedAbility.name }}实战要领与机制要点</span>
                </div>
                <span v-if="currentAbilityPatchNotes.length > 0" class="text-2xs text-amber-300 font-bold font-mono px-1.5 py-0.5 border border-amber-500/40 rounded bg-amber-500/20">{{ currentPatch }} 调整</span>
              </div>

              <div v-if="currentAbilityTips.length > 0" class="space-y-1.5">
                <div v-for="(t, idx) in currentAbilityTips" :key="idx" class="p-2 border border-amber-900/40 rounded-lg bg-black/40 space-y-0.5">
                  <div class="text-xs text-amber-300 font-semibold flex items-center space-x-1">
                    <span class="rounded-full bg-amber-400 h-1.5 w-1.5" />
                    <span>{{ t.title }}</span>
                  </div>
                  <p class="text-xs text-dota-text leading-relaxed pl-2.5">{{ cleanHtml(t.content) }}</p>
                </div>
              </div>

              <div v-if="currentAbilityNotes.length > 0" class="text-xs text-slate-300 p-2 border border-slate-800 rounded-lg bg-black/30 space-y-1">
                <div class="text-xs text-slate-400 font-medium">官方机制注解：</div>
                <ul class="text-xs text-slate-300 list-disc list-inside space-y-0.5">
                  <li v-for="(note, nIdx) in currentAbilityNotes" :key="nIdx">{{ cleanHtml(note) }}</li>
                </ul>
              </div>

              <div v-if="currentAbilityPatchNotes.length > 0" class="text-xs text-amber-200/90 p-2 border border-amber-800/60 rounded-lg bg-amber-950/40 space-y-1">
                <div class="text-xs text-amber-400 font-bold">现行版本 (v{{ currentPatch }}) 调整记录：</div>
                <ul class="list-disc list-inside space-y-0.5">
                  <li v-for="(pNote, pIdx) in currentAbilityPatchNotes" :key="pIdx">{{ cleanHtml(pNote) }}</li>
                </ul>
              </div>
            </div>
          </div>

          <!-- 模式 B：英雄天赋树全景展示 -->
          <div v-else-if="selectedShowcaseTab === 'talents'" class="py-2 space-y-4">
            <div class="mx-auto py-2 max-w-3xl space-y-3">
              <div v-for="(lvl, index) in [25, 20, 15, 10]" :key="lvl" class="flex items-center space-x-2">
                <div
                  class="text-xs text-dota-text leading-relaxed p-3 text-right border border-dota-border/60 rounded-xl bg-dota-panel flex-1 shadow-sm transition-all hover:border-dota-gold/50 hover:bg-dota-hover"
                  :title="cleanHtml(resolveTalentText(activeDetails?.talents?.[index * 2 + 1], activeDetails || undefined))"
                >
                  {{ cleanHtml(resolveTalentText(activeDetails?.talents?.[index * 2 + 1], activeDetails || undefined)) }}
                </div>

                <div class="text-xs text-slate-950 font-black font-mono border-2 border-amber-300 rounded-full flex shrink-0 h-9 w-9 shadow-lg items-center justify-center from-amber-400 to-amber-600 bg-gradient-to-b">
                  {{ lvl }}
                </div>

                <div
                  class="text-xs text-dota-text leading-relaxed p-3 text-left border border-dota-border/60 rounded-xl bg-dota-panel flex-1 shadow-sm transition-all hover:border-dota-gold/50 hover:bg-dota-hover"
                  :title="cleanHtml(resolveTalentText(activeDetails?.talents?.[index * 2], activeDetails || undefined)) "
                >
                  {{ cleanHtml(resolveTalentText(activeDetails?.talents?.[index * 2], activeDetails || undefined)) }}
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 3. 页面下方：英雄技巧和热门出装 -->
      <div class="pt-1 gap-5 grid grid-cols-1 lg:grid-cols-2">
        <div v-if="generalHeroTips.length > 0" class="p-4 border border-dota-border rounded-xl bg-dota-panel shadow-md space-y-3">
          <div class="pb-2.5 border-b border-dota-border/60 flex items-center space-x-2">
            <Sparkles class="text-dota-gold h-4 w-4" />
            <h3 class="text-xs text-dota-goldLight font-bold">英雄宏观打法与实战战略</h3>
          </div>
          <div class="space-y-2.5">
            <div v-for="(tip, idx) in generalHeroTips" :key="idx" class="p-3 border border-dota-border/50 rounded-lg bg-dota-surface/60 space-y-1">
              <div class="text-xs text-dota-gold font-bold flex items-center space-x-1.5">
                <span class="rounded-full bg-dota-gold h-1.5 w-1.5" />
                <span>{{ tip.title }}</span>
              </div>
              <p class="text-xs text-dota-text leading-relaxed pl-3">
                {{ cleanHtml(tip.content) }}
              </p>
            </div>
          </div>
        </div>

        <div class="p-4 border border-dota-border rounded-xl bg-dota-panel shadow-md space-y-3">
          <div class="pb-2.5 border-b border-dota-border/60 flex items-center space-x-2">
            <ShoppingBag class="text-dota-gold h-4 w-4" />
            <h3 class="text-xs text-dota-goldLight font-bold">
              推荐与热门出装路线
            </h3>
          </div>
          <div class="space-y-3">
            <div v-for="b in heroGuide.builds" :key="b.stage" class="p-2.5 border border-dota-border/50 rounded-lg bg-dota-surface/60 space-y-2">
              <div class="text-xs text-dota-subtle font-bold flex items-center justify-between">
                <span>{{ b.stage }}</span>
                <span class="text-xs text-dota-muted">当前 {{ currentPatch }} 热门</span>
              </div>
              <div class="flex flex-wrap gap-2 items-center space-x-2">
                <div
                  v-for="item in b.items"
                  :key="item.name"
                  class="p-1.5 border border-dota-border/80 rounded bg-dota-panel flex items-center space-x-1.5"
                  :title="`${item.dname} (${item.cost} 金币)`"
                >
                  <img :src="item.img" :alt="item.dname" class="border border-dota-border/60 rounded-sm shrink-0 h-5 w-7 object-cover">
                  <div class="text-xs leading-tight">
                    <div class="text-dota-text font-medium">
                      {{ item.dname }}
                    </div>
                    <div class="text-2xs text-dota-gold font-mono">
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

  <!-- 视图 1：全屏英雄图鉴网格 -->
  <div v-else class="bg-dota-bg flex flex-1 flex-col select-none overflow-hidden">
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
      <div class="text-xs flex items-center relative space-x-2">
        <span class="text-dota-subtle">段位环境:</span>
        <button
          type="button"
          class="group text-xs text-dota-gold px-2 border border-dota-border rounded-lg bg-dota-panel flex h-7.5 cursor-pointer shadow-sm transition-all items-center space-x-1.5 hover:border-dota-gold/70"
          @click="isRankDropdownOpen = !isRankDropdownOpen"
        >
          <img :src="currentRankOption.icon" :alt="currentRankOption.label" class="shrink-0 h-4.5 w-4.5 object-contain drop-shadow">
          <span class="font-bold group-hover:text-dota-goldLight">{{ currentRankOption.label }}</span>
          <ChevronDown class="text-dota-muted h-3.5 w-3.5 transition-transform duration-200 group-hover:text-dota-gold" :class="{ 'rotate-180': isRankDropdownOpen }" />
        </button>

        <div v-if="isRankDropdownOpen" class="inset-0 fixed z-40" @click="isRankDropdownOpen = false" />

        <div
          v-if="isRankDropdownOpen"
          class="mt-1 py-1 border border-dota-gold/40 rounded-xl bg-dota-surface max-h-72 w-40 shadow-2xl left-16 top-full absolute z-50 overflow-y-auto backdrop-blur-md"
        >
          <div
            v-for="rank in RANK_OPTIONS"
            :key="rank.id"
            class="text-xs px-3 py-1.5 flex cursor-pointer transition-colors items-center space-x-2.5"
            :class="[selectedRank === rank.id ? 'bg-dota-panel text-dota-gold font-bold' : 'text-dota-text hover:bg-dota-hover hover:text-dota-goldLight']"
            @click="selectRank(rank.id)"
          >
            <img :src="rank.icon" :alt="rank.label" class="shrink-0 h-4.5 w-4.5 object-contain">
            <span class="flex-1">{{ rank.label }}</span>
            <span v-if="selectedRank === rank.id" class="text-2xs text-dota-gold">✓</span>
          </div>
        </div>
      </div>

      <div class="text-xs flex items-center space-x-1">
        <button
          class="px-2.5 py-1 rounded transition-colors"
          :class="[sortMode === 'win_desc' ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40' : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text']"
          @click="sortMode = 'win_desc'"
        >
          胜率最高
        </button>
        <button
          class="px-2.5 py-1 rounded transition-colors"
          :class="[sortMode === 'pick_desc' ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40' : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text']"
          @click="sortMode = 'pick_desc'"
        >
          出场最多
        </button>
      </div>
    </div>

    <div class="p-4 flex-1 overflow-y-auto">
      <div class="gap-4 grid grid-cols-2 2xl:grid-cols-7 lg:grid-cols-5 md:grid-cols-4 sm:grid-cols-3 xl:grid-cols-6">
        <div
          v-for="hero in filteredHeroes"
          :key="hero.id"
          class="group border border-dota-border/80 rounded-2xl bg-dota-surface flex flex-col cursor-pointer transition-all duration-300 relative overflow-hidden hover:border-dota-gold hover:shadow-[0_8px_25px_rgba(0,0,0,0.7)] hover:-translate-y-1"
          @click="openHeroDetail(hero)"
        >
          <div class="bg-dota-card aspect-[16/10] relative overflow-hidden">
            <img
              :src="getHeroDisplayImg(hero)"
              :alt="hero.localized_name"
              class="h-full w-full transition-transform duration-500 object-cover group-hover:scale-110"
              loading="lazy"
              @error="(e) => handleImageFallback(e, getHeroCdnUrl(hero.name))"
            >
            <div class="pointer-events-none inset-0 absolute from-dota-surface to-black/30 via-transparent bg-gradient-to-t" />

            <div class="p-1 border border-white/10 rounded-lg bg-black/70 shadow left-2 top-2 absolute backdrop-blur-xs">
              <img :src="getAttrIcon(hero.primary_attr)" :alt="hero.primary_attr" class="h-3.5 w-3.5 object-contain">
            </div>

            <div
              v-if="dataSyncService.isHeroNew(hero.id)"
              class="text-1 text-slate-950 tracking-wider font-black px-1.5 py-0.5 border border-amber-300 rounded uppercase shadow-[0_0_10px_rgba(245,158,11,0.6)] left-9 top-2 absolute animate-pulse from-amber-500 to-rose-500 bg-gradient-to-r"
            >
              v{{ currentPatch }} 更新
            </div>

            <div
              class="text-xs text-dota-gold font-bold font-mono px-2 py-0.5 border border-dota-gold/40 rounded-lg bg-black/80 shadow right-2 top-2 absolute backdrop-blur-xs"
            >
              {{ getWinRate(hero) }}%
            </div>

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
