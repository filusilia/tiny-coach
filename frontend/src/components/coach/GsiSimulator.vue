<script setup lang="ts">
import type { CoachAdvice, ReminderCategoryKey } from '../../types'
import {
  Box,
  Compass,
  PlayCircle,
  ShoppingBag,
  Skull,
  Zap,
} from '@lucide/vue'
import { ref } from 'vue'
import { speechService } from '../../services/speechService'
import { useAppStore } from '../../store'

interface SimulationPreset {
  id: string
  category: ReminderCategoryKey
  categoryLabel: string
  title: string
  desc: string
  icon: any
  heroName: string
  hpPercent: number
  manaPercent: number
  advice: Omit<CoachAdvice, 'id' | 'created_at'>
}

const closedFeedback = ref<string | null>(null)
let feedbackTimer: any = null

const presets: SimulationPreset[] = [
  {
    id: 'roshan-window',
    category: 'roshan_tactics',
    categoryLabel: '肉山控盾',
    title: '肉山刷新预警 (前60s)',
    desc: '肉山巢穴即将在 60 秒内进入刷新窗口',
    icon: Skull,
    heroName: '熊战士',
    hpPercent: 90,
    manaPercent: 65,
    advice: {
      rule_id: 'ROSHAN_WINDOW',
      rule: 'ROSHAN_WINDOW',
      category: 'roshan_tactics',
      title: '肉山刷新预警',
      text: '肉山巢穴将在 60 秒内进入刷新窗口，请排布视野控盾！',
      severity: 'Warning',
      suggested_action: '带扫描提前占领肉山坑高坡视野，开雾寻找抓人机会。',
      ai_explanation: '控下二代肉山的不朽之守护和奶酪可直接锁定上高胜局。',
    },
  },
  {
    id: 'tormentor-refresh',
    category: 'tormentor_tactics',
    categoryLabel: '痛苦魔方',
    title: '痛苦魔方刷新 (20分钟)',
    desc: '魔方降临，提示团队获取魔晶',
    icon: Box,
    heroName: '潮汐猎人',
    hpPercent: 100,
    manaPercent: 85,
    advice: {
      rule_id: 'TORMENTOR_SPAWN',
      rule: 'TORMENTOR_SPAWN',
      category: 'tormentor_tactics',
      title: '痛苦魔方刷新',
      text: '痛苦魔方将在 10 秒后刷新，建议召集队友集结拿魔晶！',
      severity: 'Info',
      suggested_action: '在魔方区域排布防守眼，2-3人快速击杀。',
      ai_explanation: '魔方提供的阿哈利姆魔晶价值 1400 经济，是前期质变关键点。',
    },
  },
  {
    id: 'rune-power',
    category: 'rune_control',
    categoryLabel: '神符视野掌控',
    title: '双极强化神符前 15s',
    desc: '河道双极强化神符即将在 15 秒后降临',
    icon: Compass,
    heroName: '影魔',
    hpPercent: 85,
    manaPercent: 70,
    advice: {
      rule_id: 'POWER_RUNE_SPAWN',
      rule: 'POWER_RUNE_SPAWN',
      category: 'rune_control',
      title: '强化神符争夺',
      text: '河道双极强化神符将在 15 秒后刷新，注意控符！',
      severity: 'Warning',
      suggested_action: '推完中路线，呼叫边路辅助协助控制上下河道神符。',
      ai_explanation: '双倍伤害或急速神符对前期节奏有决定性影响，必须全力争取。',
    },
  },
  {
    id: 'buyback-critical',
    category: 'buyback_strategy',
    categoryLabel: '买活储备与防守',
    title: '买活资金不足警告',
    desc: '后期阵亡但身上资金不足以买活',
    icon: Skull,
    heroName: '斯温',
    hpPercent: 100,
    manaPercent: 80,
    advice: {
      rule_id: 'BUYBACK_SHORTAGE',
      rule: 'BUYBACK_SHORTAGE',
      category: 'buyback_strategy',
      title: '买活警戒',
      text: '比赛进入 35 分钟后期，当前存款不足以支撑买活，请预留买活钱！',
      severity: 'Critical',
      suggested_action: '优先保留 2200 储备金，切忌一次性花光所有金钱。',
      ai_explanation: '后期无买活一旦阵亡将被一波推平基地。',
    },
  },
  {
    id: 'key-items',
    category: 'item_timing_threat',
    categoryLabel: '关键装成型与克制',
    title: '关键质变装备跳刀成型',
    desc: '马格纳斯科勒的闪烁匕首已送达',
    icon: ShoppingBag,
    heroName: '马格纳斯',
    hpPercent: 90,
    manaPercent: 85,
    advice: {
      rule_id: 'KEY_ITEM_BLINK',
      rule: 'KEY_ITEM_BLINK',
      category: 'item_timing_threat',
      title: '关键装备成型',
      text: '跳刀已送达！英雄迎来质变发力期，立即召集队友开雾进攻！',
      severity: 'Info',
      suggested_action: '开诡计之雾切入敌方野区打先手大招。',
      ai_explanation: '跳刀第一波出其不意能直接打碎对手节奏。',
    },
  },
  {
    id: 'ai-tactical',
    category: 'tactical_ai_ask',
    categoryLabel: '大模型战术研判',
    title: '大模型战术推塔研判',
    desc: '端侧大模型研判当前兵线与敌方买活状态',
    icon: Zap,
    heroName: '主宰',
    hpPercent: 95,
    manaPercent: 80,
    advice: {
      rule_id: 'AI_TACTICAL_HIGHGROUND',
      rule: 'AI_TACTICAL_HIGHGROUND',
      category: 'tactical_ai_ask',
      title: 'AI 战术推塔研判',
      text: '敌方关键核心无买活且中路二塔已破，建议直接集结推中路高地！',
      severity: 'Info',
      suggested_action: '带盾直接推中，辅助站高坡后方插真眼防切。',
      ai_explanation: '根据 2026 Qwen 3.5 决策评估，此时推高胜率超过 78%。',
    },
  },
]

