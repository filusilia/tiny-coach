<script setup lang="ts">
/**
 * 战术语音播报与策略矩阵设置组件 (src/views/settings/sections/VoiceBroadcastSection.vue)
 * 职责：
 * 1. 战术语音提醒策略配置（关闭 / 仅文字 / 语音+文字）
 * 2. 优化小字与卡片布局，文字完整不截断，字体清晰锐利抗发糊
 * 3. 音量与语速微调控制
 */

import type { ReminderCategoryKey } from '@/types'
import { Bell, Clock, Play, RotateCcw, Volume2, Zap } from '@lucide/vue'
import { ref } from 'vue'
import ThreeSegmentToggle from '@/components/coach/ThreeSegmentToggle.vue'
import { DEFAULT_REMINDER_CATEGORIES } from '@/data/reminderCategories.ts'
import { speechService } from '@/services/speechService.ts'
import { useAppStore } from '@/stores'

defineOptions({ name: 'VoiceBroadcastSection' })

const appStore = useAppStore()
const isTestingVoice = ref(false)
function isTimeSensitiveCategory(key: string): boolean {
  return key === 'rune_control' || key === 'tormentor_tactics' || key === 'roshan_tactics'
}

function getCategoryAdvanceSeconds(key: ReminderCategoryKey | string): number {
  const dict = appStore.settings.reminder_advance_seconds as Record<string, number> | undefined
  const specific = dict?.[key]
  if (typeof specific === 'number') {
    return specific
  }
  return appStore.settings.reminder_lead_time_seconds ?? 15
}

function setCategoryAdvanceSeconds(key: ReminderCategoryKey | string, sec: number) {
  const current: Record<string, number> = { ...(appStore.settings.reminder_advance_seconds || {}) }
  current[key] = sec
  appStore.updateSettings({ reminder_advance_seconds: current as any })
}

function getCategoryTriggerCondition(cat: { key: string, triggerCondition?: string }) {
  if (cat.key === 'tormentor_tactics') {
    const sec = getCategoryAdvanceSeconds('tormentor_tactics')
    return sec > 0 ? `魔方前 ${sec}s` : '准点'
  }
  if (cat.key === 'rune_control') {
    const sec = getCategoryAdvanceSeconds('rune_control')
    return sec > 0 ? `神符前 ${sec}s` : '准点'
  }
  if (cat.key === 'roshan_tactics') {
    const sec = getCategoryAdvanceSeconds('roshan_tactics')
    return sec > 0 ? `肉山前 ${sec}s` : '肉山与盾消'
  }
  return cat.triggerCondition || ''
}

async function testCurrentVoice() {
  if (isTestingVoice.value) return
  isTestingVoice.value = true
  try {
    await speechService.speak(
      '神盾教练语音播报就绪，敌方核心英雄买活处于冷却期，建议果断集合推进上高！',
      {
        rate: appStore.settings.voice_speed,
        volume: appStore.settings.voice_volume,
        mode: 2,
      },
    )
  } catch {
    appStore.showToast('语音试听失败，请检查 TTS 引擎配置', 'warning')
  } finally {
    isTestingVoice.value = false
  }
}
</script>

