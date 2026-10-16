<script setup lang="ts">
/**
 * 实时对局战况与 AI 战术参谋核心视图 (src/views/CoachView.vue)
 * 职责：
 * 1. 呈现基于 10Hz GSI 毫秒快照的实战 HUD（英雄生命/魔法、KDA、GPM/XPM、即时买活状态、6格物品与技能冷却）
 * 2. 遵守开工铁律第 8 条（零伪造原则）：无对局连接时严格呈现“等待 GSI 接入”真实空态守卫
 * 3. 提供 AI 实时战术参谋问答交互（Solution Hierarchy 思维链快速生成团战/出装/控盾决策）
 * 4. 实时动态展示确定性规则引擎生成的战略告警与决策建议流
 */

import type { CoachAdvice } from '@/types'
import {
  Moon,
  Radio,
  Shield,
  ShieldAlert,
  Sparkles,
  Sun,
  Volume2,
} from '@lucide/vue'
import { computed, onMounted } from 'vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { activeBattleContext } from '@/services/gsiRuleEngine.ts'
import { speechService } from '@/services/speechService.ts'
import { useAppStore, useBattleStore, useVisionStore } from '@/stores'
import {
  getAbilityCdnUrl,
  getAbilityIconUrl,
  getHeroCdnUrl,
  getHeroIconUrl,
  getItemCdnUrl,
  getItemIconUrl,
} from '@/utils/assetUrl.ts'
import WindowControls from '@/components/layout/WindowControls.vue'
import MatchupRecommendationPanel from './coach/MatchupRecommendationPanel.vue'
import BpVisionCaptureCard from './coach/BpVisionCaptureCard.vue'

defineOptions({
  name: 'CoachView',
})

const appStore = useAppStore()
const battleStore = useBattleStore()
const visionStore = useVisionStore()

/** 实战全局上下文响应式对象引用 */
const battle = activeBattleContext

/** 当前背包装备内部标识名称列表 (供针对性出装面板高亮已拥有状态) */
const currentInventoryItemNames = computed<string[]>(() => {
  const list: string[] = []
  if (battle.items) {
    for (const it of battle.items) {
      if (it && it.name) list.push(it.name)
    }
  }
  if (battle.backpack) {
    for (const it of battle.backpack) {
      if (it && it.name) list.push(it.name)
    }
  }
  return list
})

onMounted(async () => {
  await appStore.checkOverlayStatus()
})

/**
 * 格式化比赛时钟秒数为 MM:SS 文本
 * @param seconds 比赛时钟（负数表示号角吹响前的准备期）
 */
function formatClockTime(seconds: number): string {
  const isNegative = seconds < 0
  const abs = Math.abs(seconds)
  const min = Math.floor(abs / 60)
  const sec = (abs % 60).toString().padStart(2, '0')
  return `${isNegative ? '-' : ''}${min}:${sec}`
}

/**
 * 获取英雄头像 URL (本地缓存优先，落盘兜底)
 */
function getHeroAvatarUrl(internalName: string): string {
  return getHeroIconUrl(internalName)
}

/**
 * 获取技能图标 URL (本地缓存优先，落盘兜底)
 */
function getAbilityIcon(abilityName: string): string {
  return getAbilityIconUrl(abilityName)
}

/**
 * 获取物品图标 URL (维护官网规格，本地缓存优先)
 */
function getItemIcon(itemName: string): string {
  return getItemIconUrl(itemName)
}

/**
 * 解析战术规则的视觉标签名称
 * @param ruleKey 规则键名
 */
