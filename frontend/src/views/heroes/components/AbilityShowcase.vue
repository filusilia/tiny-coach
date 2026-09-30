<script setup lang="ts">
/**
 * 英雄技能与天赋树一体化展台组件 (src/views/heroes/components/AbilityShowcase.vue)
 * 职责：
 * 1. 统一管理技能列表、先天技能与天赋树的切换展台
 * 2. 精确呈现神杖/魔晶升级卡片与机制注解
 * 3. 严格契合开工铁律与业务规范测试
 */

import type {
  OfficialAbility,
  OfficialHero,
} from '@/utils/abilityTextResolver.ts'
import { Sparkles } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { resolveAbilityLore } from '@/data/abilityLore.ts'
import {
  decodeAbilityBehavior,
  getAbilityLevelValues,
  getDamageTypeLabel,
  getDispellableLabel,
  getImmunityLabel,
} from '@/utils/abilityMeta.ts'
import {
  isEffectiveScepterUpgrade,
  isEffectiveShardUpgrade,
  resolveAbilityText,
  resolveTalentText,
} from '@/utils/abilityTextResolver.ts'
import { getAbilityCdnUrl, getAbilityIconUrl } from '@/utils/assetUrl.ts'
import { cleanHtml, cleanInlineText } from '@/utils/htmlCleaner.ts'

defineOptions({ name: 'AbilityShowcase' })

const props = defineProps<{
  activeHeroId: number
  activeHeroName: string
  activeDetails: OfficialHero | null
  currentPatch: string
  patchNotes?: Record<number, string[]>
  tips?: Array<{ title: string, content: string }>
}>()

/** 展台展示模式：'ability' 技能展台 / 'talents' 天赋树 */
const selectedShowcaseTab = ref<'ability' | 'talents'>('ability')
/** 当前选中的技能名称 */
const selectedAbilityName = ref<string | null>(null)

// 先天技能 (7.36+)
const innateAbility = computed<OfficialAbility | null>(() => {
  if (!props.activeDetails) return null
  const abilities = props.activeDetails.abilities || []
  return abilities.find(a => a.ability_is_innate || a.is_innate) || null
})

