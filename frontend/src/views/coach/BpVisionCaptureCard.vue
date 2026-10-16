<script setup lang="ts">
/**
 * 赛前 BP 视觉截图与端侧 ONNX 英雄槽位识别组件 (src/views/coach/BpVisionCaptureCard.vue)
 * 职责：
 * 1. 呈现 F10 全局热键系统级锁定监听状态与 600ms 防抖保护
 * 2. 智能阵营视角判定 (天辉/夜魇动态归属，支持一键切换与 GSI 自动对齐)
 * 3. 分轮次 BP 识别兼容与槽位手动微调 (增/删/修单个英雄)
 * 4. 端到端自动触发 OpenDota 大数据对抗克制与针对性出装建议展台
 * 5. 置顶悬浮窗实时联动与生命周期严密闭环
 */

import {
  ArrowLeftRight,
  Camera,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Cpu,
  Eye,
  Info,
  Keyboard,
  Plus,
  RefreshCw,
  Search,
  ShieldAlert,
  Sparkles,
  Swords,
  Trash2,
  X,
} from '@lucide/vue'
import { computed, onMounted, onUnmounted, ref } from 'vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { HERO_CHINESE_NAMES, HERO_SHORT_NAMES } from '@/data/heroNames'
import { useVisionStore } from '@/stores'
import { getHeroCdnUrl, getHeroIconUrl, getItemIconUrl } from '@/utils/assetUrl'

defineOptions({
  name: 'BpVisionCaptureCard',
})

const visionStore = useVisionStore()
const isExpanded = ref(true)

// 槽位手动微调弹窗状态
const showHeroPicker = ref(false)
const pickerSide = ref<'radiant' | 'dire'>('radiant')
const pickerSlotIdx = ref(0)
const pickerSearchQuery = ref('')

let unlistenCapture: (() => void) | null = null

// 全量英雄列表 (供手动纠错微调使用)
const allHeroOptions = computed(() => {
  const query = pickerSearchQuery.value.trim().toLowerCase()
  const list: { id: number, name: string, shortName: string }[] = []

  for (const [idStr, name] of Object.entries(HERO_CHINESE_NAMES)) {
    const id = Number(idStr)
    const shortName = HERO_SHORT_NAMES[id] || ''
    if (!query || name.toLowerCase().includes(query) || shortName.toLowerCase().includes(query)) {
      list.push({ id, name, shortName })
    }
  }
  return list.slice(0, 30) // 展示匹配前 30 个
})

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

function getHeroAvatar(shortName: string): string {
  return getHeroIconUrl(shortName)
}

function getItemIcon(itemKey: string): string {
  return getItemIconUrl(itemKey)
}

// 打开槽位微调选择器
function openSlotPicker(side: 'radiant' | 'dire', slotIdx: number) {
  pickerSide.value = side
  pickerSlotIdx.value = slotIdx
  pickerSearchQuery.value = ''
  showHeroPicker.value = true
}

// 选择英雄
async function selectHeroForSlot(heroId: number | null) {
  showHeroPicker.value = false
  await visionStore.updateSlotHero(pickerSide.value, pickerSlotIdx.value, heroId)
}
</script>

