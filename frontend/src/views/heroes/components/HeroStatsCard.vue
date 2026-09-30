<script setup lang="ts">
/**
 * 英雄基础属性与资料卡片组件 (src/views/heroes/components/HeroStatsCard.vue)
 */

import type { HeroStat } from '@/types'
import type { OfficialHero } from '@/utils/abilityTextResolver.ts'
import type { Level1Stats } from '@/utils/heroStats.ts'
import { ChevronDown } from '@lucide/vue'
import { ref } from 'vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { dataSyncService } from '@/services/dataSyncService.ts'
import { getHeroCdnUrl, getHeroIconUrl } from '@/utils/assetUrl.ts'
import { cleanHtml } from '@/utils/htmlCleaner.ts'

defineOptions({ name: 'HeroStatsCard' })

defineProps<{
  hero: HeroStat
  officialDetails: OfficialHero | null
  level1Stats: Level1Stats | null
}>()

const isBioExpanded = ref(false)

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
</script>

<template>
  <div
    class="p-4 border border-dota-border rounded-xl bg-dota-panel/95 flex flex-col gap-3.5 select-none shadow-lg relative overflow-hidden"
  >
    <div
      v-if="hero.img"
      class="opacity-10 w-full pointer-events-none bottom-0 right-0 top-0 absolute bg-cover bg-right bg-no-repeat"
      :style="{ backgroundImage: `url(${getHeroIconUrl(hero.name)})` }"
    />

    <!-- 顶部：英雄头像与核心定位 -->
    <div class="flex items-start relative z-10 space-x-3.5">
      <!-- 英雄头像：使用 SmartImage -->
      <div class="border-2 border-dota-border/80 rounded-xl bg-slate-950 shrink-0 h-14 w-21 shadow-md overflow-hidden">
        <SmartImage
          :local-src="getHeroIconUrl(hero.name)"
          :cdn-src="getHeroCdnUrl(hero.name)"
          :alt="hero.localized_name"
          class="h-full w-full object-cover"
        />
      </div>

      <div class="flex-1 min-w-0 space-y-1">
        <div class="flex flex-wrap gap-2 items-center">
          <h1 class="text-xl text-dota-gold tracking-wide font-extrabold truncate">
            {{ hero.localized_name }}
          </h1>
          <span
            v-if="dataSyncService.isHeroNew(hero.id)"
            class="text-2xs text-slate-950 tracking-wider font-black px-1.5 py-0.5 border border-amber-300 rounded uppercase shadow animate-pulse from-amber-500 to-rose-500 bg-gradient-to-r"
          >
            官方更新
          </span>
        </div>

        <div class="text-xs text-dota-muted flex flex-wrap gap-1 items-center">
          <span class="text-dota-gold font-medium">核心定位:</span>
          <span class="text-slate-300">{{ hero.roles.map(getRoleChineseName).join(" · ") }}</span>
        </div>

        <p v-if="officialDetails?.hype_loc" class="text-2xs text-dota-subtle leading-relaxed line-clamp-2">
          {{ cleanHtml(officialDetails.hype_loc) }}
        </p>
      </div>
    </div>

    <!-- 背景故事折叠展开 -->
    <div v-if="officialDetails?.bio_loc" class="relative z-10">
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
        class="text-xs text-dota-muted leading-relaxed mt-2 p-3 border border-dota-border/60 rounded-xl bg-black/60 max-h-48 whitespace-pre-line shadow-inner overflow-y-auto"
      >
        {{ cleanHtml(officialDetails.bio_loc) }}
      </div>
    </div>

    <!-- 紧凑属性卡片区 (力/敏/智三维成长) -->
    <div class="font-sans flex flex-col relative z-10 space-y-2.5">
      <div class="text-xs gap-2 grid grid-cols-3">
        <!-- 力量卡片 -->
        <div
          class="p-2 border rounded-lg flex transition-all items-center space-x-2"
          :class="[
            hero.primary_attr === 'str'
              ? 'bg-red-950/40 border-dota-red/80 shadow-[0_0_10px_rgba(239,68,68,0.25)]'
              : 'bg-dota-surface/70 border-dota-border/60',
          ]"
        >
          <img src="/assets/icons/hero_strength.png" alt="力量" class="h-4.5 w-4.5 object-contain">
          <div class="min-w-0">
            <div class="text-3xs text-dota-subtle font-medium flex items-center space-x-1">
              <span>力量</span>
              <span v-if="hero.primary_attr === 'str'" class="text-[8px] text-dota-red font-bold px-0.5 rounded bg-dota-red/30">主</span>
            </div>
            <div class="text-xs text-dota-text font-bold font-mono truncate">
              {{ officialDetails?.str_base || hero.base_str }}
              <span class="text-3xs text-dota-subtle font-normal">+{{ officialDetails?.str_gain?.toFixed(1) || hero.str_gain?.toFixed(1) }}</span>
            </div>
          </div>
        </div>

        <!-- 敏捷卡片 -->
        <div
          class="p-2 border rounded-lg flex transition-all items-center space-x-2"
          :class="[
            hero.primary_attr === 'agi'
              ? 'bg-emerald-950/40 border-dota-green/80 shadow-[0_0_10px_rgba(16,185,129,0.25)]'
              : 'bg-dota-surface/70 border-dota-border/60',
          ]"
        >
          <img src="/assets/icons/hero_agility.png" alt="敏捷" class="h-4.5 w-4.5 object-contain">
          <div class="min-w-0">
            <div class="text-3xs text-dota-subtle font-medium flex items-center space-x-1">
              <span>敏捷</span>
              <span v-if="hero.primary_attr === 'agi'" class="text-[8px] text-dota-green font-bold px-0.5 rounded bg-dota-green/30">主</span>
            </div>
            <div class="text-xs text-dota-text font-bold font-mono truncate">
              {{ officialDetails?.agi_base || hero.base_agi }}
              <span class="text-3xs text-dota-subtle font-normal">+{{ officialDetails?.agi_gain?.toFixed(1) || hero.agi_gain?.toFixed(1) }}</span>
            </div>
          </div>
        </div>

        <!-- 智力卡片 -->
        <div
          class="p-2 border rounded-lg flex transition-all items-center space-x-2"
          :class="[
            hero.primary_attr === 'int'
              ? 'bg-blue-950/40 border-dota-cyan/80 shadow-[0_0_10px_rgba(56,189,248,0.25)]'
              : 'bg-dota-surface/70 border-dota-border/60',
          ]"
        >
          <img src="/assets/icons/hero_intelligence.png" alt="智力" class="h-4.5 w-4.5 object-contain">
          <div class="min-w-0">
            <div class="text-3xs text-dota-subtle font-medium flex items-center space-x-1">
              <span>智力</span>
              <span v-if="hero.primary_attr === 'int'" class="text-[8px] text-dota-cyan font-bold px-0.5 rounded bg-dota-cyan/30">主</span>
            </div>
            <div class="text-xs text-dota-text font-bold font-mono truncate">
              {{ officialDetails?.int_base || hero.base_int }}
              <span class="text-3xs text-dota-subtle font-normal">+{{ officialDetails?.int_gain?.toFixed(1) || hero.int_gain?.toFixed(1) }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 1 级生命与魔法条 -->
      <div v-if="level1Stats" class="text-xs font-mono space-y-1">
        <div class="text-xs text-dota-green font-bold px-2.5 border border-emerald-800/60 rounded-md bg-emerald-950/60 flex h-5 shadow-inner items-center justify-between">
          <span>生命值 {{ level1Stats.hp }}</span>
          <span class="text-xs text-emerald-400 font-normal">+{{ level1Stats.hpRegen }}/s</span>
        </div>
        <div class="text-xs text-dota-cyan font-bold px-2.5 border border-blue-800/60 rounded-md bg-blue-950/60 flex h-5 shadow-inner items-center justify-between">
          <span>魔法值 {{ level1Stats.mana }}</span>
          <span class="text-xs text-cyan-300 font-normal">+{{ level1Stats.manaRegen }}/s</span>
        </div>
      </div>

      <!-- 4 宫格数值 (攻击力、护甲、移速、射程) -->
      <div v-if="level1Stats" class="text-xs font-mono gap-1.5 grid grid-cols-4">
        <div class="text-xs p-1 border border-dota-border/60 rounded-md bg-dota-surface/80 flex items-center space-x-1" title="基础攻击力">
          <img src="/assets/icons/icon_damage.png" alt="攻击" class="shrink-0 h-3.5 w-3.5 object-contain">
          <span class="text-dota-text font-bold">{{ level1Stats.attackMin }}-{{ level1Stats.attackMax }}</span>
        </div>
        <div class="text-xs p-1 border border-dota-border/60 rounded-md bg-dota-surface/80 flex items-center space-x-1" title="物理护甲">
          <img src="/assets/icons/icon_armor.png" alt="护甲" class="shrink-0 h-3.5 w-3.5 object-contain">
          <span class="text-dota-text font-bold">{{ level1Stats.armor }}</span>
        </div>
        <div class="text-xs p-1 border border-dota-border/60 rounded-md bg-dota-surface/80 flex items-center space-x-1" title="移动速度">
          <img src="/assets/icons/icon_movement_speed.png" alt="移速" class="shrink-0 h-3.5 w-3.5 object-contain">
          <span class="text-dota-text font-bold">{{ level1Stats.moveSpeed }}</span>
        </div>
        <div class="text-xs p-1 border border-dota-border/60 rounded-md bg-dota-surface/80 flex items-center space-x-1" title="攻击距离">
          <img src="/assets/icons/icon_attack_range.png" alt="射程" class="shrink-0 h-3.5 w-3.5 object-contain">
          <span class="text-dota-text font-bold">{{ hero.attack_range }}</span>
        </div>
      </div>
    </div>
  </div>
</template>
