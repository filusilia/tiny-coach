/**
 * 屏幕视觉与 BP 智能决策中枢 Store (src/stores/vision.ts)
 * 职责：
 * 1. 集中管理截图、端侧 ONNX 目标检测流水线与 F10 全局热键联动
 * 2. 智能阵营视角判定 (天辉/夜魇动态归属，支持一键切换与 GSI 自动对齐)
 * 3. 分轮次 BP 识别与手动微调槽位英雄增删
 * 4. 端到端自动触发 OpenDota 大数据对抗克制与出装推荐引擎
 * 5. 置顶悬浮窗 (Overlay) 跨窗口实时广播与 600ms 连按防抖保护
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { playerApi, visionApi } from '../api'
import type { HeroMatchupRecommendation } from '../api/types'
import type { CaptureResult } from '../../bindings/aegis-coach/internal/models/models'
import { HERO_CHINESE_NAMES, HERO_SHORT_NAMES } from '../data/heroNames'
import { detectBpHeroes } from '../services/vision/heroDetector'
import type { BpDetectionResult, BpOverlaySyncPayload, HeroSlotDetection } from '../services/vision/types'
import { useBattleStore } from './battle'

export const useVisionStore = defineStore('vision', () => {
  const battleStore = useBattleStore()

  // 截图基础状态
  const lastCapture = ref<CaptureResult | null>(null)
  const isCapturing = ref(false)
  const isHotkeyActive = ref(false)
  const errorMessage = ref<string | null>(null)

  // 端侧视觉推理状态
  const lastDetection = ref<BpDetectionResult | null>(null)
  const isDetecting = ref(false)
  const detectionError = ref<string | null>(null)

  // 阵营视角：天辉 (radiant) 或 夜魇 (dire)，默认天辉为我方
  const myFaction = ref<'radiant' | 'dire'>('radiant')

  // 对抗大数据出装与克制推荐结果
  const matchupRecommendation = ref<HeroMatchupRecommendation | null>(null)
  const isRecommending = ref(false)

  // 防抖时间戳 (防 F10 快速连按导致队列堆积)
  let lastTriggerTime = 0

  /**
   * 派生：我方 5 个槽位英雄列表
   */
  const myHeroes = computed<(HeroSlotDetection | null)[]>(() => {
    if (!lastDetection.value) return [null, null, null, null, null]
    return myFaction.value === 'radiant' ? lastDetection.value.radiant : lastDetection.value.dire
  })

  /**
   * 派生：敌方 5 个槽位英雄列表
   */
  const enemyHeroes = computed<(HeroSlotDetection | null)[]>(() => {
    if (!lastDetection.value) return [null, null, null, null, null]
    return myFaction.value === 'radiant' ? lastDetection.value.dire : lastDetection.value.radiant
  })

  /**
   * 派生：敌方有效英雄数字 ID 列表
   */
  const enemyHeroIds = computed<number[]>(() => {
    return enemyHeroes.value
      .filter((h): h is HeroSlotDetection => Boolean(h && h.heroId > 0))
      .map(h => h.heroId)
  })

  /**
   * 派生：我方有效英雄数字 ID 列表
   */
  const myHeroIds = computed<number[]>(() => {
    return myHeroes.value
      .filter((h): h is HeroSlotDetection => Boolean(h && h.heroId > 0))
      .map(h => h.heroId)
  })

  /**
   * 检测并同步 GSI 阵营视角（若对局 GSI 已就绪）
   */
  function syncFactionFromGsi() {
    const gsiTeam = battleStore.playerTeam
    if (gsiTeam === 'radiant' || gsiTeam === 'dire') {
      if (myFaction.value !== gsiTeam) {
        myFaction.value = gsiTeam
      }
    }
  }

  /**
   * 手动一键切换我方阵营 (天辉 ⇄ 夜魇)
   * 切换后立即重算敌我归属并刷新大数据克制推荐
   */
  async function toggleMyFaction() {
    myFaction.value = myFaction.value === 'radiant' ? 'dire' : 'radiant'
    await triggerAutoRecommendation()
  }

  /**
   * 检查全局热键在后台系统的运行状态
   */
  async function checkHotkeyStatus() {
    try {
      isHotkeyActive.value = await visionApi.isHotkeyActive()
    } catch {
      isHotkeyActive.value = false
    }
  }

  /**
   * 核心闭环：调用后端推荐引擎并向独立置顶悬浮窗 (Overlay) 广播
   */
  async function triggerAutoRecommendation() {
    const enemies = enemyHeroIds.value
    if (enemies.length === 0) {
      matchupRecommendation.value = null
      broadcastToOverlay(null)
      return
    }

    // 优先取 GSI 当前操控英雄；若无则取我方槽位已识别的第 1 个英雄；再无则以敌方首位作为对位参考
    let focusHeroId = battleStore.currentHeroId
    if (focusHeroId <= 0 && myHeroIds.value.length > 0) {
      focusHeroId = myHeroIds.value[0]
    }
    if (focusHeroId <= 0) {
      // 双方尚无我方英雄时，选敌方第一位的对立面参考英雄进行大盘推导
      focusHeroId = enemies[0]
    }

    isRecommending.value = true
    try {
      const res = await playerApi.getHeroMatchupRecommendations(focusHeroId, enemies)
      matchupRecommendation.value = res
      broadcastToOverlay(res)
    } catch (err) {
      console.warn('[VisionStore] 大数据推荐拉取失败:', err)
    } finally {
      isRecommending.value = false
    }
  }

  /**
   * 同步 BP 战况至独立置顶悬浮窗 (跨窗口 Storage + Wails Events)
   */
  function broadcastToOverlay(rec: HeroMatchupRecommendation | null) {
    const payload: BpOverlaySyncPayload = {
      myFaction: myFaction.value,
      enemyHeroes: enemyHeroes.value
        .filter((h): h is HeroSlotDetection => Boolean(h && h.heroId > 0))
        .map(h => ({ heroId: h.heroId, heroName: h.heroName, shortName: h.shortName })),
      myHeroes: myHeroes.value
        .filter((h): h is HeroSlotDetection => Boolean(h && h.heroId > 0))
        .map(h => ({ heroId: h.heroId, heroName: h.heroName, shortName: h.shortName })),
      counterItems: (rec?.counterItems || []).map(it => ({
        itemKey: it.itemKey,
        itemName: it.itemName,
        reason: it.reason,
      })),
      threatHeroes: (rec?.threatHeroes || []).map(th => ({
        heroId: th.heroId,
        heroName: th.heroName,
        winRate: th.winRate,
        threatDesc: th.threatDesc,
      })),
      updatedAt: Date.now(),
    }

    // 1. 本地 Storage 广播 (多窗口通用监听)
    try {
      localStorage.setItem('aegis_bp_overlay', JSON.stringify(payload))
    } catch {}

    // 2. Wails 原生事件总线推送
    const win = window as any
    if (win.wails?.Events?.Emit) {
      try {
        win.wails.Events.Emit('overlay:bp-update', payload)
      } catch {}
    }
  }

  /**
   * 对截图执行端侧目标检测与槽位识别
   */
  async function runDetection(
    imageSource: string | HTMLImageElement,
    screenWidth: number,
    screenHeight: number,
    cropWidth?: number,
    cropHeight?: number,
  ): Promise<BpDetectionResult | null> {
    isDetecting.value = true
    detectionError.value = null
    try {
      // 检查对局中是否有明确阵营信息
      syncFactionFromGsi()

      const res = await detectBpHeroes(imageSource, screenWidth, screenHeight, cropWidth, cropHeight)
      lastDetection.value = res

      // 端到端闭环：识别完成后自动触发克制推荐
      await triggerAutoRecommendation()
      return res
    } catch (err: any) {
      console.error('[VisionStore] 端侧识别失败:', err)
      detectionError.value = err?.message || '端侧目标检测执行失败'
      return null
    } finally {
      isDetecting.value = false
    }
  }

  /**
   * 手动触发顶部 18% ROI 截屏并驱动全链路 (含 600ms 防抖)
   */
  async function triggerCapture(): Promise<CaptureResult | null> {
    const now = Date.now()
    if (now - lastTriggerTime < 600 || isCapturing.value || isDetecting.value) {
      return null // 600ms 防抖与并发互斥
    }
    lastTriggerTime = now
    isCapturing.value = true
    errorMessage.value = null

    try {
      const res = await visionApi.captureTopScreen()
      if (res) {
        lastCapture.value = res
        if (!res.success) {
          errorMessage.value = res.errorMessage || '未能捕获游戏画面（请确认游戏窗口未最小化）'
        } else if (res.imageBase64) {
          await runDetection(
            res.imageBase64,
            res.screenWidth || 1920,
            res.screenHeight || 1080,
            res.cropWidth,
            res.cropHeight,
          )
        }
      }
      return res
    } catch (e: any) {
      errorMessage.value = e?.message || '截屏过程发生异常'
      return null
    } finally {
      isCapturing.value = false
    }
  }

  /**
   * 处理 Go 后端全局 F10 事件回调
   */
  async function handleCaptureEvent(payload: CaptureResult) {
    const now = Date.now()
    if (now - lastTriggerTime < 600 || isCapturing.value || isDetecting.value) {
      return
    }
    lastTriggerTime = now

    lastCapture.value = payload
    if (!payload.success) {
      errorMessage.value = payload.errorMessage || '未能捕获游戏画面（请确认游戏窗口未最小化）'
    } else {
      errorMessage.value = null
      if (payload.imageBase64) {
        await runDetection(
          payload.imageBase64,
          payload.screenWidth || 1920,
          payload.screenHeight || 1080,
          payload.cropWidth,
          payload.cropHeight,
        )
      }
    }
  }

  /**
   * 手动微调修正单个槽位英雄 (增删修)
   * @param side 'radiant' | 'dire'
   * @param slotIndex 0-4
   * @param heroId 英雄 ID，传入 null 表示清空该槽位
   */
  async function updateSlotHero(side: 'radiant' | 'dire', slotIndex: number, heroId: number | null) {
    if (!lastDetection.value) {
      // 若尚未有检测结果，初始化空结构以承载手动微调
      lastDetection.value = {
        radiant: [null, null, null, null, null],
        dire: [null, null, null, null, null],
        inferenceDurationMs: 0,
        preprocessDurationMs: 0,
        totalDurationMs: 0,
        rawDetectionsCount: 0,
        screenWidth: 1920,
        screenHeight: 1080,
        timestamp: Date.now(),
      }
    }

    if (heroId === null || heroId <= 0) {
      lastDetection.value[side][slotIndex] = null
    } else {
      const heroName = HERO_CHINESE_NAMES[heroId] || `英雄 #${heroId}`
      const shortName = HERO_SHORT_NAMES[heroId] || ''
      lastDetection.value[side][slotIndex] = {
        slotIndex,
        side,
        heroId,
        heroName,
        shortName,
        confidence: 1.0, // 手工修正置信度标为 100%
        bbox: [0, 0, 0, 0],
      }
    }

    // 微调后重新触发推荐与悬浮窗同步
    await triggerAutoRecommendation()
  }

  /**
   * 清空重置当前 BP 视觉识别结果与克制建议，彻底清除历史识别与测试残留，回归 100% 纯净空态
   */
  function clearDetection() {
    lastCapture.value = null
    lastDetection.value = null
    matchupRecommendation.value = null
    detectionError.value = null
    errorMessage.value = null

    // 清空本地持久化存储
    try {
      localStorage.removeItem('aegis_bp_overlay')
    } catch {}

    // 通知置顶悬浮窗同步清空，回归纯净守卫态
    const win = window as any
    if (win.wails?.Events?.Emit) {
      try {
        win.wails.Events.Emit('overlay:bp-update', null)
      } catch {}
    }
  }

  return {
    lastCapture,
    isCapturing,
    isHotkeyActive,
    errorMessage,
    lastDetection,
    isDetecting,
    detectionError,
    myFaction,
    myHeroes,
    enemyHeroes,
    enemyHeroIds,
    myHeroIds,
    matchupRecommendation,
    isRecommending,
    checkHotkeyStatus,
    toggleMyFaction,
    triggerCapture,
    handleCaptureEvent,
    runDetection,
    updateSlotHero,
    triggerAutoRecommendation,
    clearDetection,
  }
})
