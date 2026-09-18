<template>
  <div class="flex-1 overflow-y-auto p-6 space-y-6 bg-dota-bg select-none">
    <div class="border-b border-dota-border pb-3">
      <h1 class="text-lg font-bold text-dota-goldLight">应用设置与游戏协同</h1>
      <p class="text-xs text-dota-muted mt-0.5">配置局内 GSI 监听端口、Edge-TTS 战术播报及悬浮窗行为</p>
    </div>

    <!-- 1. 语音播报引擎 -->
    <div class="p-5 rounded-xl bg-dota-panel border border-dota-border space-y-4">
      <div class="flex items-center space-x-2 text-sm font-bold text-dota-gold">
        <Volume2 class="w-4 h-4" />
        <span>战术语音播报 (TTS)</span>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
        <div>
          <label class="block text-dota-muted mb-1.5">播报模式</label>
          <div class="flex space-x-2">
            <button
              v-for="mode in voiceModes"
              :key="mode.id"
              @click="updateSettings({ voice_broadcast_mode: mode.id })"
              :class="[
                'flex-1 py-1.5 rounded-lg border font-medium transition-all',
                state.settings.voice_broadcast_mode === mode.id
                  ? 'bg-dota-surface text-dota-gold border-dota-gold/60 shadow-sm'
                  : 'bg-dota-surface/40 text-dota-muted border-dota-border hover:text-dota-text'
              ]"
            >
              {{ mode.label }}
            </button>
          </div>
        </div>

        <div>
          <label class="block text-dota-muted mb-1.5">语音角色 (Edge-TTS 神经拟真)</label>
          <select
            :value="state.settings.edge_voice_name"
            @change="(e) => updateSettings({ edge_voice_name: (e.target as HTMLSelectElement).value })"
            class="w-full h-8 bg-dota-surface border border-dota-border rounded-lg px-3 text-xs text-dota-text outline-none focus:border-dota-gold"
          >
            <option value="zh-CN-YunxiNeural">云希 (沉稳电竞解说男声)</option>
            <option value="zh-CN-XiaoxiaoNeural">晓晓 (清晰战术指挥女声)</option>
            <option value="zh-CN-YunjianNeural">云健 (激昂团战解说男声)</option>
          </select>
        </div>

        <!-- 音量滑块 -->
        <div>
          <div class="flex justify-between text-dota-muted mb-1.5">
            <span>播报音量</span>
            <span class="font-mono text-dota-gold">{{ state.settings.voice_volume }}%</span>
          </div>
          <input
            type="range"
            min="0"
            max="100"
            :value="state.settings.voice_volume"
            @input="(e) => updateSettings({ voice_volume: Number((e.target as HTMLInputElement).value) })"
            class="w-full h-1.5 bg-dota-surface rounded-lg appearance-none cursor-pointer accent-dota-gold"
          />
        </div>

        <!-- 语速滑块 -->
        <div>
          <div class="flex justify-between text-dota-muted mb-1.5">
            <span>播报语速</span>
            <span class="font-mono text-dota-gold">{{ state.settings.voice_speed.toFixed(1) }}x</span>
          </div>
          <input
            type="range"
            min="0.5"
            max="2.0"
            step="0.1"
            :value="state.settings.voice_speed"
            @input="(e) => updateSettings({ voice_speed: Number((e.target as HTMLInputElement).value) })"
            class="w-full h-1.5 bg-dota-surface rounded-lg appearance-none cursor-pointer accent-dota-gold"
          />
        </div>
      </div>

      <div class="pt-2 flex justify-end">
        <button
          @click="testVoice"
          class="flex items-center space-x-1.5 px-3 py-1.5 rounded-lg bg-dota-surface hover:bg-dota-hover border border-dota-border text-dota-gold text-xs transition-colors"
        >
          <Play class="w-3.5 h-3.5" />
          <span>试听战术播报</span>
        </button>
      </div>
    </div>

    <!-- 2. Dota 2 GSI 局内高速监听服务 -->
    <div class="p-5 rounded-xl bg-dota-panel border border-dota-border space-y-4">
      <div class="flex items-center space-x-2 text-sm font-bold text-dota-gold">
        <Sliders class="w-4 h-4" />
        <span>Dota 2 GSI 局内通信配置</span>
      </div>

      <div class="space-y-3 text-xs">
        <div class="flex items-center justify-between">
          <div>
            <div class="font-medium text-dota-text">本机监听端口</div>
            <div class="text-[11px] text-dota-subtle">默认 4444 端口，接收 Dota 2 客户端 10Hz 战局状态快照</div>
          </div>
          <input
            type="number"
            :value="state.settings.gsi_port"
            @input="(e) => updateSettings({ gsi_port: Number((e.target as HTMLInputElement).value) })"
            class="w-24 h-7 bg-dota-surface border border-dota-border rounded px-2 font-mono text-center text-dota-gold outline-none focus:border-dota-gold"
          />
        </div>

        <div class="p-3 rounded-lg bg-dota-surface/60 border border-dota-border text-[11px] text-dota-muted space-y-1.5">
          <div class="flex items-center space-x-1.5 text-dota-cyan font-bold">
            <Info class="w-3.5 h-3.5" />
            <span>配置说明与游戏联动向导：</span>
          </div>
          <p>
            将配置文件放置于 Dota 2 安装目录下的
            <code class="text-dota-gold bg-dota-panel px-1 py-0.5 rounded font-mono">
              dota 2 beta/game/dota/cfg/gamestate_integration/
            </code>
            目录中，文件名命名为 <code class="text-dota-gold">gamestate_integration_aegis.cfg</code>。
          </p>
          <div class="pt-2 flex items-center space-x-2">
            <button
              @click="downloadConfigFile"
              class="flex items-center space-x-1 px-2.5 py-1 rounded bg-dota-panel hover:bg-dota-hover border border-dota-border text-dota-text transition-colors"
            >
              <Download class="w-3.5 h-3.5 text-dota-gold" />
              <span>下载配置文件</span>
            </button>
            <button
              @click="copyConfig"
              class="flex items-center space-x-1 px-2.5 py-1 rounded bg-dota-panel hover:bg-dota-hover border border-dota-border text-dota-text transition-colors"
            >
              <component :is="copied ? Check : Copy" class="w-3.5 h-3.5 text-dota-cyan" />
              <span>{{ copied ? '已复制到剪贴板' : '复制配置内容' }}</span>
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- 3. 数据缓存管理 -->
    <div class="p-5 rounded-xl bg-dota-panel border border-dota-border space-y-3">
      <div class="flex items-center space-x-2 text-sm font-bold text-dota-gold">
        <Database class="w-4 h-4" />
        <span>本地缓存与存储管理</span>
      </div>
      <div class="flex items-center justify-between text-xs">
        <div>
          <div class="text-dota-text font-medium">离线数据与语音本地缓存</div>
          <div class="text-[11px] text-dota-subtle">内置 127 英雄常数、技能模板及 Edge-TTS 缓存</div>
        </div>
        <button
          @click="clearCache"
          class="flex items-center space-x-1.5 px-3 py-1.5 rounded-lg bg-dota-surface hover:bg-dota-hover border border-dota-border text-dota-muted hover:text-dota-red transition-colors"
        >
          <Trash2 class="w-3.5 h-3.5" />
          <span>清除本地缓存</span>
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { Volume2, Sliders, Database, Info, Play, Download, Copy, Check, Trash2 } from 'lucide-vue-next';
import { VoiceMode } from '../types';
import { useAppStore } from '../store';
import { speechService } from '../services/speechService';
import { downloadGsiConfigFile, copyGsiConfigToClipboard } from '../services/gsiConfig';

const { state, updateSettings } = useAppStore();
const copied = ref(false);

const voiceModes: { id: VoiceMode; label: string }[] = [
  { id: 0, label: '静音' },
  { id: 1, label: '仅屏幕通报' },
  { id: 2, label: '语音+屏幕' },
];

const testVoice = () => {
  speechService.speak(
    '神盾教练语音播报就绪。当前金币达到买活阈值，注意留买活守高。',
    {
      mode: 2, // 试听强制发声
      rate: state.settings.voice_speed,
      volume: state.settings.voice_volume,
      voiceName: state.settings.edge_voice_name,
    }
  );
};

const downloadConfigFile = () => {
  downloadGsiConfigFile(state.settings.gsi_port);
};

const copyConfig = async () => {
  await copyGsiConfigToClipboard(state.settings.gsi_port);
  copied.value = true;
  setTimeout(() => {
    copied.value = false;
  }, 2000);
};

const clearCache = () => {
  speechService.clearCache();
  alert('本地语音与数据缓存已清理完毕！');
};
</script>
