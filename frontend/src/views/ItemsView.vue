<script setup lang="ts">
import type { PatchInfo } from '@/services/gameDataService.ts'
/**
 * 物品合成树与图谱视图 (src/views/ItemsView.vue)
 * 职责：
 * 1. 严格呈现 Dota 2 现行官网规范物品分类（基础物品、升级装备、中立装备 5 阶）
 * 2. 严格遵循 Valve 官方 56x42 物品格黄金比例规范与深色高对比悬浮信息卡
 * 3. 动态解析装备双向合成关系（向上溯源合成物、向下解构配件树及合成配方）
 * 4. 支持名称、简称与拼音别名极速全局搜索过滤
 */
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import ItemTooltipCard from '@/components/common/ItemTooltipCard.vue'
import SmartImage from '@/components/common/SmartImage.vue'
import { ITEM_ALIASES } from '@/data/aliases.ts'
import { getItemCategory, getNeutralTier, isCurrentItem } from '@/data/currentItemCatalog.ts'
import { dataSyncService } from '@/services/dataSyncService.ts'
import { gameDataService } from '@/services/gameDataService.ts'
import { useAppStore } from '@/stores/app.ts'
import {
  getItemCdnUrl,
  getItemIconUrl,
} from '@/utils/assetUrl.ts'

defineOptions({
  name: 'ItemsView',
})

interface MaterialItemMeta {
  key: string
  dname: string
  cost: number
  img: string
}

interface CatalogItem {
  id: string | number
  key: string
  name: string
  dname: string
  cost: number
  desc: string
  attrib: string
  notes: string
  lore: string
  mc: string | number | boolean
  cd: string | number | boolean
  components: string[] | null
  componentNames: string[] | null
  img: string
  tier: number
  category: string
  isUpgrade: boolean
}

type MainTab = 'all' | 'basics' | 'upgrades' | 'neutrals'

interface SubFilterOption {
  id: string
  label: string
}

const patchInfo = ref<PatchInfo | null>(null)
const currentPatch = computed(() => dataSyncService.state.currentPatch || patchInfo.value?.version || '')

function isItemUpdated(item: CatalogItem): boolean {
  const numId = Number(item.id)
  if (!numId || !patchInfo.value) return false
  return patchInfo.value.updatedItemIds.includes(numId)
}

const MAIN_TABS = [
  { id: 'all' as MainTab, label: '全部物品' },
  { id: 'basics' as MainTab, label: '基础物品 (BASICS)' },
  { id: 'upgrades' as MainTab, label: '升级装备 (UPGRADES)' },
  { id: 'neutrals' as MainTab, label: '中立装备 (NEUTRALS)' },
]

const SUB_FILTERS_BASICS: SubFilterOption[] = [
  { id: 'all', label: '全部基础' },
  { id: '消耗品', label: '消耗品' },
  { id: '属性', label: '属性' },
  { id: '军备', label: '军备' },
  { id: '奥术', label: '奥术' },
  { id: '神秘商店', label: '神秘商店' },
]

const SUB_FILTERS_UPGRADES: SubFilterOption[] = [
  { id: 'all', label: '全部升级' },
  { id: '辅助', label: '辅助' },
  { id: '法器', label: '法器' },
  { id: '防具', label: '防具' },
  { id: '武器', label: '武器' },
  { id: '神器', label: '神器' },
]

const SUB_FILTERS_NEUTRALS: SubFilterOption[] = [
  { id: 'all', label: '全部阶级' },
  { id: '1', label: '第 1 阶' },
  { id: '2', label: '第 2 阶' },
  { id: '3', label: '第 3 阶' },
  { id: '4', label: '第 4 阶' },
  { id: '5', label: '第 5 阶' },
]

interface GroupDefinition {
  id: string
  title: string
  enTitle: string
  section: 'basics' | 'upgrades' | 'neutrals'
  accentColor: string
  filter: (item: CatalogItem) => boolean
}

