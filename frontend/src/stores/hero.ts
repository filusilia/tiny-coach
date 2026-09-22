/**
 * 英雄图鉴与攻略知识库 Store (src/stores/hero.ts)
 * 职责：
 * 1. 维护当前选中的英雄 ID (默认 1 敌法师)
 * 2. 聚合 127 英雄静态常数表与官方 1 级基础属性
 * 3. 关联出装攻略、技能连招与天赋树数据
 */

import type { HeroGuideData } from '../data/heroGuides'
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { getHeroGuideData } from '../data/heroGuides'
import { HERO_CHINESE_NAMES } from '../data/heroNames'

export const useHeroStore = defineStore('hero', () => {
  /** 当前高亮选中的英雄数字 ID (1 为敌法师) */
  const selectedHeroId = ref<number>(1)

  /** 当前选中英雄的中文名称 */
  const selectedHeroName = computed<string>(() => {
    return HERO_CHINESE_NAMES[selectedHeroId.value] || '未知英雄'
  })

  /** 获取指定英雄的指南数据 */
  function getGuide(heroId: number = selectedHeroId.value, primaryAttr: string = 'agi'): HeroGuideData {
    return getHeroGuideData(heroId, primaryAttr)
  }

  /** 切换选中的英雄 ID */
  function setSelectedHeroId(id: number) {
    selectedHeroId.value = id
  }

  return {
    selectedHeroId,
    selectedHeroName,
    getGuide,
    setSelectedHeroId,
  }
})
