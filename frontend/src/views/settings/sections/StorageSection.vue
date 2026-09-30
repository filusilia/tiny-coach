<script setup lang="ts">
/**
 * 数据缓存与本地文件目录设置组件 (src/views/settings/sections/StorageSection.vue)
 */

import { Database, ExternalLink, FolderOpen, Sparkles } from '@lucide/vue'
import { ref, watch } from 'vue'
import { systemApi } from '@/api'
import { useAppStore } from '@/stores'

defineOptions({ name: 'StorageSection' })

const appStore = useAppStore()

const cacheDirInput = ref(appStore.settings.cache_directory || '')
const cachingIcons = ref(false)
const iconCacheStats = ref<{ cached_heroes: number, cached_items: number } | null>(null)

watch(
  () => appStore.settings.cache_directory,
  (newDir) => {
    cacheDirInput.value = newDir || ''
  },
)

async function handleCacheAllIcons() {
  if (cachingIcons.value) return
  cachingIcons.value = true
  appStore.showToast('正在后台建立全量英雄与装备离线图标缓存...', 'info')
  try {
    const res = await systemApi.cacheAllIcons()
    // 统计口径直接取自本次后端预热结果，避免调用"取统计"接口反而触发一次全量下载
    iconCacheStats.value = { cached_heroes: res.cached_heroes, cached_items: res.cached_items }
    appStore.showToast(
      `全量离线图标缓存就绪：英雄 ${res.cached_heroes} 个，装备 ${res.cached_items} 个`,
      'success',
    )
  } catch (err: any) {
    appStore.showToast(`预热图标缓存失败: ${err?.message || '网络连接超时'}`, 'error')
  } finally {
    cachingIcons.value = false
  }
}

async function handleSelectCacheFolder() {
  try {
    const selected = await appStore.selectCacheFolder()
    if (selected) {
      cacheDirInput.value = selected
      appStore.showToast(`数据存储目录已成功更改至: ${selected}`, 'success')
    }
  } catch (err: any) {
    appStore.showToast(`更改目录失败: ${err?.message || '用户已取消'}`, 'error')
  }
}

async function handleOpenCacheDir() {
  try {
    await appStore.openCacheDirectory()
    appStore.showToast('已在系统文件资源管理器中定位当前缓存目录', 'info')
  } catch (err: any) {
    appStore.showToast(`打开目录失败: ${err?.message || '路径不可用'}`, 'error')
  }
}

async function handleResetCacheDir() {
  try {
    const defaultDir = await systemApi.getDefaultCacheDirectory()
    if (defaultDir) {
      await appStore.updateCacheDirectory(defaultDir)
      cacheDirInput.value = defaultDir
      appStore.showToast(`已重置回系统默认用户文档目录: ${defaultDir}`, 'success')
    }
  } catch (err: any) {
    appStore.showToast(`重置默认路径失败: ${err?.message || '系统限制'}`, 'error')
  }
}

async function handleClearCache() {
  try {
    await appStore.clearCacheDirectory()
    appStore.showToast('临时缓存文件清理完成！', 'success')
  } catch (err: any) {
    appStore.showToast(`清理失败: ${err?.message || '文件可能被占用'}`, 'error')
  }
}
</script>

<template>
  <section class="p-5 border border-slate-700/80 rounded-2xl bg-slate-900/90 select-none shadow-xl space-y-4">
    <div class="flex items-center space-x-3">
      <Database class="text-amber-400 h-5 w-5" />
      <div>
        <h2 class="text-sm text-slate-100 font-bold">
          数据缓存与本地文件目录
        </h2>
        <p class="text-xs text-slate-400">
          管理英雄静态图谱、战局 GSI 快照日志、赛后复盘分析数据及本地 GGUF 模型存储路径
        </p>
      </div>
    </div>

    <div class="p-3.5 border border-slate-800 rounded-xl bg-slate-950/60 space-y-3">
      <div>
        <label class="text-xs text-slate-300 font-bold mb-1 block">当前数据存储绝对路径</label>
        <div class="flex flex-col gap-2 sm:flex-row">
          <input
            v-model="cacheDirInput"
            type="text"
            readonly
            class="text-xs text-slate-200 font-mono px-3 py-1.5 outline-none border border-slate-700 rounded-lg bg-slate-900 flex-1 select-all"
          >
          <div class="flex items-center space-x-2">
            <button
              class="text-xs text-amber-300 font-bold px-3 py-1.5 border border-amber-500/50 rounded-lg bg-amber-500/20 flex cursor-pointer items-center space-x-1.5 hover:bg-amber-500/30"
              @click="handleSelectCacheFolder"
            >
              <FolderOpen class="h-3.5 w-3.5" />
              <span>选择文件夹</span>
            </button>
            <button
              class="text-xs text-slate-200 px-3 py-1.5 border border-slate-700 rounded-lg bg-slate-800 flex cursor-pointer items-center space-x-1 hover:bg-slate-700"
              @click="handleOpenCacheDir"
            >
              <ExternalLink class="h-3.5 w-3.5" />
              <span>在资源管理器打开</span>
            </button>
          </div>
        </div>
      </div>

      <!-- 图标离线本地缓存状态与预热 -->
      <div class="p-2.5 border border-slate-800/80 rounded-lg bg-slate-900/50 flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div class="space-y-0.5">
          <div class="text-xs text-slate-200 font-bold flex items-center space-x-1.5">
            <span>本地图标缓存状态</span>
            <span v-if="iconCacheStats" class="text-xs text-emerald-400 font-mono px-1.5 py-0.2 border border-emerald-500/30 rounded bg-emerald-950/60">
              已缓存英雄: {{ iconCacheStats.cached_heroes }} | 装备: {{ iconCacheStats.cached_items }}
            </span>
          </div>
          <div class="text-xs text-slate-400">
            默认优先加载本地缓存文件，离线秒级呈现；缺失时自动落盘并由官方 CDN 兜底。
          </div>
        </div>
        <button
          :disabled="cachingIcons"
          class="text-xs text-emerald-300 font-bold px-3 py-1.5 border border-emerald-500/40 rounded-lg bg-emerald-950/40 flex cursor-pointer items-center space-x-1 hover:bg-emerald-900/50 disabled:opacity-50"
          @click="handleCacheAllIcons"
        >
          <Sparkles class="h-3.5 w-3.5" />
          <span>{{ cachingIcons ? '正在检查缓存...' : '刷新本地图标缓存' }}</span>
        </button>
      </div>

      <div class="text-xs pt-2 border-t border-slate-800/80 flex items-center justify-between">
        <button
          class="text-slate-400 cursor-pointer transition-colors hover:text-slate-200"
          @click="handleResetCacheDir"
        >
          重置为系统默认用户文档路径
        </button>
        <button
          class="text-rose-400 cursor-pointer transition-colors hover:text-rose-300"
          @click="handleClearCache"
        >
          清理临时缓存文件
        </button>
      </div>
    </div>
  </section>
</template>
