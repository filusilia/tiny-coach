import type { CoachAdvice, ReminderCategoryKey } from '@/types'
import { reactive } from 'vue'
import { overlayApi } from '@/api'
import { HERO_CHINESE_NAMES } from '@/data/heroNames.ts'
import { convertSteamId64ToAccountId, playerProfileService } from '@/services/playerProfileService.ts'
import { speechService } from '@/services/speechService.ts'
import { useAppStore, useBattleStore } from '@/stores'
import { checkIsOverlayWindow } from '@/utils/windowMode.ts'

/**
 * 实时技能栏 HUD 实体
 * 映射自 Valve GSI 的 abilities 快照字段
 */
export interface AbilityHudItem {
  /** 技能英文内部标识符 (如 "antimage_mana_void") */
  name: string
  /** 当前已加点等级 (0 表示尚未学习) */
  level: number
  /** 当前剩余冷却时间 (秒，0 表示就绪) */
  cooldown: number
  /** 当前是否满足施法条件 (魔法充足且非被禁魔/被击晕) */
  canCast: boolean
  /** 是否为纯被动技能 */
  passive: boolean
}

/**
 * 实时物品栏/背包/中立装备 HUD 实体
 * 映射自 Valve GSI 的 items 快照字段
 */
export interface ItemHudItem {
  /** 物品槽位标识 (如 "slot0" ~ "slot5", "backpack0" ~ "backpack2", "neutral0") */
  slot: string
  /** 物品内部标识符 (如 "item_black_king_bar") */
  name: string
  /** 物品当前剩余主动冷却时间 (秒) */
  cooldown: number
  /** 是否可主动释放/使用 */
  canCast: boolean
}

/**
 * 实时对局实战上下文响应式核心实体
 * 统一收束 10Hz GSI 快照中的所有战局要素，驱动电竞 HUD 面板与独立置顶悬浮窗
 */
export interface ActiveBattleContext {
  /** 是否与 Dota 2 客户端建立 GSI 通信连接 */
  connected: boolean
  /** 比赛对局时钟秒数 (负数表示号角吹响前的准备阶段) */
  clockTime: number
  /** 引擎底层绝对时钟时间 (秒) */
  gameTime: number
  /** 当前比赛状态 (如 "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS") */
  gameState: string
  /** 玩家当前操纵英雄的官方数字 ID */
  heroId: number
  /** 英雄本地化中文名称 (如 "敌法师") */
  heroName: string
  /** 英雄英文内部类名 (如 "npc_dota_hero_antimage") */
  heroInternalName: string
  /** 英雄当前等级 (1 ~ 30) */
  heroLevel: number
  /** 英雄当前存活状态 (true: 存活, false: 阵亡待复活) */
  alive: boolean
  /** 英雄当前生命值 */
  health: number
  /** 英雄当前生命值上限 */
  maxHealth: number
  /** 英雄生命值百分比 (0 ~ 100) */
  healthPercent: number
  /** 英雄当前魔法值 */
  mana: number
  /** 英雄当前魔法值上限 */
  maxMana: number
  /** 英雄魔法值百分比 (0 ~ 100) */
  manaPercent: number
  /** 玩家当前总金钱数量 */
  gold: number
  /** 玩家不可被击杀掉落的可靠金钱数量 */
  goldReliable: number
  /** 玩家每分钟金钱收益 (GPM) */
  gpm: number
  /** 玩家每分钟经验收益 (XPM) */
  xpm: number
  /** 当前累计击杀敌方英雄次数 (K) */
  kills: number
  /** 当前累计阵亡次数 (D) */
  deaths: number
  /** 当前累计助攻次数 (A) */
  assists: number
  /** 正补小兵数量 (Last Hits) */
  lastHits: number
  /** 反补己方小兵数量 (Denies) */
  denies: number
  /** 立即买活所需金钱总额 */
  buybackCost: number
  /** 买活冷却剩余时间 (秒) */
  buybackCooldown: number
  /** 是否满足立即买活条件 (金钱充裕且冷却归零) */
  canBuyback: boolean
  /** 英雄主动/被动技能列表 HUD 快照 */
  abilities: AbilityHudItem[]
  /** 6 格主物品栏列表 */
  items: ItemHudItem[]
  /** 3 格备用背包栏列表 */
  backpack: ItemHudItem[]
  /** 1 格中立装备槽位物品 */
  neutralItem: ItemHudItem | null
  /** 肉山刷新剩余时间倒计时 (秒) */
  roshanTimer: number
  /** 痛苦魔方 (苦难魔军) 刷新倒计时 (秒) */
  tormentorTimer: number
  /** 当前是否处于白天视野环境 (true: 白天, false: 黑夜) */
  daytime: boolean
  /** 英雄是否拥有阿哈利姆神杖效果 (购买装备或吞噬合成) */
  hasScepter: boolean
  /** 英雄是否拥有阿哈利姆魔晶效果 */
  hasShard: boolean
  /** 本局已识别的敌方英雄数字 ID 列表 */
  enemyHeroIds: number[]
}

