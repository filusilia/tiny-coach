/**
 * BP 选人顶部英雄槽位坐标配置与空间匹配引擎 (src/services/vision/slotOverrides.ts)
 * 职责：
 * 1. 预置常见电竞分辨率（包括 1920x1080、2K、4K、准 3K 与 21:9 带鱼屏）的槽位坐标
 * 2. 支持未知分辨率的宽高比（16:9 / 21:9）自适应线性缩放
 * 3. 严格对齐原项目的 30% 边距中心点容差判定，输出天辉 5 槽位与夜魇 5 槽位
 */

import type { HeroSlotDetection, RawDetectionBox } from './types'
import { resolveHeroFromClass } from './heroClasses'

/** 槽位矩形: [x, y, width, height] */
export type SlotRect = [number, number, number, number]

export interface ResolutionSlots {
  comment?: string
  radiant: SlotRect[]
  dire: SlotRect[]
}

/**
 * 权威槽位预设表 (继承并扩展 slot-overrides.json)
 */
export const SLOT_PRESETS: Record<string, ResolutionSlots> = {
  // 1080P 黄金电竞分辨率 (由 2560x1440 按 0.75 比例精准映射)
  '1920x1080': {
    comment: '1080P 16:9 权威电竞基线',
    radiant: [
      [210, 8, 118, 65],
      [335, 8, 115, 65],
      [458, 6, 116, 67],
      [581, 8, 116, 65],
      [705, 8, 118, 65],
    ],
    dire: [
      [1093, 7, 121, 66],
      [1223, 7, 113, 67],
      [1348, 8, 113, 65],
      [1470, 8, 115, 64],
      [1595, 8, 111, 65],
    ],
  },
  '2560x1440': {
    comment: '2K 16:9 标定',
    radiant: [
      [280, 10, 157, 86],
      [447, 10, 153, 86],
      [611, 8, 154, 89],
      [775, 10, 154, 86],
      [940, 10, 157, 87],
    ],
    dire: [
      [1457, 9, 161, 88],
      [1631, 9, 151, 89],
      [1797, 10, 150, 87],
      [1960, 10, 153, 85],
      [2127, 10, 148, 87],
    ],
  },
  '3198x1800': {
    comment: '3K 实测标定',
    radiant: [
      [350, 8, 193, 114],
      [553, 5, 196, 115],
      [763, 11, 192, 111],
      [972, 12, 187, 109],
      [1177, 10, 187, 111],
    ],
    dire: [
      [1829, 15, 192, 108],
      [2041, 11, 184, 108],
      [2251, 12, 175, 110],
      [2465, 10, 169, 110],
      [2670, 10, 180, 107],
    ],
  },
  '3440x1440': {
    comment: '21:9 超宽带鱼屏实测标定',
    radiant: [
      [720, 8, 150, 90],
      [885, 8, 155, 88],
      [1050, 7, 155, 90],
      [1215, 8, 157, 90],
      [1383, 11, 151, 85],
    ],
    dire: [
      [1905, 9, 152, 91],
      [2069, 11, 151, 89],
      [2239, 12, 146, 86],
      [2402, 8, 147, 94],
      [2565, 11, 151, 88],
    ],
  },
  '3840x2160': {
    comment: '4K 16:9 标定',
    radiant: [
      [419, 13, 229, 134],
      [670, 15, 233, 129],
      [916, 11, 230, 134],
      [1162, 11, 236, 135],
      [1411, 15, 233, 132],
    ],
    dire: [
      [2196, 13, 232, 134],
      [2443, 13, 226, 135],
      [2695, 13, 225, 135],
      [2946, 16, 219, 131],
      [3202, 12, 216, 128],
    ],
  },
}

/**
 * 根据全屏物理分辨率寻找或换算对应的 10 英雄槽位
 */
export function getSlotsForResolution(width: number, height: number): ResolutionSlots {
  const key = `${width}x${height}`
  if (SLOT_PRESETS[key]) {
    return SLOT_PRESETS[key]
  }

  // 宽高比模糊匹配 (16:9 ≈ 1.778, 21:9 ≈ 2.389)
  const aspect = width / height
  let baseKey = '2560x1440'
  if (Math.abs(aspect - 2.388) < 0.2) {
    baseKey = '3440x1440'
  }

  const baseSlots = SLOT_PRESETS[baseKey] || SLOT_PRESETS['2560x1440']
  const [baseW] = baseKey.split('x').map(Number)
  const scale = width / baseW

  const scaledSlots: ResolutionSlots = {
    comment: `自适应缩放自 ${baseKey} (比例 ${scale.toFixed(3)})`,
    radiant: baseSlots.radiant.map(([x, y, w, h]) => [
      Math.round(x * scale),
      Math.round(y * scale),
      Math.max(Math.round(w * scale), 20),
      Math.max(Math.round(h * scale), 20),
    ]),
    dire: baseSlots.dire.map(([x, y, w, h]) => [
      Math.round(x * scale),
      Math.round(y * scale),
      Math.max(Math.round(w * scale), 20),
      Math.max(Math.round(h * scale), 20),
    ]),
  }

  return scaledSlots
}

/**
 * 严格按照原项目算法：将检测框与预设槽位进行容差匹配
 * 容差阈值：margin = 0.30 (槽位尺寸的 ±30% 允许偏差)
 */
export function matchDetectionsToSlots(
  detections: RawDetectionBox[],
  screenWidth: number,
  screenHeight: number,
): {
  radiant: (HeroSlotDetection | null)[]
  dire: (HeroSlotDetection | null)[]
} {
  const slots = getSlotsForResolution(screenWidth, screenHeight)
  const margin = 0.30

  const allSlots: Array<{
    side: 'radiant' | 'dire'
    idx: number
    rect: SlotRect
  }> = [
    ...slots.radiant.map((rect, idx) => ({ side: 'radiant' as const, idx, rect })),
    ...slots.dire.map((rect, idx) => ({ side: 'dire' as const, idx, rect })),
  ]

  const radiantList: (HeroSlotDetection | null)[] = [null, null, null, null, null]
  const direList: (HeroSlotDetection | null)[] = [null, null, null, null, null]

  for (const item of allSlots) {
    const [sx, sy, sw, sh] = item.rect
    const cx = sx + sw / 2
    const cy = sy + sh / 2
    const halfW = (sw / 2) * (1 + margin)
    const halfH = (sh / 2) * (1 + margin)

    let bestDetection: RawDetectionBox | null = null

    for (const d of detections) {
      const dcx = d.x + d.width / 2
      const dcy = d.y + d.height / 2

      if (Math.abs(dcx - cx) < halfW && Math.abs(dcy - cy) < halfH) {
        if (!bestDetection || d.confidence > bestDetection.confidence) {
          bestDetection = d
        }
      }
    }

    if (bestDetection) {
      const meta = resolveHeroFromClass(bestDetection.className)
      const slotDetection: HeroSlotDetection = {
        slotIndex: item.idx,
        side: item.side,
        heroId: meta.heroId,
        heroName: meta.heroName,
        shortName: meta.shortName,
        confidence: Math.round(bestDetection.confidence * 1000) / 1000,
        bbox: [
          Math.round(bestDetection.x),
          Math.round(bestDetection.y),
          Math.round(bestDetection.width),
          Math.round(bestDetection.height),
        ],
      }

      if (item.side === 'radiant') {
        radiantList[item.idx] = slotDetection
      } else {
        direList[item.idx] = slotDetection
      }
    }
  }

  return {
    radiant: radiantList,
    dire: direList,
  }
}
