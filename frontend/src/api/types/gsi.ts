/**
 * Dota 2 GSI 实时数据快照模型契约定义 (src/api/types/gsi.ts)
 */

export interface GsiPlayerState {
  steamId: string
  name: string
  activity: string
  kills: number
  deaths: number
  assists: number
  lastHits: number
  denies: number
  killStreak: number
  gold: number
  goldReliable: number
  goldUnreliable: number
  goldPerMin: number
  xpPerMin: number
}

export interface GsiHeroState {
  id: number
  name: string
  level: number
  alive: boolean
  respawnSeconds: number
  buybackCost: number
  buybackCooldown: number
  health: number
  maxHealth: number
  healthPercent: number
  mana: number
  maxMana: number
  manaPercent: number
  hasAghsScepter: boolean
  hasAghsShard: boolean
}

export interface GsiMapState {
  matchId: string
  gameTime: number
  clockTime: number
  daytime: boolean
  nightstalkerNight: boolean
  radiantScore: number
  direScore: number
  gameState: string
  winTeam: string
}

export interface GsiItemSlot {
  name: string
  purchaser: number
  passive: boolean
  canCast: boolean
  cooldown: number
  charges?: number
}

export interface GsiAbilitySlot {
  name: string
  level: number
  canCast: boolean
  passive: boolean
  abilityActive: boolean
  cooldown: number
  ultimate: boolean
}

export interface GsiSnapshot {
  provider?: {
    name: string
    appid: number
    version: number
    timestamp: number
  }
  map?: GsiMapState
  player?: GsiPlayerState
  hero?: GsiHeroState
  abilities?: Record<string, GsiAbilitySlot>
  items?: Record<string, GsiItemSlot>
}