const { state, addAdvice, setGsiConnected, getReminderMode } = useAppStore()

function handleRunPreset(preset: SimulationPreset) {
  setGsiConnected(true)

  // 根据当前三段式配置裁定
  const mode = getReminderMode(preset.category)

  // 1. 若当前类别为 0 (关闭)：不生成战术流，也不发声
  if (mode === 0) {
    if (feedbackTimer) clearTimeout(feedbackTimer)
    closedFeedback.value = `【${preset.categoryLabel}】当前已设为关闭，不触发播报与战术流`
    feedbackTimer = setTimeout(() => {
      closedFeedback.value = null
    }, 2500)
    return
  }

  closedFeedback.value = null

  const adviceItem: CoachAdvice = {
    ...preset.advice,
    category: preset.category,
    id: `sim-${Date.now()}-${Math.random().toString(36).substr(2, 4)}`,
    created_at: new Date().toLocaleTimeString('zh-CN', { hour12: false }),
  }

  // 2. 模式 1 (仅文字) 与 模式 2 (语音+文字)：均展示文字战术流
  addAdvice(adviceItem)

  // 3. 模式 2 (语音+文字)：通过 Edge-TTS 朗读
  if (mode === 2) {
    speechService.speak(
      `${adviceItem.title}。${adviceItem.text}`,
      {
        mode: 2,
        rate: state.settings.voice_speed,
        volume: state.settings.voice_volume,
        voiceName: state.settings.edge_voice_name || state.settings.voice_name,
      },
    )
  }
}
</script>

<template>
  <div class="bg-dota-panel border-dota-border p-4 border rounded-xl space-y-3">
    <div class="border-dota-border pb-2.5 border-b flex flex-wrap gap-2 items-center justify-between">
      <div class="flex items-center space-x-2">
        <PlayCircle class="text-dota-gold h-4 w-4" />
        <h3 class="text-dota-goldLight text-xs font-bold">
          GSI 局内 9 类战术事件模拟注入
        </h3>
      </div>
      <div class="text-dota-subtle text-[10px] flex items-center space-x-2">
        <span v-if="closedFeedback" class="text-dota-red font-medium animate-pulse">
          {{ closedFeedback }}
        </span>
        <span v-else>点击模拟触发，遵循上方三段式开关判定</span>
      </div>
    </div>

    <!-- 9 类提醒模拟卡片网格 -->
    <div class="gap-2 grid grid-cols-2 lg:grid-cols-5 sm:grid-cols-3">
      <button
        v-for="preset in presets"
        :key="preset.id"
        class="group p-2.5 text-left border rounded-lg flex flex-col transition-all justify-between relative" :class="[
          getReminderMode(preset.category) === 0
            ? 'bg-dota-surface/40 border-dota-border/60 opacity-60 hover:opacity-100'
            : getReminderMode(preset.category) === 1
              ? 'bg-dota-surface hover:bg-dota-hover border-dota-cyan/30'
              : 'bg-dota-surface hover:bg-dota-hover border-dota-gold/30',
        ]"
        @click="handleRunPreset(preset)"
      >
        <div class="flex w-full items-center justify-between">
          <component
            :is="preset.icon"
            class="h-4 w-4" :class="[
              getReminderMode(preset.category) === 2
                ? 'text-dota-gold'
                : getReminderMode(preset.category) === 1
                  ? 'text-dota-cyan'
                  : 'text-dota-subtle',
            ]"
          />
          <!-- 当前生效模式徽章 -->
          <span
            class="text-[9px] font-mono px-1.5 py-0.2 border rounded" :class="[
              getReminderMode(preset.category) === 2
                ? 'bg-dota-gold/20 text-dota-gold border-dota-gold/40'
                : getReminderMode(preset.category) === 1
                  ? 'bg-dota-cyan/20 text-dota-cyan border-dota-cyan/40'
                  : 'bg-dota-red/10 text-dota-red/80 border-dota-red/30',
            ]"
          >
            {{
              getReminderMode(preset.category) === 2
                ? '语音+文字'
                : getReminderMode(preset.category) === 1
                  ? '仅文字'
                  : '关闭'
            }}
          </span>
        </div>

        <div class="mt-2 w-full">
          <div class="text-dota-text group-hover:text-dota-goldLight text-xs font-bold flex items-center justify-between">
            <span>{{ preset.title }}</span>
          </div>
          <div class="text-dota-subtle text-[10px] mt-0.5 truncate" :title="preset.desc">
            {{ preset.desc }}
          </div>
          <div class="text-dota-muted border-dota-border/40 text-[9px] font-mono mt-1 pt-1 border-t flex items-center justify-between">
            <span>{{ preset.heroName }}</span>
            <span class="text-dota-subtle text-[9px]">{{ preset.categoryLabel }}</span>
          </div>
        </div>
      </button>
    </div>
  </div>
</template>