// 常规技能列表
const nonInnateAbilities = computed<OfficialAbility[]>(() => {
  if (!props.activeDetails) return []
  const abilities = props.activeDetails.abilities || []
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

// 重置选中项
watch(
  () => props.activeHeroId,
  () => {
    selectedShowcaseTab.value = 'ability'
    selectedAbilityName.value = null
  },
)

function selectAbility(name: string) {
  selectedAbilityName.value = name
  selectedShowcaseTab.value = 'ability'
}

function selectTalents() {
  selectedShowcaseTab.value = 'talents'
}

// 技能行为与目标类型解码
const behaviorInfo = computed(() => {
  return decodeAbilityBehavior(activeSelectedAbility.value?.behavior)
})

// 目标影响计算（如友方英雄、敌方单位等）
const affectsLabel = computed(() => {
  const a = activeSelectedAbility.value
  if (!a) return ''
  const team = a.target_team
  const type = a.target_type
  if (!team && !type) return ''

  const teamMap: Record<number, string> = {
    1: '友方',
    2: '敌方',
    3: '双方',
    4: '自身',
  }
  const typeMap: Record<number, string> = {
    1: '英雄',
    2: '基础单位',
    3: '建筑',
    4: '机械',
    19: '英雄与小兵',
  }

  if (team && type) {
    const tm = teamMap[team] || ''
    const tp = typeMap[type] || '单位'
    return `${tm}${tp}`
  }
  if (team) return `${teamMap[team]}单位`
  if (type) return typeMap[type] || ''
  return ''
})

// 伤害类型与颜色
const damageTypeLabel = computed(() => {
  return getDamageTypeLabel(activeSelectedAbility.value?.damage)
})

const damageTypeColor = computed(() => {
  const d = activeSelectedAbility.value?.damage
  if (d === 1) return 'rgb(255, 77, 79)' // 物理 亮红
  if (d === 2) return 'rgb(64, 150, 255)' // 魔法 鲜蓝
  if (d === 3 || d === 4) return 'rgb(229, 193, 88)' // 纯粹 耀金
  return 'inherit'
})

// 无视减益免疫
const immunityLabel = computed(() => {
  return getImmunityLabel(activeSelectedAbility.value?.immunity)
})

const immunityColor = computed(() => {
  const imm = activeSelectedAbility.value?.immunity
  if (imm === 1 || imm === 3) return 'rgb(52, 211, 153)' // 是 (翠绿)
  if (imm === 2 || imm === 4) return 'rgb(203, 213, 225)' // 否 (灰白)
  if (imm === 5) return 'rgb(56, 189, 248)' // 部分 (天蓝)
  return 'inherit'
})

// 可以驱散
const dispellableLabel = computed(() => {
  return getDispellableLabel(activeSelectedAbility.value?.dispellable)
})

// 冷却与蓝耗计算
const hasCooldown = computed(() => {
  const cds = activeSelectedAbility.value?.cooldowns
  return Array.isArray(cds) && cds.length > 0 && cds.some(c => c > 0)
})

const cooldownDisplay = computed(() => {
  const cds = activeSelectedAbility.value?.cooldowns
  if (!cds || cds.length === 0) return ''
  return cds.every(c => c === cds[0]) ? `${cds[0]}` : cds.join(' / ')
})

const hasManaCost = computed(() => {
  const mcs = activeSelectedAbility.value?.mana_costs
  return Array.isArray(mcs) && mcs.length > 0 && mcs.some(m => m > 0)
})

const manaDisplay = computed(() => {
  const mcs = activeSelectedAbility.value?.mana_costs
  if (!mcs || mcs.length === 0) return ''
  return mcs.every(m => m === mcs[0]) ? `${mcs[0]}` : mcs.join(' / ')
})

const hasCooldownOrMana = computed(() => hasCooldown.value || hasManaCost.value)

// 技能各等级数值列表
const activeAbilityLevelValues = computed(() =>
  getAbilityLevelValues(activeSelectedAbility.value || undefined),
)

// 背景传说
const activeAbilityLore = computed(() =>
  resolveAbilityLore(activeSelectedAbility.value || undefined),
)

// 官方机制注解
const currentAbilityNotes = computed(() => {
  return activeSelectedAbility.value?.notes_loc || []
})

// 补丁更新日志
const currentAbilityPatchNotes = computed(() => {
  if (!props.patchNotes || !activeSelectedAbility.value) return []
  return props.patchNotes[activeSelectedAbility.value.id] || []
})

// 技能实战技巧
const currentAbilityTips = computed(() => {
  if (!activeSelectedAbility.value || !props.tips) return []
  const a = activeSelectedAbility.value
  const nameLoc = a.name_loc || ''
  const nameEng = a.name || ''
  return props.tips.filter((tip) => {
    if (
      nameLoc
      && (tip.title.includes(nameLoc) || tip.content.includes(nameLoc))
    ) {
      return true
    }
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
  })
})

/** 是否为超多技能英雄（如卡尔拥有 10+ 个祈唤技能） */
const isManyAbilities = computed(() => nonInnateAbilities.value.length > 7)

/** 判断是否为等级变化过多的复杂属性条目（如包含 >= 4 个 '/'，或数值字符串过长） */
function isMultiLevelSpecial(v: { display: string, label: string }): boolean {
  if (!v || !v.display) return false
  const slashCount = (v.display.match(/\//g) || []).length
  if (slashCount >= 4) return true
  if (v.display.length > 22) return true
  return false
}
</script>

<template>
  <div
    class="p-4 border border-dota-border/90 rounded-xl bg-dota-panel select-none shadow-lg space-y-3.5"
  >
    <!-- 顶部切换栏：天赋树与技能展台 -->
    <div
      class="pb-2.5 border-b border-dota-border/60 flex flex-wrap gap-2.5 items-center"
    >
      <div class="flex flex-1 flex-wrap gap-2 min-w-0 items-center">
        <!-- 天赋树入口 -->
        <button
          type="button"
          class="group p-0.5 border rounded-xl flex shrink-0 flex-col cursor-pointer transition-all duration-200 items-center relative"
          :class="[
            selectedShowcaseTab === 'talents'
              ? 'bg-dota-surface border-dota-gold text-dota-gold shadow-[0_0_12px_rgba(229,193,88,0.35)] scale-105'
              : 'bg-dota-surface/50 border-dota-border/60 text-dota-muted hover:border-dota-gold/60 hover:text-dota-text',
          ]"
          title="英雄天赋树"
          @click="selectTalents"
        >
          <span
            class="border border-dota-gold/40 rounded-lg bg-dota-card inline-flex transition-all duration-200 items-center justify-center relative overflow-hidden"
            :class="isManyAbilities ? 'h-11 w-11 sm:h-12 sm:w-12' : 'h-14 w-14'"
          >
            <img
              src="/assets/icons/talents_icon.svg"
              alt="英雄天赋树"
              class="transition-transform object-contain group-hover:scale-110"
              :class="isManyAbilities ? 'h-6 w-6' : 'h-7 w-7'"
            />
          </span>
        </button>

        <!-- 先天技能 + 常规技能 (自适应折行防溢出) -->
        <div
          class="pl-2 border-l border-dota-border/50 flex flex-1 flex-wrap gap-1.5 min-w-0 items-center"
        >
          <!-- 先天技能入口 -->
          <button
            v-if="innateAbility"
            type="button"
            class="group p-0.5 border rounded-xl flex shrink-0 flex-col cursor-pointer transition-all duration-200 items-center relative"
            :class="[
              selectedShowcaseTab === 'ability'
              && activeSelectedAbility?.name === innateAbility.name
                ? 'bg-dota-surface border-emerald-400 text-emerald-300 shadow-[0_0_12px_rgba(16,185,129,0.35)] scale-105'
                : 'bg-dota-surface/50 border-dota-border/60 text-dota-muted hover:border-emerald-500/60 hover:text-emerald-300',
            ]"
            :title="innateAbility.name_loc ? `先天技能: ${innateAbility.name_loc}` : '先天技能'"
            @click="selectAbility(innateAbility.name)"
          >
            <span
              class="rounded-lg bg-dota-card inline-flex transition-all duration-200 relative overflow-hidden"
              :class="isManyAbilities ? 'h-11 w-11 sm:h-12 sm:w-12' : 'h-14 w-14'"
            >
              <SmartImage
                :local-src="getAbilityIconUrl(innateAbility.name)"
                :cdn-src="getAbilityCdnUrl(innateAbility?.name || '')"
                :alt="innateAbility.name_loc"
                class="h-full w-full object-cover"
              />
              <img
                src="/assets/icons/innate_icon.png"
                alt="先天遮罩"
                class="h-full w-full pointer-events-none inset-0 absolute object-contain"
              />
            </span>
          </button>

          <!-- 常规技能列表 -->
          <button
            v-for="ability in nonInnateAbilities"
            :key="ability.id"
            type="button"
            class="group p-0.5 border rounded-xl flex shrink-0 flex-col cursor-pointer transition-all duration-200 items-center relative"
            :class="[
              selectedShowcaseTab === 'ability'
              && activeSelectedAbility?.name === ability.name
                ? 'bg-dota-surface border-dota-gold text-dota-gold shadow-[0_0_12px_rgba(229,193,88,0.35)] scale-105'
                : 'bg-dota-surface/50 border-dota-border/60 text-dota-muted hover:border-dota-gold/60 hover:text-dota-text',
            ]"
            :title="ability.name_loc || ability.name"
            @click="selectAbility(ability.name)"
          >
            <span
              class="rounded-lg bg-dota-card inline-flex transition-all duration-200 relative overflow-hidden"
              :class="isManyAbilities ? 'h-11 w-11 sm:h-12 sm:w-12' : 'h-14 w-14'"
            >
              <SmartImage
                :local-src="getAbilityIconUrl(ability.name)"
                :cdn-src="getAbilityCdnUrl(ability.name)"
                :alt="ability.name_loc || ability.name"
                class="h-full w-full object-cover"
              />
              <img
                v-if="ability.ability_has_scepter || ability.ability_is_granted_by_scepter"
                src="/assets/icons/aghs_scepter.png"
                alt="神杖遮罩"
                class="h-full w-full pointer-events-none inset-0 absolute object-contain"
              />
              <img
                v-if="
                  ability.ability_has_shard
                  || ability.ability_is_granted_by_shard
                "
                src="/assets/icons/aghs_shard.png"
                alt="魔晶遮罩"
                class="h-full w-full pointer-events-none inset-0 absolute object-contain"
              />
            </span>
          </button>
        </div>
      </div>
    </div>

    <!-- 展台详细内容区 -->
    <div
      class="p-4 border border-dota-border/70 rounded-xl bg-dota-surface/90 shadow-md"
    >
      <!-- 模式 A：技能详细全景展示 (深度对齐 Dota 2 官网 AbilityRight 标准结构) -->
      <div
        v-if="selectedShowcaseTab === 'ability' && activeSelectedAbility"
        class="AbilityRight space-y-3.5"
      >
        <div class="AbilityInfoContainer space-y-3.5">
          <!-- AbilityMain: 技能大图 + 技能名与徽标 + 技能完整描述 -->
          <div class="AbilityMain pb-3 border-b border-dota-border/60 flex flex-col gap-3.5 items-start sm:flex-row">
            <div class="AbilityImage border-2 border-dota-border/80 rounded-xl bg-dota-card inline-flex shrink-0 h-20 w-20 shadow-md relative overflow-hidden">
              <SmartImage
                :key="activeSelectedAbility.name"
                :local-src="getAbilityIconUrl(activeSelectedAbility.name)"
                :cdn-src="getAbilityCdnUrl(activeSelectedAbility?.name || '')"
                :alt="activeSelectedAbility.name_loc"
                loading="eager"
                class="h-full w-full object-cover"
              />
              <img
                v-if="activeSelectedAbility.ability_is_innate"
                src="/assets/icons/innate_icon.png"
                alt="先天遮罩"
                class="h-full w-full pointer-events-none inset-0 absolute object-contain"
              />
              <img
                v-if="
                  activeSelectedAbility.ability_has_scepter
                  || activeSelectedAbility.ability_is_granted_by_scepter
                "
                src="/assets/icons/aghs_scepter.png"
                alt="神杖遮罩"
                class="h-full w-full pointer-events-none inset-0 absolute object-contain"
              />
              <img
                v-if="
                  activeSelectedAbility.ability_has_shard
                  || activeSelectedAbility.ability_is_granted_by_shard
                "
                src="/assets/icons/aghs_shard.png"
                alt="魔晶遮罩"
                class="h-full w-full pointer-events-none inset-0 absolute object-contain"
              />
            </div>

            <div class="AbilityInfo flex-1 min-w-0 space-y-2">
              <div class="flex flex-wrap gap-2 items-center">
                <div class="AbilityName text-xl text-dota-goldLight tracking-wide font-extrabold">
                  {{
                    activeSelectedAbility.name_loc || activeSelectedAbility.name
                  }}
                </div>
                <span
                  v-if="activeSelectedAbility.ability_is_innate"
                  class="text-xs text-emerald-300 font-semibold px-2 py-0.5 border border-emerald-700/60 rounded bg-emerald-950/60 shadow-xs"
                >
                  先天技能
                </span>
                <span
                  v-if="activeSelectedAbility.ability_is_granted_by_scepter"
                  class="text-xs text-amber-300 font-semibold px-2 py-0.5 border border-amber-600/60 rounded bg-amber-950/60 shadow-xs"
                >
                  神杖提供新技能
                </span>
                <span
                  v-if="activeSelectedAbility.ability_is_granted_by_shard"
                  class="text-xs text-cyan-300 font-semibold px-2 py-0.5 border border-cyan-700/60 rounded bg-cyan-950/60 shadow-xs"
                >
                  魔晶提供新技能
                </span>
              </div>

              <!-- 技能官方描述 -->
              <div class="AbilityDesc text-xs text-slate-200 leading-relaxed p-3 border border-dota-border/40 rounded-lg bg-black/30 whitespace-pre-line">
                {{
                  cleanHtml(
                    resolveAbilityText(
                      activeSelectedAbility.desc_loc,
                      activeSelectedAbility,
                      "none",
                      activeDetails || undefined,
                    ),
                  )
                }}
              </div>
            </div>
          </div>

          <!-- AbilityDetails: GenericValues + SpecificValues + BottomValues + Lore -->
          <div class="AbilityDetails space-y-4">
            <!-- GenericValues 基础属性矩阵 (官网两列 Column 布局) -->
            <div class="GenericValues p-3.5 border border-dota-border/60 rounded-xl bg-black/40 gap-4 grid grid-cols-1 md:grid-cols-2">
              <div class="Column space-y-2">
                <div class="DetailsValues space-y-1.5">
                  <div class="ValueElement text-xs py-1 flex items-baseline justify-between">
                    <span class="text-dota-subtle font-medium">技能:</span>
                    <div class="ValueValue text-dota-text font-semibold flex items-center space-x-1">
                      <span>{{ behaviorInfo.primary }}</span>
                      <span v-if="behaviorInfo.modifiers.length" class="text-3xs text-dota-subtle">
                        ({{ behaviorInfo.modifiers.join(' · ') }})
                      </span>
                    </div>
                  </div>

                  <div v-if="affectsLabel" class="ValueElement text-xs py-1 flex items-baseline justify-between">
                    <span class="text-dota-subtle font-medium">影响:</span>
                    <div class="ValueValue text-dota-text font-semibold">
                      <span>{{ affectsLabel }}</span>
                    </div>
                  </div>

                  <div v-if="damageTypeLabel && damageTypeLabel !== '不适用'" class="ValueElement text-xs py-1 flex items-baseline justify-between">
                    <span class="text-dota-subtle font-medium">伤害类型:</span>
                    <div class="ValueValue font-bold" :style="{ color: damageTypeColor }">
                      <span>{{ damageTypeLabel }}</span>
                    </div>
                  </div>
                </div>
              </div>

              <div class="Column space-y-2">
                <div class="DetailsValues space-y-1.5">
                  <div class="ValueElement text-xs py-1 flex items-baseline justify-between">
                    <span class="text-dota-subtle font-medium">无视减益免疫:</span>
                    <div class="ValueValue font-semibold" :style="{ color: immunityColor }">
                      <span>{{ immunityLabel }}</span>
                    </div>
                  </div>

                  <div v-if="dispellableLabel && dispellableLabel !== '不适用'" class="ValueElement text-xs py-1 flex items-baseline justify-between">
                    <span class="text-dota-subtle font-medium">可以驱散:</span>
                    <div class="ValueValue text-slate-300 font-semibold">
                      <span>{{ dispellableLabel }}</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <!-- SpecificValues 各等级数值列表 (响应式 2 列紧凑网格，多等级复杂条目独占整行) -->
            <div
              v-if="activeAbilityLevelValues.length > 0"
              class="SpecificValues p-2.5 border border-dota-border/50 rounded-xl bg-black/35 gap-2 grid grid-cols-1 md:grid-cols-2"
            >
              <div
                v-for="(v, vIdx) in activeAbilityLevelValues"
                :key="vIdx"
                class="SpecialElement text-xs px-2.5 py-2 rounded bg-black/20 flex gap-4 transition-colors items-baseline justify-between hover:bg-white/[0.03]"
                :class="isMultiLevelSpecial(v) ? 'md:col-span-2 col-span-full' : ''"
              >
                <span class="text-dota-subtle font-medium shrink-0 select-text">{{ cleanInlineText(v.label) }}：</span>
                <div class="SpecialValue text-dota-goldLight tracking-tight font-bold font-mono text-right select-text break-words">
                  {{ v.display }}
                </div>
              </div>
            </div>

            <!-- BottomValues 底部冷却与蓝耗容器 (新官方图标) -->
            <div
              v-if="hasCooldownOrMana"
              class="BottomValues pt-1 flex flex-wrap gap-3 items-center"
            >
              <div
                v-if="hasCooldown"
                class="CooldownContainer px-3 py-1.5 border border-amber-500/40 rounded-lg bg-black/60 flex shadow-sm items-center space-x-2"
                :title="`冷却时间: ${cooldownDisplay} 秒`"
              >
                <div class="CooldownIcon flex shrink-0 items-center">
                  <img
                    src="/assets/icons/cooldown_icon.png"
                    alt="冷却时间"
                    class="h-4 w-4 object-contain"
                  />
                </div>
                <div class="CooldownText text-xs text-amber-300 font-bold font-mono">
                  {{ cooldownDisplay }}
                </div>
              </div>

              <div
                v-if="hasManaCost"
                class="ManaContainer px-3 py-1.5 border border-cyan-500/40 rounded-lg bg-black/60 flex shadow-sm items-center space-x-2"
                :title="`魔法消耗: ${manaDisplay}`"
              >
                <div class="ManaIcon flex shrink-0 items-center">
                  <img
                    src="/assets/icons/manacost_icon.png"
                    alt="魔法消耗"
                    class="h-4 w-4 object-contain"
                  />
                </div>
                <div class="ManaText text-xs text-cyan-300 font-bold font-mono">
                  {{ manaDisplay }}
                </div>
              </div>
            </div>

            <!-- Lore 官方背景传说引言 -->
            <div
              v-if="activeAbilityLore"
              class="Lore text-xs text-dota-subtle/80 leading-relaxed px-3.5 py-2.5 border-dota-gold/50 border-l-2 rounded-lg bg-black/25 italic"
            >
              {{ activeAbilityLore.text }}
            </div>
          </div>
        </div>

        <!-- 阿哈利姆神杖与魔晶效果卡片 (横向撑满整个宽度，绝不局限于半宽) -->
        <div
          v-if="isEffectiveScepterUpgrade(activeSelectedAbility) || isEffectiveShardUpgrade(activeSelectedAbility)"
          class="w-full space-y-3"
        >
          <!-- 神杖效果卡片 (w-full 撑满整行) -->
          <div
            v-if="isEffectiveScepterUpgrade(activeSelectedAbility)"
            class="p-3.5 border border-cyan-800/40 rounded-xl bg-cyan-950/20 flex w-full shadow-xs items-start space-x-3"
          >
            <div class="p-1 border border-cyan-800/50 rounded-lg bg-cyan-950/60 shrink-0">
              <img src="/assets/icons/ultimate_scepter.png" alt="阿哈利姆神杖" class="h-8 w-11 object-contain" />
            </div>
            <div class="flex-1 min-w-0 space-y-1">
              <div class="text-xs text-cyan-300 font-bold flex items-center justify-between">
                <div class="flex items-center space-x-2">
                  <span>阿哈利姆神杖技能增强</span>
                  <span class="text-3xs text-cyan-300 px-1.5 py-0.5 border border-cyan-600/40 rounded bg-cyan-950/80">
                    神杖升级
                  </span>
                </div>
              </div>
              <p class="text-xs text-dota-text leading-relaxed">
                {{
                  cleanHtml(
                    resolveAbilityText(
                      activeSelectedAbility.scepter_loc,
                      activeSelectedAbility,
                      "scepter",
                      activeDetails || undefined,
                    ),
                  )
                }}
              </p>
            </div>
          </div>

          <!-- 魔晶效果卡片 (w-full 撑满整行) -->
          <div
            v-if="isEffectiveShardUpgrade(activeSelectedAbility)"
            class="p-3.5 border border-cyan-800/40 rounded-xl bg-cyan-950/20 flex w-full shadow-xs items-start space-x-3"
          >
            <div
              class="p-1 border border-cyan-800/50 rounded-lg bg-cyan-950/60 shrink-0"
            >
              <img
                src="/assets/icons/aghanims_shard.png"
                alt="阿哈利姆魔晶"
                class="h-8 w-11 object-contain"
              />
            </div>
            <div class="flex-1 min-w-0 space-y-1">
              <div class="text-xs text-cyan-300 font-bold flex items-center justify-between">
                <div class="flex items-center space-x-2">
                  <span>阿哈利姆魔晶技能增强</span>
                  <span class="text-3xs text-cyan-300 px-1.5 py-0.5 border border-cyan-600/40 rounded bg-cyan-950/80">
                    魔晶升级
                  </span>
                </div>
              </div>
              <p class="text-xs text-dota-text leading-relaxed">
                {{
                  cleanHtml(
                    resolveAbilityText(
                      activeSelectedAbility.shard_loc,
                      activeSelectedAbility,
                      "shard",
                      activeDetails || undefined,
                    ),
                  )
                }}
              </p>
            </div>
          </div>
        </div>

        <!-- 技能机制要点与更新日志 -->
        <div
          v-if="
            currentAbilityTips.length > 0
            || currentAbilityNotes.length > 0
            || currentAbilityPatchNotes.length > 0
          "
          class="p-3.5 border border-amber-500/30 rounded-xl shadow-md from-amber-950/25 to-dota-panel/90 bg-gradient-to-br space-y-2.5"
        >
          <div
            class="pb-1.5 border-b border-amber-500/20 flex items-center justify-between"
          >
            <div
              class="text-xs text-amber-300 font-bold flex items-center space-x-2"
            >
              <Sparkles class="text-amber-400 h-3.5 w-3.5" />
              <span
                >{{
                  activeSelectedAbility.name_loc || activeSelectedAbility.name
                }}
                实战要领与机制要点</span
              >
            </div>
            <span
              v-if="currentAbilityPatchNotes.length > 0"
              class="text-2xs text-amber-300 font-bold font-mono px-1.5 py-0.5 border border-amber-500/40 rounded bg-amber-500/20"
              >{{ currentPatch }} 调整</span
            >
          </div>

          <div v-if="currentAbilityTips.length > 0" class="space-y-1.5">
            <div
              v-for="(t, idx) in currentAbilityTips"
              :key="idx"
              class="p-2 border border-amber-900/40 rounded-lg bg-black/40 space-y-0.5"
            >
              <div
                class="text-xs text-amber-300 font-semibold flex items-center space-x-1"
              >
                <span class="rounded-full bg-amber-400 h-1.5 w-1.5" />
                <span>{{ t.title }}</span>
              </div>
              <p class="text-xs text-dota-text leading-relaxed pl-2.5">
                {{ cleanHtml(t.content) }}
              </p>
            </div>
          </div>

          <div
            v-if="currentAbilityNotes.length > 0"
            class="text-xs text-slate-300 p-2 border border-slate-800 rounded-lg bg-black/30 space-y-1"
          >
            <div class="text-xs text-slate-400 font-medium">官方机制注解：</div>
            <ul
              class="text-xs text-slate-300 list-disc list-inside space-y-0.5"
            >
              <li v-for="(note, nIdx) in currentAbilityNotes" :key="nIdx">
                {{
                  cleanHtml(
                    resolveAbilityText(
                      note,
                      activeSelectedAbility,
                      "none",
                      activeDetails || undefined,
                    ),
                  )
                }}
              </li>
            </ul>
          </div>

          <div
            v-if="currentAbilityPatchNotes.length > 0"
            class="text-xs text-amber-200/90 p-2 border border-amber-800/60 rounded-lg bg-amber-950/40 space-y-1"
          >
            <div class="text-xs text-amber-400 font-bold">
              现行版本 (v{{ currentPatch }}) 调整记录：
            </div>
            <ul class="list-disc list-inside space-y-0.5">
              <li v-for="(pNote, pIdx) in currentAbilityPatchNotes" :key="pIdx">
                {{
                  cleanHtml(
                    resolveAbilityText(
                      pNote,
                      activeSelectedAbility,
                      "none",
                      activeDetails || undefined,
                    ),
                  )
                }}
              </li>
            </ul>
          </div>
        </div>
      </div>

      <!-- 模式 B：英雄天赋树全景展示 -->
      <div v-else-if="selectedShowcaseTab === 'talents'" class="py-2 space-y-4">
        <div class="mx-auto py-2 max-w-3xl space-y-3">
          <div
            v-for="(lvl, index) in [25, 20, 15, 10]"
            :key="lvl"
            class="flex items-center space-x-2"
          >
            <div
              class="text-xs text-dota-text leading-relaxed p-3 text-right border border-dota-border/60 rounded-xl bg-dota-panel flex-1 shadow-sm transition-all hover:border-dota-gold/50 hover:bg-dota-hover"
              :title="
                cleanHtml(
                  resolveTalentText(
                    activeDetails?.talents?.[index * 2 + 1],
                    activeDetails || undefined,
                  ),
                )
              "
            >
              {{
                cleanHtml(
                  resolveTalentText(
                    activeDetails?.talents?.[index * 2 + 1],
                    activeDetails || undefined,
                  ),
                )
              }}
            </div>

            <div
              class="text-xs text-slate-950 font-black font-mono border-2 border-amber-300 rounded-full flex shrink-0 h-9 w-9 shadow-lg items-center justify-center from-amber-400 to-amber-600 bg-gradient-to-b"
            >
              {{ lvl }}
            </div>

            <div
              class="text-xs text-dota-text leading-relaxed p-3 text-left border border-dota-border/60 rounded-xl bg-dota-panel flex-1 shadow-sm transition-all hover:border-dota-gold/50 hover:bg-dota-hover"
              :title="
                cleanHtml(
                  resolveTalentText(
                    activeDetails?.talents?.[index * 2],
                    activeDetails || undefined,
                  ),
                )
              "
            >
              {{
                cleanHtml(
                  resolveTalentText(
                    activeDetails?.talents?.[index * 2],
                    activeDetails || undefined,
                  ),
                )
              }}
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
