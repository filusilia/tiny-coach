<script setup lang="ts">
/**
 * 比赛搜索栏组件 (src/views/analysis/MatchSearchBar.vue)
 * 紧凑单行无外部卡片设计
 */

import { Activity, Search } from '@lucide/vue'

defineOptions({ name: 'MatchSearchBar' })

defineProps<{
  modelValue: string
  searching: boolean
  searchError: string
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'search'): void
}>()

function onInput(e: Event) {
  emit('update:modelValue', (e.target as HTMLInputElement).value)
}

function handleSearch() {
  emit('search')
}
</script>

<template>
  <div class="w-full select-none space-y-1.5">
    <div class="flex gap-2 items-center">
      <div class="flex-1 relative">
        <input
          :value="modelValue"
          type="text"
          placeholder="输入 Dota 2 比赛编号 (Match ID) 检索全场十人装备与战局数据..."
          class="text-xs text-slate-100 font-mono px-3.5 py-2 pl-9 outline-none border border-slate-700/80 rounded-xl bg-slate-900/90 w-full transition-colors placeholder:text-slate-500 focus:border-amber-400 focus:ring-1 focus:ring-amber-400"
          @input="onInput"
          @keydown.enter="handleSearch"
        >
        <Search class="text-slate-400 h-3.5 w-3.5 left-3 top-2.5 absolute" />
      </div>
      <button
        class="text-xs text-slate-950 font-bold px-4 py-2 rounded-xl bg-amber-500 flex shrink-0 cursor-pointer shadow transition-colors items-center justify-center space-x-1.5 hover:bg-amber-400 disabled:opacity-60 disabled:cursor-not-allowed"
        :disabled="searching"
        @click="handleSearch"
      >
        <Activity class="h-3.5 w-3.5" :class="{ 'animate-spin': searching }" />
        <span>{{ searching ? '检索中...' : '载入对局' }}</span>
      </button>
    </div>

    <div v-if="searchError" class="text-xs text-rose-400 font-mono px-1">
      {{ searchError }}
    </div>
  </div>
</template>
