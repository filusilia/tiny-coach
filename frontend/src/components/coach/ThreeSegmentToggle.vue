<script setup lang="ts">
import type { BroadcastMode } from '@/types'
import { FileText, Volume2, VolumeX } from '@lucide/vue'

const props = withDefaults(
  defineProps<{
    modelValue: BroadcastMode
    disabled?: boolean
    size?: 'sm' | 'md'
  }>(),
  {
    disabled: false,
    size: 'sm',
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', val: BroadcastMode): void
  (e: 'change', val: BroadcastMode): void
}>()

function handleSelect(val: BroadcastMode) {
  if (props.disabled) return
  emit('update:modelValue', val)
  emit('change', val)
}
</script>

<template>
  <div
    class="p-0.5 border border-dota-border rounded-lg bg-dota-surface/90 inline-flex shrink-0 select-none items-center"
    role="radiogroup"
    aria-label="播报模式选择"
  >
    <!-- 0: 关闭 -->
    <button
      type="button"
      role="radio"
      :aria-checked="modelValue === 0"
      :disabled="disabled"
      class="font-medium rounded flex transition-all items-center space-x-1"
      :class="[
        size === 'sm' ? 'px-2 py-0.5 text-xs' : 'px-2.5 py-1 text-xs',
        modelValue === 0
          ? 'bg-dota-red/20 text-dota-red border border-dota-red/40 shadow-xs'
          : 'text-dota-subtle hover:text-dota-text hover:bg-dota-hover/50 border border-transparent',
      ]" title="完全关闭该类提醒"
      @click="handleSelect(0)"
    >
      <VolumeX :class="size === 'sm' ? 'w-3 h-3' : 'w-3.5 h-3.5'" />
      <span>关</span>
    </button>

    <!-- 1: 仅文字播报 -->
    <button
      type="button"
      role="radio"
      :aria-checked="modelValue === 1"
      :disabled="disabled"
      class="font-medium rounded flex transition-all items-center space-x-1"
      :class="[
        size === 'sm' ? 'px-2 py-0.5 text-xs' : 'px-2.5 py-1 text-xs',
        modelValue === 1
          ? 'bg-dota-cyan/20 text-dota-cyan border border-dota-cyan/40 shadow-xs'
          : 'text-dota-subtle hover:text-dota-text hover:bg-dota-hover/50 border border-transparent',
      ]" title="仅文字通报（屏幕气泡，TTS静音）"
      @click="handleSelect(1)"
    >
      <FileText :class="size === 'sm' ? 'w-3 h-3' : 'w-3.5 h-3.5'" />
      <span>仅文字</span>
    </button>

    <!-- 2: 语音+文字播报 -->
    <button
      type="button"
      role="radio"
      :aria-checked="modelValue === 2"
      :disabled="disabled"
      class="font-medium rounded flex transition-all items-center space-x-1"
      :class="[
        size === 'sm' ? 'px-2 py-0.5 text-xs' : 'px-2.5 py-1 text-xs',
        modelValue === 2
          ? 'bg-dota-gold/20 text-dota-gold border border-dota-gold/40 shadow-xs font-semibold'
          : 'text-dota-subtle hover:text-dota-text hover:bg-dota-hover/50 border border-transparent',
      ]" title="语音+文字播报（屏幕气泡 + MeloTTS 超保真朗读）"
      @click="handleSelect(2)"
    >
      <Volume2 :class="size === 'sm' ? 'w-3 h-3' : 'w-3.5 h-3.5'" />
      <span>语音+文字</span>
    </button>
  </div>
</template>