function getRuleDisplayTag(ruleKey?: string): string | null {
  if (!ruleKey) return null
  const clean = ruleKey.toUpperCase().replace(/^\[|\]$/g, '').trim()
  const map: Record<string, string> = {
    ROSHAN_WINDOW: '肉山掌控',
    TORMENTOR_SPAWN: '魔方刷新',
    POWER_RUNE_SPAWN: '神符争夺',
    RUNE_SPAWN: '神符争夺',
    BUYBACK_SHORTAGE: '买活储备',
    BUYBACK_READY: '买活警戒',
    KEY_ITEM_BLINK: '跳刀成型',
    KEY_ITEM_BKB: '黑皇杖成型',
    AI_TACTICAL_HIGHGROUND: '决战上高',
    AI_TACTICAL_CONSULTANT: 'AI 参谋研判',
    DEATH_RESPAWN: '阵亡复活',
    ABILITY_READY: '核心技能',
  }
  return map[clean] || (clean.startsWith('RULE_') ? '即时战术' : clean.replace(/_/g, ' '))
}

/**
 * 重新播报单条战术建议的语音
 * @param item 建议数据项
 */
function replayVoice(item: CoachAdvice) {
  speechService.speak(
    `${item.title}。${item.text}`,
    {
      mode: 2,
      rate: appStore.settings.voice_speed,
      volume: appStore.settings.voice_volume,
      voiceName: appStore.settings.voice_name,
    },
  )
}



const buybackCardClass = computed(() => {
  return battle.canBuyback
    ? 'bg-emerald-950/50 border-emerald-700/60'
    : 'bg-slate-950/80 border-slate-800'
})

const buybackTextClass = computed(() => {
  return battle.canBuyback ? 'text-emerald-400' : 'text-slate-400'
})

function getAdviceBorderClass(severity: string) {
  if (severity === 'Critical') return 'border-rose-500/60 shadow-rose-950/20'
  if (severity === 'Warning') return 'border-amber-500/60 shadow-amber-950/20'
  return 'border-cyan-500/40 shadow-cyan-950/20'
}

function getAdviceBarClass(severity: string) {
  if (severity === 'Critical') return 'bg-rose-500'
  if (severity === 'Warning') return 'bg-amber-500'
  return 'bg-cyan-500'
}

function getAdviceBadgeClass(severity: string) {
  if (severity === 'Critical') return 'bg-rose-500/20 text-rose-300 border border-rose-500/40'
  if (severity === 'Warning') return 'bg-amber-500/20 text-amber-300 border border-amber-500/40'
  return 'bg-cyan-500/20 text-cyan-300 border border-cyan-500/40'
}
</script>