/**
 * 全局共享的实战上下文响应式单例
 * 在零伪造原则下，初始状态必须保持空态守卫 (connected: false, heroId: 0)
 */
export const activeBattleContext = reactive<ActiveBattleContext>({
  connected: false,
  clockTime: 0,
  gameTime: 0,
  gameState: '等待比赛接入',
  heroId: 0,
  heroName: '',
  heroInternalName: '',
  heroLevel: 0,
  alive: true,
  health: 0,
  maxHealth: 0,
  healthPercent: 0,
  mana: 0,
  maxMana: 0,
  manaPercent: 0,
  gold: 0,
  goldReliable: 0,
  gpm: 0,
  xpm: 0,
  kills: 0,
  deaths: 0,
  assists: 0,
  lastHits: 0,
  denies: 0,
  buybackCost: 0,
  buybackCooldown: 0,
  canBuyback: false,
  abilities: [],
  items: [],
  backpack: [],
  neutralItem: null,
  roshanTimer: 0,
  tormentorTimer: 0,
  daytime: true,
  hasScepter: false,
  hasShard: false,
  enemyHeroIds: [],
})

/**
 * 官方 GSI 原始快照 JSON 结构定义
 */
export interface GsiSnapshot {
  provider?: { name: string, timestamp: number }
  map?: {
    clock_time?: number
    game_time?: number
    game_state?: string
    roshan_state?: string
    roshan_state_end_seconds?: number
  }
  player?: {
    steamid?: string
    accountid?: string
    gold?: number
    gold_reliable?: number
    gold_unreliable?: number
    gpm?: number
    xpm?: number
    kills?: number
    deaths?: number
    team_name?: string
  }
  hero?: {
    id?: number
    name?: string
    level?: number
    alive?: boolean
    respawn_seconds?: number
    buyback_cost?: number
    buyback_cooldown?: number
    health?: number
    max_health?: number
    health_percent?: number
    mana?: number
    max_mana?: number
    mana_percent?: number
    stunned?: boolean
    silenced?: boolean
  }
  abilities?: Record<string, {
    name?: string
    level?: number
    can_cast?: boolean
    passive?: boolean
    ability_active?: boolean
    cooldown?: number
    ultimate?: boolean
  }>
  items?: Record<string, {
    name?: string
    can_cast?: boolean
    cooldown?: number
  }>
}

/**
 * GSI 实时确定性规则裁决引擎
 * 负责接收后端推送的对局状态快照，判定宏观节奏与关键节点，触发战术卡片与语音播报
 */
class GsiRuleEngine {
  /** 规则防刷屏时间戳记录映射表 (ruleKey -> lastTriggerEpochMs) */
  private lastTriggerTimes: Record<string, number> = {}
  /** 上一帧存活状态快照，用于对比英雄是否阵亡 */
  private prevAlive: boolean | null = null
  /** 上一帧黑皇杖 BKB 冷却剩余时间，用于捕捉就绪质变时刻 */
  private prevBkbCooldown: number | null = null
  /** 标记当前对局是否已自动弹出过一次独立悬浮窗 */
  private hasAutoLaunchedOverlay = false

