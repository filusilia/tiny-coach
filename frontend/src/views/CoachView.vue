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
  AlertTriangle,
  Clock,
  Cpu,
  Flame,
  Moon,
  Radio,
  Send,
  Shield,
  ShieldAlert,
  Sparkles,
  Sun,
  Volume2,
} from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { useAppStore, useBattleStore } from '@/stores'
import { aiCoachService } from '../services/aiCoachService'
import { activeBattleContext } from '../services/gsiRuleEngine'
import { speechService } from '../services/speechService'
import {
  getAbilityCdnUrl,
  getAbilityIconUrl,
  getHeroCdnUrl,
  getHeroIconUrl,
  getItemCdnUrl,
  getItemIconUrl,
  handleImageFallback,
} from '../utils/assetUrl'

const appStore = useAppStore()
const battleStore = useBattleStore()

/** 实战全局上下文响应式对象引用 */
const battle = activeBattleContext

/** 自定义提问输入文本 */
const customQuestion = ref('')
/** AI 研判生成中状态 */
const asking = ref(false)
/** 最新一次 AI 研判结果 */
const lastAiResult = ref<{ question: string, answer: string, model: string } | null>(null)

/** 预置高频战术研判问题列表 */
const presetQuestions = [
  { q: '当前经济与战况，我核心英雄接下来应该出什么装备？', label: '出装决策', icon: Shield },
  { q: '接下来的团战进场时机与目标优先级是什么？', label: '团战进场', icon: Flame },
  { q: '当前比赛时间点，我们应该控盾打肉山还是推进外塔？', label: '控盾/推进', icon: Clock },
  { q: '如果局势处于逆风，我们团队该如何拉扯寻找翻盘契机？', label: '逆风翻盘', icon: AlertTriangle },
]

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
 * 触发预置战术问题提问
 * @param q 问题文本
 */
async function triggerPresetQuestion(q: string) {
  customQuestion.value = q
  await handleCustomAsk()
}

/**
 * 提交自定义战况问题向 AI 教练提问
 */
async function handleCustomAsk() {
  const q = customQuestion.value.trim()
  if (!q || asking.value) return

  asking.value = true
  try {
    const res = await aiCoachService.askCoach(q, {
      heroId: battle.heroId,
      heroName: battle.heroName,
      heroLevel: battle.heroLevel,
      healthPercent: battle.healthPercent,
      manaPercent: battle.manaPercent,
      gold: battle.gold,
      clockTime: battle.clockTime,
      kda: `${battle.kills}/${battle.deaths}/${battle.assists}`,
      items: battle.items.map(i => i.name),
      abilities: battle.abilities.map(a => a.name),
      userNotes: appStore.settings.user_context,
    })

    lastAiResult.value = {
      question: q,
      answer: res.answer,
      model: res.modelUsed,
    }

    // 若配置开启语音，自动通过 TTS 朗读提炼结论
    if (appStore.settings.voice_broadcast_mode === 2) {
      speechService.speak(res.answer, {
        mode: 2,
        rate: appStore.settings.voice_speed,
        volume: appStore.settings.voice_volume,
        voiceName: appStore.settings.custom_tts_voice || appStore.settings.edge_voice_name || appStore.settings.voice_name,
      })
    }
  } catch (err: any) {
    appStore.showToast(`AI 战术研判失败: ${err?.message || '推理超时'}`, 'error')
  } finally {
    asking.value = false
  }
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
      voiceName: appStore.settings.custom_tts_voice || appStore.settings.edge_voice_name || appStore.settings.voice_name,
    },
  )
}

/**
 * 重新语音播报指定文本内容
 * @param text 待朗读文本
 */
function replayText(text: string) {
  speechService.speak(text, {
    mode: 2,
    rate: appStore.settings.voice_speed,
    volume: appStore.settings.voice_volume,
    voiceName: appStore.settings.custom_tts_voice || appStore.settings.edge_voice_name || appStore.settings.voice_name,
  })
}
</script>

