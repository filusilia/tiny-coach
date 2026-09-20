<script setup lang="ts">
import { Clock, Coins, Droplet, GitBranch, Package } from '@lucide/vue'
import { computed, nextTick, onMounted, ref } from 'vue'
import { ITEM_ALIASES } from '../data/aliases'
import { getItemCategory, getNeutralTier, isCurrentItem } from '../data/currentItemCatalog'
import { useAppStore } from '../store'
import { cleanHtml } from '../utils/htmlCleaner'

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

const { state } = useAppStore()
const items = ref<CatalogItem[]>([])
const mainTab = ref<MainTab>('all')
const subFilter = ref<string>('all')
const hoveredItem = ref<CatalogItem | null>(null)
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

// 权威图片地址解析器 (杜绝双重前缀与协议截断)
function getItemIcon(itemOrKey: any): string {
  if (!itemOrKey) return '/assets/icons/aghs_scepter.png'

  if (typeof itemOrKey === 'object') {
    const rawImg = itemOrKey.img
    if (typeof rawImg === 'string' && rawImg.length > 0) {
      if (rawImg.startsWith('http://') || rawImg.startsWith('https://')) {
        return rawImg
      }
      if (rawImg.startsWith('/')) {
        return `https://cdn.cloudflare.steamstatic.com${rawImg}`
      }
      const clean = rawImg.replace(/_lg\.png$/, '')
      return `https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/${clean}.png`
    }
  }

  const key = typeof itemOrKey === 'string' ? itemOrKey : itemOrKey.key || ''
  const cleanKey = key.replace(/^item_/, '')
  if (cleanKey.includes('recipe')) {
    return 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/recipe.png'
  }
  return `https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/${cleanKey}.png`
}

const itemMap = computed(() => {
  const map = new Map<string, CatalogItem>()
  for (const it of items.value) {
    map.set(it.key, it)
    map.set(it.key.replace(/^item_/, ''), it)
  }
  return map
})

