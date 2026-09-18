<template>
  <div class="flex flex-col h-screen w-screen overflow-hidden bg-dota-bg">
    <!-- 顶部标题栏 -->
    <TitleBar />

    <!-- 下方主容器 (侧边栏 + 内容视口) -->
    <div class="flex flex-1 overflow-hidden">
      <Sidebar />

      <main class="flex-1 flex overflow-hidden">
        <HeroesPage v-if="state.currentTab === 'heroes'" />
        <ItemsPage v-else-if="state.currentTab === 'items'" />
        <CoachPage v-else-if="state.currentTab === 'coach'" />
        <AnalysisPage v-else-if="state.currentTab === 'analysis'" />
        <SettingsPage v-else-if="state.currentTab === 'settings'" />
      </main>
    </div>

    <!-- 局内经济悬浮窗 -->
    <OverlayWindow v-if="state.isOverlayOpen" />
  </div>
</template>

<script setup lang="ts">
import TitleBar from './components/layout/TitleBar.vue';
import Sidebar from './components/layout/Sidebar.vue';
import HeroesPage from './pages/HeroesPage.vue';
import ItemsPage from './pages/ItemsPage.vue';
import CoachPage from './pages/CoachPage.vue';
import AnalysisPage from './pages/AnalysisPage.vue';
import SettingsPage from './pages/SettingsPage.vue';
import OverlayWindow from './windows/OverlayWindow.vue';
import { useAppStore } from './store';

const { state } = useAppStore();
</script>