const GROUP_DEFINITIONS: GroupDefinition[] = [
  // 基础物品
  { id: '消耗品', title: '消耗品', enTitle: 'CONSUMABLES', section: 'basics', accentColor: 'bg-emerald-500', filter: i => i.tier === 0 && i.category === '消耗品' },
  { id: '属性', title: '属性', enTitle: 'ATTRIBUTES', section: 'basics', accentColor: 'bg-blue-500', filter: i => i.tier === 0 && i.category === '属性' },
  { id: '军备', title: '军备', enTitle: 'EQUIPMENT', section: 'basics', accentColor: 'bg-amber-500', filter: i => i.tier === 0 && i.category === '军备' },
  { id: '奥术', title: '奥术', enTitle: 'ARCANE', section: 'basics', accentColor: 'bg-purple-500', filter: i => i.tier === 0 && i.category === '奥术' },
  { id: '神秘商店', title: '神秘商店', enTitle: 'SECRET SHOP', section: 'basics', accentColor: 'bg-rose-500', filter: i => i.tier === 0 && i.category === '神秘商店' },

  // 升级装备
  { id: '辅助', title: '辅助', enTitle: 'ACCESSORIES', section: 'upgrades', accentColor: 'bg-teal-500', filter: i => i.tier === 0 && i.category === '辅助' },
  { id: '法器', title: '法器', enTitle: 'SUPPORT & MAGICAL', section: 'upgrades', accentColor: 'bg-cyan-500', filter: i => i.tier === 0 && i.category === '法器' },
  { id: '防具', title: '防具', enTitle: 'ARMOR', section: 'upgrades', accentColor: 'bg-indigo-500', filter: i => i.tier === 0 && i.category === '防具' },
  { id: '武器', title: '武器', enTitle: 'WEAPONS', section: 'upgrades', accentColor: 'bg-orange-500', filter: i => i.tier === 0 && i.category === '武器' },
  { id: '神器', title: '神器', enTitle: 'ARTIFACTS', section: 'upgrades', accentColor: 'bg-amber-400', filter: i => i.tier === 0 && i.category === '神器' },

  // 中立装备
  { id: 'tier_1', title: '第 1 阶中立物品', enTitle: 'TIER 1 NEUTRALS', section: 'neutrals', accentColor: 'bg-slate-400', filter: i => i.tier === 1 },
  { id: 'tier_2', title: '第 2 阶中立物品', enTitle: 'TIER 2 NEUTRALS', section: 'neutrals', accentColor: 'bg-emerald-400', filter: i => i.tier === 2 },
  { id: 'tier_3', title: '第 3 阶中立物品', enTitle: 'TIER 3 NEUTRALS', section: 'neutrals', accentColor: 'bg-blue-400', filter: i => i.tier === 3 },
  { id: 'tier_4', title: '第 4 阶中立物品', enTitle: 'TIER 4 NEUTRALS', section: 'neutrals', accentColor: 'bg-purple-400', filter: i => i.tier === 4 },
  { id: 'tier_5', title: '第 5 阶中立物品', enTitle: 'TIER 5 NEUTRALS', section: 'neutrals', accentColor: 'bg-amber-400', filter: i => i.tier === 5 },
]

const appStore = useAppStore()
const items = ref<CatalogItem[]>([])
const mainTab = ref<MainTab>('all')
const subFilter = ref<string>('all')
const hoveredItem = ref<CatalogItem | null>(null)
const pinnedItem = ref<CatalogItem | null>(null)
const activeItem = computed(() => pinnedItem.value || hoveredItem.value)
const tooltipRef = ref<HTMLElement | null>(null)
const loading = ref(true)

function setMainTab(tab: MainTab) {
  mainTab.value = tab
  subFilter.value = 'all'
}

const currentSubFilters = computed(() => {
  if (mainTab.value === 'basics') return SUB_FILTERS_BASICS
  if (mainTab.value === 'upgrades') return SUB_FILTERS_UPGRADES
  if (mainTab.value === 'neutrals') return SUB_FILTERS_NEUTRALS
  return []
})

// 自适应悬浮卡片坐标
const tooltipPosition = ref<{ x: number, y: number }>({ x: 0, y: 0 })
const currentMousePos = { x: 0, y: 0 }

// 权威图片地址解析器 (本地持久化缓存优先，官方 CDN 兜底)
function getItemIcon(itemOrKey: any): string {
  if (!itemOrKey) return '/assets/icons/aghs_scepter.png'
  const key = typeof itemOrKey === 'string' ? itemOrKey : itemOrKey.key || ''
  return getItemIconUrl(key)
}

