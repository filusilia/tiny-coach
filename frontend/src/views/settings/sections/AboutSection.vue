<script setup lang="ts">
/**
 * 关于神盾教练与合规声明组件 (src/views/settings/sections/AboutSection.vue)
 */

import { CheckCircle2, ShieldCheck } from '@lucide/vue'

defineOptions({ name: 'AboutSection' })
</script>

<template>
  <section class="p-5 border border-slate-800 rounded-2xl bg-slate-950/80 select-none shadow-xl space-y-3">
    <div class="flex items-center justify-between">
      <div class="flex items-center space-x-3">
        <div class="border border-amber-500/40 rounded-xl bg-amber-500/10 flex h-9 w-9 items-center justify-center">
          <ShieldCheck class="text-amber-400 h-5 w-5" />
        </div>
        <div>
          <div class="text-sm text-slate-100 font-extrabold flex items-center space-x-2">
            <span>神盾教练 (AegisCoach)</span>
            <span class="text-sm text-amber-400 font-bold font-mono px-1.5 py-0.2 border border-amber-500/40 rounded bg-amber-500/20">
              v2.0.0
            </span>
          </div>
        </div>
      </div>
      <span class="text-xs text-emerald-400 font-mono px-2 py-0.5 border border-emerald-800/60 rounded bg-emerald-950 flex items-center space-x-1">
        <CheckCircle2 class="h-3 w-3" />
        <span>基于 Valve GSI</span>
      </span>
    </div>
  </section>
</template>
