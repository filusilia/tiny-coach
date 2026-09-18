<template>
  <div class="flex-1 overflow-y-auto p-6 space-y-6 bg-dota-bg select-none">
    <div class="flex items-center justify-between border-b border-dota-border pb-4">
      <div>
        <h1 class="text-xl font-bold text-dota-goldLight">赛后战绩与 AI 复盘</h1>
        <p class="text-xs text-dota-muted mt-1">深度挖掘本地对局日志、经济曲线与团战参战率</p>
      </div>
      <div class="flex items-center space-x-2 text-xs">
        <span class="px-3 py-1.5 rounded-lg bg-dota-panel text-dota-cyan border border-dota-border font-medium">
          {{ matches.length > 0 ? `已归档 ${matches.length} 场对局` : '暂无对局归档' }}
        </span>
      </div>
    </div>

    <!-- 真实空状态展示 (严格杜绝示例假数据) -->
    <div v-if="matches.length === 0" class="py-20 flex flex-col items-center justify-center text-center space-y-4 rounded-2xl bg-dota-panel/40 border border-dashed border-dota-border max-w-2xl mx-auto my-8 p-8">
      <div class="w-16 h-16 rounded-full bg-dota-surface flex items-center justify-center border border-dota-border text-dota-gold shadow-inner">
        <Trophy class="w-8 h-8 opacity-70" />
      </div>
      <div class="space-y-1.5">
        <h3 class="text-base font-bold text-dota-text">暂无已归档的赛后对局记录</h3>
        <p class="text-xs text-dota-subtle max-w-md leading-relaxed">
          当您在 Dota 2 客户端中完成对局后，神盾教练将通过本地 GSI 引擎自动捕获全场关键数据，并在此呈现包含经济折线、击杀时间轴与胜负复盘报告。
        </p>
      </div>
      <div class="pt-2 flex items-center space-x-2 text-xs text-dota-cyan font-mono bg-dota-panel px-3 py-1.5 rounded-lg border border-dota-border">
        <span>GSI 监听服务在线 (端口 4444)</span>
      </div>
    </div>

    <!-- 历史对局列表 (当存在真实归档时渲染) -->
    <div v-else class="space-y-3">
      <div
        v-for="m in matches"
        :key="m.id"
        class="p-4 rounded-xl bg-dota-panel border border-dota-border flex items-center justify-between hover:border-dota-gold/40 transition-colors"
      >
        <div class="flex items-center space-x-4">
          <div
            :class="[
              'w-10 h-10 rounded-lg flex items-center justify-center font-bold text-xs',
              m.result === '胜利' ? 'bg-dota-green/20 text-dota-green border border-dota-green/40' : 'bg-dota-red/20 text-dota-red border border-dota-red/40'
            ]"
          >
            {{ m.result }}
          </div>
          <div>
            <div class="text-sm font-bold text-dota-text">{{ m.hero }}</div>
            <div class="text-xs text-dota-subtle font-mono mt-0.5">比赛 ID: {{ m.id }} · 时长 {{ m.duration }}</div>
          </div>
        </div>

        <div class="flex items-center space-x-8 font-mono text-xs">
          <div>
            <div class="text-dota-subtle text-[11px]">K / D / A</div>
            <div class="text-dota-text font-bold">{{ m.kda }}</div>
          </div>
          <div>
            <div class="text-dota-subtle text-[11px]">GPM / XPM</div>
            <div class="text-dota-gold font-bold">{{ m.gpm }} / {{ m.xpm }}</div>
          </div>
          <button class="flex items-center space-x-1 px-3 py-1.5 rounded-lg bg-dota-surface hover:bg-dota-hover border border-dota-border text-dota-gold text-xs transition-colors cursor-pointer">
            <span>战术复盘</span>
            <ArrowUpRight class="w-3.5 h-3.5" />
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { Trophy, ArrowUpRight } from 'lucide-vue-next';

interface MatchRecord {
  id: string;
  hero: string;
  result: string;
  kda: string;
  gpm: number;
  xpm: number;
  duration: string;
}

// 初始严格置空，绝不渲染写死的伪造数据
const matches = ref<MatchRecord[]>([]);
</script>
