<script setup lang="ts">
import type { ReminderCategoryKey } from '../../types'
import { ChevronDown, ChevronUp, RotateCcw, Volume2 } from '@lucide/vue'
import { computed, ref } from 'vue'
import { DEFAULT_REMINDER_CATEGORIES } from '../../data/reminderCategories'
import { speechService } from '../../services/speechService'
import { useAppStore } from '../../store'
import ThreeSegmentToggle from './ThreeSegmentToggle.vue'

const {
  state,
  getReminderMode,
  setReminderMode,
  setAllReminderModes,
  resetReminderModes,
} = useAppStore()

const isExpanded = ref(true)

const voiceCount = computed(() => {
  return DEFAULT_REMINDER_CATEGORIES.filter(c => getReminderMode(c.key) === 2).length
})

const textOnlyCount = computed(() => {
  return DEFAULT_REMINDER_CATEGORIES.filter(c => getReminderMode(c.key) === 1).length
})

const disabledCount = computed(() => {
  return DEFAULT_REMINDER_CATEGORIES.filter(c => getReminderMode(c.key) === 0).length
})

const sampleTexts: Partial<Record<ReminderCategoryKey, string>> = {
  macro_tempo: '全局节奏！注意当前对局核心刷钱与压制节奏，防范敌方开雾抓人！',
  roshan_tactics: '肉山控盾！肉山洞穴即将刷新，提前争夺三角区与外塔视野！',
  tormentor_tactics: '痛苦魔方！20分钟痛苦魔方即将降临，组织队友快速拿魔晶！',
  rune_control: '控符时机！注意控双极神符与赏金符，压制敌方中单游走空间！',
  buyback_strategy: '买活储备！防守关键高地，保留核心英雄买活金！',
  item_timing_threat: '装备质变！敌方核心关键装已出，避免正面盲目接团！',
  tactical_ai_ask: 'AI研判！AI 实时推演敌我战力曲线，建议集合推塔！',
  ability_ready: '核心技能就绪！终极技能与黑皇杖已就绪，具备接团战力！',
  death_respawn: '阵亡复活！防守高地注意观察买活资金！',
}

function previewCategoryVoice(key: ReminderCategoryKey) {
  const text = sampleTexts[key] || '战术语音播报测试。'
  speechService.speak(text, {
    mode: 2, // 试听强制发声
    rate: state.settings.voice_speed,
    volume: state.settings.voice_volume,
    voiceName: state.settings.edge_voice_name || state.settings.voice_name,
  })
}
</script>

