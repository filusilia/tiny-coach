<script setup lang="ts">
import type { CoachAdvice } from '../types'
import {
  AlertTriangle,
  BrainCircuit,
  Clock,
  Cpu,
  Flame,
  Layers,
  Moon,
  Radio,
  Send,
  Shield,
  ShieldAlert,
  Sparkles,
  Sun,
  Trash2,
  Volume2,
  Zap,
} from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { aiCoachService } from '../services/aiCoachService'
import { activeBattleContext } from '../services/gsiRuleEngine'
import { speechService } from '../services/speechService'
import { useAppStore } from '../store'

const {
  state,
  clearAdvices,
  addAdvice,
  checkOverlayStatus,
  toggleStandaloneOverlay,
} = useAppStore()

const battle = activeBattleContext
const customQuestion = ref('')
const asking = ref(false)
const lastAiResult = ref<{ question: string, answer: string, model: string } | null>(null)

const presetQuestions = [
  { q: '当前经济与战况，我核心英雄接下来应该出什么装备？', label: '出装决策', icon: Shield },
  { q: '接下来的团战进场时机与目标优先级是什么？', label: '团战进场', icon: Flame },
  { q: '当前比赛时间点，我们应该控盾打肉山还是推进外塔？', label: '控盾/推进', icon: Clock },
  { q: '如果局势处于逆风，我们团队该如何拉扯寻找翻盘契机？', label: '逆风翻盘', icon: AlertTriangle },
]

onMounted(() => {
  checkOverlayStatus()
})

async function handleToggleOverlay() {
  await toggleStandaloneOverlay()
}

function formatClockTime(seconds: number): string {
  const isNegative = seconds < 0
  const abs = Math.abs(seconds)
  const min = Math.floor(abs / 60)
  const sec = (abs % 60).toString().padStart(2, '0')
  return `${isNegative ? '-' : ''}${min}:${sec}`
}

function getHeroAvatarUrl(internalName: string): string {
  const cleanName = internalName.replace('npc_dota_hero_', '')
  return `https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/heroes/${cleanName}.png`
}

function getAbilityIconUrl(abilityName: string): string {
  return `https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/abilities/${abilityName}.png`
}

function getItemIconUrl(itemName: string): string {
  const clean = itemName.replace('item_', '')
  return `https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/${clean}.png`
}

async function triggerPresetQuestion(q: string) {
  customQuestion.value = q
  await handleCustomAsk()
}

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
      userNotes: state.settings.user_context,
    })

    lastAiResult.value = {
      question: q,
      answer: res.answer,
      model: res.modelUsed,
    }

    addAdvice({
      id: `ai-ask-${Date.now()}`,
      category: 'tactical_ai_ask',
      rule_id: 'AI_TACTICAL_CONSULTANT',
      title: 'AI 参谋研判',
      text: res.answer,
      severity: 'Info',
      created_at: new Date().toLocaleTimeString('zh-CN', { hour12: false }),
      suggested_action: res.answer,
      ai_explanation: `经 ${res.modelUsed} 深度战术推演验证`,
    })

    speechService.speak(res.answer, {
      mode: 2,
      rate: state.settings.voice_speed,
      volume: state.settings.voice_volume,
      voiceName: state.settings.edge_voice_name || state.settings.voice_name,
    })

    customQuestion.value = ''
  } catch (err) {
    console.error('AI 研判失败:', err)
  } finally {
    asking.value = false
  }
}

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

function replayVoice(item: CoachAdvice) {
  speechService.speak(
    `${item.title}。${item.text}`,
    {
      mode: 2,
      rate: state.settings.voice_speed,
      volume: state.settings.voice_volume,
      voiceName: state.settings.edge_voice_name || state.settings.voice_name,
    },
  )
}

function replayText(text: string) {
  speechService.speak(text, {
    mode: 2,
    rate: state.settings.voice_speed,
    volume: state.settings.voice_volume,
    voiceName: state.settings.edge_voice_name || state.settings.voice_name,
  })
}
</script>

