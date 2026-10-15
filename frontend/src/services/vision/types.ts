/**
 * BP 视觉识别服务通用类型定义契约 (src/services/vision/types.ts)
 */

export interface RawDetectionBox {
  classId: number
  className: string
  confidence: number
  /** 全屏还原后的物理像素坐标与尺寸 */
  x: number
  y: number
  width: number
  height: number
}

export interface HeroSlotDetection {
  slotIndex: number // 0-4
  side: 'radiant' | 'dire'
  heroId: number
  heroName: string // 中文名 (如 '敌法师')
  shortName: string // 内部短名 (如 'antimage')
  confidence: number
  bbox: [number, number, number, number] // [x, y, w, h] 全屏物理像素坐标
}

export interface BpDetectionResult {
  radiant: (HeroSlotDetection | null)[]
  dire: (HeroSlotDetection | null)[]
  inferenceDurationMs: number
  preprocessDurationMs: number
  totalDurationMs: number
  rawDetectionsCount: number
  screenWidth: number
  screenHeight: number
  timestamp: number
}

/**
 * 悬浮窗实时联动的轻量 BP 情报同步载荷
 */
export interface BpOverlaySyncPayload {
  myFaction: 'radiant' | 'dire'
  enemyHeroes: { heroId: number, heroName: string, shortName: string }[]
  myHeroes: { heroId: number, heroName: string, shortName: string }[]
  counterItems: { itemKey: string, itemName: string, reason: string }[]
  threatHeroes: { heroId: number, heroName: string, winRate: number, threatDesc: string }[]
  updatedAt: number
}
