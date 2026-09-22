/**
 * 战况与实时对局状态 Store (src/stores/battle.ts)
 * 核心优化：
 * 1. 严格使用 shallowRef 存储 10Hz GSI 高频快照，避免 Vue 建立深层响应式代理导致掉帧
 * 2. 状态由 Go 端 GSI 服务为唯一权威输入，严禁前端伪造任何虚拟英雄或空战局假数据
 */

import type { GsiSnapshot } from '../api/types'
import type { CoachAdvice } from '../types'
import { defineStore } from 'pinia'
import { computed, ref, shallowRef } from 'vue'

export const useBattleStore = defineStore('battle', () => {
  /** 是否与 Valve GSI 本机 4444 端口建立实时数据通信 */
  const gsiConnected = ref<boolean>(false)

  /** GSI 最新战局完整快照 (必须 shallowRef，单帧整体替换) */
  const snapshot = shallowRef<GsiSnapshot | null>(null)

  /** 战术建议/高光提醒流水列表 (上限保留 50 条先进先出) */
  const advices = ref<CoachAdvice[]>([])

  /** 当前操控的英雄 ID (从 snapshot 派生) */
  const currentHeroId = computed<number | null>(() => {
    return snapshot.value?.hero?.id ?? null
  })

  /** 当前操控的英雄系统名 */
  const currentHeroName = computed<string>(() => {
    return snapshot.value?.hero?.name ?? ''
  })

  /** 战局时钟秒数 */
  const gameTime = computed<number>(() => {
    return snapshot.value?.map?.gameTime ?? 0
  })

  /** 对局状态 (如 "DOTA_GAMERULES_STATE_PRE_GAME", "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS") */
  const gameState = computed<string>(() => {
    return snapshot.value?.map?.gameState ?? ''
  })

  /** 当前是否在有效战局中进行 */
  const isInGame = computed<boolean>(() => {
    const s = gameState.value
    return s === 'DOTA_GAMERULES_STATE_PRE_GAME' || s === 'DOTA_GAMERULES_STATE_GAME_IN_PROGRESS'
  })

  /** 更新连接状态 */
  function setGsiConnected(connected: boolean) {
    gsiConnected.value = connected
    if (!connected) {
      snapshot.value = null
    }
  }

  /** 更新战况快照 (整体单向赋值) */
  function setSnapshot(newSnapshot: GsiSnapshot | null) {
    snapshot.value = newSnapshot
    if (newSnapshot) {
      gsiConnected.value = true
    }
  }

  /** 追加战术建议 */
  function addAdvice(advice: CoachAdvice) {
    advices.value = [advice, ...advices.value].slice(0, 50)
  }

  /** 清空战术建议流水 */
  function clearAdvices() {
    advices.value = []
  }

  return {
    gsiConnected,
    snapshot,
    advices,
    currentHeroId,
    currentHeroName,
    gameTime,
    gameState,
    isInGame,
    setGsiConnected,
    setSnapshot,
    addAdvice,
    clearAdvices,
  }
})