<template>
  <div class="bg-dota-bg text-slate-100 flex flex-1 flex-col select-none overflow-hidden">
    <!-- 1. 顶部实战对局状态栏 (纯实战信息展示与快捷操作，无任何设置项) -->
    <header class="border-dota-border bg-dota-surface px-5 border-b flex shrink-0 h-13 items-center justify-between">
      <div class="flex items-center space-x-3.5">
        <!-- GSI 状态指示 -->
        <div class="flex items-center space-x-2">
          <span
            class="rounded-full h-2.5 w-2.5 ring-4 transition-all" :class="[
              battle.connected
                ? 'bg-emerald-400 ring-emerald-500/20 animate-pulse'
                : 'bg-slate-600 ring-slate-700/20',
            ]"
          />
          <span class="text-xs tracking-wide font-bold">
            {{ battle.connected ? `Dota 2 实战通信在线 · ${battle.gameState}` : '等待 Dota 2 客户端启动对局 (GSI 4444 端口)...' }}
          </span>
        </div>

        <!-- 比赛时钟与昼夜指示 -->
        <div v-if="battle.connected" class="text-xs text-amber-400 font-bold font-mono px-2.5 py-0.5 border border-slate-800 rounded-md bg-slate-950 flex items-center space-x-1.5">
          <Sun v-if="battle.daytime" class="text-amber-400 h-3.5 w-3.5" />
          <Moon v-else class="text-cyan-400 h-3.5 w-3.5" />
          <span>{{ formatClockTime(battle.clockTime) }}</span>
        </div>

        <!-- 10Hz 毫秒快照徽章 -->
        <span class="text-[11px] text-cyan-300 font-medium font-mono px-2 py-0.5 border border-cyan-800/40 rounded-md bg-slate-800/80 flex items-center space-x-1">
          <Zap class="text-cyan-400 h-3 w-3" />
          <span>10Hz 毫秒快照</span>
        </span>

        <!-- 当前生效 AI 引擎只读指示 (不可设置，设置需前往设置页) -->
        <span
          v-if="state.settings.ai_mode === 'rule_only'"
          class="text-[11px] text-slate-300 font-medium font-mono px-2 py-0.5 border border-slate-700 rounded-md bg-slate-800 flex items-center space-x-1"
        >
          <ShieldAlert class="text-slate-400 h-3 w-3" />
          <span>规则引擎纯降级</span>
        </span>
        <span
          v-else-if="state.settings.ai_mode === 'local_npu' || state.settings.ai_mode === 'npu'"
          class="text-[11px] text-emerald-300 font-medium font-mono px-2 py-0.5 border border-emerald-700/40 rounded-md bg-emerald-950/60 flex items-center space-x-1"
        >
          <Cpu class="text-emerald-400 h-3 w-3" />
          <span>本机端侧: {{ state.settings.npu_model || 'Qwen 3.5 4B' }}</span>
        </span>
        <span
          v-else
          class="text-[11px] text-amber-300 font-medium font-mono px-2 py-0.5 border border-amber-700/40 rounded-md bg-amber-950/60 flex items-center space-x-1"
        >
          <BrainCircuit class="text-amber-400 h-3 w-3" />
          <span>云端 API: {{ state.settings.cloud_model || 'DeepSeek' }}</span>
        </span>
      </div>

      <div class="flex items-center space-x-2.5">
        <!-- 独立置顶经济悬浮窗呼出/收起 (纯操作入口) -->
        <button
          class="text-xs font-bold px-3 py-1.5 rounded-lg flex shadow-md transition-all items-center space-x-1.5" :class="[
            state.isOverlayRunning
              ? 'bg-rose-500/20 text-rose-300 border border-rose-500/40 hover:bg-rose-500/30'
              : 'bg-amber-500 text-slate-950 hover:bg-amber-400 border border-amber-400',
          ]"
          @click="handleToggleOverlay"
        >
          <Layers class="h-3.5 w-3.5" />
          <span>{{ state.isOverlayRunning ? '关闭悬浮窗' : '呼出悬浮窗' }}</span>
        </button>

        <!-- 清空战术决策流 -->
        <button
          v-if="state.advices.length > 0"
          class="text-xs text-slate-400 font-medium px-3 py-1.5 border border-transparent rounded-lg flex transition-colors items-center space-x-1.5 hover:text-rose-400 hover:border-rose-900/40 hover:bg-rose-950/30"
          @click="clearAdvices"
        >
          <Trash2 class="h-3.5 w-3.5" />
          <span>清空决策流</span>
        </button>
      </div>
    </header>

    <!-- 2. 实战主视口 (HUD + 战局问答 + 决策流) -->
    <div class="p-5 flex-1 overflow-y-auto space-y-4">
      <!-- 2.1 战场核心实时 HUD 面板 (仅在真实比赛连接且有有效英雄数据时呈现，严禁任何假数据) -->
      <section v-if="battle.connected && battle.heroId > 0 && battle.heroInternalName" class="p-4 border border-slate-700/80 rounded-2xl bg-slate-900/90 shadow-xl space-y-3.5">
        <div class="flex flex-col gap-4 items-stretch justify-between xl:flex-row xl:items-center">
          <!-- 英雄头像、等级与血蓝战备槽 -->
          <div class="flex min-w-[280px] items-center space-x-4">
            <div class="shrink-0 relative">
              <img
                :src="getHeroAvatarUrl(battle.heroInternalName)"
                :alt="battle.heroName"
                class="border-2 border-amber-500/60 rounded-xl bg-black h-14 w-20 shadow-lg object-cover"
                @error="(e) => ((e.target as HTMLImageElement).src = '/assets/icons/hero_universal.png')"
              >
              <span class="text-[10px] text-slate-950 font-black font-mono px-1.5 py-0.2 border border-amber-300 rounded bg-amber-500 absolute -bottom-1 -right-1">
                Lv.{{ battle.heroLevel }}
              </span>
            </div>

            <div class="flex-1 space-y-1.5">
              <div class="flex items-center justify-between">
                <span class="text-sm text-amber-400 tracking-wide font-extrabold">{{ battle.heroName }}</span>
                <span
                  class="text-[10px] font-bold px-2 py-0.5 rounded uppercase" :class="[
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
                  <div class="text-[9px] text-white font-bold px-2 flex items-center inset-0 justify-between absolute drop-shadow">
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
                  <div class="text-[9px] text-white font-bold px-2 flex items-center inset-0 justify-between absolute drop-shadow">
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
              <div class="text-[10px] text-slate-400">
                战绩 K / D / A
              </div>
              <div class="text-sm text-slate-100 font-extrabold">
                <span class="text-emerald-400">{{ battle.kills }}</span> /
                <span class="text-rose-400">{{ battle.deaths }}</span> /
                <span class="text-cyan-400">{{ battle.assists }}</span>
              </div>
              <div class="text-[10px] text-slate-500">
                补刀: {{ battle.lastHits }} / {{ battle.denies }}
              </div>
            </div>

            <div class="p-2.5 border border-slate-800 rounded-xl bg-slate-950/80 space-y-0.5">
              <div class="text-[10px] text-slate-400">
                储备金币 (Gold)
              </div>
              <div class="text-sm text-amber-400 font-extrabold">
                {{ battle.gold }}
              </div>
              <div class="text-[10px] text-slate-500">
                可靠: {{ battle.goldReliable }}
              </div>
            </div>

            <div class="p-2.5 border border-slate-800 rounded-xl bg-slate-950/80 space-y-0.5">
              <div class="text-[10px] text-slate-400">
                经济 / 经验 (分)
              </div>
              <div class="text-sm text-cyan-400 font-extrabold">
                {{ battle.gpm }} <span class="text-xs text-slate-500">/</span> {{ battle.xpm }}
              </div>
              <div class="text-[10px] text-slate-500">
                实时效率
              </div>
            </div>

            <div
              class="p-2.5 border rounded-xl space-y-0.5" :class="[
                battle.canBuyback
                  ? 'bg-emerald-950/50 border-emerald-700/60'
                  : 'bg-slate-950/80 border-slate-800',
              ]"
            >
              <div class="text-[10px] flex items-center justify-between" :class="battle.canBuyback ? 'text-emerald-300' : 'text-slate-400'">
                <span>买活状态</span>
                <span class="font-bold">{{ battle.canBuyback ? '充足' : '受限' }}</span>
              </div>
              <div class="text-sm font-extrabold" :class="battle.canBuyback ? 'text-emerald-400' : 'text-slate-400'">
                {{ battle.buybackCost }} 金
              </div>
              <div class="text-[10px] text-slate-500">
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
            <span class="text-amber-400 font-bold">
              每 2 分钟河道
            </span>
          </div>
        </div>

        <!-- 技能与 56x42 物品栏实战展台 -->
        <div class="pt-2.5 border-t border-slate-800/80 flex flex-col gap-3 items-start justify-between md:flex-row md:items-center">
          <!-- 技能格 -->
          <div class="flex items-center space-x-2">
            <span class="text-[11px] text-slate-400 font-bold shrink-0">技能:</span>
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
                  :src="getAbilityIconUrl(ab.name)"
                  :alt="ab.name"
                  class="h-full w-full object-cover"
                  @error="(e) => ((e.target as HTMLImageElement).src = '/assets/icons/hero_universal.png')"
                >
                <!-- 冷却蒙层 -->
                <div
                  v-if="ab.cooldown > 0"
                  class="text-xs text-amber-400 font-bold font-mono bg-black/80 flex items-center inset-0 justify-center absolute"
                >
                  {{ ab.cooldown }}
                </div>
                <!-- 等级点 -->
                <div class="text-[8px] text-emerald-400 font-bold font-mono px-1 bg-black/90 bottom-0 right-0 absolute">
                  {{ ab.level }}
                </div>
              </div>
            </div>
          </div>

          <!-- 官网 56x42 规格物品格与背包 -->
          <div class="flex items-center space-x-3">
            <!-- 6 个主装备格 -->
            <div class="flex items-center space-x-1.5">
              <div
                v-for="i in 6"
                :key="i"
                class="border border-slate-700 rounded-sm bg-slate-950 flex h-[30px] w-[40px] items-center justify-center relative overflow-hidden"
                :title="battle.items[i - 1]?.name || `装备格 ${i}`"
              >
                <img
                  v-if="battle.items[i - 1]?.name"
                  :src="getItemIconUrl(battle.items[i - 1].name)"
                  :alt="battle.items[i - 1].name"
                  class="h-full w-full object-cover"
                  @error="(e) => ((e.target as HTMLImageElement).src = '/assets/icons/ultimate_scepter.png')"
                >
                <span v-else class="text-[9px] text-slate-700 font-mono">{{ i }}</span>
                <!-- 冷却 -->
                <div
                  v-if="(battle.items[i - 1]?.cooldown ?? 0) > 0"
                  class="text-[10px] text-amber-400 font-bold font-mono bg-black/80 flex items-center inset-0 justify-center absolute"
                >
                  {{ battle.items[i - 1].cooldown }}
                </div>
              </div>
            </div>

            <!-- 神杖与魔晶徽章 -->
            <div class="flex items-center space-x-1">
              <div
                class="p-1 border rounded-lg flex h-7 w-7 transition-all items-center justify-center relative overflow-hidden" :class="[
                  battle.hasScepter
                    ? 'border-amber-400 bg-amber-500/20 shadow-[0_0_8px_rgba(251,191,36,0.5)]'
                    : 'border-slate-800 bg-slate-900/60 opacity-30 grayscale',
                ]"
                title="阿哈利姆神杖"
              >
                <img
                  src="/assets/icons/ultimate_scepter.png"
                  alt="神杖"
                  class="h-full w-full object-contain"
                >
              </div>
              <div
                class="p-1 border rounded-lg flex h-7 w-7 transition-all items-center justify-center relative overflow-hidden" :class="[
                  battle.hasShard
                    ? 'border-blue-400 bg-blue-500/20 shadow-[0_0_8px_rgba(96,165,250,0.5)]'
                    : 'border-slate-800 bg-slate-900/60 opacity-30 grayscale',
                ]"
                title="阿哈利姆魔晶"
              >
                <img
                  src="https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/aghanims_shard.png"
                  alt="魔晶"
                  class="h-full w-full object-contain"
                  @error="(e) => ((e.target as HTMLImageElement).src = '/assets/icons/ultimate_scepter.png')"
                >
              </div>
            </div>

            <!-- 中立物品格 -->
            <div class="pl-1 flex items-center space-x-1">
              <span class="text-[10px] text-cyan-400 font-bold">中立:</span>
              <div
                class="border border-cyan-700/60 rounded bg-slate-950 flex h-[24px] w-[32px] items-center justify-center relative overflow-hidden"
                :title="battle.neutralItem?.name || '空中立装备'"
              >
                <img
                  v-if="battle.neutralItem?.name"
                  :src="getItemIconUrl(battle.neutralItem.name)"
                  :alt="battle.neutralItem.name"
                  class="h-full w-full object-cover"
                  @error="(e) => ((e.target as HTMLImageElement).src = '/assets/icons/ultimate_scepter.png')"
                >
                <span v-else class="text-[8px] text-slate-700 font-mono">N</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 2.1 未接入对局时的安全空态看板 (严格杜绝任何假数据展示) -->
      <section v-else class="p-8 text-center border border-slate-700/80 rounded-2xl bg-slate-900/60 shadow-xl space-y-4">
        <div class="text-amber-400 mx-auto border border-slate-700 rounded-full bg-slate-800 flex h-16 w-16 shadow-inner items-center justify-center">
          <Shield class="opacity-80 h-8 w-8" />
        </div>
        <div class="mx-auto max-w-xl space-y-1.5">
          <h2 class="text-base text-slate-100 tracking-wide font-bold">
            等待 Dota 2 客户端对局接入
          </h2>
          <p class="text-xs text-slate-300 leading-relaxed font-normal">
            神盾教练严格遵守非外挂合规原则，严禁伪造任何英雄与对局模拟数据。系统当前正持续监听本机 4444 端口官方 GSI 数据流。当您启动游戏并进入选人或正式比赛后，将自动呈现您实操英雄的血蓝战备、冷却节奏与全局战况。
          </p>
        </div>

        <div class="pt-2 flex flex-wrap gap-3 items-center justify-center">
          <div class="text-xs text-cyan-300 font-mono px-3.5 py-1.5 border border-slate-800 rounded-lg bg-slate-950/80 flex items-center space-x-2">
            <span class="rounded-full bg-emerald-400 h-2 w-2 animate-pulse" />
            <span>GSI 本机监听端口: 4444</span>
          </div>

          <div v-if="state.settings.dota_player_id" class="text-xs text-amber-300 px-3.5 py-1.5 border border-slate-800 rounded-lg bg-slate-950/80 flex items-center space-x-1.5">
            <span class="text-slate-400">主绑定玩家:</span>
            <span class="font-bold font-mono">{{ state.settings.dota_player_name || state.settings.dota_player_id }}</span>
          </div>
          <div v-else class="text-xs text-slate-400 px-3.5 py-1.5 border border-slate-800 rounded-lg bg-slate-950/80">
            <span>未绑定玩家ID（可在设置页绑定您的 Dota 2 账号以获得专属战术分析）</span>
          </div>
        </div>
      </section>

      <!-- 2.2 AI 实时战术参谋交互区 (纯实战研判，无任何设置项) -->
      <section class="p-4 border border-slate-700/60 rounded-2xl bg-slate-900/80 shadow-md space-y-3">
        <div class="flex items-center justify-between">
          <div class="flex items-center space-x-2">
            <Sparkles class="text-amber-400 h-4 w-4" />
            <h2 class="text-xs text-amber-400 tracking-wide font-bold">
              AI 实时战术参谋 · 即时战局问答
            </h2>
            <span class="text-[10px] text-slate-400 font-medium">
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
            class="text-xs text-slate-200 px-2.5 py-1 border border-slate-700/80 rounded-lg bg-slate-800/90 flex shadow-sm transition-colors items-center space-x-1.5 hover:text-white hover:bg-slate-700 disabled:opacity-50"
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
              class="text-xs text-slate-100 px-3.5 py-2 outline-none border border-slate-700 rounded-lg bg-slate-950 w-full transition-colors placeholder:text-slate-500 focus:border-amber-400"
              @keydown.enter="handleCustomAsk"
            >
          </div>
          <button
            :disabled="asking || !customQuestion.trim()"
            class="text-xs text-slate-950 font-bold px-4 py-2 rounded-lg flex shadow-md transition-all items-center from-amber-500 to-amber-600 bg-gradient-to-r space-x-1.5 disabled:opacity-50 disabled:cursor-not-allowed hover:from-amber-400 hover:to-amber-500"
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
              <span class="text-[10px] text-amber-300 font-bold px-2 py-0.5 border border-amber-500/40 rounded bg-amber-500/20">
                AI 参谋研判成果
              </span>
              <span class="text-slate-300 font-medium">问题: “{{ lastAiResult.question }}”</span>
            </div>
            <div class="flex items-center space-x-2">
              <span class="text-[10px] text-slate-500 font-mono">{{ lastAiResult.model }}</span>
              <button
                title="重新语音朗读"
                class="text-slate-400 p-1 rounded transition-colors hover:text-amber-400 hover:bg-slate-800"
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
            <span>实时实战决策流 ({{ state.advices.length }})</span>
          </h2>
          <span class="text-[11px] text-slate-400">
            优先掌控全局变量与宏观战术 · 低价值微观噪音已在设置页控制
          </span>
        </div>

        <div v-if="state.advices.length === 0" class="text-xs text-slate-400 p-10 text-center border border-slate-700/80 rounded-2xl border-dashed bg-slate-900/30">
          <Flame class="text-slate-600 mx-auto mb-2.5 opacity-60 h-8 w-8" />
          <p class="text-slate-300 font-medium">
            对战就绪。比赛进行时系统将依据神符刷新、痛苦魔方、肉山控盾与买活储备毫秒级裁定告警！
          </p>
          <p class="text-[11px] text-slate-500 mt-1">
            您可在上方输入框中主动向 AI 战术参谋发起实时战局询问。
          </p>
        </div>

        <div v-else class="space-y-3">
          <div
            v-for="item in state.advices"
            :key="item.id"
            class="p-4 border rounded-2xl bg-slate-900/90 shadow-md transition-all relative overflow-hidden space-y-2" :class="[
              item.severity === 'Critical' ? 'border-rose-500/60 shadow-rose-950/20'
              : item.severity === 'Warning' ? 'border-amber-500/60 shadow-amber-950/20' : 'border-cyan-500/40 shadow-cyan-950/20',
            ]"
          >
            <!-- 视觉层级指示条 -->
            <div
              class="w-1.5 bottom-0 left-0 top-0 absolute" :class="[
                item.severity === 'Critical' ? 'bg-rose-500'
                : item.severity === 'Warning' ? 'bg-amber-500' : 'bg-cyan-500',
              ]"
            />

            <div class="pl-1 flex items-center justify-between">
              <div class="flex items-center space-x-2.5">
                <span
                  class="text-[10px] font-bold px-2 py-0.5 rounded uppercase" :class="[
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
                  class="text-[10px] text-amber-300/90 font-medium px-1.5 py-0.2 border border-amber-500/30 rounded bg-amber-500/10"
                >
                  {{ getRuleDisplayTag(item.rule_id || item.rule) }}
                </span>
              </div>

              <div class="flex items-center space-x-2">
                <span class="text-[10px] text-slate-400 font-mono">{{ item.created_at }}</span>
                <button
                  title="重新语音播报"
                  class="text-slate-400 p-1 rounded transition-colors hover:text-amber-400 hover:bg-slate-800"
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
              <span class="text-[11px] text-amber-400 font-bold shrink-0">行动指南：</span>
              <span class="text-slate-100 font-semibold">{{ item.suggested_action }}</span>
            </div>

            <!-- AI 深度战术推演 (Solution Hierarchy) -->
            <div v-if="item.ai_explanation" class="text-xs p-2.5 border border-slate-800/80 rounded-lg bg-slate-950/80 flex items-start space-x-2.5">
              <Sparkles class="text-cyan-400 mt-0.5 shrink-0 h-3.5 w-3.5" />
              <div>
                <span class="text-[11px] text-cyan-300 font-bold">AI 战术推演 (Solution Hierarchy)：</span>
                <span class="text-slate-300 leading-relaxed">{{ item.ai_explanation }}</span>
              </div>
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>
