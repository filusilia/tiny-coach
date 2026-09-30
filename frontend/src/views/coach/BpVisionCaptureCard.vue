<script setup lang="ts">
/**
 * 赛前 BP 视觉截图与端侧 ONNX 英雄槽位识别组件 (src/views/coach/BpVisionCaptureCard.vue)
 * 职责：
 * 1. 呈现 F10 全局热键系统级锁定监听状态
 * 2. 提供手动触发截屏与本地图片上传测试双通道
 * 3. 驱动端侧 ONNX Runtime Web 推理并呈现天辉与夜魇 10 个英雄槽位识别成果
 * 4. 毫秒级展示截屏耗时、Letterbox 预处理耗时与神经网络推理耗时
 * 5. 严格闭环生命周期：组件卸载时注销 Wails 事件监听，防句柄泄漏
 */

import {
  Camera,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Cpu,
  Eye,
  FileImage,
  Info,
  Keyboard,
  RefreshCw,
  Sparkles,
  Upload,
} from '@lucide/vue'
import { onMounted, onUnmounted, ref } from 'vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { useVisionStore } from '@/stores'
import { getHeroCdnUrl, getHeroIconUrl } from '@/utils/assetUrl'

defineOptions({
  name: 'BpVisionCaptureCard',
})

const visionStore = useVisionStore()
const isExpanded = ref(true)
const fileInputRef = ref<HTMLInputElement | null>(null)

let unlistenCapture: (() => void) | null = null

onMounted(async () => {
  await visionStore.checkHotkeyStatus()

  // 监听 Go 后端 F10 或上层触发的全局截屏事件 (铁律 5: Go -> 前端走 Event)
  const win = window as any
  if (win.wails?.Events?.On) {
    unlistenCapture = win.wails.Events.On('vision:capture-completed', (eventPayload: unknown) => {
      const payload = (eventPayload as { data?: any })?.data ?? eventPayload
      if (payload) {
        visionStore.handleCaptureEvent(payload)
      }
    })
  }
})

onUnmounted(() => {
  if (unlistenCapture) {
    unlistenCapture()
    unlistenCapture = null
  }
})

async function onManualCapture() {
  await visionStore.triggerCapture()
}

function onTriggerFileInput() {
  if (fileInputRef.value) {
    fileInputRef.value.value = ''
    fileInputRef.value.click()
  }
}

async function onFileSelected(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (file) {
    await visionStore.testWithLocalImageFile(file)
  }
}

async function onLoadSampleImage() {
  try {
    const response = await fetch('/test_bp.png')
    const blob = await response.blob()
    const file = new File([blob], 'test_bp.png', { type: 'image/png' })
    await visionStore.testWithLocalImageFile(file)
  } catch (e: any) {
    console.error('加载内置样例失败:', e)
  }
}

function getHeroAvatar(shortName: string): string {
  return getHeroIconUrl(shortName)
}
</script>