function getItemFallbackCdn(itemOrKey: any): string {
  if (!itemOrKey) return 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/recipe.png'
  const key = typeof itemOrKey === 'string' ? itemOrKey : itemOrKey.key || ''
  return getItemCdnUrl(key)
}

const itemMap = computed(() => {
  const map = new Map<string, CatalogItem>()
  for (const it of items.value) {
    map.set(it.key, it)
    map.set(it.key.replace(/^item_/, ''), it)
  }
  return map
})

const materialsMap = ref<Map<string, MaterialItemMeta>>(new Map())

// 进阶装备反查表 (由当前配件能合成哪些更高阶物品，忽略图纸本身的反查)
const upgradesIndex = computed(() => {
  const index = new Map<string, CatalogItem[]>()
  for (const it of items.value) {
    if (it.components) {
      for (const comp of it.components) {
        const clean = comp.replace(/^item_/, '')
        if (clean.startsWith('recipe')) continue
        if (!index.has(clean)) {
          index.set(clean, [])
        }
        index.get(clean)!.push(it)
      }
    }
  }
  return index
})

function getUpgradesForItem(key: string): CatalogItem[] {
  const clean = key.replace(/^item_/, '')
  return upgradesIndex.value.get(clean) || []
}

// 解析物品主动/被动技能特征 (保留用于状态探测与测试契约)
function getItemSkillTypes(item: CatalogItem): { hasActive: boolean, hasPassive: boolean } {
  if (!item) return { hasActive: false, hasPassive: false }
  const desc = item.desc || ''
  const notes = item.notes || ''
  const combined = `${desc}\n${notes}`

  const hasActive
    = Boolean(item.cd && item.cd !== '0' && item.cd !== 0)
      || Boolean(item.mc && item.mc !== '0' && item.mc !== 0)
      || combined.includes('主动：')
      || combined.includes('主动 -')
      || combined.includes('Active:')
      || combined.includes('使用：')

  const hasPassive
    = combined.includes('被动：')
      || combined.includes('被动 -')
      || combined.includes('Passive:')
      || (!hasActive && Boolean(desc.trim()) && item.category !== '消耗品')

  return { hasActive, hasPassive }
}

// 安全获取配件物品模型（包含有效图标与单价，彻底支持图纸与官方价格）
function getComponentItem(compKey: string): MaterialItemMeta {
  const cleanKey = compKey.replace(/^item_/, '')
  if (itemMap.value.has(cleanKey)) {
    const found = itemMap.value.get(cleanKey)!
    return {
      key: found.key,
      dname: found.dname,
      img: getItemIcon(found),
      cost: found.cost,
    }
  }
  if (materialsMap.value.has(cleanKey)) {
    return materialsMap.value.get(cleanKey)!
  }
  if (materialsMap.value.has(compKey)) {
    return materialsMap.value.get(compKey)!
  }

  // 图纸或基础件兜底
  const isRecipe = cleanKey.includes('recipe')
  return {
    key: compKey,
    dname: isRecipe ? '合成卷轴' : cleanKey,
    img: isRecipe
      ? 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/recipe.png'
      : getItemIcon(cleanKey),
    cost: 0,
  }
}