  // ─────────────────────────────────────────────────────────────────────────────
  // 事件订阅职责声明（单一消费者铁律）
  //
  // 本引擎**不再自行注册任何事件监听**。10Hz GSI 快照的唯一订阅方是
  // composables/useGsiStream.ts，由其完成 requestAnimationFrame 合并后再调用
  // processGsiSnapshot()，从而满足铁律 9「事件驱动合并最新状态、严禁阻塞渲染主线程」。
  //
  // 历史上本类曾在 init() 中再次订阅 gsi_data / dota_gsi_update，与 useGsiStream
  // 形成双订阅：每帧快照被处理两次，且 rAF 合并被完全架空。二者又都不注销返回值，
  // 违反铁律 12。故此处彻底移除订阅与 init()，避免同类回归。
  // ─────────────────────────────────────────────────────────────────────────────

  /**
   * 规则内部冷却判定器
   * 确保同类战术提醒在指定时间窗口内不发生无意义的重复刷屏与爆音
   * @param ruleKey 规则唯一标识符
   * @param cooldownSec 冷却保护时间 (秒)
   * @returns 是否已脱离冷却期
   */
  private isCooledDown(ruleKey: string, cooldownSec: number): boolean {
    const now = Date.now()
    const last = this.lastTriggerTimes[ruleKey] || 0
    if (now - last >= cooldownSec * 1000) {
      this.lastTriggerTimes[ruleKey] = now
      return true
    }
    return false
  }