// 进阶装备反查表 (由当前配件能合成哪些更高阶物品)
const upgradesIndex = computed(() => {
  const index = new Map<string, CatalogItem[]>()
  for (const it of items.value) {
    if (it.components) {
      for (const comp of it.components) {
        const clean = comp.replace(/^item_/, '')
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

// 解析物品主动/被动技能特征
function getItemSkillTypes(item: CatalogItem): { hasActive: boolean, hasPassive: boolean } {
  if (!item) return { hasActive: false, hasPassive: false }
  const desc = item.desc || ''
  const notes = item.notes || ''
  const combined = `${desc}\n${notes}`

  const hasActive
    = Boolean(item.cd && item.cd !== '0' && item.cd !== false)
      || Boolean(item.mc && item.mc !== '0' && item.mc !== false)
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

// 解析属性加成文本并切分为紧凑条目
function parseAttributes(attribStr: string): string[] {
  if (!attribStr) return []
  const clean = cleanHtml(attribStr)
  return clean
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(line => line.length > 0)
}

// 安全获取配件物品模型（包含有效图标与单价）
function getComponentItem(compKey: string) {
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
  if (itemMap.value.has(compKey)) {
    const found = itemMap.value.get(compKey)!
    return {
      key: found.key,
      dname: found.dname,
      img: getItemIcon(found),
      cost: found.cost,
    }
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
    const [officialRes, constantsRes] = await Promise.all([
      fetch('/assets/data/items-details-official.json'),
      fetch('/assets/data/item_constants.json'),
    ])
    const officialJson = await officialRes.json()
    const constantsJson = await constantsRes.json()

    const officialData = officialJson.itemdata || {}
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
      // 严格根据 7.41e 白名单过滤废弃物品
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
      const isUpgrade
        = (constItem.components && constItem.components.length > 0)
          || ['辅助', '法器', '防具', '武器', '神器'].includes(category)

      // 解析配件中文名
      const rawComponents = Array.isArray(constItem.components) ? constItem.components : null
      const componentNames = rawComponents
        ? rawComponents.map((c: string) => nameDict[c] || c.replace(/^item_/, ''))
        : null

      list.push({
        id: raw.id || key,
        key,
        name: key,
        dname: raw.dname || constItem.dname || key,
        cost: costNum,
        desc: raw.desc || (constItem.abilities && constItem.abilities[0]?.description) || '',
        attrib: raw.attrib || '',
        notes: raw.notes || constItem.notes || '',
        lore: raw.lore || constItem.lore || '',
        mc: raw.mc || constItem.mc || false,
        cd: raw.cd || constItem.cd || false,
        components: rawComponents,
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
  const q = state.searchQuery.toLowerCase().trim()
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

// 计算自适应 Hover 卡片位置 (确保不超出窗口上下左右边界)
function calculateTooltipPos(mouseX: number, mouseY: number) {
  const cardWidth = 340 // 对应 w-84
  const cardHeight = tooltipRef.value ? tooltipRef.value.offsetHeight : 380
  const padding = 14

  let targetX = mouseX + 16
  let targetY = mouseY + 16

  // 1. 水平边界判定：如果右侧超出，翻转至鼠标左侧
  if (targetX + cardWidth > window.innerWidth - padding) {
    targetX = mouseX - cardWidth - 14
  }
  // 保证不越过左边缘
  if (targetX < padding) {
    targetX = padding
  }

  // 2. 垂直边界判定：如果底部超出，将 Placement 改为 Top (翻转至鼠标上方)
  if (targetY + cardHeight > window.innerHeight - padding) {
    targetY = mouseY - cardHeight - 14
  }
  // 保证不越过顶部边缘
  if (targetY < padding) {
    targetY = padding
  }

  tooltipPosition.value = { x: targetX, y: targetY }
}

function handleMouseMove(e: MouseEvent) {
  currentMousePos.x = e.clientX
  currentMousePos.y = e.clientY

  if (hoveredItem.value) {
    calculateTooltipPos(e.clientX, e.clientY)
  }
}

function onItemHover(item: CatalogItem) {
  hoveredItem.value = item
  nextTick(() => {
    calculateTooltipPos(currentMousePos.x, currentMousePos.y)
  })
}
</script>

<template>
  <div v-if="loading" class="text-dota-muted bg-dota-bg text-sm flex flex-1 items-center justify-center">
    <div class="border-dota-gold mr-3 border-b-2 border-t-2 rounded-full h-8 w-8 animate-spin" />
    正在构建 7.41e 现行有效物品与合成图谱数据库...
  </div>

  <div
    v-else
    class="bg-dota-bg flex flex-1 flex-col select-none relative overflow-hidden"
    @mousemove="handleMouseMove"
  >
    <!-- 顶部单行高密度控制栏 (Dota 2 官网多层分类体系) -->
    <div class="border-dota-border bg-dota-surface px-4 border-b flex shrink-0 gap-4 h-12 items-center justify-between">
      <!-- 左侧：官网顶级分类切换 -->
      <div class="bg-dota-panel border-dota-border p-0.5 border rounded-lg flex items-center space-x-1">
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
      <div class="text-dota-subtle text-xs font-mono shrink-0">
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
            <h2 class="text-dota-text text-sm tracking-wide font-extrabold">
              {{ group.title }}
            </h2>
            <span class="text-dota-subtle text-[10px] tracking-wider font-mono font-semibold uppercase">
              {{ group.enTitle }}
            </span>
          </div>
          <span class="text-dota-subtle text-xs font-mono">({{ group.items.length }})</span>
        </div>

        <!-- 官网 56x42 标准比例物品格网格 -->
        <div class="gap-2.5 grid auto-rows-max grid-cols-7 lg:grid-cols-13 md:grid-cols-11 sm:grid-cols-9 xl:grid-cols-15">
          <div
            v-for="item in group.items"
            :key="item.key"
            class="group cursor-pointer relative"
            @mouseenter="onItemHover(item)"
            @mouseleave="hoveredItem = null"
          >
            <!-- 56x42 (w-14 h-10.5) 规格方格容器 -->
            <div class="bg-dota-panel border-dota-border/80 group-hover:border-dota-gold border rounded h-10.5 w-14 transition-all relative overflow-hidden group-hover:shadow-[0_0_12px_rgba(229,193,88,0.35)] group-hover:scale-105">
              <img
                :src="getItemIcon(item)"
                :alt="item.dname"
                class="h-full w-full object-cover"
                loading="lazy"
                @error="(e) => ((e.target as HTMLImageElement).src = '/assets/icons/aghs_scepter.png')"
              >

              <!-- 左上角主动/被动微型徽标 -->
              <div
                v-if="getItemSkillTypes(item).hasActive"
                class="text-[8px] text-slate-950 leading-none font-black px-1 py-0.5 rounded-br bg-amber-500 pointer-events-none shadow-sm left-0 top-0 absolute"
                title="主动技能装备"
              >
                主
              </div>
              <div
                v-else-if="getItemSkillTypes(item).hasPassive"
                class="text-[8px] text-white leading-none font-black px-1 py-0.5 rounded-br bg-emerald-600 pointer-events-none shadow-sm left-0 top-0 absolute"
                title="被动技能装备"
              >
                被
              </div>

              <!-- 右上角售价/中立阶级微型徽章 -->
              <div
                v-if="item.cost > 0"
                class="text-[8px] text-amber-300 font-bold font-mono px-1 border-b border-l border-white/10 rounded-bl bg-black/80 pointer-events-none right-0 top-0 absolute"
              >
                {{ item.cost }}
              </div>
              <div
                v-else-if="item.tier > 0"
                class="text-[8px] text-emerald-300 font-bold font-mono px-1 border-b border-l border-emerald-500/30 rounded-bl bg-emerald-950/85 pointer-events-none right-0 top-0 absolute"
              >
                T{{ item.tier }}
              </div>
            </div>

            <!-- 下方中文品名 -->
            <div class="text-dota-muted group-hover:text-dota-goldLight text-[10px] mt-1 text-center max-w-[56px] truncate transition-colors">
              {{ item.dname }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 悬浮高保真深色高对比度详情卡片 (ToolTip / 自适应防溢出) -->
    <div
      v-if="hoveredItem"
      ref="tooltipRef"
      :style="{
        position: 'fixed',
        left: `${tooltipPosition.x}px`,
        top: `${tooltipPosition.y}px`,
        pointerEvents: 'none',
        zIndex: 9999,
      }"
      class="p-4 border border-[#405068] rounded-xl bg-[#141720]/95 w-84 select-none shadow-2xl transition-all duration-75 backdrop-blur-md space-y-3"
    >
      <!-- 1. 顶部：图标 + 名称 + 价格/阶级 + 分类 + 主动/被动标签 -->
      <div class="flex items-start space-x-3">
        <div class="bg-dota-panel border-dota-gold/50 border rounded shrink-0 h-10.5 w-14 shadow-md overflow-hidden">
          <img
            :src="getItemIcon(hoveredItem)"
            :alt="hoveredItem.dname"
            class="h-full w-full object-cover"
            @error="(e) => ((e.target as HTMLImageElement).src = '/assets/icons/aghs_scepter.png')"
          >
        </div>
        <div class="flex-1 min-w-0">
          <div class="flex items-center space-x-2">
            <h3 class="text-dota-goldLight text-sm font-bold truncate">
              {{ hoveredItem.dname }}
            </h3>
            <!-- 主动/被动标签 -->
            <span
              v-if="getItemSkillTypes(hoveredItem).hasActive"
              class="text-[9px] text-amber-300 font-bold px-1.5 py-0.2 border border-amber-500/50 rounded bg-amber-500/20 shrink-0"
            >
              主动技能
            </span>
            <span
              v-if="getItemSkillTypes(hoveredItem).hasPassive"
              class="text-[9px] text-emerald-300 font-bold px-1.5 py-0.2 border border-emerald-500/50 rounded bg-emerald-500/20 shrink-0"
            >
              被动技能
            </span>
          </div>

          <div class="text-xs font-mono mt-0.5 flex items-center space-x-2">
            <div v-if="hoveredItem.cost > 0" class="text-dota-gold font-bold flex items-center space-x-1">
              <Coins class="h-3.5 w-3.5" />
              <span>{{ hoveredItem.cost }}</span>
            </div>
            <span v-else class="text-dota-cyan font-bold">
              {{ hoveredItem.tier > 0 ? `第 ${hoveredItem.tier} 阶中立` : '免费' }}
            </span>
            <span class="text-dota-subtle">·</span>
            <span class="text-dota-muted">{{ hoveredItem.category }}</span>
          </div>
        </div>
      </div>

      <!-- 2. 紧凑网格属性加成展示 (2 列药丸排版，信息密度最大化) -->
      <div v-if="hoveredItem.attrib" class="border-dota-border/60 pt-2 border-t space-y-1">
        <div class="text-dota-subtle text-[10px] tracking-wider font-bold uppercase">
          基础属性加成
        </div>
        <div class="gap-1.5 grid grid-cols-2">
          <div
            v-for="(attr, idx) in parseAttributes(hoveredItem.attrib)"
            :key="idx"
            class="bg-dota-surface/90 border-dota-border/70 text-[11px] text-emerald-300 leading-tight font-mono px-2 py-1 border rounded flex shadow-xs items-center"
          >
            {{ attr }}
          </div>
        </div>
      </div>

      <!-- 3. 技能与主动/被动效果描述 -->
      <div v-if="hoveredItem.desc" class="text-dota-text bg-dota-surface/60 border-dota-border/40 text-xs leading-relaxed p-2.5 border rounded-lg whitespace-pre-line">
        {{ cleanHtml(hoveredItem.desc) }}
      </div>

      <!-- 4. 冷却与魔耗 -->
      <div v-if="hoveredItem.cd || hoveredItem.mc" class="text-dota-subtle border-dota-border/40 text-xs font-mono pt-1.5 border-t flex items-center space-x-4">
        <div v-if="hoveredItem.cd" class="text-dota-gold flex items-center space-x-1">
          <Clock class="h-3.5 w-3.5" />
          <span>{{ hoveredItem.cd }}s 冷却</span>
        </div>
        <div v-if="hoveredItem.mc" class="text-dota-cyan flex items-center space-x-1">
          <Droplet class="h-3.5 w-3.5 fill-current" />
          <span>{{ hoveredItem.mc }} 魔法消耗</span>
        </div>
      </div>

      <!-- 5. 合成配方与前置配件清单 (100% 补齐图标与价格) -->
      <div v-if="hoveredItem.components && hoveredItem.components.length > 0" class="border-dota-border/60 pt-2.5 border-t space-y-1.5">
        <div class="text-dota-goldLight text-xs font-bold flex items-center space-x-1.5">
          <Package class="text-dota-gold h-3.5 w-3.5" />
          <span>合成配方前置配件 ({{ hoveredItem.components.length }} 件):</span>
        </div>
        <div class="flex flex-wrap gap-1.5 items-center space-x-2">
          <div
            v-for="(compKey, i) in hoveredItem.components"
            :key="i"
            class="bg-dota-surface/90 border-dota-border hover:border-dota-gold/60 px-2 py-1 border rounded flex shadow-sm transition-colors items-center space-x-1.5"
          >
            <!-- 56x42 规格微缩图标 (保证绝对 URL 或占位图) -->
            <img
              :src="getComponentItem(compKey).img"
              :alt="getComponentItem(compKey).dname"
              class="border-dota-border/60 border rounded-xs shrink-0 h-4.5 w-6 object-cover"
              @error="(e) => ((e.target as HTMLImageElement).src = 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/recipe.png')"
            >
            <span class="text-dota-text text-xs font-medium">{{ getComponentItem(compKey).dname }}</span>
            <span v-if="getComponentItem(compKey).cost > 0" class="text-dota-gold text-[10px] font-mono flex items-center">
              <Coins class="mr-0.5 h-2.5 w-2.5" />
              {{ getComponentItem(compKey).cost }}
            </span>
          </div>
        </div>
      </div>

      <!-- 6. 进阶合成去向 (可合成的进阶装备) -->
      <div v-if="getUpgradesForItem(hoveredItem.key).length > 0" class="border-dota-border/60 pt-2 border-t space-y-1.5">
        <div class="text-dota-cyan text-[11px] font-bold flex items-center space-x-1">
          <GitBranch class="text-dota-cyan h-3 w-3" />
          <span>可用于进阶合成:</span>
        </div>
        <div class="flex flex-wrap gap-1 items-center space-x-1.5">
          <div
            v-for="upg in getUpgradesForItem(hoveredItem.key)"
            :key="upg.key"
            class="bg-dota-surface/70 border-dota-border text-[10px] px-1.5 py-0.5 border rounded flex items-center space-x-1"
            :title="`${upg.dname} (${upg.cost}金)`"
          >
            <img
              :src="getItemIcon(upg)"
              :alt="upg.dname"
              class="rounded-xs shrink-0 h-3.5 w-5 object-cover"
              @error="(e) => ((e.target as HTMLImageElement).src = '/assets/icons/aghs_scepter.png')"
            >
            <span class="text-dota-text font-medium">{{ upg.dname }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
