<template>
  <div
    ref="overlayRef"
    :style="{
      left: position.x !== null ? `${position.x}px` : undefined,
      right: position.x === null ? '24px' : undefined,
      top: `${position.y}px`,
      opacity: opacity / 100,
    }"
    class="fixed z-50 w-84 bg-[#0B0E14]/95 text-dota-text border border-dota-gold/40 rounded-xl shadow-2xl flex flex-col overflow-hidden select-none font-sans backdrop-blur-md transition-shadow"
  >
    <!-- 悬浮窗顶栏 (支持自由拖拽移动) -->
    <div
      @mousedown="startDrag"
      class="h-9 bg-dota-surface/90 border-b border-dota-border px-3 flex items-center justify-between cursor-move shrink-0"
    >
      <div class="flex items-center space-x-2">
        <div
          :class="[
            'w-2.5 h-2.5 rounded-full',
            state.gsiConnected ? 'bg-dota-green animate-pulse' : 'bg-dota-subtle'
          ]"
        />
        <span class="text-xs font-bold text-dota-goldLight">Aegis 局内经济天平</span>
        <span class="text-[10px] px-1 rounded bg-dota-panel text-dota-cyan border border-dota-border">置顶</span>
      </div>

      <div class="flex items-center space-x-1.5" @mousedown.stop>
        <!-- 透明度滑块 -->
        <div class="flex items-center space-x-1 text-[10px] text-dota-subtle mr-1">
          <span>透明</span>
          <input
            type="range"
            min="40"
            max="100"
            v-model="opacity"
            class="w-12 h-1 accent-dota-gold cursor-pointer"
          />
        </div>

        <!-- 最小化折叠 -->
        <button
          @click="isCollapsed = !isCollapsed"
          title="展开/收起"
          class="p-1 hover:bg-dota-hover rounded text-dota-muted hover:text-dota-text cursor-pointer transition-colors"
        >
          <component :is="isCollapsed ? Maximize2 : Minimize2" class="w-3.5 h-3.5" />
        </button>

        <!-- 关闭悬浮窗 -->
        <button
          @click="setIsOverlayOpen(false)"
          title="关闭悬浮窗"
          class="p-1 hover:bg-dota-red/80 hover:text-white rounded text-dota-muted cursor-pointer transition-colors"
        >
          <X class="w-3.5 h-3.5" />
        </button>
      </div>
    </div>

    <!-- 展开内容区 (严格不包含任何 mock 假数据) -->
    <div v-if="!isCollapsed" class="p-3 space-y-2.5 text-xs">
      <div v-if="!state.gsiConnected" class="p-4 text-center space-y-2 rounded-lg bg-dota-panel/60 border border-dashed border-dota-border">
        <div class="text-dota-gold font-bold text-xs">等待 Dota 2 客户端对局接入</div>
        <p class="text-[11px] text-dota-subtle leading-relaxed">
          请启动 Dota 2 并进入对局，系统将通过 4444 端口实时解析敌我双方买活状态与经济差距。
        </p>
      </div>

      <div v-else class="space-y-2">
        <div class="flex items-center justify-between text-[11px] font-mono border-b border-dota-border pb-1.5 text-dota-subtle">
          <span class="text-dota-green">天辉方实时追踪</span>
          <span class="text-dota-red">夜魇方实时追踪</span>
        </div>
        <div class="py-3 text-center text-xs text-dota-muted">
          实时战况监测在线 · 正在持续监听 10Hz GSI 快照流
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
  import { ref, reactive, onMounted, onUnmounted } from 'vue';
  import { Maximize2, Minimize2, X } from 'lucide-vue-next';
  import { useAppStore } from '../store';

  const { state, setIsOverlayOpen } = useAppStore();

  const isCollapsed = ref(false);
  const opacity = ref(90);

  // 悬浮窗位置 (默认右上角)
  const position = reactive<{ x: number | null; y: number }>({
    x: null,
    y: 56,
  });

  let isDragging = false;
  let dragOffset = { x: 0, y: 0 };

  const startDrag = (e: MouseEvent) => {
    isDragging = true;
    const rect = (e.currentTarget as HTMLElement).closest('.fixed')!.getBoundingClientRect();
    dragOffset = {
      x: e.clientX - rect.left,
      y: e.clientY - rect.top,
    };
    position.x = rect.left;
    position.y = rect.top;

    window.addEventListener('mousemove', onDrag);
    window.addEventListener('mouseup', stopDrag);
  };

  const onDrag = (e: MouseEvent) => {
    if (!isDragging) return;
    const maxX = window.innerWidth - 300;
    const maxY = window.innerHeight - 80;
    position.x = Math.max(10, Math.min(maxX, e.clientX - dragOffset.x));
    position.y = Math.max(10, Math.min(maxY, e.clientY - dragOffset.y));
  };

  const stopDrag = () => {
    isDragging = false;
    window.removeEventListener('mousemove', onDrag);
    window.removeEventListener('mouseup', stopDrag);
  };

  onUnmounted(() => {
    window.removeEventListener('mousemove', onDrag);
    window.removeEventListener('mouseup', stopDrag);
  });
</script>