onMounted(async () => {
  try {
    const [{ officialData, constantsJson }, pInfo] = await Promise.all([
      gameDataService.getItemsData(),
      gameDataService.getPatchInfo(),
    ])
    patchInfo.value = pInfo

    // 构建全量物料字典 (覆盖全部基础件、升级件及图纸)
    const matMap = new Map<string, MaterialItemMeta>()
    for (const [key, raw] of Object.entries(officialData) as [string, any][]) {
      const cleanKey = key.replace(/^item_/, '')
      const cost = Number(raw.cost) || 0
      const dname = raw.dname || key
      const img = getItemIcon(key)
      const meta: MaterialItemMeta = { key, dname, cost, img }
      matMap.set(key, meta)
      matMap.set(cleanKey, meta)
    }
    for (const [key, raw] of Object.entries(constantsJson) as [string, any][]) {
      const cleanKey = key.replace(/^item_/, '')
      if (!matMap.has(cleanKey)) {
        const cost = Number(raw.cost) || 0
        const dname = raw.dname || key
        const img = getItemIcon(key)
        const meta: MaterialItemMeta = { key, dname, cost, img }
        matMap.set(key, meta)
        matMap.set(cleanKey, meta)
      }
    }

    // 7.41f / Liquid / 官网权威补充修正
    matMap.set('recipe_heavens_halberd', {
      key: 'recipe_heavens_halberd',
      dname: '图纸（天堂之戟）',
      cost: 350,
      img: getItemIcon('recipe'),
    })
    matMap.set('recipe_hydras_breath', {
      key: 'recipe_hydras_breath',
      dname: '图纸（怪蛇之息）',
      cost: 1000,
      img: getItemIcon('recipe'),
    })
    if (matMap.has('recipe_octarine_core')) {
      matMap.get('recipe_octarine_core')!.cost = 400
    }
    materialsMap.value = matMap

    const list: CatalogItem[] = []

    // 建立 ID/Key 到中文名的映射表以展示小件中文名
    const nameDict: Record<string, string> = {}
    for (const [key, raw] of Object.entries(officialData) as [string, any][]) {
      nameDict[key] = raw.dname || key
      nameDict[`item_${key}`] = raw.dname || key
    }
    for (const [key, raw] of Object.entries(constantsJson) as [string, any][]) {
      if (raw.dname && !nameDict[key]) {
        nameDict[key] = raw.dname
        nameDict[`item_${key}`] = raw.dname
      }
    }

    for (const [key, raw] of Object.entries(officialData) as [string, any][]) {
      // 严格根据现行有效物品白名单过滤废弃物品
      if (!isCurrentItem(key)) {
        continue
      }

      const constItem = constantsJson[key] || constantsJson[`item_${key}`] || {}
      const costNum = Number(raw.cost) || 0
      const tier = getNeutralTier(key)

      const imgName = raw.img ? raw.img.replace(/_lg\.png$/, '') : key
      const imgUrl = `https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/${imgName}.png`

      // 权威分类获取
      const category = getItemCategory(key)
      const isUpgrade = (constItem.components && constItem.components.length > 0) || ['辅助', '法器', '防具', '武器', '神器'].includes(category)

      // 权威获取配方与组件清单 (优先使用官方 requirements 以完整包含 recipe 图纸)
      let rawReq: string[] | null = null
      if (key === 'octarine_core') {
        // 玲珑心官网快照遗漏灵魂之球，在此依据 7.41f 权威补全 (灵魂之球3000 + 赛莉蒙妮之冠1700 + 图纸400 = 5100)
        rawReq = ['soul_booster', 'tiara_of_selemene', 'recipe_octarine_core']
      } else if (key === 'hydras_breath') {
        // 怪蛇之息新装备配方补齐 (行家阵列2550 + 魔龙枪2000 + 淬毒之珠350 + 图纸1000 = 5900)
        rawReq = ['specialists_array', 'dragon_lance', 'orb_of_venom', 'recipe_hydras_breath']
      } else if (Array.isArray(raw.requirements) && raw.requirements.length > 0) {
        rawReq = raw.requirements
      } else if (Array.isArray(constItem.components) && constItem.components.length > 0) {
        rawReq = constItem.components
      }

      // 清洗组件名称 (剥离官网 core item 的 * 号及空项)
      const components = rawReq
        ? rawReq
            .map(c => typeof c === 'string' ? c.replace(/\*+$/, '').trim() : '')
            .filter(Boolean)
        : null

      const componentNames = components
        ? components.map((c) => {
            const clean = c.replace(/^item_/, '')
            return matMap.get(clean)?.dname || nameDict[c] || clean
          })
        : null

      // 宏占位符清洗与权威数值修正
      let desc = raw.desc || (constItem.abilities && constItem.abilities[0]?.description) || ''
      if (desc) {
        desc = desc.replace(/%AbilityCharges%/g, '3')
      }

      // 属性加成兜底：若官方 attrib 为空且常量表有结构化加成，格式化为标准属性行
      let attrib = raw.attrib || ''
      if (!attrib && Array.isArray(constItem.attrib)) {
        const lines: string[] = []
        for (const a of constItem.attrib) {
          const valStr = String(a.value || '').trim()
          if (valStr && valStr !== '0' && valStr !== '0.0') {
            if (a.display) {
              lines.push(a.display.replace('{value}', valStr))
            } else if (a.header || a.footer) {
              lines.push(`${a.header || ''} ${valStr} ${a.footer || ''}`.trim())
            }
          }
        }
        if (lines.length > 0) {
          attrib = lines.join('\n')
        }
      }

      let mc = raw.mc !== undefined && raw.mc !== '' ? raw.mc : (constItem.mc || false)
      let cd = raw.cd !== undefined && raw.cd !== '' ? raw.cd : (constItem.cd || false)

      // 关键实战技能数值校准 (修复官方 datafeed 遗漏或错填)
      if (key === 'nullifier') {
        mc = 75 // 否决坠饰主动技能消耗 75 魔法
      } else if (key === 'hand_of_midas') {
        cd = 100 // 迈达斯之手充能冷却 100 秒
      } else if (key === 'holy_locket') {
        cd = 13 // 圣洁吊坠充能时间 13 秒
      }

      list.push({
        id: raw.id || key,
        key,
        name: key,
        dname: raw.dname || constItem.dname || key,
        cost: costNum,
        desc,
        attrib,
        notes: raw.notes || constItem.notes || '',
        lore: raw.lore || constItem.lore || '',
        mc,
        cd,
        components,
        componentNames,
        img: imgUrl,
        tier,
        category,
        isUpgrade,
      })
    }

    list.sort((a, b) => {
      if (a.tier !== b.tier) return a.tier - b.tier
      return a.cost - b.cost
    })

    items.value = list
  } catch (err) {
    console.error('加载物品数据失败', err)
  } finally {
    loading.value = false
  }
})