<template>
  <div class="bp-vision-card border border-slate-700/60 bg-slate-900/85 backdrop-blur-md rounded-xl p-4 shadow-xl transition-all space-y-3 relative">
    <!-- 1. 头部栏：标题、快捷键徽章与操作控制组 -->
    <div class="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
      <div class="flex items-center gap-2.5">
        <div class="w-8 h-8 rounded-lg bg-cyan-500/10 border border-cyan-500/30 flex items-center justify-center text-cyan-400 shrink-0">
          <Camera class="w-4 h-4" />
        </div>
        <div>
          <div class="flex items-center gap-2 flex-wrap">
            <h3 class="text-sm font-bold text-slate-100 tracking-wide">
              对战识别
            </h3>
            <span class="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-xs font-mono font-semibold bg-cyan-950/60 text-cyan-300 border border-cyan-700/40">
              <Keyboard class="w-3.5 h-3.5" />
              F10
            </span>
            <span
              v-if="visionStore.isHotkeyActive"
              class="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs bg-emerald-950/60 text-emerald-300 border border-emerald-700/40"
              title="全局 F10 热键已在后台 OS 线程锁定监听，可在游戏内盲按触发 (带 600ms 连按防抖)"
            >
              <span class="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span>
              全局热键就绪
            </span>
          </div>
        </div>
      </div>

      <!-- 控制按钮组 -->
      <div class="flex items-center gap-1.5 flex-wrap self-end sm:self-auto">
        <!-- 阵营切换按钮 -->
        <button
          type="button"
          class="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-semibold rounded-lg transition-colors border shadow-sm"
          :class="[
            visionStore.myFaction === 'radiant'
              ? 'border-emerald-700/70 bg-emerald-950/80 text-emerald-300 hover:bg-emerald-900/80'
              : 'border-rose-700/70 bg-rose-950/80 text-rose-300 hover:bg-rose-900/80',
          ]"
          title="点击切换我方归属视角 (天辉 ⇄ 夜魇)，自动反转敌我阵容并刷新克制出装"
          @click="visionStore.toggleMyFaction"
        >
          <ArrowLeftRight class="w-3.5 h-3.5" />
          <span>我方: {{ visionStore.myFaction === 'radiant' ? '天辉军团' : '夜魇军团' }}</span>
        </button>

        <!-- 一键清空重置 -->
        <button
          v-if="visionStore.lastDetection || visionStore.lastCapture"
          type="button"
          class="inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-medium rounded-lg transition-colors border border-rose-900/70 bg-rose-950/60 hover:bg-rose-900/80 text-rose-300 shadow-sm"
          title="清空当前对战识别槽位与推荐，恢复纯净空态"
          @click="visionStore.clearDetection"
        >
          <Trash2 class="w-3.5 h-3.5 text-rose-400" />
          <span>清空重置</span>
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

    <!-- 展开区域：识别状态、10 英雄槽位微调展台与克制推荐 -->
    <div v-show="isExpanded" class="pt-2 border-t border-slate-800/80 space-y-3">
      <!-- 2. 识别状态反馈条 (已移除物理屏幕等调试信息，保持清晰简洁) -->
      <div
        v-if="visionStore.isDetecting || visionStore.lastDetection"
        class="flex flex-wrap items-center justify-between gap-2 text-xs text-slate-400 bg-slate-950/50 rounded-lg px-3 py-1.5 border border-slate-800/60 font-mono"
      >
        <div class="flex items-center gap-2">
          <span class="text-slate-300 font-sans font-medium">识别状态:</span>
          <span v-if="visionStore.isDetecting" class="inline-flex items-center gap-1 text-cyan-300 animate-pulse">
            <Cpu class="w-3.5 h-3.5 animate-spin" />
            ONNX 端侧推理中...
          </span>
          <span v-else-if="visionStore.lastDetection" class="text-emerald-400 flex items-center gap-1 font-sans">
            <CheckCircle2 class="w-3.5 h-3.5" />
            识别完成
          </span>
        </div>

        <div v-if="visionStore.lastDetection" class="flex items-center gap-3">
          <span>模型推理: <strong class="text-emerald-400 font-bold">{{ visionStore.lastDetection.inferenceDurationMs }}ms</strong></span>
          <span class="text-slate-600">|</span>
          <span>总耗时: <strong class="text-slate-200 font-bold">{{ visionStore.lastDetection.totalDurationMs }}ms</strong></span>
        </div>
      </div>

      <!-- 3. 天辉与夜魇 10 英雄槽位识别成果展台 (支持分轮次识别与手动微调) -->
      <div v-if="visionStore.lastDetection" class="space-y-2">
        <div class="flex items-center justify-between text-xs font-semibold px-1">
          <div class="flex items-center gap-2">
            <span
              class="flex items-center gap-1"
              :class="visionStore.myFaction === 'radiant' ? 'text-emerald-400 font-bold' : 'text-slate-400'"
            >
              <Sparkles class="w-3.5 h-3.5" />
              天辉阵营 (Radiant 1-5)
            </span>
            <span
              class="text-xs px-2 py-0.5 rounded font-mono font-medium"
              :class="visionStore.myFaction === 'radiant' ? 'bg-emerald-950 text-emerald-300 border border-emerald-700/60' : 'bg-slate-900 text-slate-400 border border-slate-700/40'"
            >
              {{ visionStore.myFaction === 'radiant' ? '★ 我方阵容' : '敌方阵容' }}
            </span>
          </div>

          <div class="flex items-center gap-2">
            <span
              class="text-xs px-2 py-0.5 rounded font-mono font-medium"
              :class="visionStore.myFaction === 'dire' ? 'bg-rose-950 text-rose-300 border border-rose-700/60' : 'bg-slate-900 text-slate-400 border border-slate-700/40'"
            >
              {{ visionStore.myFaction === 'dire' ? '★ 我方阵容' : '敌方阵容' }}
            </span>
            <span
              class="flex items-center gap-1"
              :class="visionStore.myFaction === 'dire' ? 'text-rose-400 font-bold' : 'text-slate-400'"
            >
              <Sparkles class="w-3.5 h-3.5" />
              夜魇阵营 (Dire 1-5)
            </span>
          </div>
        </div>

        <!-- 10 槽位网格 (5 天辉 + 5 夜魇，可点击增删改微调) -->
        <div class="grid grid-cols-2 gap-3">
          <!-- 天辉 5 槽位 -->
          <div class="grid grid-cols-5 gap-1.5 bg-emerald-950/20 p-2 rounded-xl border border-emerald-900/40">
            <div
              v-for="(slot, idx) in visionStore.lastDetection.radiant"
              :key="'rad-' + idx"
              class="slot-card flex flex-col items-center justify-center p-1 rounded-lg border text-center transition-all min-h-[72px] relative group cursor-pointer"
              :class="[
                slot
                  ? 'bg-emerald-950/60 border-emerald-600/50 shadow-sm'
                  : 'bg-slate-950/40 border-slate-800/60 border-dashed hover:border-emerald-600/60',
              ]"
              @click="openSlotPicker('radiant', idx)"
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
                <div class="text-xs font-bold text-slate-100 truncate w-full" :title="slot.heroName">
                  {{ slot.heroName }}
                </div>
                <span class="text-[10px] font-mono font-bold text-emerald-300 bg-emerald-900/60 px-1 rounded">
                  {{ Math.round(slot.confidence * 100) }}%
                </span>
                <!-- 悬停微调操作按钮 -->
                <button
                  type="button"
                  class="absolute -top-1.5 -right-1.5 w-4 h-4 rounded-full bg-rose-600 text-white flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity shadow"
                  title="清空此槽位"
                  @click.stop="visionStore.updateSlotHero('radiant', idx, null)"
                >
                  <X class="w-2.5 h-2.5" />
                </button>
              </div>
              <div v-else class="text-xs text-slate-400 flex flex-col items-center gap-0.5 group-hover:text-emerald-400">
                <Plus class="w-3.5 h-3.5 opacity-60 group-hover:opacity-100" />
                <span>#{{ idx + 1 }} 添加</span>
              </div>
            </div>
          </div>

          <!-- 夜魇 5 槽位 -->
          <div class="grid grid-cols-5 gap-1.5 bg-rose-950/20 p-2 rounded-xl border border-rose-900/40">
            <div
              v-for="(slot, idx) in visionStore.lastDetection.dire"
              :key="'dire-' + idx"
              class="slot-card flex flex-col items-center justify-center p-1 rounded-lg border text-center transition-all min-h-[72px] relative group cursor-pointer"
              :class="[
                slot
                  ? 'bg-rose-950/60 border-rose-600/50 shadow-sm'
                  : 'bg-slate-950/40 border-slate-800/60 border-dashed hover:border-rose-600/60',
              ]"
              @click="openSlotPicker('dire', idx)"
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
                <div class="text-xs font-bold text-slate-100 truncate w-full" :title="slot.heroName">
                  {{ slot.heroName }}
                </div>
                <span class="text-[10px] font-mono font-bold text-rose-300 bg-rose-900/60 px-1 rounded">
                  {{ Math.round(slot.confidence * 100) }}%
                </span>
                <button
                  type="button"
                  class="absolute -top-1.5 -right-1.5 w-4 h-4 rounded-full bg-rose-600 text-white flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity shadow"
                  title="清空此槽位"
                  @click.stop="visionStore.updateSlotHero('dire', idx, null)"
                >
                  <X class="w-2.5 h-2.5" />
                </button>
              </div>
              <div v-else class="text-xs text-slate-400 flex flex-col items-center gap-0.5 group-hover:text-rose-400">
                <Plus class="w-3.5 h-3.5 opacity-60 group-hover:opacity-100" />
                <span>#{{ idx + 1 }} 添加</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 4. 端到端大数据对抗克制与针对性出装建议展台 (核心闭环) -->
      <div
        v-if="visionStore.matchupRecommendation"
        class="p-3 border border-amber-500/40 rounded-xl bg-slate-950/90 shadow-md space-y-2.5"
      >
        <div class="flex items-center justify-between text-xs pb-1.5 border-b border-slate-800">
          <div class="flex items-center gap-1.5 text-amber-400 font-bold">
            <Swords class="w-4 h-4" />
            <span>敌方阵容大数据克制与出装推导闭环</span>
            <span class="text-[11px] font-normal text-slate-400">
              (已识别敌方 {{ visionStore.enemyHeroIds.length }} 名英雄)
            </span>
          </div>
          <span v-if="visionStore.isRecommending" class="text-cyan-400 text-xs flex items-center gap-1 animate-pulse font-mono">
            <RefreshCw class="w-3 h-3 animate-spin" />
            克制推导中...
          </span>
          <span v-else class="text-xs text-emerald-400 font-mono flex items-center gap-1">
            <CheckCircle2 class="w-3.5 h-3.5" />
            已实时同步置顶悬浮窗
          </span>
        </div>

        <!-- 针对性装备推荐横向卡片 -->
        <div v-if="visionStore.matchupRecommendation.counterItems?.length > 0" class="space-y-1.5">
          <div class="text-xs text-slate-200 font-semibold flex items-center gap-1">
            <ShieldAlert class="w-3.5 h-3.5 text-rose-400" />
            <span>针对性质变装备建议：</span>
          </div>
          <div class="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-2">
            <div
              v-for="item in visionStore.matchupRecommendation.counterItems"
              :key="item.itemKey"
              class="flex items-center gap-2 p-2 rounded-lg bg-slate-900 border border-slate-800"
            >
              <div class="w-10 h-7 shrink-0 rounded overflow-hidden border border-amber-500/50 bg-black">
                <img :src="getItemIcon(item.itemKey)" :alt="item.itemName" class="w-full h-full object-cover" />
              </div>
              <div class="min-w-0 flex-1">
                <div class="text-xs font-bold text-amber-300 truncate">{{ item.itemName }}</div>
                <div class="text-xs text-slate-400 truncate" :title="item.reason">{{ item.reason }}</div>
              </div>
            </div>
          </div>
        </div>

        <!-- 敌方核心高危威胁分析 -->
        <div v-if="visionStore.matchupRecommendation.threatHeroes?.length > 0" class="space-y-1 pt-1 border-t border-slate-800/80">
          <div class="text-xs text-slate-300 font-medium">敌方核心机制威胁源：</div>
          <div class="flex flex-wrap gap-2">
            <div
              v-for="th in visionStore.matchupRecommendation.threatHeroes"
              :key="th.heroId"
              class="inline-flex items-center gap-1.5 px-2 py-1 rounded bg-rose-950/40 border border-rose-900/60 text-xs"
            >
              <span class="text-rose-300 font-bold">{{ th.heroName }}</span>
              <span class="text-slate-400">({{ th.threatDesc }})</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 5. 图像预览容器 (带视口悬浮标签) -->
      <div
        v-if="visionStore.lastCapture?.success && visionStore.lastCapture.imageBase64"
        class="relative rounded-lg overflow-hidden border border-slate-700/80 bg-black/60 group"
      >
        <img
          :src="visionStore.lastCapture.imageBase64"
          alt="DOTA2 顶部英雄槽位截图"
          class="w-full h-auto max-h-40 object-contain object-top block"
        />
        <div class="absolute bottom-2 right-2 px-2 py-1 rounded bg-black/80 backdrop-blur-sm border border-slate-700 text-xs text-slate-300 pointer-events-none opacity-80 group-hover:opacity-100 transition-opacity flex items-center gap-1">
          <Eye class="w-3.5 h-3.5 text-cyan-400" />
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

    <!-- 6. 槽位英雄手动纠错微调浮层 Modal -->
    <div
      v-if="showHeroPicker"
      class="fixed inset-0 bg-black/70 backdrop-blur-sm z-50 flex items-center justify-center p-4"
      @click.self="showHeroPicker = false"
    >
      <div class="bg-slate-900 border border-slate-700 rounded-xl shadow-2xl w-full max-w-md p-4 space-y-3">
        <div class="flex items-center justify-between border-b border-slate-800 pb-2">
          <div class="text-sm font-bold text-slate-100 flex items-center gap-1.5">
            <span>手动微调槽位:</span>
            <span :class="pickerSide === 'radiant' ? 'text-emerald-400' : 'text-rose-400'">
              {{ pickerSide === 'radiant' ? '天辉' : '夜魇' }} #{{ pickerSlotIdx + 1 }}
            </span>
          </div>
          <button
            class="text-slate-400 hover:text-slate-200"
            @click="showHeroPicker = false"
          >
            <X class="w-4 h-4" />
          </button>
        </div>

        <!-- 搜索输入框 -->
        <div class="relative">
          <Search class="w-4 h-4 text-slate-400 absolute left-2.5 top-2.5" />
          <input
            v-model="pickerSearchQuery"
            type="text"
            placeholder="搜索英雄中文名或拼音/英文..."
            class="w-full bg-slate-950 border border-slate-700 rounded-lg pl-9 pr-3 py-1.5 text-xs text-slate-200 focus:outline-none focus:border-cyan-500"
          />
        </div>

        <!-- 清空该槽位按钮 -->
        <button
          type="button"
          class="w-full py-1.5 text-xs text-rose-400 border border-rose-900/50 rounded-lg hover:bg-rose-950/40 flex items-center justify-center gap-1 transition-colors"
          @click="selectHeroForSlot(null)"
        >
          <Trash2 class="w-3.5 h-3.5" />
          <span>清空该槽位 (设为空态)</span>
        </button>

        <!-- 英雄快速选择网格 -->
        <div class="grid grid-cols-4 gap-2 max-h-60 overflow-y-auto pr-1">
          <button
            v-for="h in allHeroOptions"
            :key="h.id"
            type="button"
            class="flex flex-col items-center p-1.5 rounded-lg border border-slate-800 hover:border-cyan-500/60 bg-slate-950 hover:bg-slate-800/80 transition-all text-center"
            @click="selectHeroForSlot(h.id)"
          >
            <div class="w-10 h-7 rounded overflow-hidden bg-black mb-1">
              <img :src="getHeroAvatar(h.shortName)" :alt="h.name" class="w-full h-full object-cover" />
            </div>
            <span class="text-[11px] text-slate-200 truncate w-full">{{ h.name }}</span>
          </button>
        </div>
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
