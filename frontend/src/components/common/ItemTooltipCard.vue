<script setup lang="ts">
/**
 * 通用高保真物品悬浮与锁定详情卡片 (src/components/common/ItemTooltipCard.vue)
 * 职责：
 * 1. 深度复用于物品图鉴 (ItemsView)、英雄出装推荐 (HeroBuildPanel)、赛后对战详情大盘 (MatchDetailPanel)
 * 2. 严格遵循 Valve 官方深色高对比度 HUD 视觉规范
 * 3. 支持未固定 (pointer-events: none 防遮挡) 与 固定 (pointer-events: auto 鼠标滚轮自由滚动)
 * 4. 自动汉化装备与中立物品的属性加成、技能说明，并清洗 0 增益无效占位
 */

import type { PatchInfo } from '@/services/gameDataService.ts'
import {
  Coins,
  GitBranch,
  Info,
  Package,
  Pin,
  X,
} from '@lucide/vue'
import SmartImage from '@/components/common/SmartImage.vue'
import {
  getItemCdnUrl,
  getItemIconUrl,
} from '@/utils/assetUrl.ts'
import { cleanHtml } from '@/utils/htmlCleaner.ts'
import {
  hasValidCd,
  hasValidMc,
  parseAttributes,
  parseItemSkills,
  translateItemNotes,
} from '@/utils/itemTextResolver.ts'

defineOptions({ name: 'ItemTooltipCard' })

const props = defineProps<{
  item: {
    id?: string | number
    key: string
    dname: string
    cost?: number
    tier?: number
    category?: string
    desc?: string
    attrib?: string
    notes?: string
    mc?: string | number | boolean
    cd?: string | number | boolean
    components?: string[] | null
    componentNames?: string[] | null
  } | null
  position: { x: number, y: number }
  pinned?: boolean
  patchInfo?: PatchInfo | null
  getComponentMeta?: (compKey: string) => { dname: string, cost: number, img: string }
  upgrades?: any[]
}>()

const emit = defineEmits<{
  (e: 'unpin'): void
}>()

function getItemIcon(itemOrKey: any): string {
  if (!itemOrKey) return '/assets/icons/aghs_scepter.png'
  const key = typeof itemOrKey === 'string' ? itemOrKey : itemOrKey.key || ''
  return getItemIconUrl(key)
}

function getItemFallbackCdn(itemOrKey: any): string {
  if (!itemOrKey) return 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/recipe.png'
  const key = typeof itemOrKey === 'string' ? itemOrKey : itemOrKey.key || ''
  return getItemCdnUrl(key)
}

function resolveComponent(compKey: string) {
  if (props.getComponentMeta) {
    return props.getComponentMeta(compKey)
  }
  const isRecipe = compKey.includes('recipe')
  return {
    dname: isRecipe ? '合成卷轴' : compKey.replace(/^item_/, ''),
    cost: 0,
    img: isRecipe
      ? 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/recipe.png'
      : getItemIcon(compKey),
  }
}
</script>

