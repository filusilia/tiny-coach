/**
 * 玩家档案与战绩分析 Store (src/stores/player.ts)
 * 职责：
 * 1. 维护当前绑定的 Dota 玩家 ID 及规范化结果
 * 2. 缓存玩家高维战绩画像与标签数据
 * 3. 驱动 playerApi 与后端网关交互
 */

import type { PlayerProfileAnalysis } from '../api/types'
import { defineStore } from 'pinia'
import { ref } from 'vue'
import { playerApi } from '../api'

export const usePlayerStore = defineStore('player', () => {
  /** 当前绑定的玩家数字账号 ID */
  const accountId = ref<string>('')

  /** 玩家画像分析数据 */
  const profile = ref<PlayerProfileAnalysis | null>(null)

  /** 加载中指示器 */
  const loading = ref<boolean>(false)

  /** 最近一次的错误文本 */
  const error = ref<string | null>(null)

  /** 设置并规范化玩家账号 */
  async function bindAccountId(rawInput: string): Promise<string | null> {
    if (!rawInput || !rawInput.trim()) {
      accountId.value = ''
      profile.value = null
      return null
    }

    try {
      const normalized = await playerApi.normalizeAccountId(rawInput.trim())
      if (normalized) {
        accountId.value = normalized
        return normalized
      }
    } catch (e) {
      console.warn('[PlayerStore] 规范化账号失败:', e)
    }
    accountId.value = rawInput.trim()
    return accountId.value
  }

  /** 加载玩家画像档案 */
  async function loadProfile(targetId?: string): Promise<PlayerProfileAnalysis | null> {
    const queryId = targetId || accountId.value
    if (!queryId) {
      error.value = '未指定有效的 Dota 玩家 ID'
      return null
    }

    loading.value = true
    error.value = null

    try {
      const res = await playerApi.getPlayerProfile(queryId)
      profile.value = res
      if (!accountId.value) {
        accountId.value = res.accountId
      }
      return res
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err)
      error.value = errMsg
      return null
    } finally {
      loading.value = false
    }
  }

  /** 清除画像状态 */
  function clearProfile() {
    profile.value = null
    error.value = null
    loading.value = false
  }

  return {
    accountId,
    profile,
    loading,
    error,
    bindAccountId,
    loadProfile,
    clearProfile,
  }
})