const itemGroups = computed(() => {
  const q = appStore.searchQuery.toLowerCase().trim()
  const hasSearch = q.length > 0

  const groups: {
    id: string
    title: string
    enTitle: string
    section: 'basics' | 'upgrades' | 'neutrals'
    accentColor: string
    items: CatalogItem[]
  }[] = []

  for (const def of GROUP_DEFINITIONS) {
    if (mainTab.value !== 'all' && def.section !== mainTab.value) {
      continue
    }

    if (subFilter.value !== 'all') {
      if (def.section === 'neutrals') {
        if (def.id !== `tier_${subFilter.value}`) continue
      } else {
        if (def.id !== subFilter.value) continue
      }
    }

    let list = items.value.filter(def.filter)

    if (hasSearch) {
      list = list.filter((item) => {
        const nameMatch = item.dname.toLowerCase().includes(q) || item.key.toLowerCase().includes(q)
        const aliases = ITEM_ALIASES[item.key] || []
        const aliasMatch = aliases.some(a => a.toLowerCase().includes(q))
        return nameMatch || aliasMatch
      })
    }

    if (list.length > 0) {
      groups.push({
        id: def.id,
        title: def.title,
        enTitle: def.enTitle,
        section: def.section,
        accentColor: def.accentColor,
        items: list,
      })
    }
  }

  return groups
})

const totalItemsCount = computed(() => {
  return itemGroups.value.reduce((acc, g) => acc + g.items.length, 0)
})

// 基于 DOM 元素边界盒精准计算展开位置 (优先在方格右侧展开，不足则翻转至左侧)
function calculateTooltipPosForElement(el: HTMLElement) {
  const rect = el.getBoundingClientRect()
  const cardWidth = 368
  const cardHeight = 420
  const padding = 12

  // 1. 水平定位：优先在元素右侧展开
  let targetX = rect.right + 12
  if (targetX + cardWidth > window.innerWidth - padding) {
    targetX = rect.left - cardWidth - 12
  }
  if (targetX < padding) {
    targetX = Math.max(padding, window.innerWidth - cardWidth - padding)
  }

  // 2. 垂直定位：对齐元素顶部，超出视口底部则向上收敛
  let targetY = rect.top
  if (targetY + cardHeight > window.innerHeight - padding) {
    targetY = Math.max(padding, window.innerHeight - cardHeight - padding)
  }

  tooltipPosition.value = { x: targetX, y: targetY }
}

