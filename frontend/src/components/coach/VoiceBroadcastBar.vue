<script setup lang="ts">
import type { ReminderCategoryKey } from '../../types'
import {
  ChevronDown,
  ChevronUp,
  Headphones,
  RotateCcw,
  Settings,
  Shield,
  Sparkles,
  Swords,
  Volume2,
} from '@lucide/vue'
import { storeToRefs } from 'pinia'
import { computed, ref } from 'vue'
import {
  BROADCAST_STRATEGY_PRESETS,
  DEFAULT_REMINDER_CATEGORIES,
} from '../../data/reminderCategories'
import { speechService } from '../../services/speechService'
import { useAppStore } from '../../stores'

const appStore = useAppStore()
const { currentBroadcastStrategy } = storeToRefs(appStore)
const {
  getReminderMode,
  setReminderMode,
  resetReminderModes,
  applyBroadcastStrategy,
  setCurrentTab,
} = appStore

const isExpanded = ref(false)

const voiceCount = computed(() => {
  return DEFAULT_REMINDER_CATEGORIES.filter(c => getReminderMode(c.key) === 2).length
})

const textOnlyCount = computed(() => {
  return DEFAULT_REMINDER_CATEGORIES.filter(c => getReminderMode(c.key) === 1).length
})

const sampleTexts: Partial<Record<ReminderCategoryKey, string>> = {
  macro_tempo: '全局节奏！对线期优势，组织队友迅速推掉敌方优势路外塔！',
  roshan_tactics: '肉山控盾！肉山洞穴即将刷新，提前争夺三角区与外塔视野！',
  tormentor_tactics: '痛苦魔方！20分钟痛苦魔方即将降临，组织队友快速拿魔晶！',
  rune_control: '控符时机！河道极速神符已刷新，中单配合开雾抓人！',
  buyback_strategy: '买活储备！核心英雄注意保留 1500 块买活金，防备对方买活反打！',
  item_timing_threat: '关键质变！敌方核心已做出闪烁匕首与黑皇杖，注意分散站位！',
  tactical_ai_ask: 'AI研判！敌方阵亡两人且买活在冷却，全队立即上高推中路兵营！',
  ability_ready: '核心技能就绪！终极技能与黑皇杖冷却完毕，具备绝对团战战力！',
  death_respawn: '阵亡复活！核心复活还有 10 秒，高地塔下坚守拉扯！',
}

function previewVoice(key?: ReminderCategoryKey) {
  const text = (key && sampleTexts[key])
    || (currentBroadcastStrategy.value === 'silent_hud'
      ? '战术语音播报已静音，当前仅展示屏幕文字卡片。'
      : '神盾战术教练就绪。肉山还有 30 秒刷新，建议全队排布河道视野。')

  speechService.speak(text, {
    mode: 2,
    rate: appStore.settings.voice_speed,
    volume: appStore.settings.voice_volume,
    voiceName: appStore.settings.edge_voice_name || appStore.settings.voice_name,
  })
}

// 三大功能分组数据映射
const reminderGroups = computed(() => [
  {
    id: 'macro',
    name: '地图节拍与局势',
    icon: Swords,
    desc: '肉山刷新、痛苦魔方、神符视野与推线转线节拍',
    items: DEFAULT_REMINDER_CATEGORIES.filter(c => c.group === 'macro' && c.key !== 'buyback_strategy'),
  },
  {
    id: 'defense',
    name: '关键装与买活警戒',
    icon: Shield,
    desc: '核心买活资金储备、敌方跳刀/BKB质变提醒、阵亡守高',
    items: DEFAULT_REMINDER_CATEGORIES.filter(c => ['buyback_strategy', 'item_timing_threat', 'death_respawn'].includes(c.key)),
  },
  {
    id: 'tactical',
    name: 'AI研判与技能就绪',
    icon: Sparkles,
    desc: 'AI 实时战况解答、终极技能与质变大招冷却就绪',
    items: DEFAULT_REMINDER_CATEGORIES.filter(c => ['tactical_ai_ask', 'ability_ready'].includes(c.key)),
  },
])
</script>