<template>
  <div class="bg-dota-panel border-dota-border border rounded-xl select-none shadow-sm transition-all overflow-hidden">
    <!-- 顶栏：标题、当前统计与快捷操作 -->
    <div class="bg-dota-surface/60 border-dota-border p-3.5 border-b flex flex-wrap gap-3 items-center justify-between">
      <div class="flex items-center space-x-2.5">
        <div class="bg-dota-gold/15 border-dota-gold/30 border rounded-lg flex h-7 w-7 items-center justify-center">
          <Volume2 class="text-dota-gold h-4 w-4" />
        </div>
        <div>
          <h3 class="text-xs text-slate-100 tracking-wide font-bold">
            AI 提醒快捷控制栏
          </h3>
          <div class="text-[11px] text-slate-300 font-medium mt-0.5 flex items-center space-x-3">
            <span>
              已启用语音: <strong class="text-amber-400 font-mono">{{ voiceCount }}</strong>
            </span>
            <span>
              仅文字: <strong class="text-cyan-400 font-mono">{{ textOnlyCount }}</strong>
            </span>
            <span>
              已关闭: <strong class="text-rose-400 font-mono">{{ disabledCount }}</strong>
            </span>
          </div>
        </div>
      </div>

      <!-- 快捷批处理与折叠操作 -->
      <div class="text-xs flex items-center space-x-1.5">
        <button
          title="一键将全部 9 类提醒设置为【语音+文字】"
          class="bg-dota-surface hover:bg-dota-hover border-dota-border text-dota-gold text-[11px] px-2 py-1 border rounded transition-colors"
          @click="setAllReminderModes(2)"
        >
          全开语音
        </button>
        <button
          title="一键将全部 9 类提醒设置为【仅文字播报】"
          class="bg-dota-surface hover:bg-dota-hover border-dota-border text-dota-cyan text-[11px] px-2 py-1 border rounded transition-colors"
          @click="setAllReminderModes(1)"
        >
          全开文字
        </button>
        <button
          title="一键将全部 9 类提醒完全关闭"
          class="bg-dota-surface hover:bg-dota-hover border-dota-border text-dota-muted hover:text-dota-red text-[11px] px-2 py-1 border rounded transition-colors"
          @click="setAllReminderModes(0)"
        >
          全部关闭
        </button>
        <button
          title="恢复官方默认推荐设置"
          class="bg-dota-surface hover:bg-dota-hover border-dota-border text-dota-subtle hover:text-dota-text text-[11px] px-2 py-1 border rounded flex transition-colors items-center space-x-1"
          @click="resetReminderModes"
        >
          <RotateCcw class="h-3 w-3" />
          <span>恢复默认</span>
        </button>
        <button
          class="bg-dota-surface hover:bg-dota-hover border-dota-border text-dota-subtle hover:text-dota-text ml-1 p-1 border rounded transition-colors"
          :title="isExpanded ? '收起配置栏' : '展开配置栏'"
          @click="isExpanded = !isExpanded"
        >
          <component :is="isExpanded ? ChevronUp : ChevronDown" class="h-3.5 w-3.5" />
        </button>
      </div>
    </div>

    <!-- 展开面板：9 类提醒独立三段式开关列表 -->
    <div v-show="isExpanded" class="p-3.5 space-y-2">
      <div class="gap-2.5 grid grid-cols-1 lg:grid-cols-3 md:grid-cols-2">
        <div
          v-for="item in DEFAULT_REMINDER_CATEGORIES"
          :key="item.key"
          class="p-3 border rounded-lg flex flex-col transition-all justify-between space-y-2" :class="[
            getReminderMode(item.key) === 2
              ? 'bg-dota-surface/90 border-dota-gold/30'
              : getReminderMode(item.key) === 1
                ? 'bg-dota-surface/60 border-dota-cyan/30'
                : 'bg-dota-surface/30 border-dota-border opacity-70',
          ]"
        >
          <!-- 类别头部与条件标签 -->
          <div class="flex items-start justify-between">
            <div>
              <div class="flex items-center space-x-1.5">
                <span class="text-xs text-slate-100 font-bold">{{ item.label }}</span>
                <span
                  v-if="getReminderMode(item.key) !== item.defaultMode"
                  class="text-[9px] text-amber-300 font-mono px-1 border border-amber-500/40 rounded bg-amber-500/20"
                  title="已修改默认设置"
                >
                  自定义
                </span>
              </div>
              <div class="text-[11px] text-slate-300 font-normal mt-0.5 line-clamp-1" :title="item.description">
                {{ item.description }}
              </div>
            </div>

            <!-- 试听发声按钮 -->
            <button
              class="text-slate-400 p-1.5 rounded transition-colors hover:text-amber-400 hover:bg-slate-800"
              title="试听该类别播报效果"
              @click="previewCategoryVoice(item.key)"
            >
              <Volume2 class="h-3.5 w-3.5" />
            </button>
          </div>

          <!-- 触发条件与三段式开关 -->
          <div class="pt-1.5 border-t border-slate-800 flex items-center justify-between">
            <span
              class="text-[10px] text-amber-300 font-mono px-2 py-0.5 border border-slate-800 rounded bg-slate-950 max-w-[130px] truncate"
              :title="`触发条件: ${item.triggerCondition}`"
            >
              {{ item.triggerCondition }}
            </span>

            <ThreeSegmentToggle
              :model-value="getReminderMode(item.key)"
              size="sm"
              @update:model-value="(val) => setReminderMode(item.key, val)"
            />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