<template>
  <div class="text-slate-100 bg-dota-bg flex flex-1 flex-col select-none overflow-hidden">
    <!-- 1. 顶部实战对局状态栏 (纯实战信息展示与快捷操作) -->
    <header class="px-5 border-b border-dota-border bg-dota-surface flex shrink-0 h-13 items-center justify-between">
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
          class="text-slate-300 font-medium font-mono px-2 py-0.5 border border-slate-700 rounded-md bg-slate-800 flex items-center space-x-1 text-[11px]"
        >
          <ShieldAlert class="text-slate-400 h-3 w-3" />
          <span>规则引擎纯降级</span>
        </span>
        <span
          v-else-if="appStore.settings.ai_mode === 'local_npu' || appStore.settings.ai_mode === 'npu'"
          class="text-emerald-300 font-medium font-mono px-2 py-0.5 border border-emerald-700/40 rounded-md bg-emerald-950/60 flex items-center space-x-1 text-[11px]"
        >
          <Cpu class="text-emerald-400 h-3 w-3" />
          <span>端侧高能效: {{ appStore.settings.npu_model }}</span>
        </span>
        <span
          v-else
          class="text-amber-300 font-medium font-mono px-2 py-0.5 border border-amber-700/40 rounded-md bg-amber-950/60 flex items-center space-x-1 text-[11px]"
        >
          <Sparkles class="text-amber-400 h-3 w-3" />
          <span>云端大模型: {{ appStore.settings.cloud_model }}</span>
        </span>
      </div>

      <!-- 右侧局内经济悬浮窗独立置顶呼出快捷按钮 -->
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
    </header>

    <!-- 2. 实战战况核心面板主体 -->
    <div class="p-5 flex-1 overflow-y-auto space-y-4">
      <!-- 2.1 战场核心实时 HUD 面板 (仅在真实比赛连接且有有效英雄数据时呈现，严禁任何假数据) -->
      <section v-if="battle.connected && battle.heroId > 0 && battle.heroInternalName" class="p-4 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-3.5">
        <div class="flex flex-col gap-4 items-stretch justify-between xl:flex-row xl:items-center">
          <!-- 英雄头像、等级与血蓝战备槽 -->
          <div class="flex min-w-70 items-center space-x-4">
            <div class="shrink-0 relative">
              <img
                :src="getHeroAvatarUrl(battle.heroInternalName)"
                :alt="battle.heroName"
                class="border-2 border-amber-500/60 rounded-xl bg-black h-14 w-20 shadow-lg object-cover"
                @error="(e) => handleImageFallback(e, getHeroCdnUrl(battle.heroInternalName))"
              >
              <span class="text-slate-950 font-black font-mono px-1.5 py-0.2 border border-amber-300 rounded bg-amber-500 absolute text-[10px] -bottom-1 -right-1">
                Lv.{{ battle.heroLevel }}
              </span>
            </div>

            <div class="flex-1 space-y-1.5">
              <div class="flex items-center justify-between">
                <span class="text-sm text-amber-400 tracking-wide font-extrabold">{{ battle.heroName }}</span>
                <span
                  class="font-bold px-2 py-0.5 rounded uppercase text-[10px]"
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
                  <div class="text-white font-bold px-2 flex items-center inset-0 justify-between absolute drop-shadow text-[9px]">
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
                  <div class="text-white font-bold px-2 flex items-center inset-0 justify-between absolute drop-shadow text-[9px]">
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
              <div class="text-slate-400 text-[10px]">
                战绩 K / D / A
              </div>
              <div class="text-sm text-slate-100 font-extrabold">
                <span class="text-emerald-400">{{ battle.kills }}</span> /
                <span class="text-rose-400">{{ battle.deaths }}</span> /
                <span class="text-cyan-400">{{ battle.assists }}</span>
              </div>
              <div class="text-slate-500 text-[10px]">
                补刀: {{ battle.lastHits }} / {{ battle.denies }}
              </div>
            </div>

            <div class="p-2.5 border border-slate-800 rounded-xl bg-slate-950/80 space-y-0.5">
              <div class="text-slate-400 text-[10px]">
                储备金币 (Gold)
              </div>
              <div class="text-sm text-amber-400 font-extrabold">
                {{ battle.gold }}
              </div>
              <div class="text-slate-500 text-[10px]">
                可靠: {{ battle.goldReliable }}
              </div>
            </div>

            <div class="p-2.5 border border-slate-800 rounded-xl bg-slate-950/80 space-y-0.5">
              <div class="text-slate-400 text-[10px]">
                经济 / 经验 (分)
              </div>
              <div class="text-sm text-cyan-400 font-extrabold">
                {{ battle.gpm }} <span class="text-xs text-slate-500">/</span> {{ battle.xpm }}
              </div>
              <div class="text-slate-500 text-[10px]">
                实时效率
              </div>
            </div>

            <div
              class="p-2.5 border rounded-xl space-y-0.5"
              :class="[
                battle.canBuyback
                  ? 'bg-emerald-950/50 border-emerald-700/60'
                  : 'bg-slate-950/80 border-slate-800',
              ]"
            >
              <div class="flex items-center justify-between text-[10px]" :class="battle.canBuyback ? 'text-emerald-300' : 'text-slate-400'">
                <span>买活状态</span>
                <span class="font-bold">{{ battle.canBuyback ? '充足' : '受限' }}</span>
              </div>
              <div class="text-sm font-extrabold" :class="battle.canBuyback ? 'text-emerald-400' : 'text-slate-400'">
                {{ battle.buybackCost }} 金
              </div>
              <div class="text-slate-500 text-[10px]">
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
            <span class="text-slate-400 font-bold shrink-0 text-[11px]">技能:</span>
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
                <img
                  :src="getAbilityIcon(ab.name)"
                  :alt="ab.name"
                  class="h-full w-full object-cover"
                  @error="(e) => handleImageFallback(e, getAbilityCdnUrl(ab.name))"
                >
                <div
                  v-if="ab.cooldown > 0"
                  class="text-xs text-amber-400 font-bold font-mono bg-black/80 flex items-center inset-0 justify-center absolute"
                >
                  {{ ab.cooldown }}
                </div>
                <div class="text-emerald-400 font-bold font-mono px-1 bg-black/90 bottom-0 right-0 absolute text-[8px]">
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
                <img
                  v-if="battle.items[i - 1]?.name"
                  :src="getItemIcon(battle.items[i - 1].name)"
                  :alt="battle.items[i - 1].name"
                  class="h-full w-full object-cover"
                  @error="(e) => handleImageFallback(e, getItemCdnUrl(battle.items[i - 1].name))"
                >
                <span v-else class="text-slate-700 font-mono text-[9px]">{{ i }}</span>
                <div
                  v-if="(battle.items[i - 1]?.cooldown ?? 0) > 0"
                  class="text-amber-400 font-bold font-mono bg-black/80 flex items-center inset-0 justify-center absolute text-[10px]"
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
                <img
                  :src="getItemIcon('aghanims_shard')"
                  alt="魔晶"
                  class="h-full w-full object-contain"
                  @error="(e) => handleImageFallback(e, getItemCdnUrl('aghanims_shard'))"
                >
              </div>
            </div>

            <!-- 中立物品格 -->
            <div class="pl-1 flex items-center space-x-1">
              <span class="text-cyan-400 font-bold text-[10px]">中立:</span>
              <div
                class="border border-cyan-700/60 rounded bg-slate-950 flex h-[24px] w-[32px] items-center justify-center relative overflow-hidden"
                :title="battle.neutralItem?.name || '空中立装备'"
              >
                <img
                  v-if="battle.neutralItem?.name"
                  :src="getItemIcon(battle.neutralItem.name)"
                  :alt="battle.neutralItem.name"
                  class="h-full w-full object-cover"
                  @error="(e) => handleImageFallback(e, getItemCdnUrl(battle.neutralItem?.name || ''))"
                >
                <span v-else class="text-slate-700 font-mono text-[8px]">N</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 2.1 未接入对局时的安全空态看板 (严格遵守铁律 8 零伪造原则) -->
      <section v-else class="p-8 text-center border border-slate-700/80 rounded-2xl bg-slate-900/60 shadow-xl space-y-4">
        <div class="text-amber-400 mx-auto border border-slate-700 rounded-full bg-slate-800 flex h-16 w-16 shadow-inner items-center justify-center">
          <Shield class="opacity-80 h-8 w-8" />
        </div>
        <div class="mx-auto max-w-xl space-y-1.5">
          <h2 class="text-base text-slate-100 tracking-wide font-bold">
            等待对局接入
          </h2>
          <p class="text-xs text-slate-300 leading-relaxed font-normal">
            当您启动游戏并进入选人或正式比赛后，将自动呈现您实操英雄的血蓝战备、冷却节奏与全局战况。
          </p>
        </div>

        <div class="pt-2 flex flex-wrap gap-3 items-center justify-center">
          <div v-if="appStore.settings.dota_player_id" class="text-xs text-amber-300 px-3.5 py-1.5 border border-slate-800 rounded-lg bg-slate-950/80 flex items-center space-x-1.5">
            <span class="text-slate-400">已绑定:</span>
            <span class="font-bold font-mono">{{ appStore.settings.dota_player_name || appStore.settings.dota_player_id }}</span>
          </div>
          <div v-else class="text-xs text-slate-400 px-3.5 py-1.5 border border-slate-800 rounded-lg bg-slate-950/80">
            <span>未绑定ID</span>
          </div>
        </div>
      </section>

      <!-- 2.2 AI 实时战术参谋交互区 (纯实战研判) -->
      <section class="p-4 border border-slate-700/60 rounded-2xl bg-slate-900/80 shadow-md space-y-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center space-x-2">
            <Sparkles class="text-amber-400 h-4 w-4" />
            <h2 class="text-xs text-amber-400 tracking-wide font-bold">
              AI 实时战术参谋 · 即时战局问答
            </h2>
            <span class="text-slate-400 font-medium text-[10px]">
              (输入当前战况直接提问，基于 Solution Hierarchy 思维链快速返回实战行动)
            </span>
          </div>

          <div v-if="asking" class="text-xs text-amber-300 font-medium flex items-center animate-pulse space-x-1.5">
            <Cpu class="h-3.5 w-3.5 animate-spin" />
            <span>AI 推理研判中...</span>
          </div>
        </div>

        <!-- 预置高频战术求助胶囊 -->
        <div class="flex flex-wrap gap-2">
          <button
            v-for="btn in presetQuestions"
            :key="btn.q"
            :disabled="asking"
            class="text-xs text-slate-200 px-2.5 py-1 border border-slate-700/80 rounded-lg bg-slate-800/90 flex cursor-pointer shadow-sm transition-colors items-center space-x-1.5 hover:text-white hover:bg-slate-700 disabled:opacity-50"
            @click="triggerPresetQuestion(btn.q)"
          >
            <component :is="btn.icon" class="text-amber-400 h-3 w-3" />
            <span>{{ btn.label }}</span>
          </button>
        </div>

        <!-- 自定义提问输入框 -->
        <div class="flex items-center space-x-2">
          <div class="flex-1 relative">
            <input
              v-model="customQuestion"
              type="text"
              :disabled="asking"
              placeholder="向 AI 教练提问当前战局决策 (例如: 对面先出了紫苑我该先做吹风还是憋 BKB？当前该打盾还是推塔？)"
              class="text-xs text-slate-100 px-3.5 py-2 outline-none border border-slate-700 rounded-lg bg-slate-950 w-full transition-colors placeholder:text-slate-500 focus:ring-1 focus:ring-amber-400"
              @keydown.enter="handleCustomAsk"
            >
          </div>
          <button
            :disabled="asking || !customQuestion.trim()"
            class="text-xs text-slate-950 font-bold px-4 py-2 rounded-lg flex cursor-pointer shadow-md transition-all items-center from-amber-500 to-amber-600 bg-gradient-to-r space-x-1.5 disabled:opacity-50 disabled:cursor-not-allowed hover:from-amber-400 hover:to-amber-500"
            @click="handleCustomAsk"
          >
            <Send class="h-3.5 w-3.5" />
            <span>立即研判</span>
          </button>
        </div>

        <!-- 最新研判成果呈现 -->
        <div v-if="lastAiResult" class="text-xs p-3.5 border border-amber-500/30 rounded-xl bg-slate-950 space-y-2">
          <div class="flex items-center justify-between">
            <div class="flex items-center space-x-2">
              <span class="text-amber-300 font-bold px-2 py-0.5 border border-amber-500/40 rounded bg-amber-500/20 text-[10px]">
                AI 参谋研判成果
              </span>
              <span class="text-slate-300 font-medium">问题: “{{ lastAiResult.question }}”</span>
            </div>
            <div class="flex items-center space-x-2">
              <span class="text-slate-500 font-mono text-[10px]">{{ lastAiResult.model }}</span>
              <button
                title="重新语音朗读"
                class="text-slate-400 p-1 rounded cursor-pointer transition-colors hover:text-amber-400 hover:bg-slate-800"
                @click="replayText(lastAiResult.answer)"
              >
                <Volume2 class="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
          <p class="text-slate-100 leading-relaxed font-medium">
            {{ lastAiResult.answer }}
          </p>
        </div>
      </section>

      <!-- 2.3 全局战略决策流 (宏观战局与核心变量) -->
      <section class="space-y-3">
        <div class="flex items-center justify-between">
          <h2 class="text-xs text-slate-100 font-bold flex items-center space-x-2">
            <Radio class="text-amber-400 h-4 w-4" />
            <span>实时决策流 ({{ battleStore.advices.length }})</span>
          </h2>
        </div>

        <div v-if="battleStore.advices.length === 0" class="text-xs text-slate-400 p-10 text-center border border-slate-700/80 rounded-2xl border-dashed bg-slate-900/30">
          <Flame class="text-slate-600 mx-auto mb-2.5 opacity-60 h-8 w-8" />
          <p class="text-slate-300 font-medium">
            对战就绪。比赛进行时系统将依据神符刷新、痛苦魔方、肉山控盾与买活储备毫秒级裁定告警！
          </p>
          <p class="text-slate-500 mt-1 text-[11px]">
            您可在上方输入框中主动向 AI 战术参谋发起实时战局询问。
          </p>
        </div>

        <div v-else class="space-y-3">
          <div
            v-for="item in battleStore.advices"
            :key="item.id"
            class="p-4 border rounded-2xl bg-slate-900/90 shadow-md transition-all relative overflow-hidden space-y-2"
            :class="[
              item.severity === 'Critical' ? 'border-rose-500/60 shadow-rose-950/20'
              : item.severity === 'Warning' ? 'border-amber-500/60 shadow-amber-950/20' : 'border-cyan-500/40 shadow-cyan-950/20',
            ]"
          >
            <!-- 视觉层级指示条 -->
            <div
              class="w-1.5 bottom-0 left-0 top-0 absolute"
              :class="[
                item.severity === 'Critical' ? 'bg-rose-500'
                : item.severity === 'Warning' ? 'bg-amber-500' : 'bg-cyan-500',
              ]"
            />

            <div class="pl-1 flex items-center justify-between">
              <div class="flex items-center space-x-2.5">
                <span
                  class="font-bold px-2 py-0.5 rounded uppercase text-[10px]"
                  :class="[
                    item.severity === 'Critical' ? 'bg-rose-500/20 text-rose-300 border border-rose-500/40'
                    : item.severity === 'Warning' ? 'bg-amber-500/20 text-amber-300 border border-amber-500/40'
                      : 'bg-cyan-500/20 text-cyan-300 border border-cyan-500/40',
                  ]"
                >
                  {{ item.severity === 'Critical' ? '战略告警' : item.severity === 'Warning' ? '战术节拍' : '战局通报' }}
                </span>
                <span class="text-xs text-slate-100 font-bold">{{ item.title }}</span>
                <span
                  v-if="getRuleDisplayTag(item.rule_id || item.rule)"
                  class="text-amber-300/90 font-medium px-1.5 py-0.2 border border-amber-500/30 rounded bg-amber-500/10 text-[10px]"
                >
                  {{ getRuleDisplayTag(item.rule_id || item.rule) }}
                </span>
              </div>

              <div class="flex items-center space-x-2">
                <span class="text-slate-400 font-mono text-[10px]">{{ item.created_at }}</span>
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
            <div v-if="item.suggested_action" class="text-xs pl-1 pt-1.5 border-t border-slate-800/80 flex items-start space-x-2">
              <span class="text-amber-400 font-bold shrink-0 text-[11px]">行动指南：</span>
              <span class="text-slate-100 font-semibold">{{ item.suggested_action }}</span>
            </div>

            <!-- AI 深度战术推演 (Solution Hierarchy) -->
            <div v-if="item.ai_explanation" class="text-xs p-2.5 border border-slate-800/80 rounded-lg bg-slate-950/80 flex items-start space-x-2.5">
              <Sparkles class="text-cyan-400 mt-0.5 shrink-0 h-3.5 w-3.5" />
              <div>
                <span class="text-cyan-300 font-bold text-[11px]">AI 战术推演 (Solution Hierarchy)：</span>
                <span class="text-slate-300 leading-relaxed">{{ item.ai_explanation }}</span>
              </div>
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>
