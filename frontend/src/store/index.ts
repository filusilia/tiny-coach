import { reactive } from 'vue';
import { AppSettings, CoachAdvice } from '../types';

export interface AppState {
  currentTab: 'heroes' | 'items' | 'coach' | 'analysis' | 'settings';
  searchQuery: string;
  selectedHeroId: number | null;
  selectedItemId: number | null;
  gsiConnected: boolean;
  advices: CoachAdvice[];
  settings: AppSettings;
  isOverlayOpen: boolean;
}

const state = reactive<AppState>({
  currentTab: 'heroes',
  searchQuery: '',
  selectedHeroId: 1, // 默认选中 1 敌法师
  selectedItemId: null,
  gsiConnected: false,
  advices: [],
  settings: {
    gsi_port: 4444,
    auto_start_gsi: true,
    smart_coach_enabled: true,
    voice_broadcast_mode: 2, // 2: 播报-语音
    voice_volume: 100,
    voice_speed: 1.0,
    voice_name: 'zh-CN-YunxiNeural',
    edge_voice_name: 'zh-CN-YunxiNeural',
    voice_lexicon_enabled: true,
    voice_ssml_enabled: true,
    auto_show_overlay: false,
  },
  isOverlayOpen: false,
});

export const useAppStore = () => {
  const setCurrentTab = (tab: AppState['currentTab']) => {
    state.currentTab = tab;
  };
  const setSearchQuery = (query: string) => {
    state.searchQuery = query;
  };
  const setSelectedHeroId = (id: number | null) => {
    state.selectedHeroId = id;
  };
  const setSelectedItemId = (id: number | null) => {
    state.selectedItemId = id;
  };
  const setGsiConnected = (connected: boolean) => {
    state.gsiConnected = connected;
  };
  const addAdvice = (advice: CoachAdvice) => {
    state.advices = [advice, ...state.advices].slice(0, 50);
  };
  const clearAdvices = () => {
    state.advices = [];
  };
  const updateSettings = (partial: Partial<AppSettings>) => {
    Object.assign(state.settings, partial);
  };
  const setIsOverlayOpen = (open: boolean) => {
    state.isOverlayOpen = open;
  };

  return {
    state,
    setCurrentTab,
    setSearchQuery,
    setSelectedHeroId,
    setSelectedItemId,
    setGsiConnected,
    addAdvice,
    clearAdvices,
    updateSettings,
    setIsOverlayOpen,
  };
};
