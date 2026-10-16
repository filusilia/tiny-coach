/**
 * 战况与实时对局状态 Store (src/stores/battle.ts)
 * 核心架构：
 * 1. 严格以 activeBattleContext 为唯一单一权威数据源 (Single Source of Truth)
 * 2. 状态由 Go 端 GSI 服务为唯一权威输入，严禁前端伪造任何虚拟英雄或空战局假数据
 * 3. 对外暴露响应式只读代理属性与计算字段，供 OverlayView、CoachView 等统一消费
 */

import type { CoachAdvice } from '@/types'
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { activeBattleContext } from '@/services/gsiRuleEngine.ts'

export const useBattleStore = defineStore('battle', () => {
  /** 战术建议/高光提醒流水列表 (上限保留 50 条先进先出) */
  const advices = ref<CoachAdvice[]>([])

  /** 是否与 Valve GSI 本机 4444 端口建立实时数据通信 */
  const gsiConnected = computed<boolean>(() => activeBattleContext.connected)

  /** 当前是否有真实对局进行中且选定英雄 (零伪造守卫) */
  const isGsiActive = computed<boolean>(() => activeBattleContext.connected && activeBattleContext.heroId > 0)

  /** 当前操控的英雄 ID */
  const currentHeroId = computed<number>(() => activeBattleContext.heroId)

  /** 当前操控的英雄系统名/内部类名 */
  const currentHeroName = computed<string>(() => activeBattleContext.heroInternalName)

  /** 玩家当前所属阵营 ('radiant' | 'dire' | '') */
  const playerTeam = computed<'radiant' | 'dire' | ''>(() => activeBattleContext.playerTeam)

  /** 当前操控英雄本地化中文名称 */
  const heroName = computed<string>(() => activeBattleContext.heroName || '等待英雄入场')

  /** 英雄等级 */
  const heroLevel = computed<number>(() => activeBattleContext.heroLevel || (activeBattleContext.heroId > 0 ? 1 : 0))

  /** 英雄生命值百分比 (0 ~ 100) */
  const heroHpPercent = computed<number>(() => activeBattleContext.healthPercent || 0)

  /** 英雄魔法值百分比 (0 ~ 100) */
  const heroManaPercent = computed<number>(() => activeBattleContext.manaPercent || 0)

  /** 玩家当前总金钱数量 */
  const currentGold = computed<number>(() => activeBattleContext.gold || 0)

  /** KDA 格式化文本 */
  const kdaString = computed<string>(() => {
    return `${activeBattleContext.kills ?? 0} / ${activeBattleContext.deaths ?? 0} / ${activeBattleContext.assists ?? 0}`
  })

  /** 每分钟金钱收益 (GPM) */
  const gpm = computed<number>(() => activeBattleContext.gpm || 0)

  /** 每分钟经验收益 (XPM) */
  const xpm = computed<number>(() => activeBattleContext.xpm || 0)

  /** 战局底层绝对时钟秒数 */
  const gameTime = computed<number>(() => activeBattleContext.gameTime || 0)

  /** 比赛比赛时钟秒数 (负数表示号角前) */
  const clockTime = computed<number>(() => activeBattleContext.clockTime || 0)

  /** 对局状态 */
  const gameState = computed<string>(() => activeBattleContext.gameState || '')

  /** 当前是否在有效战局中进行 */
  const isInGame = computed<boolean>(() => {
    const s = gameState.value
    return s === 'DOTA_GAMERULES_STATE_PRE_GAME' || s === 'DOTA_GAMERULES_STATE_GAME_IN_PROGRESS' || clockTime.value >= 0
  })

  /** 格式化后的对局时钟 MM:SS */
  const formattedGameTime = computed<string>(() => {
    const sec = clockTime.value
    const isNeg = sec < 0
    const abs = Math.abs(sec)
    const m = Math.floor(abs / 60)
    const s = abs % 60
    return `${isNeg ? '-' : ''}${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
  })

  /** 立即买活所需金钱总额 */
  const buybackCost = computed<number>(() => activeBattleContext.buybackCost || 0)

  /** 买活冷却剩余时间 (秒) */
  const buybackCooldown = computed<number>(() => activeBattleContext.buybackCooldown || 0)

  /** 是否满足立即买活条件 */
  const canBuyback = computed<boolean>(() => activeBattleContext.canBuyback)

  /** 官方典型辅助英雄 ID 常数集合 (37+ 位纯辅助英雄) */
  const supportHeroIds = new Set([
    3, 5, 7, 16, 20, 26, 27, 30, 31, 37, 40, 50, 57, 58, 64, 66, 68, 79,
    83, 84, 85, 86, 87, 90, 91, 92, 101, 103, 105, 110, 111, 112, 119,
    121, 123, 128, 131, 155,
  ])

  /** 全量已拥有装备类名集合 (用于出装推荐过滤已购物品) */
  const ownedItemNames = computed<string[]>(() => activeBattleContext.allOwnedItemNames || [])

  /**
   * 智能分路与角色识别：判定当前操控英雄是否处于辅助分路 (4/5号位)
   * 1. 英雄先验：若属于官方经典辅助英雄表，直接确立为辅助位；
   * 2. 局内动态遥测：若携带补刀斧则为核心；若携带真假眼/雾/秘法/微光/大骨灰或低补刀，则确立为辅助。
   */
  const isSupportRole = computed<boolean>(() => {
    const heroId = currentHeroId.value
    if (heroId > 0 && supportHeroIds.has(heroId)) {
      return true
    }
    const items = ownedItemNames.value
    // 携带补刀斧则为大哥/核心分路
    if (items.some(n => n.includes('quelling_blade'))) {
      return false
    }
    // 购买辅助类特定起手与辅助装
    const hasSupportItem = items.some(n =>
      n.includes('ward_observer') || n.includes('ward_sentry')
      || n.includes('smoke_of_deceit') || n.includes('glimmer_cape')
      || n.includes('force_staff') || n.includes('spirit_vessel')
      || n.includes('urn_of_shadows') || n.includes('tranquil_boots')
      || n.includes('arcane_boots') || n.includes('pavise'),
    )
    if (hasSupportItem) {
      return true
    }
    // 比赛进行 3 分钟后，若正补极少 (< 12) 且 GPM 低于 380，研判为辅助分路
    if (clockTime.value > 180 && activeBattleContext.lastHits <= 12 && gpm.value < 380) {
      return true
    }
    return false
  })

  /** 更新连接状态 */
  function setGsiConnected(connected: boolean) {
    activeBattleContext.connected = connected
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
    context: activeBattleContext,
    advices,
    gsiConnected,
    isGsiActive,
    currentHeroId,
    currentHeroName,
    heroName,
    playerTeam,
    heroLevel,
    heroHpPercent,
    heroManaPercent,
    currentGold,
    kdaString,
    gpm,
    xpm,
    gameTime,
    clockTime,
    gameState,
    isInGame,
    formattedGameTime,
    buybackCost,
    buybackCooldown,
    canBuyback,
    ownedItemNames,
    isSupportRole,
    setGsiConnected,
    addAdvice,
    clearAdvices,
  }
})
