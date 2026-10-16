<script setup lang="ts">
/**
 * 战术语音播报与策略矩阵设置组件 (src/views/settings/sections/VoiceBroadcastSection.vue)
 * 职责：
 * 1. 战术语音提醒策略配置（关闭 / 仅文字 / 语音+文字）
 * 2. 优化小字与卡片布局，文字完整不截断，字体清晰锐利抗发糊
 * 3. 音量与语速微调控制
 */

import { Bell, Play, RotateCcw, Volume2, Zap } from '@lucide/vue'
import { ref } from 'vue'
import ThreeSegmentToggle from '@/components/coach/ThreeSegmentToggle.vue'
import { DEFAULT_REMINDER_CATEGORIES } from '@/data/reminderCategories.ts'
import { speechService } from '@/services/speechService.ts'
import { useAppStore } from '@/stores'

defineOptions({ name: 'VoiceBroadcastSection' })

const appStore = useAppStore()
const isTestingVoice = ref(false)

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
  <section class="p-5 border border-slate-700/90 rounded-2xl bg-slate-900 antialiased shadow-xl space-y-5">
    <!-- 头部说明与快捷方案 -->
    <div class="flex flex-col gap-3 justify-between lg:flex-row lg:items-center">
      <div class="flex items-center space-x-3">
        <div class="text-amber-400 p-2 border border-amber-500/40 rounded-xl bg-amber-500/15">
          <Volume2 class="shrink-0 h-5 w-5" />
        </div>
        <div>
          <h2 class="text-base text-slate-100 font-bold flex gap-2 items-center">
            战术语音提醒矩阵
            <span class="text-xs text-amber-300 font-semibold px-2 py-0.5 border border-amber-500/40 rounded-full bg-amber-500/15">
              9 大战略事件
            </span>
          </h2>
        </div>
      </div>

      <!-- 一键快捷套用策略方案 -->
      <div class="flex flex-wrap gap-2 items-center">
        <span class="text-xs text-slate-300 font-medium mr-1 hidden sm:inline">策略预设:</span>
        <button
          class="text-xs font-semibold px-3 py-1.5 border rounded-lg cursor-pointer transition-all duration-200"
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
          class="text-xs font-semibold px-3 py-1.5 border rounded-lg cursor-pointer transition-all duration-200"
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
          class="text-xs font-semibold px-3 py-1.5 border rounded-lg cursor-pointer transition-all duration-200"
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
          class="text-slate-300 p-1.5 border border-slate-700 rounded-lg bg-slate-800 cursor-pointer transition-colors hover:text-white hover:border-slate-500"
          title="重置为默认策略"
          @click="appStore.resetReminderModes"
        >
          <RotateCcw class="h-4 w-4" />
        </button>
      </div>
    </div>

    <!-- 9 项细分策略卡片列表 (字体锐化、对比度提升、绝不截断) -->
    <div class="gap-3.5 grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3">
      <div
        v-for="cat in DEFAULT_REMINDER_CATEGORIES"
        :key="cat.key"
        class="p-4 border border-slate-800 rounded-xl bg-slate-950/80 flex flex-col transition-all duration-200 justify-between space-y-3 hover:border-slate-600 hover:bg-slate-950"
      >
        <!-- 卡片头部：分类名称与触发条件徽章 -->
        <div class="flex gap-2 items-start justify-between">
          <div class="flex items-center space-x-2">
            <Bell class="text-amber-400 shrink-0 h-4 w-4" />
            <span class="text-xs text-slate-100 tracking-wide font-bold">
              {{ cat.label }}
            </span>
          </div>
          <span
            v-if="cat.triggerCondition"
            class="text-xs text-slate-300 font-mono px-2 py-0.5 border border-slate-700 rounded bg-slate-900 shrink-0"
          >
            {{ cat.triggerCondition }}
          </span>
        </div>

        <!-- 详细说明小字：高对比度、清晰抗发糊 -->
        <p class="text-xs text-slate-300 leading-relaxed font-normal flex-1">
          {{ cat.description }}
        </p>

        <!-- 底部操作栏：三段式切换器与状态提示 -->
        <div class="pt-2.5 border-t border-slate-800 flex items-center justify-between">
          <span class="text-xs text-slate-300 font-medium flex gap-1 items-center">
            <Zap class="text-amber-400 h-3.5 w-3.5" />
            提醒档位:
          </span>
          <ThreeSegmentToggle
            :model-value="appStore.getReminderMode(cat.key)"
            @update:model-value="(val) => appStore.setReminderMode(cat.key, val)"
          />
        </div>
      </div>
    </div>

    <!-- 音量与语速微调及试听测试 -->
    <div class="p-4 border border-slate-800 rounded-xl bg-slate-950/80 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
      <div class="flex-1 gap-6 grid grid-cols-1 sm:grid-cols-2">
        <!-- 音量 -->
        <div class="flex items-center space-x-3">
          <span class="text-xs text-slate-200 font-semibold shrink-0">播报音量:</span>
          <input
            v-model.number="appStore.settings.voice_volume"
            type="range"
            min="10"
            max="100"
            step="5"
            class="appearance-none accent-amber-500 rounded-lg bg-slate-800 flex-1 h-2 cursor-pointer"
            @change="appStore.updateSettings({ voice_volume: appStore.settings.voice_volume })"
          >
          <span class="text-xs text-amber-400 font-bold font-mono text-right w-10">{{ appStore.settings.voice_volume }}%</span>
        </div>

        <!-- 语速 -->
        <div class="flex items-center space-x-3">
          <span class="text-xs text-slate-200 font-semibold shrink-0">播报语速:</span>
          <input
            v-model.number="appStore.settings.voice_speed"
            type="range"
            min="0.8"
            max="1.6"
            step="0.1"
            class="appearance-none accent-amber-500 rounded-lg bg-slate-800 flex-1 h-2 cursor-pointer"
            @change="appStore.updateSettings({ voice_speed: appStore.settings.voice_speed })"
          >
          <span class="text-xs text-amber-400 font-bold font-mono text-right w-10">{{ appStore.settings.voice_speed }}x</span>
        </div>
      </div>

      <!-- 试听按钮与引擎说明 -->
      <div class="flex shrink-0 items-center self-end space-x-2.5 sm:self-auto">
        <span class="text-[11px] text-cyan-300 font-medium px-2 py-1 border border-cyan-500/30 rounded-lg bg-cyan-500/10 gap-1 hidden items-center lg:inline-flex">
          <span class="rounded-full bg-cyan-400 h-1.5 w-1.5 animate-pulse" />
          内置原生离线 MeloTTS 引擎
        </span>
        <button
          class="text-xs text-slate-950 font-bold px-4 py-2 rounded-lg bg-amber-500 flex shrink-0 cursor-pointer shadow-sm transition-all items-center justify-center space-x-1.5 hover:bg-amber-400 active:scale-95"
          :disabled="isTestingVoice"
          @click="testCurrentVoice"
        >
          <Play class="h-3.5 w-3.5" :class="{ 'animate-spin': isTestingVoice }" />
          <span>{{ isTestingVoice ? '正在播报...' : '试听当前音效' }}</span>
        </button>
      </div>
    </div>
  </section>
</template>
