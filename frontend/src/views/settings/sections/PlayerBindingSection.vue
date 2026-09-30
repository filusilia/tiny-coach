<script setup lang="ts">
/**
 * 个人账号绑定与指定紧凑控制条 (src/views/settings/sections/PlayerBindingSection.vue)
 * 职责：
 * 1. Dota 2 个人账号指定与绑定（支持 32 位 Account ID、Steam ID64、主页链接）
 * 2. 绑定后战绩画像自动同步、雷达图与复盘专属定位
 * 3. 极致紧凑高密度布局，零冗余留白，高度压缩至 70px 左右
 */

import { Check, CheckCircle2, Copy, Edit3, RefreshCw, ShieldCheck, UserCheck, XCircle } from '@lucide/vue'
import { ref } from 'vue'
import { playerApi } from '@/api'
import { normalizeDotaAccountId, playerProfileService } from '@/services/playerProfileService.ts'
import { useAppStore } from '@/stores'

defineOptions({ name: 'PlayerBindingSection' })

const appStore = useAppStore()

const playerInputId = ref(appStore.settings.dota_player_id || '')
const verifyingPlayer = ref(false)
const syncingProfile = ref(false)
const playerBindError = ref('')
const isEditing = ref(false)
const idCopied = ref(false)

async function copyPlayerId(id: string) {
  if (!id) return
  try {
    await navigator.clipboard.writeText(id)
    idCopied.value = true
    setTimeout(() => {
      idCopied.value = false
    }, 2000)
  } catch {
    // 忽略
  }
}

async function handleBindPlayerId() {
  const raw = playerInputId.value.trim()
  if (!raw) {
    playerBindError.value = '请输入有效的 Dota 2 账号数字 ID、好友代码或 Steam 个人主页链接'
    return
  }

  verifyingPlayer.value = true
  playerBindError.value = ''

  try {
    const normalized = await normalizeDotaAccountId(raw)
    if (!normalized) {
      throw new Error('无法解析为有效的 32 位 Dota 账号数字 ID，请核对输入')
    }

    // 统一出口：经 Go 侧 PlayerService 代理 OpenDota，前端不直连第三方
    const rawCard = await playerApi.getPlayerCard(normalized)
    if (!rawCard) {
      throw new Error('未能获取该玩家公开个人主页数据')
    }

    const data = JSON.parse(rawCard)
    const name = data.profile?.personaname || `选手 #${normalized}`
    const avatar = data.profile?.avatarfull || data.profile?.avatar || ''

    appStore.updateSettings({
      dota_player_id: normalized,
      dota_player_name: name,
      dota_player_avatar: avatar,
    })

    playerProfileService.setAccountId(normalized)
    await playerProfileService.fetchRecentMatches(normalized).catch(() => {})

    isEditing.value = false
    appStore.showToast(`成功指定并绑定选手：${name} (${normalized})`, 'success')
  } catch (err: any) {
    playerBindError.value = err?.message || '验证玩家 ID 失败，请检查网络或确认 ID 正确'
    appStore.showToast(`绑定失败: ${playerBindError.value}`, 'error')
  } finally {
    verifyingPlayer.value = false
  }
}

async function handleSyncProfile() {
  if (!appStore.settings.dota_player_id || syncingProfile.value) return
  syncingProfile.value = true
  try {
    await playerProfileService.fetchRecentMatches(appStore.settings.dota_player_id)
    appStore.showToast('战绩画像已重新同步最新对局！', 'success')
  } catch {
    appStore.showToast('同步战绩画像失败，请稍后重试', 'warning')
  } finally {
    syncingProfile.value = false
  }
}

function handleUnbindPlayer() {
  appStore.updateSettings({
    dota_player_id: '',
    dota_player_name: '',
    dota_player_avatar: '',
  })
  playerInputId.value = ''
  playerProfileService.setAccountId('')
  isEditing.value = false
  appStore.showToast('已解绑当前指定账号', 'info')
}
</script>

