<template>
  <div class="flex-1 flex flex-col overflow-hidden bg-dota-bg select-none">
    <!-- 顶部状态栏 -->
    <div class="h-12 border-b border-dota-border px-5 flex items-center justify-between bg-dota-surface shrink-0">
      <div class="flex items-center space-x-3">
        <div class="flex items-center space-x-2">
          <span
            :class="[
              'w-2.5 h-2.5 rounded-full',
              state.gsiConnected ? 'bg-dota-green animate-pulse' : 'bg-dota-subtle'
            ]"
          />
          <span class="text-xs font-bold text-dota-text">
            {{ state.gsiConnected ? 'Dota 2 GSI 实时通信在线 (4444 端口)' : '等待 Dota 2 GSI 客户端接入...' }}
          </span>
        </div>
        <span class="text-[10px] px-2 py-0.5 rounded bg-dota-panel text-dota-subtle border border-dota-border font-mono">
          10Hz 高频快照流
        </span>
      </div>

      <button
        v-if="state.advices.length > 0"
        @click="clearAdvices"
        class="flex items-center space-x-1 px-2.5 py-1 rounded text-xs text-dota-subtle hover:text-dota-red hover:bg-dota-hover transition-colors"
      >
        <Trash2 class="w-3.5 h-3.5" />
        <span>清空战术流</span>
      </button>
    </div>

    <!-- 主体区域 -->
    <div class="flex-1 overflow-y-auto p-5 space-y-4">
      <!-- 战局模拟器面板 -->
      <GsiSimulator />

      <!-- 实时战术决策流 -->
      <div class="space-y-3">
        <div class="flex items-center justify-between">
          <h2 class="text-xs font-bold text-dota-gold flex items-center space-x-1.5">
            <Radio class="w-4 h-4" />
            <span>实时裁定建议流 ({{ state.advices.length }})</span>
          </h2>
          <span class="text-[10px] text-dota-subtle">确定性规则毫秒级直出 · AI 深度战术旁路</span>
        </div>

        <div v-if="state.advices.length === 0" class="p-12 text-center text-dota-subtle text-xs border border-dashed border-dota-border rounded-xl">
          暂无实时战术裁定。在游戏中进行操作或点击上方快捷按钮模拟突发战局！
        </div>

        <div v-else class="space-y-2.5">
          <div
            v-for="item in state.advices"
            :key="item.id"
            :class="[
              'p-4 rounded-xl border transition-all space-y-2 bg-dota-panel',
              item.severity === 'Critical' ? 'border-dota-red/50' :
              item.severity === 'Warning' ? 'border-dota-gold/50' : 'border-dota-border'
            ]"
          >
            <div class="flex items-center justify-between">
              <div class="flex items-center space-x-2">
                <span
                  :class="[
                    'text-[10px] px-2 py-0.5 rounded font-bold uppercase',
                    item.severity === 'Critical' ? 'bg-dota-red/20 text-dota-red border border-dota-red/40' :
                    item.severity === 'Warning' ? 'bg-dota-gold/20 text-dota-gold border border-dota-gold/40' :
                    'bg-dota-surface text-dota-cyan border border-dota-border'
                  ]"
                >
                  {{ item.severity === 'Critical' ? '危急告警' : item.severity === 'Warning' ? '战术提醒' : '战况通报' }}
                </span>
                <span class="text-xs font-bold text-dota-text">{{ item.title }}</span>
                <span v-if="item.rule_id || item.rule" class="text-[10px] text-dota-subtle font-mono">
                  [{{ item.rule_id || item.rule }}]
                </span>
              </div>

              <div class="flex items-center space-x-2">
                <span class="text-[10px] text-dota-subtle font-mono">{{ item.created_at }}</span>
                <button
                  @click="replayVoice(item)"
                  title="重新语音播报"
                  class="p-1 hover:bg-dota-hover rounded text-dota-muted hover:text-dota-gold transition-colors"
                >
                  <Volume2 class="w-3.5 h-3.5" />
                </button>
              </div>
            </div>

            <p class="text-xs text-dota-muted leading-relaxed">{{ item.text }}</p>

            <!-- 推荐行动建议 -->
            <div v-if="item.suggested_action" class="flex items-center space-x-2 text-xs pt-1 border-t border-dota-border/40">
              <span class="text-dota-gold font-bold text-[11px]">行动指南：</span>
              <span class="text-dota-text font-medium">{{ item.suggested_action }}</span>
            </div>

            <!-- AI 旁路深度战术解析 -->
            <div v-if="item.ai_explanation" class="p-2.5 rounded-lg bg-dota-surface border border-dota-border/60 text-xs flex items-start space-x-2">
              <Sparkles class="w-3.5 h-3.5 text-dota-gold shrink-0 mt-0.5" />
              <div>
                <span class="text-dota-goldLight font-bold text-[11px]">AI 战术旁路分析：</span>
                <span class="text-dota-muted leading-relaxed">{{ item.ai_explanation }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { Radio, Trash2, Volume2, Sparkles } from 'lucide-vue-next';
import { useAppStore } from '../store';
import { speechService } from '../services/speechService';
import GsiSimulator from '../components/coach/GsiSimulator.vue';
import { CoachAdvice } from '../types';

const { state, clearAdvices } = useAppStore();

const replayVoice = (item: CoachAdvice) => {
  speechService.speak(
    `${item.title}。${item.text}`,
    {
      mode: 2, // 主动点击重播强制发声
      rate: state.settings.voice_speed,
      volume: state.settings.voice_volume,
      voiceName: state.settings.edge_voice_name || state.settings.voice_name,
    }
  );
};
</script>
