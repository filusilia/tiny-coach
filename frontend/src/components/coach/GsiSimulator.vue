<template>
  <div class="bg-dota-panel p-4 rounded-xl border border-dota-border space-y-3">
    <div class="flex items-center justify-between border-b border-dota-border pb-2.5">
      <div class="flex items-center space-x-2">
        <PlayCircle class="w-4 h-4 text-dota-gold" />
        <h3 class="text-xs font-bold text-dota-goldLight">GSI 局内突发事件模拟注入</h3>
      </div>
      <span class="text-[10px] text-dota-subtle">点击直接模拟 10Hz 战局裁定触发</span>
    </div>

    <div class="grid grid-cols-2 sm:grid-cols-4 gap-2">
      <button
        v-for="preset in presets"
        :key="preset.id"
        @click="handleRunPreset(preset)"
        class="p-2.5 rounded-lg bg-dota-surface hover:bg-dota-hover border border-dota-border text-left transition-all group flex flex-col justify-between"
      >
        <div class="flex items-center justify-between">
          <component :is="preset.icon" class="w-4 h-4" />
          <span class="text-[10px] px-1.5 py-0.2 rounded bg-dota-panel text-dota-gold font-mono">
            {{ preset.heroName }}
          </span>
        </div>
        <div class="mt-2">
          <div class="text-xs font-bold text-dota-text group-hover:text-dota-goldLight">
            {{ preset.title }}
          </div>
          <div class="text-[10px] text-dota-subtle truncate mt-0.5">
            {{ preset.desc }}
          </div>
        </div>
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { PlayCircle, ShieldAlert, Zap, Skull, Compass } from 'lucide-vue-next';
import { useAppStore } from '../../store';
import { speechService } from '../../services/speechService';
import { CoachAdvice } from '../../types';

interface SimulationPreset {
  id: string;
  title: string;
  desc: string;
  icon: any;
  heroName: string;
  hpPercent: number;
  manaPercent: number;
  advice: Omit<CoachAdvice, 'id' | 'created_at'>;
}

const presets: SimulationPreset[] = [
  {
    id: 'hp-critical',
    title: '生命危急 (25% HP)',
    desc: '哈斯卡陷入敌方包围，生命值告急',
    icon: ShieldAlert,
    heroName: '哈斯卡',
    hpPercent: 25,
    manaPercent: 40,
    advice: {
      rule_id: 'HEALTH_CRITICAL',
      rule: 'HEALTH_CRITICAL',
      title: '生命危急',
      text: '当前生命值仅 25%，处于危险线以下！',
      severity: 'Critical',
      suggested_action: '立即开启黑皇杖或拉开距离，寻找掩体回城。',
      ai_explanation: '根据当前战况，敌方具备爆发斩杀技能，切忌盲目硬拼，先回泉水补满状态。',
    },
  },
  {
    id: 'ult-ready',
    title: '关键大招就绪',
    desc: '主宰终极技能「无敌斩」冷却完毕',
    icon: Zap,
    heroName: '主宰',
    hpPercent: 95,
    manaPercent: 80,
    advice: {
      rule_id: 'ULTIMATE_READY',
      rule: 'ULTIMATE_READY',
      title: '终极技能就绪',
      text: '终极技能「无敌斩」已转好，具备单杀能力。',
      severity: 'Info',
      suggested_action: '观察小地图落单敌方核心，配合跳刀主动出击。',
      ai_explanation: '此时敌方辅助无微光与吹风保命装，抓落单可形成多打少推进局面。',
    },
  },
  {
    id: 'rune-spawn',
    title: '战略神符刷新 (6分钟)',
    desc: '河道双极神符将在 15 秒后刷新',
    icon: Compass,
    heroName: '影魔',
    hpPercent: 85,
    manaPercent: 70,
    advice: {
      rule_id: 'POWER_RUNE_SPAWN',
      rule: 'POWER_RUNE_SPAWN',
      title: '神符争夺',
      text: '河道强化神符即将刷新，注意控符时间。',
      severity: 'Warning',
      suggested_action: '推完中路线，呼叫边路辅助协助控制上下河道神符。',
      ai_explanation: '双倍伤害或急速神符对前期节奏有决定性影响，必须全力争取。',
    },
  },
  {
    id: 'buyback-window',
    title: '阵亡与买活裁定',
    desc: '团战阵亡，防守高地买活窗口触发',
    icon: Skull,
    heroName: '斯温',
    hpPercent: 0,
    manaPercent: 0,
    advice: {
      rule_id: 'BUYBACK_CRITICAL',
      rule: 'BUYBACK_CRITICAL',
      title: '买活守高',
      text: '自身阵亡，买活资金充足，高地正遭受猛烈攻击！',
      severity: 'Critical',
      suggested_action: '等待敌方技能交完进塔后立即买活，开大招入场反打。',
      ai_explanation: '基地核心建筑面临威胁，且敌方关键团控技能处于 CD 状态，为黄金买活反打时机。',
    },
  },
];

const { state, addAdvice, setGsiConnected } = useAppStore();

const handleRunPreset = (preset: SimulationPreset) => {
  setGsiConnected(true);

  const adviceItem: CoachAdvice = {
    ...preset.advice,
    id: `sim-${Date.now()}-${Math.random().toString(36).substr(2, 4)}`,
    created_at: new Date().toLocaleTimeString('zh-CN', { hour12: false }),
  };

  addAdvice(adviceItem);

  if (state.settings.voice_broadcast_mode === 2) {
    speechService.speak(
      `${adviceItem.title}。${adviceItem.text}`,
      {
        mode: state.settings.voice_broadcast_mode,
        rate: state.settings.voice_speed,
        volume: state.settings.voice_volume,
        voiceName: state.settings.edge_voice_name || state.settings.voice_name,
      }
    );
  }
};
</script>