// 基于鼠标坐标计算自适应 Hover 卡片位置 (通用兜底)
function calculateTooltipPos(mouseX: number, mouseY: number) {
  const cardWidth = 368
  const cardHeight = 420
  const padding = 12

  let targetX = mouseX + 16
  let targetY = mouseY + 14

  if (targetX + cardWidth > window.innerWidth - padding) {
    targetX = mouseX - cardWidth - 14
  }
  if (targetX < padding) {
    targetX = padding
  }

  const viewportH = window.innerHeight
  if (targetY + cardHeight > viewportH - padding) {
    const flippedY = mouseY - cardHeight - 12
    if (flippedY >= padding) {
      targetY = flippedY
    } else {
      targetY = Math.max(padding, viewportH - cardHeight - padding)
    }
  }

  tooltipPosition.value = { x: targetX, y: targetY }
}

function handleMouseMove(e: MouseEvent) {
  currentMousePos.x = e.clientX
  currentMousePos.y = e.clientY
}

function onItemHover(item: CatalogItem, event?: MouseEvent) {
  if (pinnedItem.value) return
  hoveredItem.value = item
  const targetEl = (event?.currentTarget as HTMLElement) || null
  nextTick(() => {
    if (targetEl) {
      calculateTooltipPosForElement(targetEl)
    } else {
      calculateTooltipPos(currentMousePos.x, currentMousePos.y)
    }
  })
}

function onItemLeave() {
  if (pinnedItem.value) return
  hoveredItem.value = null
}

function togglePinItem(item: CatalogItem, event?: MouseEvent) {
  if (event) {
    event.stopPropagation()
  }
  if (pinnedItem.value?.key === item.key) {
    // 再次点击同一固定物品：解除固定
    pinnedItem.value = null
    hoveredItem.value = null
  } else {
    // 点击新物品：切换锁定并立即锚定至新物品旁展开
    pinnedItem.value = item
    hoveredItem.value = null
    const targetEl = (event?.currentTarget as HTMLElement) || null
    nextTick(() => {
      if (targetEl) {
        calculateTooltipPosForElement(targetEl)
      } else {
        const x = event?.clientX ?? currentMousePos.x
        const y = event?.clientY ?? currentMousePos.y
        calculateTooltipPos(x, y)
      }
    })
  }
}

function unpinItem() {
  pinnedItem.value = null
  hoveredItem.value = null
}

function handleGlobalKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && pinnedItem.value) {
    pinnedItem.value = null
  }
}

function handleGlobalClick(e: MouseEvent) {
  if (!pinnedItem.value) return
  if (tooltipRef.value && tooltipRef.value.contains(e.target as Node)) {
    return
  }
  pinnedItem.value = null
}

onMounted(() => {
  window.addEventListener('keydown', handleGlobalKeydown)
  window.addEventListener('click', handleGlobalClick)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleGlobalKeydown)
  window.removeEventListener('click', handleGlobalClick)
})

defineExpose({
  getItemSkillTypes,
})
</script>

