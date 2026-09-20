<script setup lang="ts">
import { Settings } from '@lucide/vue'
import { useAppStore } from '../../store'

const { state, setCurrentTab } = useAppStore()

const navItems = [
  { id: 'heroes' as const, label: '英雄资料', icon: '/assets/icons/hero_universal.png' },
  { id: 'items' as const, label: '物品资料', icon: '/assets/icons/aghs_scepter.png' },
  { id: 'coach' as const, label: '实时教练', icon: '/assets/icons/app_256.png' },
  { id: 'analysis' as const, label: '赛后分析', icon: '/assets/icons/rank_icon_8.png' },
]
</script>

<template>
  <aside class="bg-dota-surface border-dota-border p-2.5 border-r flex flex-col w-48 select-none justify-between">
    <nav class="space-y-1">
      <button
        v-for="item in navItems"
        :key="item.id"
        class="text-xs font-medium px-3 py-2 rounded-lg flex w-full transition-all items-center space-x-3" :class="[
          state.currentTab === item.id
            ? 'bg-dota-panel text-dota-gold border border-dota-gold/40 shadow-sm'
            : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text',
        ]"
        @click="setCurrentTab(item.id)"
      >
        <img :src="item.icon" :alt="item.label" class="shrink-0 h-4 w-4 object-contain">
        <span>{{ item.label }}</span>
      </button>
    </nav>

    <div class="border-dota-border pt-2 border-t">
      <button
        class="text-xs font-medium px-3 py-2 rounded-lg flex w-full transition-all items-center space-x-3" :class="[
          state.currentTab === 'settings'
            ? 'bg-dota-panel text-dota-gold border border-dota-gold/40 shadow-sm'
            : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text',
        ]"
        @click="setCurrentTab('settings')"
      >
        <Settings class="shrink-0 h-4 w-4" />
        <span>应用设置</span>
      </button>
    </div>
  </aside>
</template>