<template>
  <div
    v-if="item"
    :style="{
      position: 'fixed',
      left: `${position.x}px`,
      top: `${position.y}px`,
      pointerEvents: pinned ? 'auto' : 'none',
      zIndex: 9999,
    }"
    :class="[
      pinned ? 'select-text ring-1 ring-amber-500/50 shadow-[0_12px_40px_rgba(0,0,0,0.9)]' : 'select-none shadow-2xl',
    ]"
    class="scrollbar-thin font-sans p-3.5 border border-[#405068] rounded-xl bg-[#141720]/95 max-h-[calc(100vh-28px)] max-w-[calc(100vw-24px)] w-92 transition-all duration-75 overflow-y-auto backdrop-blur-md space-y-2.5"
    @click.stop
  >
    <!-- 1. 顶部：图标 + 名称 + 价格/阶级 + 分类 + 固定/关闭状态提示 -->
    <div class="flex gap-2 items-start justify-between">
      <div class="flex min-w-0 items-start space-x-3">
        <div class="border border-dota-gold/50 rounded bg-dota-panel shrink-0 h-10.5 w-14 shadow-md overflow-hidden">
          <SmartImage
            :src="getItemIcon(item)"
            :fallback-src="getItemFallbackCdn(item)"
            :alt="item.dname"
            class="h-full w-full object-cover"
          />
        </div>
        <div class="flex-1 min-w-0">
          <div class="flex items-center space-x-2">
            <h3 class="text-sm text-dota-goldLight font-bold truncate">
              {{ item.dname }}
            </h3>
          </div>

          <div class="text-xs font-mono mt-0.5 flex items-center space-x-2">
            <div v-if="(item.cost || 0) > 0" class="text-dota-gold font-bold flex items-center space-x-1">
              <Coins class="h-3.5 w-3.5" />
              <span>{{ item.cost }}</span>
            </div>
            <span v-else class="text-dota-cyan font-bold">
              {{ (item.tier || 0) > 0 ? `第 ${item.tier} 阶中立` : '免费' }}
            </span>
            <span v-if="item.category" class="text-dota-subtle">·</span>
            <span v-if="item.category" class="text-dota-muted">{{ item.category }}</span>
          </div>
        </div>
      </div>

      <!-- 固定状态操作区 -->
      <div class="pt-0.5 flex shrink-0 items-center">
        <button
          v-if="pinned"
          type="button"
          class="text-xs text-amber-300 px-2 py-0.5 border border-amber-500/40 rounded bg-amber-500/20 flex cursor-pointer transition-colors items-center space-x-1 hover:bg-amber-500/30"
          title="点击解除固定 (Esc)"
          @click.stop="emit('unpin')"
        >
          <Pin class="text-amber-400 h-3 w-3 rotate-45" />
          <span class="text-[11px] font-medium">已固定</span>
          <X class="text-amber-400/80 ml-0.5 h-3 w-3 hover:text-white" />
        </button>
        <span v-else class="text-[10px] text-dota-subtle/80 tracking-tight">
          悬浮预览
        </span>
      </div>
    </div>

    <!-- 2. 紧凑网格属性加成展示 (2 列药丸排版，汉化英文词汇) -->
    <div v-if="parseAttributes(item.attrib).length > 0" class="pt-2 border-t border-dota-border/60 space-y-1">
      <div class="text-xs text-dota-subtle tracking-wider font-bold uppercase">
        基础属性加成
      </div>
      <div class="gap-1.5 grid grid-cols-2">
        <div
          v-for="(attr, idx) in parseAttributes(item.attrib)"
          :key="idx"
          class="text-xs text-emerald-300 leading-tight font-mono px-2 py-1 border border-dota-border/70 rounded bg-dota-surface/90 flex shadow-xs items-center"
        >
          {{ attr }}
        </div>
      </div>
    </div>

    <!-- 3. 技能描述 (参考 Liquipedia spellcard 结构化视觉设计) -->
    <div v-if="item.desc" class="space-y-2.5">
      <div
        v-for="(skill, sIdx) in parseItemSkills(item.desc)"
        :key="sIdx"
        class="spellcard border rounded-lg shadow-md overflow-hidden"
        :class="[
          skill.type === '主动' ? 'border-amber-600/40 bg-amber-950/15'
          : skill.type === '被动' ? 'border-emerald-600/40 bg-emerald-950/15'
          : skill.type === '开关' ? 'border-cyan-600/40 bg-cyan-950/15'
          : skill.type === '使用' ? 'border-purple-600/40 bg-purple-950/15'
          : 'border-dota-border/60 bg-dota-surface/60',
        ]"
      >
        <div
          class="px-2.5 py-1.5 border-b flex items-center justify-between"
          :class="[
            skill.type === '主动' ? 'border-amber-700/50 from-amber-900/50 via-neutral-900 to-black bg-gradient-to-r'
            : skill.type === '被动' ? 'border-emerald-700/50 from-emerald-900/50 via-neutral-900 to-black bg-gradient-to-r'
            : skill.type === '开关' ? 'border-cyan-700/50 from-cyan-900/50 via-neutral-900 to-black bg-gradient-to-r'
            : skill.type === '使用' ? 'border-purple-700/50 from-purple-900/50 via-neutral-900 to-black bg-gradient-to-r'
            : 'border-dota-border/50 from-neutral-800 via-neutral-900 to-black bg-gradient-to-r',
          ]"
        >
          <div class="flex items-center space-x-2">
            <span
              class="text-xs font-bold px-1.5 py-0.5 rounded shrink-0 shadow-xs"
              :class="[
                skill.type === '主动' ? 'text-amber-200 border border-amber-500/60 bg-amber-500/25'
                : skill.type === '被动' ? 'text-emerald-200 border border-emerald-500/60 bg-emerald-500/25'
                : skill.type === '开关' ? 'text-cyan-200 border border-cyan-500/60 bg-cyan-500/25'
                : skill.type === '使用' ? 'text-purple-200 border border-purple-500/60 bg-purple-500/25'
                : 'text-slate-200 border border-slate-600/60 bg-slate-700/40',
              ]"
            >
              {{ skill.type }}
            </span>
            <span class="text-xs text-white tracking-wide font-bold drop-shadow-[0_1px_2px_rgba(0,0,0,0.8)]">
              {{ skill.title }}
            </span>
          </div>
        </div>

        <div class="text-xs text-slate-200 leading-relaxed p-2.5 whitespace-pre-line">
          {{ skill.body }}
        </div>
      </div>
    </div>

    <!-- 4. 机制提示 -->
    <div
      v-if="item.notes && item.notes.trim()"
      class="text-xs text-amber-200/90 leading-relaxed p-2.5 border border-amber-500/30 rounded-lg bg-amber-950/25 space-y-1"
    >
      <div class="text-2xs text-amber-400 font-bold flex items-center space-x-1">
        <Info class="shrink-0 h-3 w-3" />
        <span>机制提示</span>
      </div>
      <div class="text-slate-300 whitespace-pre-line">
        {{ translateItemNotes(item.notes) }}
      </div>
    </div>

    <!-- 5. 冷却与魔耗 -->
    <div v-if="hasValidCd(item.cd) || hasValidMc(item.mc)" class="pt-2 border-t border-dota-border/50 flex flex-wrap gap-2.5 items-center">
      <div
        v-if="hasValidCd(item.cd)"
        class="px-2.5 py-1 border border-amber-500/40 rounded-md bg-black/70 flex shadow-xs items-center space-x-1.5"
        :title="`冷却时间: ${item.cd} 秒`"
      >
        <img
          src="/assets/icons/cooldown_icon.png"
          alt="冷却时间"
          class="h-3.5 w-3.5 object-contain"
        />
        <span class="text-xs text-amber-300 font-bold font-mono">{{ item.cd }}s</span>
      </div>
      <div
        v-if="hasValidMc(item.mc)"
        class="px-2.5 py-1 border border-cyan-500/40 rounded-md bg-black/70 flex shadow-xs items-center space-x-1.5"
        :title="`魔法消耗: ${item.mc}`"
      >
        <img
          src="/assets/icons/manacost_icon.png"
          alt="魔法消耗"
          class="h-3.5 w-3.5 object-contain"
        />
        <span class="text-xs text-cyan-300 font-bold font-mono">{{ item.mc }}</span>
      </div>
    </div>

    <!-- 6. 合成配方与前置配件清单 -->
    <div v-if="item.components && item.components.length > 0" class="pt-2.5 border-t border-dota-border/60 space-y-1.5">
      <div class="text-xs text-dota-goldLight font-bold flex items-center space-x-1.5">
        <Package class="text-dota-gold h-3.5 w-3.5" />
        <span>合成配方前置配件 ({{ item.components.length }} 件):</span>
      </div>
      <div class="flex flex-wrap gap-1.5 items-center space-x-2">
        <div
          v-for="(compKey, i) in item.components"
          :key="i"
          class="px-2 py-1 border border-dota-border rounded bg-dota-surface/90 flex shadow-sm transition-colors items-center space-x-1.5 hover:border-dota-gold/60"
        >
          <SmartImage
            :src="getItemIcon(compKey)"
            :fallback-src="getItemFallbackCdn(compKey)"
            :alt="resolveComponent(compKey).dname"
            class="border border-dota-border/60 rounded-xs shrink-0 h-4.5 w-6 object-cover"
          />
          <span class="text-xs text-dota-text font-medium">{{ resolveComponent(compKey).dname }}</span>
          <span v-if="resolveComponent(compKey).cost > 0" class="text-xs text-dota-gold font-mono flex items-center">
            <Coins class="mr-0.5 h-2.5 w-2.5" />
            {{ resolveComponent(compKey).cost }}
          </span>
        </div>
      </div>
    </div>

    <!-- 7. 进阶合成去向 -->
    <div v-if="upgrades && upgrades.length > 0" class="pt-2 border-t border-dota-border/60 space-y-1.5">
      <div class="text-xs text-dota-cyan font-bold flex items-center space-x-1">
        <GitBranch class="text-dota-cyan h-3 w-3" />
        <span>可用于进阶合成:</span>
      </div>
      <div class="flex flex-wrap gap-1 items-center space-x-1.5">
        <div
          v-for="upg in upgrades"
          :key="upg.key"
          class="text-xs px-1.5 py-0.5 border border-dota-border rounded bg-dota-surface/70 flex items-center space-x-1"
          :title="`${upg.dname} (${upg.cost}金)`"
        >
          <SmartImage
            :src="getItemIcon(upg)"
            :fallback-src="getItemFallbackCdn(upg)"
            :alt="upg.dname"
            class="rounded-xs shrink-0 h-3.5 w-5 object-cover"
          />
          <span class="text-dota-text font-medium">{{ upg.dname }}</span>
        </div>
      </div>
    </div>

    <!-- 8. 现行版本补丁调整说明 -->
    <div
      v-if="item.id && patchInfo?.itemPatchNotes?.[Number(item.id)]?.length"
      class="text-xs text-amber-200/90 p-2 pt-2 border border-t border-amber-500/40 border-amber-800/50 rounded-lg bg-amber-950/30 space-y-1"
    >
      <div class="text-xs text-amber-400 font-bold flex items-center justify-between">
        <span>版本调整日志</span>
      </div>
      <ul class="list-disc list-inside space-y-0.5">
        <li v-for="(note, nIdx) in patchInfo.itemPatchNotes[Number(item.id)]" :key="nIdx">
          {{ cleanHtml(note) }}
        </li>
      </ul>
    </div>
  </div>
</template>