<template>
  <div class="text-slate-100 bg-dota-bg flex flex-1 flex-col select-none overflow-hidden">
    <!-- 1. 顶部实战对局状态栏 (纯实战信息展示与快捷操作，整体置顶并支持窗口拖拽) -->
    <header
      class="px-5 border-b border-dota-border bg-dota-surface flex shrink-0 h-13 items-center justify-between"
      style="--wails-draggable: drag"
    >
      <div class="flex items-center space-x-3.5">
        <!-- GSI 状态指示 -->
        <div class="flex items-center space-x-2">
          <span
            class="rounded-full h-2.5 w-2.5 ring-4 transition-all"
            :class="[
              battle.connected
                ? 'bg-emerald-400 ring-emerald-500/20 animate-pulse'
                : 'bg-slate-600 ring-slate-700/20',
            ]"
          />
          <span class="text-xs tracking-wide font-bold">
            {{ battle.connected ? `Dota 2 在线 · ${battle.gameState}` : '等待 Dota 2 客户端启动' }}
          </span>
        </div>

        <!-- 比赛时钟与昼夜指示 -->
        <div v-if="battle.connected" class="text-xs text-amber-400 font-bold font-mono px-2.5 py-0.5 border border-slate-800 rounded-md bg-slate-950 flex items-center space-x-1.5">
          <Sun v-if="battle.daytime" class="text-amber-400 h-3.5 w-3.5" />
          <Moon v-else class="text-cyan-400 h-3.5 w-3.5" />
          <span>{{ formatClockTime(battle.clockTime) }}</span>
        </div>

        <!-- 当前生效 AI 引擎只读指示 -->
        <span
          v-if="appStore.settings.ai_mode === 'rule_only'"
          class="text-xs text-slate-300 font-medium font-mono px-2 py-0.5 border border-slate-700 rounded-md bg-slate-800 flex items-center space-x-1"
        >
          <ShieldAlert class="text-slate-400 h-3 w-3" />
          <span>规则引擎纯降级</span>
        </span>
        <span
          v-else-if="appStore.settings.ai_mode === 'local_npu' || appStore.settings.ai_mode === 'npu'"
          class="text-xs text-emerald-300 font-medium font-mono px-2 py-0.5 border border-emerald-700/40 rounded-md bg-emerald-950/60 flex items-center space-x-1"
        >
          <Cpu class="text-emerald-400 h-3 w-3" />
          <span>端侧高能效: {{ appStore.settings.npu_model }}</span>
        </span>
        <span
          v-else
          class="text-xs text-amber-300 font-medium font-mono px-2 py-0.5 border border-amber-700/40 rounded-md bg-amber-950/60 flex items-center space-x-1"
        >
          <Sparkles class="text-amber-400 h-3 w-3" />
          <span>云端大模型: {{ appStore.settings.cloud_model }}</span>
        </span>
      </div>

      <!-- 右侧局内经济悬浮窗独立置顶呼出快捷按钮与窗口控制 -->
      <div class="flex items-center space-x-2" style="--wails-draggable: no-drag">
        <button
          class="text-xs font-bold px-3 py-1.5 border rounded-lg flex cursor-pointer shadow-sm transition-all items-center space-x-1.5"
          :class="[
            appStore.isOverlayRunning
              ? 'bg-amber-500 text-slate-950 border-amber-400 font-extrabold shadow-[0_0_10px_rgba(245,158,11,0.3)]'
              : 'bg-slate-800 text-slate-200 border-slate-700 hover:bg-slate-700',
          ]"
          @click="appStore.toggleStandaloneOverlay"
        >
          <Shield class="h-3.5 w-3.5" />
          <span>{{ appStore.isOverlayRunning ? '悬浮窗已启动' : '打开悬浮窗 (Alt+F2)' }}</span>
        </button>

        <WindowControls />
      </div>
    </header>

    <!-- 2. 实战战况核心面板主体 -->
    <div class="p-5 flex-1 overflow-y-auto space-y-4">
      <!-- 2.0 赛前 BP 视觉截屏与英雄槽位识别卡片 (突破 GSI 赛前选人盲区) -->
      <BpVisionCaptureCard />

      <!-- 2.1 战场核心实时 HUD 面板 (仅在真实比赛连接且有有效英雄数据时呈现，严禁任何假数据) -->
      <section v-if="battle.connected && battle.heroId > 0 && battle.heroInternalName" class="p-4 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-3.5">
        <div class="flex flex-col gap-4 items-stretch justify-between xl:flex-row xl:items-center">
          <!-- 英雄头像、等级与血蓝战备槽 -->
          <div class="flex min-w-70 items-center space-x-4">
            <div class="shrink-0 relative">
              <SmartImage
                :src="getHeroAvatarUrl(battle.heroInternalName)"
                :fallback-src="getHeroCdnUrl(battle.heroInternalName)"
                :alt="battle.heroName"
                class="border-2 border-amber-500/60 rounded-xl bg-black h-14 w-20 shadow-lg object-cover"
              />
              <span class="text-xs text-slate-950 font-black font-mono px-1.5 py-0.2 border border-amber-300 rounded bg-amber-500 absolute -bottom-1 -right-1">
                Lv.{{ battle.heroLevel }}
              </span>
            </div>

            <div class="flex-1 space-y-1.5">
              <div class="flex items-center justify-between">
                <span class="text-sm text-amber-400 tracking-wide font-extrabold">{{ battle.heroName }}</span>
                <span
                  class="text-xs font-bold px-2 py-0.5 rounded uppercase"
                  :class="[
                    battle.alive ? 'bg-emerald-950 text-emerald-300 border border-emerald-700' : 'bg-rose-950 text-rose-300 border border-rose-700',
                  ]"
                >
                  {{ battle.alive ? '存活战斗中' : '阵亡等待复活' }}
                </span>
              </div>

              <!-- 血条 -->
              <div class="font-mono space-y-0.5">
                <div class="border border-emerald-900/80 rounded bg-slate-950 h-3.5 relative overflow-hidden">
                  <div
                    class="h-full transition-all duration-300 from-emerald-600 to-emerald-400 bg-gradient-to-r"
                    :style="{ width: `${Math.min(100, Math.max(0, battle.healthPercent))}%` }"
                  />
                  <div class="text-2xs text-white font-bold px-2 flex items-center inset-0 justify-between absolute drop-shadow">
                    <span>HP</span>
                    <span>{{ battle.health }} / {{ battle.maxHealth }} ({{ battle.healthPercent }}%)</span>
                  </div>
                </div>
              </div>

              <!-- 蓝条 -->
              <div class="font-mono space-y-0.5">
                <div class="border border-blue-900/80 rounded bg-slate-950 h-3 relative overflow-hidden">
                  <div
                    class="h-full transition-all duration-300 from-blue-600 to-cyan-400 bg-gradient-to-r"
                    :style="{ width: `${Math.min(100, Math.max(0, battle.manaPercent))}%` }"
                  />
                  <div class="text-2xs text-white font-bold px-2 flex items-center inset-0 justify-between absolute drop-shadow">
                    <span>MP</span>
                    <span>{{ battle.mana }} / {{ battle.maxMana }} ({{ battle.manaPercent }}%)</span>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- 战绩经济与买活状态卡片 -->
          <div class="text-xs font-mono shrink-0 gap-2 grid grid-cols-2 sm:grid-cols-4">
            <div class="p-2.5 border border-slate-800 rounded-xl bg-slate-950/80 space-y-0.5">
              <div class="text-xs text-slate-400">
                战绩 K / D / A
              </div>
              <div class="text-sm text-slate-100 font-extrabold">
                <span class="text-emerald-400">{{ battle.kills }}</span> /
                <span class="text-rose-400">{{ battle.deaths }}</span> /
                <span class="text-cyan-400">{{ battle.assists }}</span>
              </div>
              <div class="text-xs text-slate-500">
                补刀: {{ battle.lastHits }} / {{ battle.denies }}
              </div>
            </div>

            <div class="p-2.5 border border-slate-800 rounded-xl bg-slate-950/80 space-y-0.5">
              <div class="text-xs text-slate-400">
                储备金币 (Gold)
              </div>
              <div class="text-sm text-amber-400 font-extrabold">
                {{ battle.gold }}
              </div>
              <div class="text-xs text-slate-500">
                可靠: {{ battle.goldReliable }}
              </div>
            </div>

            <div class="p-2.5 border border-slate-800 rounded-xl bg-slate-950/80 space-y-0.5">
              <div class="text-xs text-slate-400">
                经济 / 经验 (分)
              </div>
              <div class="text-sm text-cyan-400 font-extrabold">
                {{ battle.gpm }} <span class="text-xs text-slate-500">/</span> {{ battle.xpm }}
              </div>
              <div class="text-xs text-slate-500">
                实时效率
              </div>
            </div>

            <div
              class="p-2.5 border rounded-xl space-y-0.5"
              :class="buybackCardClass"
            >
              <div class="text-xs flex items-center justify-between" :class="buybackTextClass">
                <span>买活状态</span>
                <span class="font-bold">{{ battle.canBuyback ? '充足' : '受限' }}</span>
              </div>
              <div class="text-sm font-extrabold" :class="buybackTextClass">
                {{ battle.buybackCost }} 金
              </div>
              <div class="text-xs text-slate-500">
                CD: {{ battle.buybackCooldown }}s
              </div>
            </div>
          </div>
        </div>

        <!-- 关键时间节点提示栏 (白天/黑夜、魔方、肉山) -->
        <div class="text-xs font-mono pt-1 border-t border-slate-800/80 gap-2 grid grid-cols-2 sm:grid-cols-4">
          <div class="p-2 rounded-lg bg-slate-950/50 flex items-center justify-between">
            <span class="text-slate-400 flex items-center space-x-1">
              <Clock class="text-amber-400 h-3.5 w-3.5" />
              <span>昼夜交替</span>
            </span>
            <span class="text-slate-200 font-bold">
              {{ battle.daytime ? '白天 (视野开阔)' : '夜晚 (视野收缩)' }}
            </span>
          </div>

          <div class="p-2 rounded-lg bg-slate-950/50 flex items-center justify-between">
            <span class="text-slate-400 flex items-center space-x-1">
              <Shield class="text-cyan-400 h-3.5 w-3.5" />
              <span>痛苦魔方</span>
            </span>
            <span class="text-cyan-300 font-bold">
              {{ battle.tormentorTimer <= 0 ? '已刷新 (可击杀)' : `${Math.floor(battle.tormentorTimer / 60)}分${battle.tormentorTimer % 60}秒` }}
            </span>
          </div>

          <div class="p-2 rounded-lg bg-slate-950/50 flex items-center justify-between">
            <span class="text-slate-400 flex items-center space-x-1">
              <Flame class="text-rose-400 h-3.5 w-3.5" />
              <span>肉山巢穴</span>
            </span>
            <span class="font-bold" :class="battle.roshanTimer > 0 ? 'text-rose-400 animate-pulse' : 'text-slate-300'">
              {{ battle.roshanTimer > 0 ? `刷新倒计时 ${battle.roshanTimer}s` : '巡视中' }}
            </span>
          </div>

          <div class="p-2 rounded-lg bg-slate-950/50 flex items-center justify-between">
            <span class="text-slate-400 flex items-center space-x-1">
              <Clock class="text-amber-400 h-3.5 w-3.5" />
              <span>强化神符</span>
            </span>
            <span class="text-amber-400 font-bold">每 2 分钟河道</span>
          </div>
        </div>

        <!-- 技能与 56x42 物品栏实战展台 -->
        <div class="pt-2.5 border-t border-slate-800/80 flex flex-col gap-3 items-start justify-between md:flex-row md:items-center">
          <!-- 技能格 -->
          <div class="flex items-center space-x-2">
            <span class="text-xs text-slate-400 font-bold shrink-0">技能:</span>
            <div v-if="battle.abilities.length === 0" class="text-xs text-slate-500 font-mono">
              等待实战技能数据...
            </div>
            <div v-else class="flex flex-wrap gap-y-1 items-center space-x-2">
              <div
                v-for="(ab, idx) in battle.abilities"
                :key="idx"
                class="group border border-slate-700 rounded-lg bg-black h-9 w-9 relative overflow-hidden"
                :title="`${ab.name} (Lv.${ab.level})`"
              >
                <SmartImage
                  :src="getAbilityIcon(ab.name)"
                  :fallback-src="getAbilityCdnUrl(ab.name)"
                  :alt="ab.name"
                  class="h-full w-full object-cover"
                />
                <div
                  v-if="ab.cooldown > 0"
                  class="text-xs text-amber-400 font-bold font-mono bg-black/80 flex items-center inset-0 justify-center absolute"
                >
                  {{ ab.cooldown }}
                </div>
                <div class="text-xs text-emerald-400 font-bold font-mono px-1 bg-black/90 bottom-0 right-0 absolute">
                  {{ ab.level }}
                </div>
              </div>
            </div>
          </div>

          <!-- 官网 56x42 规格物品格与背包 -->
          <div class="flex items-center space-x-3">
            <div class="flex items-center space-x-1.5">
              <div
                v-for="i in 6"
                :key="i"
                class="border border-slate-700 rounded-sm bg-slate-950 flex h-[30px] w-[40px] items-center justify-center relative overflow-hidden"
                :title="battle.items[i - 1]?.name || `装备格 ${i}`"
              >
                <SmartImage
                  v-if="battle.items[i - 1]?.name"
                  :src="getItemIcon(battle.items[i - 1].name)"
                  :fallback-src="getItemCdnUrl(battle.items[i - 1].name)"
                  :alt="battle.items[i - 1].name"
                  class="h-full w-full object-cover"
                />
                <span v-else class="text-xs text-slate-600 font-mono">{{ i }}</span>
                <div
                  v-if="(battle.items[i - 1]?.cooldown ?? 0) > 0"
                  class="text-xs text-amber-400 font-bold font-mono bg-black/80 flex items-center inset-0 justify-center absolute"
                >
                  {{ battle.items[i - 1].cooldown }}
                </div>
              </div>
            </div>

            <!-- 神杖与魔晶徽章 -->
            <div class="flex items-center space-x-1">
              <div
                class="p-1 border rounded-lg flex h-7 w-7 transition-all items-center justify-center relative overflow-hidden"
                :class="[
                  battle.hasScepter
                    ? 'border-amber-400 bg-amber-500/20 shadow-[0_0_8px_rgba(251,191,36,0.5)]'
                    : 'border-slate-800 bg-slate-900/60 opacity-30 grayscale',
                ]"
                title="阿哈利姆神杖"
              >
                <img src="/assets/icons/ultimate_scepter.png" alt="神杖" class="h-full w-full object-contain">
              </div>
              <div
                class="p-1 border rounded-lg flex h-7 w-7 transition-all items-center justify-center relative overflow-hidden"
                :class="[
                  battle.hasShard
                    ? 'border-blue-400 bg-blue-500/20 shadow-[0_0_8px_rgba(96,165,250,0.5)]'
                    : 'border-slate-800 bg-slate-900/60 opacity-30 grayscale',
                ]"
                title="阿哈利姆魔晶"
              >
                <SmartImage
                  :src="getItemIcon('aghanims_shard')"
                  :fallback-src="getItemCdnUrl('aghanims_shard')"
                  alt="魔晶"
                  class="h-full w-full object-contain"
                />
              </div>
            </div>

            <!-- 中立物品格 -->
            <div class="pl-1 flex items-center space-x-1">
              <span class="text-xs text-cyan-400 font-bold">中立:</span>
              <div
                class="border border-cyan-700/60 rounded bg-slate-950 flex h-[24px] w-[32px] items-center justify-center relative overflow-hidden"
                :title="battle.neutralItem?.name || '空中立装备'"
              >
                <SmartImage
                  v-if="battle.neutralItem?.name"
                  :src="getItemIcon(battle.neutralItem.name)"
                  :fallback-src="getItemCdnUrl(battle.neutralItem?.name || '')"
                  :alt="battle.neutralItem.name"
                  class="h-full w-full object-cover"
                />
                <span v-else class="text-xs text-slate-600 font-mono">N</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 2.2 实时对局大数据针对性出装推荐面板 (优先消费 GSI 敌方英雄，BP 视觉识别敌方阵容自动无缝兜底补齐) -->
      <MatchupRecommendationPanel
        v-if="battle.connected && battle.heroId > 0 && battle.heroInternalName"
        :hero-id="battle.heroId"
        :hero-name="battle.heroName"
        :enemy-hero-ids="battle.enemyHeroIds.length > 0 ? battle.enemyHeroIds : visionStore.enemyHeroIds"
        :current-items="currentInventoryItemNames"
      />

      <!-- 2.3 AI 主动战术决策流中枢 (完全自主裁定，无输入对话) -->
      <section class="p-4 border border-slate-700/60 rounded-2xl bg-slate-900/80 shadow-md space-y-3">
        <div class="flex items-center justify-between pb-2 border-b border-slate-800">
          <div class="flex items-center space-x-2">
            <Radio class="text-amber-400 h-4 w-4" />
            <h2 class="text-sm text-amber-400 tracking-wide font-bold">
              AI 主动战术决策
            </h2>
          </div>

          <span class="text-xs text-slate-400 font-mono">
            共 {{ battleStore.advices.length }} 条实时决策
          </span>
        </div>

        <!-- 真实空态守卫 (未产生战况时呈现) -->
        <div v-if="battleStore.advices.length === 0" class="text-xs text-slate-400 p-8 text-center border border-slate-700/80 rounded-2xl border-dashed bg-slate-950/40">
          <Sparkles class="text-slate-600 mx-auto mb-2 opacity-60 h-7 w-7" />
          <p class="text-xs text-slate-400 font-medium">
            战况就绪，等待实时战术事件触发
          </p>
        </div>

        <div v-else class="pr-1 max-h-[580px] overflow-y-auto space-y-3">
          <div
            v-for="item in battleStore.advices"
            :key="item.id"
            class="p-4 border rounded-xl bg-slate-950/90 shadow-md transition-all relative overflow-hidden space-y-2.5"
            :class="getAdviceBorderClass(item.severity)"
          >
            <!-- 视觉层级指示条 -->
            <div
              class="w-1.5 bottom-0 left-0 top-0 absolute"
              :class="getAdviceBarClass(item.severity)"
            />

            <div class="pl-1 flex items-center justify-between">
              <div class="flex items-center space-x-2.5 flex-wrap">
                <span
                  class="text-xs font-bold px-2 py-0.5 rounded uppercase"
                  :class="getAdviceBadgeClass(item.severity)"
                >
                  {{ item.severity === 'Critical' ? '战略告警' : item.severity === 'Warning' ? '战术节拍' : '战局通报' }}
                </span>
                <span class="text-sm text-slate-100 font-bold">{{ item.title }}</span>
                <span
                  v-if="getRuleDisplayTag(item.rule_id || item.rule)"
                  class="text-xs text-amber-300/90 font-medium px-2 py-0.5 border border-amber-500/30 rounded bg-amber-500/10"
                >
                  {{ getRuleDisplayTag(item.rule_id || item.rule) }}
                </span>
              </div>

              <div class="flex items-center space-x-2">
                <span class="text-xs text-slate-400 font-mono">{{ item.created_at }}</span>
                <button
                  title="重新语音播报"
                  class="text-slate-400 p-1 rounded cursor-pointer transition-colors hover:text-amber-400 hover:bg-slate-800"
                  @click="replayVoice(item)"
                >
                  <Volume2 class="h-3.5 w-3.5" />
                </button>
              </div>
            </div>

            <p class="text-xs text-slate-200 leading-relaxed font-medium pl-1">
              {{ item.text }}
            </p>

            <!-- 推荐行动指南 -->
            <div v-if="item.suggested_action" class="text-xs pl-1 pt-2 border-t border-slate-800/80 flex items-start space-x-2">
              <span class="text-xs text-amber-400 font-bold shrink-0">行动指南：</span>
              <span class="text-xs text-slate-100 font-semibold leading-relaxed">{{ item.suggested_action }}</span>
            </div>

            <!-- AI 深度战术推演 (Solution Hierarchy) -->
            <div v-if="item.ai_explanation" class="text-xs p-2.5 border border-slate-800/80 rounded-lg bg-slate-900/80 flex items-start space-x-2">
              <Sparkles class="text-cyan-400 mt-0.5 shrink-0 h-3.5 w-3.5" />
              <div>
                <span class="text-cyan-300 font-bold">AI 战术推演 (Solution Hierarchy)：</span>
                <span class="text-slate-300 leading-relaxed">{{ item.ai_explanation }}</span>
              </div>
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>