<template>
  <div class="bp-vision-card border border-slate-700/60 bg-slate-900/85 backdrop-blur-md rounded-xl p-4 shadow-xl transition-all space-y-3">
    <!-- 1. 头部栏：标题、快捷键徽章与操作控制组 -->
    <div class="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
      <div class="flex items-center gap-2.5">
        <div class="w-8 h-8 rounded-lg bg-cyan-500/10 border border-cyan-500/30 flex items-center justify-center text-cyan-400 shrink-0">
          <Camera class="w-4 h-4" />
        </div>
        <div>
          <div class="flex items-center gap-2 flex-wrap">
            <h3 class="text-sm font-bold text-slate-100 tracking-wide">
              BP 视觉感知与英雄识别
            </h3>
            <span class="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[11px] font-mono font-semibold bg-cyan-950/60 text-cyan-300 border border-cyan-700/40">
              <Keyboard class="w-3 h-3" />
              F10
            </span>
            <span
              v-if="visionStore.isHotkeyActive"
              class="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] bg-emerald-950/60 text-emerald-300 border border-emerald-700/40"
              title="全局 F10 热键已在后台 OS 线程锁定监听，可在游戏内盲按触发"
            >
              <span class="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse"></span>
              全局热键就绪
            </span>
          </div>
          <p class="text-xs text-slate-400 mt-0.5">
            突破 GSI 赛前选人盲区 · 顶部 18% ROI 极速穿透 · 端侧 ONNX 毫秒级识别
          </p>
        </div>
      </div>

      <!-- 控制按钮组 -->
      <div class="flex items-center gap-1.5 flex-wrap self-end sm:self-auto">
        <!-- 隐藏的文件选择器 -->
        <input
          ref="fileInputRef"
          type="file"
          accept="image/*"
          class="hidden"
          @change="onFileSelected"
        />

        <!-- 上传测试图 -->
        <button
          type="button"
          class="inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-medium rounded-lg transition-colors border border-slate-700 bg-slate-800/90 hover:bg-slate-700 text-slate-200"
          title="上传本地全屏或顶部截屏图片测试端侧 ONNX 推理"
          :disabled="visionStore.isCapturing || visionStore.isDetecting"
          @click="onTriggerFileInput"
        >
          <Upload class="w-3.5 h-3.5 text-cyan-400" />
          <span>上传测试图</span>
        </button>

        <!-- 加载内置样例测试图 -->
        <button
          type="button"
          class="inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-medium rounded-lg transition-colors border border-slate-700 bg-slate-800/90 hover:bg-slate-700 text-slate-200"
          title="一键加载内置 BP 实战测试图进行推理闭环"
          :disabled="visionStore.isCapturing || visionStore.isDetecting"
          @click="onLoadSampleImage"
        >
          <FileImage class="w-3.5 h-3.5 text-amber-400" />
          <span>实战样例</span>
        </button>

        <!-- 手动抓取按钮 -->
        <button
          type="button"
          class="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-lg transition-all duration-150 shadow-sm disabled:opacity-50 disabled:cursor-not-allowed bg-gradient-to-r from-cyan-600 to-blue-600 hover:from-cyan-500 hover:to-blue-500 text-white border border-cyan-400/30 active:scale-95"
          :disabled="visionStore.isCapturing || visionStore.isDetecting"
          @click="onManualCapture"
        >
          <RefreshCw
            class="w-3.5 h-3.5"
            :class="{ 'animate-spin': visionStore.isCapturing || visionStore.isDetecting }"
          />
          <span>{{ visionStore.isCapturing ? '正在抓取...' : visionStore.isDetecting ? 'ONNX推理中...' : '手动抓取顶部画面' }}</span>
        </button>

        <!-- 折叠/展开 -->
        <button
          type="button"
          class="p-1.5 text-slate-400 hover:text-slate-200 rounded-lg hover:bg-slate-800/60 transition-colors"
          :title="isExpanded ? '收起预览' : '展开预览'"
          @click="isExpanded = !isExpanded"
        >
          <ChevronUp v-if="isExpanded" class="w-4 h-4" />
          <ChevronDown v-else class="w-4 h-4" />
        </button>
      </div>
    </div>

    <!-- 异常状态提示 -->
    <div
      v-if="visionStore.errorMessage || visionStore.detectionError"
      class="px-3 py-2 rounded-lg bg-amber-950/40 border border-amber-600/40 text-amber-200 text-xs flex items-center gap-2"
    >
      <Info class="w-4 h-4 text-amber-400 shrink-0" />
      <span>{{ visionStore.errorMessage || visionStore.detectionError }}</span>
    </div>

    <!-- 展开区域：指标流、10 英雄槽位识别展示与图像预览 -->
    <div v-show="isExpanded" class="pt-2 border-t border-slate-800/80 space-y-3">
      <!-- 2. 毫秒级性能指标条 (物理分辨率、截屏耗时、ONNX推理耗时) -->
      <div
        v-if="visionStore.lastCapture?.success || visionStore.lastDetection"
        class="flex flex-wrap items-center justify-between gap-2 text-[11px] text-slate-400 bg-slate-950/50 rounded-lg px-3 py-1.5 border border-slate-800/60 font-mono"
      >
        <div class="flex items-center gap-3 flex-wrap">
          <span v-if="visionStore.lastCapture?.screenWidth">
            物理屏幕: <strong class="text-slate-200">{{ visionStore.lastCapture.screenWidth }}×{{ visionStore.lastCapture.screenHeight }}</strong>
          </span>
          <span v-if="visionStore.lastCapture?.cropWidth" class="text-slate-600">|</span>
          <span v-if="visionStore.lastCapture?.cropWidth">
            ROI 裁剪: <strong class="text-cyan-300">{{ visionStore.lastCapture.cropWidth }}×{{ visionStore.lastCapture.cropHeight }}</strong>
          </span>
          <span v-if="visionStore.lastCapture?.durationMs !== undefined" class="text-slate-600">|</span>
          <span v-if="visionStore.lastCapture?.durationMs !== undefined">
            GDI 截屏: <strong class="text-emerald-400">{{ visionStore.lastCapture.durationMs }}ms</strong>
          </span>
        </div>

        <div class="flex items-center gap-3">
          <span v-if="visionStore.isDetecting" class="inline-flex items-center gap-1 text-cyan-300 animate-pulse">
            <Cpu class="w-3 h-3 animate-spin" />
            ONNX 推理中...
          </span>
          <template v-else-if="visionStore.lastDetection">
            <span>预处理: <strong class="text-slate-300">{{ visionStore.lastDetection.preprocessDurationMs }}ms</strong></span>
            <span class="text-slate-600">|</span>
            <span class="text-emerald-300">
              模型推理: <strong class="text-emerald-400">{{ visionStore.lastDetection.inferenceDurationMs }}ms</strong>
            </span>
            <span class="text-slate-600">|</span>
            <span class="inline-flex items-center gap-1 text-amber-300">
              <CheckCircle2 class="w-3 h-3 text-emerald-400" />
              总耗时 {{ visionStore.lastDetection.totalDurationMs }}ms
            </span>
          </template>
        </div>
      </div>

      <!-- 3. 天辉与夜魇 10 英雄槽位识别成果展台 -->
      <div v-if="visionStore.lastDetection" class="space-y-2">
        <div class="flex items-center justify-between text-xs font-semibold px-1">
          <span class="text-emerald-400 flex items-center gap-1">
            <Sparkles class="w-3.5 h-3.5" />
            天辉阵营槽位 (Radiant 1-5)
          </span>
          <span class="text-rose-400 flex items-center gap-1">
            <Sparkles class="w-3.5 h-3.5" />
            夜魇阵营槽位 (Dire 1-5)
          </span>
        </div>

        <!-- 10 槽位网格 (5 天辉 + 5 夜魇) -->
        <div class="grid grid-cols-2 gap-3">
          <!-- 天辉 5 槽位 -->
          <div class="grid grid-cols-5 gap-1.5 bg-emerald-950/20 p-2 rounded-xl border border-emerald-900/40">
            <div
              v-for="(slot, idx) in visionStore.lastDetection.radiant"
              :key="'rad-' + idx"
              class="slot-card flex flex-col items-center justify-center p-1 rounded-lg border text-center transition-all min-h-[72px]"
              :class="[
                slot
                  ? 'bg-emerald-950/60 border-emerald-600/50 shadow-sm'
                  : 'bg-slate-950/40 border-slate-800/60 border-dashed opacity-60',
              ]"
            >
              <div v-if="slot" class="w-full flex flex-col items-center gap-1">
                <div class="relative w-10 h-7 rounded overflow-hidden border border-emerald-500/40 bg-black">
                  <SmartImage
                    :src="getHeroAvatar(slot.shortName)"
                    :fallback-src="getHeroCdnUrl(slot.shortName)"
                    :alt="slot.heroName"
                    class="w-full h-full object-cover"
                  />
                </div>
                <div class="text-[11px] font-bold text-slate-100 truncate w-full" :title="slot.heroName">
                  {{ slot.heroName }}
                </div>
                <span class="text-[9px] font-mono text-emerald-300 bg-emerald-900/60 px-1 rounded">
                  {{ Math.round(slot.confidence * 100) }}%
                </span>
              </div>
              <div v-else class="text-[10px] text-slate-500 flex flex-col items-center">
                <span class="font-mono text-slate-600">#{{ idx + 1 }}</span>
                <span>待选</span>
              </div>
            </div>
          </div>

          <!-- 夜魇 5 槽位 -->
          <div class="grid grid-cols-5 gap-1.5 bg-rose-950/20 p-2 rounded-xl border border-rose-900/40">
            <div
              v-for="(slot, idx) in visionStore.lastDetection.dire"
              :key="'dire-' + idx"
              class="slot-card flex flex-col items-center justify-center p-1 rounded-lg border text-center transition-all min-h-[72px]"
              :class="[
                slot
                  ? 'bg-rose-950/60 border-rose-600/50 shadow-sm'
                  : 'bg-slate-950/40 border-slate-800/60 border-dashed opacity-60',
              ]"
            >
              <div v-if="slot" class="w-full flex flex-col items-center gap-1">
                <div class="relative w-10 h-7 rounded overflow-hidden border border-rose-500/40 bg-black">
                  <SmartImage
                    :src="getHeroAvatar(slot.shortName)"
                    :fallback-src="getHeroCdnUrl(slot.shortName)"
                    :alt="slot.heroName"
                    class="w-full h-full object-cover"
                  />
                </div>
                <div class="text-[11px] font-bold text-slate-100 truncate w-full" :title="slot.heroName">
                  {{ slot.heroName }}
                </div>
                <span class="text-[9px] font-mono text-rose-300 bg-rose-900/60 px-1 rounded">
                  {{ Math.round(slot.confidence * 100) }}%
                </span>
              </div>
              <div v-else class="text-[10px] text-slate-500 flex flex-col items-center">
                <span class="font-mono text-slate-600">#{{ idx + 1 }}</span>
                <span>待选</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 4. 图像预览容器 (带视口悬浮标签) -->
      <div
        v-if="visionStore.lastCapture?.success && visionStore.lastCapture.imageBase64"
        class="relative rounded-lg overflow-hidden border border-slate-700/80 bg-black/60 group"
      >
        <img
          :src="visionStore.lastCapture.imageBase64"
          alt="DOTA2 顶部英雄槽位截图"
          class="w-full h-auto max-h-40 object-contain object-top block"
        />
        <div class="absolute bottom-2 right-2 px-2 py-1 rounded bg-black/80 backdrop-blur-sm border border-slate-700 text-[10px] text-slate-300 pointer-events-none opacity-80 group-hover:opacity-100 transition-opacity flex items-center gap-1">
          <Eye class="w-3 h-3 text-cyan-400" />
          <span>顶部 18% 英雄框捕获视图</span>
        </div>
      </div>

      <!-- 无截图时的占位引导 -->
      <div
        v-else-if="!visionStore.errorMessage && !visionStore.detectionError"
        class="py-6 px-4 rounded-lg border border-dashed border-slate-800 text-center text-xs text-slate-500 bg-slate-950/20"
      >
        <p>尚未捕获任何画面。在游戏内按 <kbd class="px-1.5 py-0.5 rounded bg-slate-800 text-slate-300 font-mono border border-slate-700">F10</kbd>，或点击上方【实战样例】/【手动抓取顶部画面】体验端侧识别。</p>
        <p class="text-[11px] text-slate-600 mt-1">
          💡 提示：建议 DOTA 2 视频选项使用【无边框窗口模式】，以确保跨层像素精准提取。
        </p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.bp-vision-card {
  box-shadow: 0 4px 20px -2px rgba(0, 0, 0, 0.4);
}
.slot-card {
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}
.slot-card:hover {
  transform: translateY(-1px);
}
</style>
