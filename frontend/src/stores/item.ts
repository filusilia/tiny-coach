/**
 * 物品图鉴与配方合成知识库 Store (src/stores/item.ts)
 * 职责：
 * 1. 维护当前高亮选中的物品 ID
 * 2. 提供 Dota 2 全量物品图鉴检索与分类
 */

import { defineStore } from 'pinia'
import { ref } from 'vue'

export const useItemStore = defineStore('item', () => {
  /** 当前高亮选中的物品数字 ID */
  const selectedItemId = ref<number | null>(null)

  /** 设置选中的物品 ID */
  function setSelectedItemId(id: number | null) {
    selectedItemId.value = id
  }

  return {
    selectedItemId,
    setSelectedItemId,
  }
})