<template>
  <div class="border border-slate-800 rounded-xl bg-slate-900/90 select-none shadow-md transition-all overflow-hidden backdrop-blur-md">
    <!-- 顶栏：策略预设一键切换与状态展示 -->
    <div class="p-3 border-b border-slate-800/80 flex flex-wrap gap-3 items-center justify-between">
      <!-- 左侧：图标与当前模式 -->
      <div class="flex items-center space-x-2.5">
        <div class="border border-amber-500/30 rounded-lg bg-amber-500/15 flex shrink-0 h-8 w-8 items-center justify-center">
          <Headphones class="text-amber-400 h-4 w-4" />
        </div>
        <div>
          <div class="flex items-center space-x-2">
            <span class="text-xs text-slate-100 font-bold">战术语音播报</span>
            <span
              v-if="currentBroadcastStrategy === 'all_coach'"
              class="text-[10px] text-amber-300 font-bold font-mono px-2 py-0.5 border border-amber-500/40 rounded bg-amber-500/20"
            >
              全能解说模式
            </span>
            <span
              v-else-if="currentBroadcastStrategy === 'focus_ranked'"
              class="text-[10px] text-cyan-300 font-bold font-mono px-2 py-0.5 border border-cyan-500/40 rounded bg-cyan-500/20"
            >
              专注排位防打扰
            </span>
            <span
              v-else-if="currentBroadcastStrategy === 'silent_hud'"
              class="text-[10px] text-slate-400 font-bold font-mono px-2 py-0.5 border border-slate-700 rounded bg-slate-800"
            >
              纯净静音模式
            </span>
            <span
              v-else
              class="text-[10px] text-purple-300 font-bold font-mono px-2 py-0.5 border border-purple-500/40 rounded bg-purple-500/20"
            >
              自定义策略
            </span>
          </div>
          <div class="text-[11px] text-slate-400 mt-0.5 flex items-center space-x-3">
            <span>语音提醒: <strong class="text-amber-400 font-mono">{{ voiceCount }}</strong> 项</span>
            <span>文字提醒: <strong class="text-cyan-400 font-mono">{{ textOnlyCount }}</strong> 项</span>
          </div>
        </div>
      </div>

      <!-- 中间：宏观策略一键选择按钮组 -->
      <div class="p-1 border border-slate-800 rounded-lg bg-slate-950 flex items-center space-x-1">
        <button
          v-for="preset in BROADCAST_STRATEGY_PRESETS"
          :key="preset.id"
          class="text-xs font-semibold px-2.5 py-1 rounded-md flex transition-all items-center space-x-1.5"
          :class="[
            currentBroadcastStrategy === preset.id
              ? 'bg-amber-500 text-slate-950 shadow-sm font-bold'
              : 'text-slate-300 hover:text-white hover:bg-slate-800',
          ]"
          :title="preset.description"
          @click="applyBroadcastStrategy(preset.id)"
        >
          <span>{{ preset.name }}</span>
        </button>
      </div>

      <!-- 右侧：试听与展开控制 -->
      <div class="flex items-center space-x-2">
        <button
          class="text-xs text-amber-300 px-2.5 py-1 border border-slate-700 rounded-lg bg-slate-800 flex transition-colors items-center space-x-1 hover:bg-slate-700"
          title="试听当前声线语音效果"
          @click="previewVoice()"
        >
          <Volume2 class="h-3.5 w-3.5" />
          <span>试听播报</span>
        </button>
        <button
          class="text-xs text-slate-400 px-2 py-1 border border-slate-700 rounded-lg bg-slate-800 flex transition-colors items-center space-x-1 hover:text-slate-200 hover:bg-slate-700"
          title="前往系统设置深入配置发音人与端点"
          @click="setCurrentTab('settings')"
        >
          <Settings class="h-3.5 w-3.5" />
          <span>配置</span>
        </button>
        <button
          class="text-slate-400 p-1 border border-slate-700 rounded-lg transition-colors hover:text-slate-200 hover:bg-slate-800"
          :title="isExpanded ? '收起分类微调' : '展开分类微调'"
          @click="isExpanded = !isExpanded"
        >
          <component :is="isExpanded ? ChevronUp : ChevronDown" class="h-4 w-4" />
        </button>
      </div>
    </div>

    <!-- 展开面板：结构化三大战术模块直观开关 -->
    <div v-show="isExpanded" class="p-4 border-t border-slate-800/80 bg-slate-950/60 space-y-3">
      <div class="gap-3 grid grid-cols-1 md:grid-cols-3">
        <div
          v-for="grp in reminderGroups"
          :key="grp.id"
          class="p-3 border border-slate-800 rounded-xl bg-slate-900/70 flex flex-col justify-between space-y-2.5"
        >
          <div>
            <div class="pb-2 border-b border-slate-800/70 flex items-center space-x-2">
              <component :is="grp.icon" class="text-amber-400 h-4 w-4" />
              <span class="text-xs text-slate-100 font-bold">{{ grp.name }}</span>
            </div>
            <p class="text-[11px] text-slate-400 leading-relaxed mt-1.5">
              {{ grp.desc }}
            </p>
          </div>

          <!-- 子项直观切换 -->
          <div class="pt-1 space-y-1.5">
            <div
              v-for="item in grp.items"
              :key="item.key"
              class="text-xs p-2 border border-slate-800/60 rounded-lg bg-slate-950/80 flex items-center justify-between"
            >
              <div class="mr-2 flex truncate items-center space-x-1.5">
                <span class="text-slate-200 font-medium truncate">{{ item.label }}</span>
              </div>
              <div class="flex shrink-0 items-center space-x-1">
                <button
                  class="text-[10px] font-semibold px-2 py-0.5 rounded transition-colors"
                  :class="[
                    getReminderMode(item.key) === 2
                      ? 'bg-amber-500/20 text-amber-300 border border-amber-500/50'
                      : 'bg-slate-900 text-slate-400 hover:text-slate-200 border border-slate-800',
                  ]"
                  title="开启语音+文字"
                  @click="setReminderMode(item.key, 2)"
                >
                  语音
                </button>
                <button
                  class="text-[10px] font-semibold px-2 py-0.5 rounded transition-colors"
                  :class="[
                    getReminderMode(item.key) === 1
                      ? 'bg-cyan-500/20 text-cyan-300 border border-cyan-500/50'
                      : 'bg-slate-900 text-slate-400 hover:text-slate-200 border border-slate-800',
                  ]"
                  title="仅文字呈现，不发声"
                  @click="setReminderMode(item.key, 1)"
                >
                  仅字
                </button>
                <button
                  class="text-[10px] font-semibold px-1.5 py-0.5 rounded transition-colors"
                  :class="[
                    getReminderMode(item.key) === 0
                      ? 'bg-rose-500/20 text-rose-300 border border-rose-500/50'
                      : 'bg-slate-900 text-slate-500 hover:text-rose-400 border border-slate-800',
                  ]"
                  title="完全关闭该提醒"
                  @click="setReminderMode(item.key, 0)"
                >
                  关
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 底部快捷工具 -->
      <div class="text-xs text-slate-400 pt-2 border-t border-slate-800/80 flex items-center justify-between">
        <span>💡 建议在连麦开黑时选用【专注排位防打扰】，团战不分心。</span>
        <button
          class="text-slate-400 flex transition-colors items-center space-x-1 hover:text-slate-200"
          @click="resetReminderModes"
        >
          <RotateCcw class="h-3 w-3" />
          <span>恢复默认配置</span>
        </button>
      </div>
    </div>
  </div>
</template>