<template>
  <section class="px-4 py-3 border border-slate-700/80 rounded-xl bg-slate-900/90 antialiased shadow-lg">
    <!-- 状态 A：已绑定账号且非编辑状态 (单行高密度徽章) -->
    <div
      v-if="appStore.settings.dota_player_id && !isEditing"
      class="flex flex-col gap-2.5 sm:flex-row sm:items-center sm:justify-between"
    >
      <!-- 左侧：选手头像 + 名字 + ID + 状态 -->
      <div class="flex min-w-0 items-center space-x-3">
        <div class="shrink-0 relative">
          <img
            v-if="appStore.settings.dota_player_avatar"
            :src="appStore.settings.dota_player_avatar"
            class="border border-amber-400/80 rounded-full h-9 w-9 shadow-sm object-cover"
            alt="选手头像"
          >
          <div
            v-else
            class="text-xs text-slate-300 font-bold border border-slate-700 rounded-full bg-slate-800 flex h-9 w-9 items-center justify-center"
          >
            ID
          </div>
          <span
            class="border border-slate-900 rounded-full bg-emerald-400 h-2.5 w-2.5 bottom-0 right-0 absolute"
            title="主视角账号已指定"
          />
        </div>

        <div class="min-w-0 space-y-0.5">
          <div class="flex items-center space-x-2">
            <span class="text-sm text-amber-300 font-bold max-w-40 truncate">
              {{ appStore.settings.dota_player_name || '已绑定选手' }}
            </span>
            <span class="text-[10px] text-emerald-300 font-semibold px-1.5 py-0.2 border border-emerald-500/40 rounded bg-emerald-500/15 flex shrink-0 gap-0.5 items-center">
              <ShieldCheck class="h-3 w-3" />
              主视角指定
            </span>
          </div>

          <div class="text-xs text-slate-400 font-mono flex items-center space-x-1.5">
            <span>ID: {{ appStore.settings.dota_player_id }}</span>
            <button
              class="text-slate-500 p-0.5 transition-colors hover:text-amber-300"
              title="复制账号 ID"
              @click="copyPlayerId(appStore.settings.dota_player_id)"
            >
              <Check v-if="idCopied" class="text-emerald-400 h-3 w-3" />
              <Copy v-else class="h-3 w-3" />
            </button>
            <span class="text-slate-600">·</span>
            <span class="text-[11px] text-slate-400 hidden md:inline">赛后复盘与局内战绩已专属对齐</span>
          </div>
        </div>
      </div>

      <!-- 右侧：快捷操作 (同步、更换、解绑) -->
      <div class="flex shrink-0 items-center self-end space-x-2 sm:self-auto">
        <button
          class="text-xs text-slate-200 font-medium px-2.5 py-1.5 border border-slate-700 rounded-lg bg-slate-800 flex cursor-pointer transition-colors items-center space-x-1 hover:text-white hover:bg-slate-700"
          :disabled="syncingProfile"
          title="重新同步最新战绩"
          @click="handleSyncProfile"
        >
          <RefreshCw class="h-3.5 w-3.5" :class="{ 'animate-spin': syncingProfile }" />
          <span>{{ syncingProfile ? '同步中' : '同步' }}</span>
        </button>

        <button
          class="text-xs text-slate-300 font-medium px-2.5 py-1.5 border border-slate-700 rounded-lg bg-slate-800 flex cursor-pointer transition-colors items-center space-x-1 hover:text-amber-300 hover:bg-slate-700"
          title="更换绑定的 Dota 2 账号"
          @click="isEditing = true"
        >
          <Edit3 class="h-3.5 w-3.5" />
          <span>更换</span>
        </button>

        <button
          class="text-xs text-rose-300 font-medium px-2.5 py-1.5 border border-rose-800/60 rounded-lg bg-rose-950/40 flex cursor-pointer transition-colors items-center space-x-1 hover:bg-rose-900/60"
          title="解除指定绑定"
          @click="handleUnbindPlayer"
        >
          <XCircle class="h-3.5 w-3.5" />
          <span>解绑</span>
        </button>
      </div>
    </div>

    <!-- 状态 B：未绑定或点击编辑状态 (单行紧凑输入与绑定) -->
    <div v-else class="space-y-2">
      <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
        <!-- 紧凑 Label -->
        <div class="flex shrink-0 items-center space-x-2">
          <div class="text-amber-400 p-1.5 border border-amber-500/40 rounded-lg bg-amber-500/15">
            <UserCheck class="h-4 w-4" />
          </div>
          <span class="text-xs text-slate-200 font-bold">指定选手:</span>
        </div>

        <!-- 单行输入框 -->
        <div class="flex-1 min-w-0">
          <input
            v-model="playerInputId"
            type="text"
            placeholder="输入 Dota 2 数字 ID (如 136414902)、好友代码或 Steam 主页链接"
            class="text-xs text-slate-100 font-mono px-3 py-1.5 outline-none border border-slate-700 rounded-lg bg-slate-950 h-8 w-full transition-colors focus:border-amber-500 focus:ring-1 focus:ring-amber-500"
            @keyup.enter="handleBindPlayerId"
          >
        </div>

        <!-- 操作按钮组 -->
        <div class="flex shrink-0 items-center space-x-2">
          <button
            class="text-xs text-slate-950 font-bold px-3.5 py-1.5 rounded-lg bg-amber-500 flex h-8 cursor-pointer transition-all items-center space-x-1 hover:bg-amber-400 active:scale-95"
            :disabled="verifyingPlayer"
            @click="handleBindPlayerId"
          >
            <CheckCircle2 class="h-3.5 w-3.5" :class="{ 'animate-spin': verifyingPlayer }" />
            <span>{{ verifyingPlayer ? '验证中...' : '绑定指定账号' }}</span>
          </button>

          <button
            v-if="appStore.settings.dota_player_id && isEditing"
            class="text-xs text-slate-400 px-2.5 py-1.5 border border-slate-700 rounded-lg h-8 cursor-pointer transition-colors hover:text-slate-200"
            @click="isEditing = false"
          >
            取消
          </button>
        </div>
      </div>

      <!-- 行内错误微型提示 -->
      <div v-if="playerBindError" class="text-xs text-rose-300 font-mono pt-0.5 flex items-center space-x-1.5">
        <XCircle class="shrink-0 h-3 w-3" />
        <span>{{ playerBindError }}</span>
      </div>
    </div>
  </section>
</template>
