<template>
  <aside class="w-48 bg-dota-surface border-r border-dota-border flex flex-col justify-between p-2.5 select-none">
    <nav class="space-y-1">
      <button
        v-for="item in navItems"
        :key="item.id"
        @click="setCurrentTab(item.id)"
        :class="[
          'w-full flex items-center space-x-3 px-3 py-2 rounded-lg text-xs font-medium transition-all',
          state.currentTab === item.id
            ? 'bg-dota-panel text-dota-gold border border-dota-gold/40 shadow-sm'
            : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text'
        ]"
      >
        <img :src="item.icon" :alt="item.label" class="w-4 h-4 object-contain shrink-0" />
        <span>{{ item.label }}</span>
      </button>
    </nav>

    <div class="pt-2 border-t border-dota-border">
      <button
        @click="setCurrentTab('settings')"
        :class="[
          'w-full flex items-center space-x-3 px-3 py-2 rounded-lg text-xs font-medium transition-all',
          state.currentTab === 'settings'
            ? 'bg-dota-panel text-dota-gold border border-dota-gold/40 shadow-sm'
            : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text'
        ]"
      >
        <Settings class="w-4 h-4 shrink-0" />
        <span>应用设置</span>
      </button>
    </div>
  </aside>
</template>

<script setup lang="ts">
import { Settings } from 'lucide-vue-next';
import { useAppStore } from '../../store';

const { state, setCurrentTab } = useAppStore();

const navItems = [
  { id: 'heroes' as const, label: '英雄资料', icon: '/assets/icons/hero_universal.png' },
  { id: 'items' as const, label: '物品资料', icon: '/assets/icons/aghs_scepter.png' },
  { id: 'coach' as const, label: '实时教练', icon: '/assets/icons/app_256.png' },
  { id: 'analysis' as const, label: '赛后分析', icon: '/assets/icons/rank_icon_8.png' },
];
</script>