  /**
   * 核心数据消费入口：处理单帧 GSI 快照
   * 1. 提炼对局参数并同步更新 activeBattleContext 响应式状态
   * 2. 自动检测 Steam ID 并转换绑定玩家账号
   * 3. 驱动 10 分钟转线、神符刷新、智慧符、魔方、肉山、买活、BKB 成型等宏观判定
   * 4. 驱动多窗口联动 (对局开始自动唤起悬浮窗)
   * @param snapshot 来自 Valve GSI 原生 HTTP POST 的原始快照对象
   */
  public processGsiSnapshot(snapshot: GsiSnapshot) {
    if (!snapshot || !snapshot.hero) return

    const appStore = useAppStore()
    const battleStore = useBattleStore()
    battleStore.setGsiConnected(true)

    const hero = snapshot.hero
    const map = snapshot.map
    const player = snapshot.player
    const isAlive = hero.alive ?? true
    const clockTime = map?.clock_time ?? 0
    const gameState = map?.game_state
    const isGameActive = gameState === 'DOTA_GAMERULES_STATE_GAME_IN_PROGRESS' || clockTime >= 0

    // 自动通过 GSI player.steamid 识别并绑定 Dota 2 Account ID (免手工输入)
    if (player?.steamid && !playerProfileService.state.accountId) {
      const convertedId = convertSteamId64ToAccountId(player.steamid)
      if (convertedId) {
        playerProfileService.setAccountId(convertedId)
        playerProfileService.fetchRecentMatches(convertedId).catch(() => {})
      }
    }

    // 同步到实战 HUD 响应式状态
    activeBattleContext.connected = true
    activeBattleContext.clockTime = clockTime
    activeBattleContext.gameTime = map?.game_time ?? 0
    activeBattleContext.gameState = gameState || '进行中'
    activeBattleContext.heroId = hero?.id ?? 0
    activeBattleContext.heroInternalName = hero?.name || ''
    activeBattleContext.heroName = (hero?.id && HERO_CHINESE_NAMES[hero.id]) || hero?.name || ''
    activeBattleContext.heroLevel = hero?.level ?? 0
    activeBattleContext.alive = isAlive
    activeBattleContext.health = hero?.health ?? 0
    activeBattleContext.maxHealth = hero?.max_health ?? 0
    activeBattleContext.healthPercent = hero?.health_percent ?? 0
    activeBattleContext.mana = hero?.mana ?? 0
    activeBattleContext.maxMana = hero?.max_mana ?? 0
    activeBattleContext.manaPercent = hero?.mana_percent ?? 0
    activeBattleContext.gold = player?.gold ?? 0
    activeBattleContext.goldReliable = player?.gold_reliable ?? 0
    activeBattleContext.gpm = player?.gpm ?? 0
    activeBattleContext.xpm = player?.xpm ?? 0
    activeBattleContext.kills = player?.kills ?? 0
    activeBattleContext.deaths = player?.deaths ?? 0
    activeBattleContext.assists = (player as any)?.assists ?? 0
    activeBattleContext.lastHits = (player as any)?.last_hits ?? 0
    activeBattleContext.denies = (player as any)?.denies ?? 0
    activeBattleContext.buybackCost = hero.buyback_cost ?? 0
    activeBattleContext.buybackCooldown = hero.buyback_cooldown ?? 0
    activeBattleContext.canBuyback = (player?.gold ?? 0) >= (hero.buyback_cost ?? 0) && (hero.buyback_cooldown ?? 0) === 0
    activeBattleContext.roshanTimer = map?.roshan_state_end_seconds ?? 0
    activeBattleContext.tormentorTimer = Math.max(0, 1200 - clockTime)
    activeBattleContext.daytime = ((clockTime % 600) < 300)

    // 同步技能
    if (snapshot.abilities) {
      activeBattleContext.abilities = Object.entries(snapshot.abilities).map(([_, ab]) => ({
        name: ab.name || '',
        level: ab.level || 0,
        cooldown: ab.cooldown || 0,
        canCast: ab.can_cast ?? true,
        passive: ab.passive ?? false,
      }))
    }

    // 同步物品与背包
    if (snapshot.items) {
      const mainItems: ItemHudItem[] = []
      const backpackItems: ItemHudItem[] = []
      let neutral: ItemHudItem | null = null

      Object.entries(snapshot.items).forEach(([slotKey, it]) => {
        const itemObj: ItemHudItem = {
          slot: slotKey,
          name: it.name || '',
          cooldown: it.cooldown || 0,
          canCast: it.can_cast ?? true,
        }
        if (slotKey.startsWith('slot')) {
          const num = parseInt(slotKey.replace('slot', ''), 10)
          if (num <= 5) {
            mainItems.push(itemObj)
          } else {
            backpackItems.push(itemObj)
          }
        } else if (slotKey.startsWith('stash')) {
          // 储藏处
        } else if (slotKey.includes('neutral')) {
          neutral = itemObj
        } else {
          mainItems.push(itemObj)
        }
      })
      activeBattleContext.items = mainItems
      activeBattleContext.backpack = backpackItems
      activeBattleContext.neutralItem = neutral

      // 判断是否装备或吃掉神杖/魔晶
      const allItemNames = Object.values(snapshot.items).map(i => i.name || '')
      activeBattleContext.hasScepter = Boolean(
        (hero as any).aghanims_scepter
        || allItemNames.some(name => name.includes('ultimate_scepter')),
      )
      activeBattleContext.hasShard = Boolean(
        (hero as any).aghanims_shard
        || allItemNames.some(name => name.includes('aghanims_shard')),
      )
    }

    // 独立桌面悬浮窗纯战况 HUD 渲染，无需在此执行全局战术规则判定与 TTS 播报
    if (checkIsOverlayWindow()) {
      return
    }

    /**
     * 内部警报触发器：执行播报模式分流并落入战术流
     * 根据用户设置中的各类别播报模式 (0: 关闭, 1: 仅文字, 2: 语音+文字) 分发建议
     * @param category 提醒所属分类
     * @param ruleId 规则唯一标识
     * @param title 提醒主标题
     * @param text 详细阐述文本
     * @param severity 紧急程度 (Critical / Warning / Info)
     * @param suggested_action 建议操作动作
     * @param ai_explanation 旁路深度推导解释
     */
    const triggerAlert = (
      category: ReminderCategoryKey,
      ruleId: string,
      title: string,
      text: string,
      severity: CoachAdvice['severity'],
      suggested_action?: string,
      ai_explanation?: string,
    ) => {
      const mode = appStore.getReminderMode(category)
      // 0: 关闭 -> 完全静默，不进战术流
      if (mode === 0) return

      const adviceItem: CoachAdvice = {
        id: `gsi-${Date.now()}-${Math.random().toString(36).substr(2, 4)}`,
        category,
        rule_id: ruleId,
        rule: ruleId,
        title,
        text,
        severity,
        created_at: new Date().toLocaleTimeString('zh-CN', { hour12: false }),
        suggested_action,
        ai_explanation,
      }

      // 1 或 2: 进文字战术流
      battleStore.addAdvice(adviceItem)

      // 2: 语音+文字播报
      if (mode === 2) {
        speechService.speak(`${title}。${text}`, {
          mode: 2,
          rate: appStore.settings.voice_speed,
          volume: appStore.settings.voice_volume,
          voiceName: appStore.settings.voice_name,
        })
      }
    }

    // ==========================================
    // 0. 自动拉起独立置顶经济悬浮窗 (游戏开始自动浮窗)
    // ==========================================
    if (isGameActive && appStore.settings.auto_overlay_on_game_start && !this.hasAutoLaunchedOverlay) {
      this.hasAutoLaunchedOverlay = true
      overlayApi.isOverlayRunning().then((running: boolean) => {
        if (!running) {
          overlayApi.toggleOverlayWindow().then(() => {
            appStore.setOverlayRunning(true)
            triggerAlert(
              'macro_tempo',
              'AUTO_OVERLAY_LAUNCH',
              '比赛开启 · 悬浮窗就绪',
              '对局正式开始，已为您自动唤起独立置顶经济悬浮窗！',
              'Info',
              '专注屏幕中央操作，右上角悬浮窗实时监控经济差与全队买活。',
              'Solution Hierarchy: 观察敌方出门装，对线期前两波兵重点控线发育。',
            )
          }).catch(() => {})
        }
      }).catch(() => {})
    }

    // 离开对局时重置标志，下一场比赛可再次自动拉起
    if (gameState === 'DOTA_GAMERULES_STATE_POST_GAME' || gameState === 'DOTA_GAMERULES_STATE_DISCONNECT') {
      this.hasAutoLaunchedOverlay = false
    }

    // ==========================================
    // 1. 全局大局与核心战略变量掌控 (AI优先，宏观全局)
    // ==========================================

    // 1.1 宏观局势节奏 (macro_tempo) - 10分钟对线破塔转线与中期推进
    if (clockTime >= 595 && clockTime <= 605) {
      if (this.isCooledDown('macro_tempo_10m', 120)) {
        triggerAlert(
          'macro_tempo',
          'TEMPO_10MIN_TRANSITION',
          '对线期转线决策',
          '比赛已进入 10 分钟节点，边路与中路防御塔保护减弱，对线期宣告结束！',
          'Warning',
          '核心位转向野区与安全线双收，辅助集结开雾争夺敌方外塔视野。',
          'Solution Hierarchy: 优先配合强势队友抓落单酱油，破除优势路一塔打开压制局面。',
        )
      }
    }

    // 1.2 神符视野掌控 (rune_control) - 2/4/6/8 分钟河道强化神符
    if (clockTime > 0 && clockTime % 120 >= 105 && clockTime % 120 <= 108) {
      if (this.isCooledDown('rune_control_river', 90)) {
        const nextMin = Math.ceil(clockTime / 60)
        triggerAlert(
          'rune_control',
          'RUNE_CONTROL_RIVER',
          '河道强化神符预警',
          `即将迎来 ${nextMin} 分钟河道强化神符刷新，请提前 15 秒推线并向符点集结！`,
          'Warning',
          '中单快速推线抢占高坡视野，辅助提前游走卡位护符。',
          'Solution Hierarchy: 双倍/急速神符可直接转化为边路游走击杀，切勿白白拱手送敌。',
        )
      }
    }

    // 1.3 智慧神符经验节点 (rune_control) - 7 / 14 / 21 分钟智慧神符
    if (clockTime > 0 && (clockTime + 18) % 420 < 4) {
      if (this.isCooledDown('rune_control_wisdom', 180)) {
        const min = Math.floor((clockTime + 18) / 60)
        triggerAlert(
          'rune_control',
          'RUNE_CONTROL_WISDOM',
          '智慧神符争夺',
          `智慧神符即将在 ${min} 分钟于两边野区刷新，全队海量经验节点！`,
          'Info',
          '辅助提前靠拢己方智慧神符守护，有机会可组织偷取敌方神符。',
          'Solution Hierarchy: 智慧神符经验大幅拉近等级差，直接决定核心英雄关键天赋解锁速度。',
        )
      }
    }

    // 1.4 痛苦魔方战略节点 (tormentor_tactics) - 20:00 (1200s)
    if (clockTime >= 1180 && clockTime <= 1185) {
      if (this.isCooledDown('tormentor_tactics', 120)) {
        triggerAlert(
          'tormentor_tactics',
          'TORMENTOR_SPAWN',
          '痛苦魔方刷新集结',
          '痛苦魔方即将在 15 秒后降临基地侧翼！团队获取免费魔晶战术节点。',
          'Warning',
          '提前在魔方区域插保护眼，核心清线后配合全队迅速打掉。',
          'Solution Hierarchy: 团队白送 1400 金币魔晶，尽早击杀可建立中盘技能形态质变。',
        )
      }
    }

    // 1.5 肉山刷新与控盾上高 (roshan_tactics)
    if (map?.roshan_state_end_seconds && map.roshan_state_end_seconds <= 60 && map.roshan_state_end_seconds > 54) {
      if (this.isCooledDown('roshan_tactics', 90)) {
        triggerAlert(
          'roshan_tactics',
          'ROSHAN_WINDOW',
          '肉山巢穴即将刷新',
          '肉山将在 60 秒内进入刷新窗口！战场进入控盾争夺最高警戒期。',
          'Critical',
          '排清肉山巢穴高台敌方侦查守卫，开雾占领三角区或准备反蹲。',
          'Solution Hierarchy: 检查全队核心大招与 BKB 冷却，带盾方将获得决定性上高推进主动权。',
        )
      }
    }

    // 1.6 买活战略防守与翻盘储备 (buyback_strategy)
    if (this.prevAlive === true && isAlive === false) {
      const buybackCost = hero.buyback_cost ?? 0
      const currentGold = player?.gold ?? 0
      const hasBuyback = currentGold >= buybackCost && (hero.buyback_cooldown ?? 0) === 0

      if (this.isCooledDown('buyback_strategy_death', 15)) {
        triggerAlert(
          'buyback_strategy',
          'DEATH_BUYBACK_CHECK',
          hasBuyback ? '英雄阵亡 · 具备买活' : '英雄阵亡 · 无法买活',
          hasBuyback
            ? `英雄阵亡，当前资金 ${currentGold} 满足买活需求 (${buybackCost})。`
            : `英雄阵亡！金钱不足或买活冷却中，敌方可能逼高。`,
          hasBuyback ? 'Warning' : 'Critical',
          hasBuyback
            ? '保留买活备用金，等待敌方攻上高地露破绽时果断买活反打！'
            : '提示队友退守高地塔下，利用塔防塔击牵制拖延复活时间。',
          'Solution Hierarchy: 切忌盲目秒买活，必须等敌方第一轮爆发交完再切入反打。',
        )
      }
    }
    this.prevAlive = isAlive

    // 1.7 关键装成型与克制建议 (item_timing_threat)
    // 检查核心装成型（大招或BKB从冷却到就绪）
    let bkbCooldown = 0
    if (snapshot.items) {
      for (const it of Object.values(snapshot.items)) {
        if (it.name?.includes('black_king_bar')) {
          bkbCooldown = it.cooldown ?? 0
          break
        }
      }
    }
    if (this.prevBkbCooldown !== null && this.prevBkbCooldown > 0 && bkbCooldown === 0) {
      if (this.isCooledDown('item_timing_bkb', 30)) {
        triggerAlert(
          'item_timing_threat',
          'BKB_READY_ATTACK',
          '黑皇杖就绪 · 强势推进期',
          '核心黑皇杖(BKB)冷却完毕！具备强行开团与上高作战能力。',
          'Info',
          '呼叫队友开雾先手，在 BKB 持续期间全力输出敌方核心。',
          'Solution Hierarchy: 绝不空开 BKB，等待敌方第一手控制或魔法爆发砸出瞬间开启反打。',
        )
      }
    }
    this.prevBkbCooldown = bkbCooldown

    // 储备金钱突破大件质变线 (如跳刀 2250 / 核心装 4000)
    if (player?.gold && player.gold >= 4200) {
      if (this.isCooledDown('item_timing_gold', 180)) {
        triggerAlert(
          'item_timing_threat',
          'ITEM_PURCHASE_TIMING',
          '大件资金积攒就绪',
          `当前储备资金达 ${player.gold}，满足关键质变大件合成！`,
          'Info',
          '派信使运送关键部件，避免带巨款在野区被抓阵亡。',
          'Solution Hierarchy: 优先根据敌方阵容补充克制装（克制闪避出MKB、破被动出隐刀、防控制出BKB）。',
        )
      }
    }
  }
}

export const gsiRuleEngine = new GsiRuleEngine()