<template>
  <section class="p-4 border border-slate-700/80 rounded-2xl bg-slate-900/90 antialiased shadow-xl space-y-3.5">
    <!-- 头部：唯一主标题与快捷方案 -->
    <div class="flex flex-col gap-2.5 justify-between lg:flex-row lg:items-center">
      <div class="flex items-center space-x-2.5">
        <div class="text-amber-400 p-1.5 border border-amber-500/40 rounded-xl bg-amber-500/15">
          <Volume2 class="shrink-0 h-4.5 w-4.5" />
        </div>
        <h2 class="text-sm text-slate-100 font-bold flex gap-2 items-center">
          战术语音提醒
          <span class="text-xs text-amber-300 font-semibold px-2 py-0.2 border border-amber-500/40 rounded-full bg-amber-500/15 font-mono">
            9 大战略事件
          </span>
        </h2>
      </div>

      <!-- 一键快捷套用策略方案 -->
      <div class="flex flex-wrap gap-1.5 items-center">
        <button
          class="text-xs font-semibold px-2.5 py-1 border rounded-lg cursor-pointer transition-all duration-200"
          :class="[
            appStore.currentBroadcastStrategy === 'all_coach'
              ? 'bg-amber-500/25 border-amber-500 text-amber-300 font-bold shadow-[0_0_12px_rgba(245,158,11,0.25)]'
              : 'bg-slate-800 border-slate-700 text-slate-200 hover:bg-slate-700 hover:text-white',
          ]"
          @click="appStore.applyBroadcastStrategy('all_coach')"
        >
          全能战术解说
        </button>
        <button
          class="text-xs font-semibold px-2.5 py-1 border rounded-lg cursor-pointer transition-all duration-200"
          :class="[
            appStore.currentBroadcastStrategy === 'focus_ranked'
              ? 'bg-cyan-500/25 border-cyan-500 text-cyan-300 font-bold shadow-[0_0_12px_rgba(6,182,212,0.25)]'
              : 'bg-slate-800 border-slate-700 text-slate-200 hover:bg-slate-700 hover:text-white',
          ]"
          @click="appStore.applyBroadcastStrategy('focus_ranked')"
        >
          天梯专注 (推荐)
        </button>
        <button
          class="text-xs font-semibold px-2.5 py-1 border rounded-lg cursor-pointer transition-all duration-200"
          :class="[
            appStore.currentBroadcastStrategy === 'silent_hud'
              ? 'bg-slate-700 border-slate-500 text-slate-100 font-bold'
              : 'bg-slate-800 border-slate-700 text-slate-200 hover:bg-slate-700 hover:text-white',
          ]"
          @click="appStore.applyBroadcastStrategy('silent_hud')"
        >
          纯净仅 HUD
        </button>
        <button
          class="text-slate-300 p-1 border border-slate-700 rounded-lg bg-slate-800 cursor-pointer transition-colors hover:text-white hover:border-slate-500"
          title="重置为默认策略"
          @click="appStore.resetReminderModes"
        >
          <RotateCcw class="h-3.5 w-3.5" />
        </button>
      </div>
    </div>

    <!-- 提前提醒时间、音量、语速与试听紧凑控制栏 -->
    <div class="p-3 border border-slate-800 rounded-xl bg-slate-950/70 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
      <div class="flex-1 gap-4 grid grid-cols-1 sm:grid-cols-3">
        <!-- 1. 时间类事件提前提醒 (0s - 30s) -->
        <div class="flex items-center space-x-2">
          <span class="text-xs text-slate-200 font-semibold shrink-0 flex items-center gap-1">
            <Clock class="w-3.5 h-3.5 text-amber-400" />
            提前时间:
          </span>
          <input
            v-model.number="appStore.settings.reminder_lead_time_seconds"
            type="range"
            min="0"
            max="30"
            step="1"
            class="appearance-none accent-amber-500 rounded-lg bg-slate-800 flex-1 h-1.5 cursor-pointer"
            @change="appStore.updateSettings({ reminder_lead_time_seconds: appStore.settings.reminder_lead_time_seconds })"
          >
          <span class="text-xs text-amber-400 font-bold font-mono text-right w-8">{{ appStore.settings.reminder_lead_time_seconds ?? 15 }}s</span>
        </div>

        <!-- 2. 音量 -->
        <div class="flex items-center space-x-2">
          <span class="text-xs text-slate-200 font-semibold shrink-0">播报音量:</span>
          <input
            v-model.number="appStore.settings.voice_volume"
            type="range"
            min="10"
            max="100"
            step="5"
            class="appearance-none accent-amber-500 rounded-lg bg-slate-800 flex-1 h-1.5 cursor-pointer"
            @change="appStore.updateSettings({ voice_volume: appStore.settings.voice_volume })"
          >
          <span class="text-xs text-amber-400 font-bold font-mono text-right w-9">{{ appStore.settings.voice_volume }}%</span>
        </div>

        <!-- 3. 语速 -->
        <div class="flex items-center space-x-2">
          <span class="text-xs text-slate-200 font-semibold shrink-0">播报语速:</span>
          <input
            v-model.number="appStore.settings.voice_speed"
            type="range"
            min="0.8"
            max="1.6"
            step="0.1"
            class="appearance-none accent-amber-500 rounded-lg bg-slate-800 flex-1 h-1.5 cursor-pointer"
            @change="appStore.updateSettings({ voice_speed: appStore.settings.voice_speed })"
          >
          <span class="text-xs text-amber-400 font-bold font-mono text-right w-8">{{ appStore.settings.voice_speed }}x</span>
        </div>
      </div>

      <!-- 试听按钮 -->
      <div class="flex shrink-0 items-center self-end sm:self-auto">
        <button
          class="text-xs text-slate-950 font-bold px-3 py-1.5 rounded-lg bg-amber-500 flex shrink-0 cursor-pointer shadow-sm transition-all items-center justify-center space-x-1.5 hover:bg-amber-400 active:scale-95"
          :disabled="isTestingVoice"
          @click="testCurrentVoice"
        >
          <Play class="h-3 w-3" :class="{ 'animate-spin': isTestingVoice }" />
          <span>{{ isTestingVoice ? '正在播报...' : '试听当前音效' }}</span>
        </button>
      </div>
    </div>

    <!-- 9 项细分策略卡片列表 (紧凑自适应网格) -->
    <div class="gap-2.5 grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3">
      <div
        v-for="cat in DEFAULT_REMINDER_CATEGORIES"
        :key="cat.key"
        class="p-3 border border-slate-800 rounded-xl bg-slate-950/80 flex flex-col transition-all duration-200 justify-between space-y-2 hover:border-slate-700 hover:bg-slate-950"
      >
        <!-- 卡片头部：分类名称与动态触发时机徽章 -->
        <div class="flex gap-2 items-center justify-between">
          <div class="flex items-center space-x-1.5">
            <Bell class="text-amber-400 shrink-0 h-3.5 w-3.5" />
            <span class="text-xs text-slate-100 tracking-wide font-bold">
              {{ cat.label }}
            </span>
          </div>
          <span
            class="text-[11px] text-slate-300 font-mono px-1.5 py-0.2 border border-slate-700 rounded bg-slate-900 shrink-0"
          >
            {{ getCategoryTriggerCondition(cat) }}
          </span>
        </div>

        <!-- 时间类专属：独立提前提醒秒数设置滑块 (如神符/痛苦魔方/肉山各自定义) -->
        <div
          v-if="isTimeSensitiveCategory(cat.key)"
          class="py-1 px-2 rounded-lg bg-slate-900/90 border border-slate-800 flex items-center justify-between gap-2"
        >
          <span class="text-[11px] text-slate-300 font-medium flex items-center gap-1 shrink-0">
            <Clock class="w-3 h-3 text-amber-400" />
            提前提醒:
          </span>
          <input
            type="range"
            min="0"
            :max="cat.key === 'roshan_tactics' ? 60 : 30"
            step="1"
            :value="getCategoryAdvanceSeconds(cat.key)"
            class="appearance-none accent-amber-500 rounded-lg bg-slate-800 flex-1 h-1 cursor-pointer"
            @input="(e: any) => setCategoryAdvanceSeconds(cat.key, Number(e.target.value))"
          >
          <span class="text-[11px] text-amber-400 font-bold font-mono w-7 text-right">
            {{ getCategoryAdvanceSeconds(cat.key) }}s
          </span>
        </div>

        <!-- 底部操作栏：三段式切换器 -->
        <div class="pt-1.5 border-t border-slate-800/80 flex items-center justify-between">
          <span class="text-xs text-slate-400 font-medium flex gap-1 items-center">
            <Zap class="text-amber-400 h-3 w-3" />
            提醒档位:
          </span>
          <ThreeSegmentToggle
            :model-value="appStore.getReminderMode(cat.key)"
            @update:model-value="(val) => appStore.setReminderMode(cat.key, val)"
          />
        </div>
      </div>
    </div>
  </section>
</template>
