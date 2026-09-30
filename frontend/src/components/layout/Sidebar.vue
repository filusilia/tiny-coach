<script setup lang="ts">
import { Settings } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAppStore } from '@/stores'

const router = useRouter()
const route = useRoute()
const appStore = useAppStore()

const navItems = [
  { id: 'coach' as const, path: '/coach', label: '实时教练', icon: '/assets/icons/coach_64.png' },
  { id: 'analysis' as const, path: '/analysis', label: '赛后分析', icon: '/assets/icons/analysis_64.png' },
  { id: 'heroes' as const, path: '/heroes', label: '英雄资料', icon: '/assets/icons/hero_64.png' },
  { id: 'items' as const, path: '/items', label: '物品资料', icon: '/assets/icons/item_64.png' },
]

const currentTab = computed(() => {
  const path = route.path
  if (path.includes('coach')) return 'coach'
  if (path.includes('analysis')) return 'analysis'
  if (path.includes('heroes')) return 'heroes'
  if (path.includes('items')) return 'items'
  if (path.includes('settings')) return 'settings'
  return appStore.currentTab
})

function navigate(id: 'heroes' | 'items' | 'coach' | 'analysis' | 'settings', path: string) {
  appStore.setCurrentTab(id)
  router.push(path).catch(() => {})
}
</script>

<template>
  <aside class="p-2.5 border-r border-dota-border bg-dota-surface flex flex-col w-48 select-none justify-between">
    <nav class="space-y-1">
      <button
        v-for="item in navItems"
        :key="item.id"
        class="text-xs font-medium px-3 py-2 rounded-lg flex w-full cursor-pointer transition-all items-center space-x-3"
        :class="[
          currentTab === item.id
            ? 'bg-dota-panel text-dota-gold border border-dota-gold/40 shadow-sm'
            : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text',
        ]"
        @click="navigate(item.id, item.path)"
      >
        <img :src="item.icon" :alt="item.label" class="shrink-0 h-5 w-5 object-contain">
        <span>{{ item.label }}</span>
      </button>
    </nav>

    <div class="pt-2 border-t border-dota-border">
      <button
        class="text-xs font-medium px-3 py-2 rounded-lg flex w-full cursor-pointer transition-all items-center space-x-3"
        :class="[
          currentTab === 'settings'
            ? 'bg-dota-panel text-dota-gold border border-dota-gold/40 shadow-sm'
            : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text',
        ]"
        @click="navigate('settings', '/settings')"
      >
        <Settings class="shrink-0 h-4 w-4" />
        <span>应用设置</span>
      </button>
    </div>
  </aside>
</template>