<template>
  <div v-if="loading" class="text-sm text-dota-muted bg-dota-bg flex flex-1 items-center justify-center">
    <div class="mr-3 border-dota-gold border-b-2 border-t-2 rounded-full h-8 w-8 animate-spin" />
    正在构建官方现行有效物品与合成图谱数据库 (优先本地缓存)...
  </div>

  <div
    v-else
    class="bg-dota-bg flex flex-1 flex-col select-none relative overflow-hidden"
    @mousemove="handleMouseMove"
  >
    <!-- 顶部单行高密度控制栏 (Dota 2 官网多层分类体系) -->
    <div class="px-4 border-b border-dota-border bg-dota-surface flex shrink-0 gap-4 h-12 items-center justify-between">
      <!-- 左侧：官网顶级分类切换 -->
      <div class="p-0.5 border border-dota-border rounded-lg bg-dota-panel flex items-center space-x-1">
        <button
          v-for="tab in MAIN_TABS"
          :key="tab.id"
          class="text-xs font-bold px-3 py-1 rounded-md cursor-pointer transition-all"
          :class="[
            mainTab === tab.id
              ? 'bg-dota-gold text-black shadow-sm'
              : 'text-dota-muted hover:text-dota-text',
          ]"
          @click="setMainTab(tab.id)"
        >
          {{ tab.label }}
        </button>
      </div>

      <!-- 中间：子分类筛选 Chips (根据当前大类自适应联动) -->
      <div v-if="currentSubFilters.length > 0" class="scrollbar-none text-xs py-1 flex flex-1 items-center overflow-x-auto space-x-1">
        <button
          v-for="sub in currentSubFilters"
          :key="sub.id"
          class="text-xs px-2.5 py-1 rounded cursor-pointer whitespace-nowrap transition-colors"
          :class="[
            subFilter === sub.id
              ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40'
              : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text',
          ]"
          @click="subFilter = sub.id"
        >
          {{ sub.label }}
        </button>
      </div>
      <div v-else class="flex-1" />

      <!-- 右侧：统计计数 -->
      <div class="text-xs text-dota-subtle font-mono shrink-0">
        共 {{ totalItemsCount }} 个物品
      </div>
    </div>

    <!-- 主体：官网式多层分组物品网格 -->
    <div class="p-5 flex-1 overflow-y-auto space-y-7">
      <div v-for="group in itemGroups" :key="group.id" class="space-y-3">
        <!-- 分组头：左侧装饰彩条 + 官方中英文标题 + 数量 -->
        <div class="flex items-center space-x-2.5">
          <div class="rounded-full h-4 w-1 shadow-sm" :class="group.accentColor" />
          <div class="flex items-baseline space-x-2">
            <h2 class="text-sm text-dota-text tracking-wide font-extrabold">
              {{ group.title }}
            </h2>
            <span class="text-xs text-dota-subtle tracking-wider font-mono font-semibold uppercase">
              {{ group.enTitle }}
            </span>
          </div>
          <span class="text-xs text-dota-subtle font-mono">({{ group.items.length }})</span>
        </div>

        <!-- 官网 56x42 标准比例物品格网格 -->
        <div class="gap-2.5 grid auto-rows-max grid-cols-7 lg:grid-cols-13 md:grid-cols-11 sm:grid-cols-9 xl:grid-cols-15">
          <div
            v-for="item in group.items"
            :key="item.key"
            class="group cursor-pointer relative"
            @mouseenter="onItemHover(item, $event)"
            @mouseleave="onItemLeave"
            @click="togglePinItem(item, $event)"
          >
            <!-- 56x42 (w-14 h-10.5) 规格方格容器 -->
            <div
              class="border rounded bg-dota-panel h-10.5 w-14 transition-all relative overflow-hidden group-hover:scale-105"
              :class="[
                pinnedItem?.key === item.key
                  ? 'border-dota-gold ring-2 ring-dota-gold/50 shadow-[0_0_14px_rgba(229,193,88,0.6)]'
                  : 'border-dota-border/80 group-hover:border-dota-gold group-hover:shadow-[0_0_12px_rgba(229,193,88,0.35)]',
              ]"
            >
              <SmartImage
                :src="getItemIcon(item)"
                :fallback-src="getItemFallbackCdn(item)"
                :alt="item.dname"
                class="h-full w-full object-cover"
                loading="lazy"
              />

              <!-- 现行补丁更新微型徽标 (仅针对真实改动装备，左下角) -->
              <div
                v-if="isItemUpdated(item)"
                class="text-[7px] text-amber-200 leading-none font-bold px-1 py-0.5 rounded-tr bg-amber-600/90 pointer-events-none shadow-sm bottom-0 left-0 absolute"
                :title="currentPatch ? `v${currentPatch} 官方调整物品` : '官方调整物品'"
              >
                新
              </div>
            </div>

            <!-- 下方中文品名 -->
            <div
              class="text-xs mt-1 text-center max-w-[56px] truncate transition-colors"
              :class="[
                pinnedItem?.key === item.key ? 'text-dota-gold font-bold' : 'text-dota-muted group-hover:text-dota-goldLight',
              ]"
            >
              {{ item.dname }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 悬浮高保真深色高对比度详情卡片 (完全复用 ItemTooltipCard 统一组件并通过 Teleport 挂载到 body) -->
    <Teleport to="body">
      <ItemTooltipCard
        :item="activeItem"
        :position="tooltipPosition"
        :pinned="Boolean(pinnedItem)"
        :patch-info="patchInfo"
        :get-component-meta="getComponentItem"
        :upgrades="activeItem ? getUpgradesForItem(activeItem.key) : []"
        @unpin="unpinItem"
      />
    </Teleport>
  </div>
</template>
