/**
 * GSI 实时数据流消费 Composable (src/composables/useGsiStream.ts)
 * 核心设计：
 * 1. 10Hz 高频快照采用 requestAnimationFrame (rAF) 节流合并缓冲
 * 2. 消费 battleStore，驱动 shallowRef 更新，保持渲染 60fps 丝滑
 * 3. 严格遵循生命周期闭环，在 onUnmounted 时彻底注销事件监听器，绝不内存泄漏
 */

import type { GsiSnapshot } from '../api/types'
import { onMounted, onUnmounted, ref } from 'vue'
import { useBattleStore } from '../stores/battle'

interface WailsRuntimeWindow extends Window {
  wails?: {
    Events?: {
      On?: (eventName: string, callback: (payload: unknown) => void) => () => void
      Off?: (eventName: string) => void
    }
  }
}

export function useGsiStream() {
  const battleStore = useBattleStore()
  const isListening = ref<boolean>(false)
  const lastPacketTimestamp = ref<number>(0)

  // rAF 缓冲槽位
  let pendingSnapshot: GsiSnapshot | null = null
  let rafHandle: number | null = null
  let unlistenFns: Array<() => void> = []

  /**
   * rAF 渲染帧合并派发
   */
  function scheduleFlush() {
    if (rafHandle !== null) return

    rafHandle = requestAnimationFrame(() => {
      rafHandle = null
      if (pendingSnapshot) {
        battleStore.setSnapshot(pendingSnapshot)
        pendingSnapshot = null
      }
    })
  }

  /**
   * 接收来自 Go 端的原始事件载荷
   */
  function handleRawPayload(data: unknown) {
    if (!data) return

    lastPacketTimestamp.value = Date.now()
    battleStore.setGsiConnected(true)

    let parsed: GsiSnapshot | null = null

    if (typeof data === 'string') {
      try {
        parsed = JSON.parse(data)
      } catch (e) {
        console.warn('[useGsiStream] GSI JSON 解析失败:', e)
        return
      }
    } else if (typeof data === 'object') {
      parsed = data as GsiSnapshot
    }

    if (parsed) {
      pendingSnapshot = parsed
      scheduleFlush()
    }
  }

  /**
   * 注册事件监听
   */
  function startStream() {
    const win = window as WailsRuntimeWindow
    if (isListening.value) return

    // 1. 优先使用 Wails v3 Events.On
    if (win.wails?.Events?.On) {
      // 监听权威通道 "gsi_data"
      const unreg1 = win.wails.Events.On('gsi_data', (eventPayload: unknown) => {
        const payload = (eventPayload as { data?: unknown })?.data ?? eventPayload
        handleRawPayload(payload)
      })
      if (typeof unreg1 === 'function') {
        unlistenFns.push(unreg1)
      }

      // 兼容旧通道 "dota_gsi_update"
      const unreg2 = win.wails.Events.On('dota_gsi_update', (eventPayload: unknown) => {
        const payload = (eventPayload as { data?: unknown })?.data ?? eventPayload
        handleRawPayload(payload)
      })
      if (typeof unreg2 === 'function') {
        unlistenFns.push(unreg2)
      }

      isListening.value = true
    } else {
      console.warn('[useGsiStream] 当前环境无 Wails 事件总线，等待原生接入')
    }
  }

  /**
   * 停止监听并彻底解绑
   */
  function stopStream() {
    // 注销所有注册的解除订阅函数
    for (const unlisten of unlistenFns) {
      try {
        unlisten()
      } catch (e) {
        console.warn('[useGsiStream] 解绑事件监听异常:', e)
      }
    }
    unlistenFns = []

    // 尝试调用 win.wails.Events.Off 彻底清场
    const win = window as WailsRuntimeWindow
    if (win.wails?.Events?.Off) {
      win.wails.Events.Off('gsi_data')
      win.wails.Events.Off('dota_gsi_update')
    }

    if (rafHandle !== null) {
      cancelAnimationFrame(rafHandle)
      rafHandle = null
    }
    pendingSnapshot = null
    isListening.value = false
  }

  onMounted(() => {
    startStream()
  })

  onUnmounted(() => {
    stopStream()
  })

  return {
    isListening,
    lastPacketTimestamp,
    snapshot: battleStore.snapshot,
    gsiConnected: battleStore.gsiConnected,
    startStream,
    stopStream,
  }
}
