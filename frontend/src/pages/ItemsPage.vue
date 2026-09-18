<template>
  <div v-if="loading" class="flex-1 flex items-center justify-center text-dota-muted text-sm bg-dota-bg">
    <div class="animate-spin rounded-full h-8 w-8 border-t-2 border-b-2 border-dota-gold mr-3" />
    正在过滤 7.41e 现行有效物品并构建全量图鉴...
  </div>

  <div
    v-else
    class="flex-1 flex flex-col overflow-hidden bg-dota-bg select-none relative"
    @mousemove="handleMouseMove"
  >
    <!-- 顶部单行高密度控制栏 -->
    <div class="h-12 border-b border-dota-border px-4 flex items-center justify-between bg-dota-surface shrink-0 gap-4">
      <!-- 左侧：商店物品 / 中立物品切换 -->
      <div class="flex items-center space-x-1 bg-dota-panel p-0.5 rounded-lg border border-dota-border">
        <button
          @click="mode = 'shop'"
          :class="[
            'px-3 py-1 rounded-md text-xs font-bold transition-all',
            mode === 'shop'
              ? 'bg-dota-gold text-black shadow-sm'
              : 'text-dota-muted hover:text-dota-text'
          ]"
        >
          商店物品
        </button>
        <button
          @click="mode = 'neutral'"
          :class="[
            'px-3 py-1 rounded-md text-xs font-bold transition-all',
            mode === 'neutral'
              ? 'bg-dota-gold text-black shadow-sm'
              : 'text-dota-muted hover:text-dota-text'
          ]"
        >
          中立物品
        </button>
      </div>

      <!-- 中间：子分类筛选 Chips -->
      <div class="flex-1 flex items-center space-x-1 overflow-x-auto text-xs py-1 scrollbar-none">
        <template v-if="mode === 'shop'">
          <button
            v-for="cat in SHOP_CATEGORIES"
            :key="cat"
            @click="shopCategory = cat"
            :class="[
              'px-2.5 py-1 rounded text-xs transition-colors whitespace-nowrap',
              shopCategory === cat
                ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40'
                : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text'
            ]"
          >
            {{ cat }}
          </button>
        </template>
        <template v-else>
          <button
            v-for="tier in NEUTRAL_TIERS"
            :key="tier.id"
            @click="neutralTier = tier.id"
            :class="[
              'px-2.5 py-1 rounded text-xs transition-colors whitespace-nowrap',
              neutralTier === tier.id
                ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40'
                : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text'
            ]"
          >
            {{ tier.label }}
          </button>
        </template>
      </div>

      <!-- 右侧：统计计数 -->
      <div class="text-xs text-dota-subtle font-mono shrink-0">
        共 {{ filteredItems.length }} 个物品
      </div>
    </div>

    <!-- 主体：分组物品网格 -->
    <div class="flex-1 overflow-y-auto p-5 space-y-6">
      <div v-for="group in itemGroups" :key="group.title" class="space-y-3">
        <!-- 分组头：左侧金色彩条 + 标题 + 数量 -->
        <div class="flex items-center space-x-2.5">
          <div class="w-1 h-4 bg-dota-gold rounded-full" />
          <h2 class="text-sm font-bold text-dota-text">{{ group.title }}</h2>
          <span class="text-xs text-dota-subtle font-mono">({{ group.count }})</span>
        </div>

        <!-- 官网 56x42 标准比例物品格 -->
        <div class="grid grid-cols-7 sm:grid-cols-9 md:grid-cols-11 lg:grid-cols-13 xl:grid-cols-15 gap-2 auto-rows-max">
          <div
            v-for="item in group.items"
            :key="item.key"
            @mouseenter="hoveredItem = item"
            @mouseleave="hoveredItem = null"
            class="relative group cursor-pointer"
          >
            <div class="w-14 h-10.5 rounded bg-dota-panel border border-dota-border/80 overflow-hidden group-hover:border-dota-gold group-hover:scale-105 group-hover:shadow-md transition-all">
              <img
                :src="item.img"
                :alt="item.dname"
                class="w-full h-full object-cover"
                loading="lazy"
                @error="(e) => ((e.target as HTMLImageElement).src = '/assets/icons/aghs_scepter.png')"
              />
            </div>
            <div class="mt-0.5 text-[10px] text-center truncate text-dota-muted group-hover:text-dota-text">
              {{ item.dname }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 悬浮高保真深色高对比度详情卡片 (ToolTip) -->
    <div
      v-if="hoveredItem"
      :style="{
        position: 'fixed',
        left: `${mousePos.x}px`,
        top: `${mousePos.y}px`,
        pointerEvents: 'none',
        zIndex: 9999,
      }"
      class="w-80 p-4 rounded-xl bg-[#141720]/95 backdrop-blur-md border border-[#405068] shadow-2xl space-y-3 select-none"
    >
      <div class="flex items-start space-x-3">
        <div class="w-14 h-10.5 rounded bg-dota-panel border border-dota-gold/50 overflow-hidden shrink-0">
          <img :src="hoveredItem.img" :alt="hoveredItem.dname" class="w-full h-full object-cover" />
        </div>
        <div class="flex-1 min-w-0">
          <h3 class="font-bold text-sm text-dota-goldLight truncate">{{ hoveredItem.dname }}</h3>
          <div class="flex items-center space-x-2 mt-0.5 text-xs font-mono">
            <div v-if="hoveredItem.cost > 0" class="flex items-center space-x-1 text-dota-gold font-bold">
              <Coins class="w-3.5 h-3.5" />
              <span>{{ hoveredItem.cost }}</span>
            </div>
            <span v-else class="text-dota-cyan">
              {{ hoveredItem.tier > 0 ? `第 ${hoveredItem.tier} 阶中立` : '免费' }}
            </span>
            <span class="text-dota-subtle">·</span>
            <span class="text-dota-muted">{{ hoveredItem.category }}</span>
          </div>
        </div>
      </div>

      <!-- 属性加成 -->
      <div v-if="hoveredItem.attrib" class="text-xs text-dota-green leading-relaxed whitespace-pre-line border-t border-dota-border/60 pt-2 font-medium">
        {{ cleanHtml(hoveredItem.attrib) }}
      </div>

      <!-- 技能与主动/被动效果 -->
      <div v-if="hoveredItem.desc" class="text-xs text-dota-text leading-relaxed whitespace-pre-line bg-dota-surface/50 p-2 rounded-lg border border-dota-border/40">
        {{ cleanHtml(hoveredItem.desc) }}
      </div>

      <!-- 冷却与魔耗 -->
      <div v-if="hoveredItem.cd || hoveredItem.mc" class="flex items-center space-x-4 text-xs font-mono text-dota-subtle border-t border-dota-border/40 pt-1.5">
        <div v-if="hoveredItem.cd" class="flex items-center space-x-1 text-dota-gold">
          <Clock class="w-3.5 h-3.5" />
          <span>{{ hoveredItem.cd }}s</span>
        </div>
        <div v-if="hoveredItem.mc" class="flex items-center space-x-1 text-dota-cyan">
          <Droplet class="w-3.5 h-3.5 fill-current" />
          <span>{{ hoveredItem.mc }}</span>
        </div>
      </div>

      <!-- 合成配方配件 (带官网 56x42 规格微缩图标 + 物品中文名 + 金币价格) -->
      <div v-if="hoveredItem.components && hoveredItem.components.length > 0" class="border-t border-dota-border/60 pt-2.5 space-y-1.5">
        <div class="text-xs text-dota-subtle flex items-center space-x-1.5 font-medium">
          <Package class="w-3.5 h-3.5 text-dota-gold" />
          <span>合成路线与配件清单 ({{ hoveredItem.components.length }} 件):</span>
        </div>
        <div class="flex items-center space-x-2 flex-wrap gap-1.5">
          <div
            v-for="(compKey, i) in hoveredItem.components"
            :key="i"
            class="flex items-center space-x-1.5 px-2 py-1 rounded bg-dota-surface border border-dota-border hover:border-dota-gold/60 transition-colors shadow-sm"
          >
            <!-- 56x42 规格微缩图标 -->
            <img
              :src="`https://cdn.cloudflare.steamstatic.com${getComponentItem(compKey).img || '/apps/dota2/images/dota_react/items/' + compKey + '.png'}`"
              :alt="getComponentItem(compKey).dname"
              class="w-7 h-5 object-cover rounded-sm border border-dota-border/60 shrink-0"
              @error="(e) => ((e.target as HTMLElement).src = 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/recipe.png')"
            />
            <span class="text-xs text-dota-text font-medium">{{ getComponentItem(compKey).dname || hoveredItem.componentNames?.[i] || compKey }}</span>
            <span v-if="getComponentItem(compKey).cost > 0" class="text-[11px] text-dota-gold font-mono flex items-center">
              <Coins class="w-2.5 h-2.5 mr-0.5" />
              {{ getComponentItem(compKey).cost }}
            </span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { isCurrentItem, getNeutralTier, getItemCategory } from '../data/currentItemCatalog';
import { ITEM_ALIASES } from '../data/aliases';
import { useAppStore } from '../store';
import { Coins, Package, Clock, Droplet } from 'lucide-vue-next';

interface CatalogItem {
  id: string | number;
  key: string;
  name: string;
  dname: string;
  cost: number;
  desc: string;
  attrib: string;
  notes: string;
  lore: string;
  mc: string | number | boolean;
  cd: string | number | boolean;
  components: string[] | null;
  componentNames: string[] | null;
  img: string;
  tier: number;
  category: string;
  isUpgrade: boolean;
}

const SHOP_CATEGORIES = [
  '全部',
  '消耗品',
  '属性',
  '军备',
  '奥术',
  '神秘商店',
  '辅助',
  '法器',
  '防具',
  '武器',
  '神器',
];

const NEUTRAL_TIERS = [
  { id: 0, label: '全部阶级' },
  { id: 1, label: '第 1 阶' },
  { id: 2, label: '第 2 阶' },
  { id: 3, label: '第 3 阶' },
  { id: 4, label: '第 4 阶' },
  { id: 5, label: '第 5 阶' },
];

const { state } = useAppStore();
const items = ref<CatalogItem[]>([]);
const mode = ref<'shop' | 'neutral'>('shop');
const shopCategory = ref<string>('全部');
const neutralTier = ref<number>(0);
const hoveredItem = ref<CatalogItem | null>(null);
const mousePos = ref<{ x: number; y: number }>({ x: 0, y: 0 });
const loading = ref(true);

const cleanHtml = (text: string): string => {
  if (!text) return '';
  return text
    .replace(/<font color="[^"]*">/g, '')
    .replace(/<\/font>/g, '')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<[^>]+>/g, '')
    .trim();
};

const itemMap = computed(() => {
  const map = new Map<string, CatalogItem>();
  for (const it of items.value) {
    map.set(it.key, it);
    map.set(it.key.replace(/^item_/, ''), it);
  }
  return map;
});

const getComponentItem = (compKey: string) => {
  const cleanKey = compKey.replace(/^item_/, '');
  if (itemMap.value.has(cleanKey)) {
    return itemMap.value.get(cleanKey)!;
  }
  if (itemMap.value.has(compKey)) {
    return itemMap.value.get(compKey)!;
  }
  return {
    key: compKey,
    name: compKey,
    dname: compKey.includes('recipe') ? '合成图纸' : compKey,
    img: '/apps/dota2/images/dota_react/items/recipe.png',
    cost: 0,
  };
};

onMounted(async () => {
  try {
    const [officialRes, constantsRes] = await Promise.all([
      fetch('/assets/data/items-details-official.json'),
      fetch('/assets/data/item_constants.json')
    ]);
    const officialJson = await officialRes.json();
    const constantsJson = await constantsRes.json();

    const officialData = officialJson.itemdata || {};
    const list: CatalogItem[] = [];

    // 建立 ID/Key 到中文名的映射表以展示小件中文名
    const nameDict: Record<string, string> = {};
    for (const [key, raw] of Object.entries(officialData) as [string, any][]) {
      nameDict[key] = raw.dname || key;
      nameDict[`item_${key}`] = raw.dname || key;
    }
    for (const [key, raw] of Object.entries(constantsJson) as [string, any][]) {
      if (raw.dname && !nameDict[key]) {
        nameDict[key] = raw.dname;
        nameDict[`item_${key}`] = raw.dname;
      }
    }

    for (const [key, raw] of Object.entries(officialData) as [string, any][]) {
      // 严格根据 7.41e 白名单过滤废弃物品
      if (!isCurrentItem(key)) {
        continue;
      }

      const constItem = constantsJson[key] || constantsJson[`item_${key}`] || {};
      const costNum = Number(raw.cost) || 0;
      const tier = getNeutralTier(key);

      const imgName = raw.img ? raw.img.replace(/_lg\.png$/, '') : key;
      const imgUrl = `https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/${imgName}.png`;

      // 权威分类获取
      const category = getItemCategory(key);
      const isUpgrade = (constItem.components && constItem.components.length > 0) || ['辅助', '法器', '防具', '武器', '神器'].includes(category);

      // 解析配件中文名
      const rawComponents = Array.isArray(constItem.components) ? constItem.components : null;
      const componentNames = rawComponents
        ? rawComponents.map((c: string) => nameDict[c] || c.replace(/^item_/, ''))
        : null;

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
      });
    }

    list.sort((a, b) => {
      if (a.tier !== b.tier) return a.tier - b.tier;
      return a.cost - b.cost;
    });

    items.value = list;
  } catch (err) {
    console.error('加载物品数据失败', err);
  } finally {
    loading.value = false;
  }
});

const filteredItems = computed(() => {
  return items.value.filter((item) => {
    if (mode.value === 'shop') {
      if (item.tier > 0) return false;
      if (shopCategory.value !== '全部' && item.category !== shopCategory.value) return false;
    } else {
      if (item.tier === 0) return false;
      if (neutralTier.value !== 0 && item.tier !== neutralTier.value) return false;
    }

    if (state.searchQuery.trim()) {
      const q = state.searchQuery.toLowerCase().trim();
      const nameMatch = item.dname.toLowerCase().includes(q) || item.key.toLowerCase().includes(q);
      const aliases = ITEM_ALIASES[item.key] || [];
      const aliasMatch = aliases.some((a) => a.toLowerCase().includes(q));
      return nameMatch || aliasMatch;
    }
    return true;
  });
});

const itemGroups = computed(() => {
  const groups: { title: string; count: number; items: CatalogItem[] }[] = [];

  if (mode.value === 'shop') {
    if (shopCategory.value !== '全部') {
      // 选中了特定分类，直接单组展示
      groups.push({
        title: shopCategory.value,
        count: filteredItems.value.length,
        items: filteredItems.value,
      });
    } else {
      // 全部模式：按“基础散件”与“进阶装备”两级标准货架分组
      const basic = filteredItems.value.filter((i) => !i.isUpgrade);
      const upgrades = filteredItems.value.filter((i) => i.isUpgrade);

      if (basic.length > 0) {
        groups.push({ title: '基础散件 (消耗品 · 属性 · 军备 · 奥术 · 神秘商店)', count: basic.length, items: basic });
      }
      if (upgrades.length > 0) {
        groups.push({ title: '进阶合成装备 (辅助 · 法器 · 防具 · 武器 · 神器)', count: upgrades.length, items: upgrades });
      }
    }
  } else {
    for (let t = 1; t <= 5; t++) {
      const tierItems = filteredItems.value.filter((i) => i.tier === t);
      if (tierItems.length > 0) {
        groups.push({ title: `第 ${t} 阶中立宝物`, count: tierItems.length, items: tierItems });
      }
    }
  }

  return groups;
});

const handleMouseMove = (e: MouseEvent) => {
  const maxX = window.innerWidth - 360;
  const maxY = window.innerHeight - 380;
  mousePos.value = {
    x: Math.min(e.clientX + 16, maxX),
    y: Math.min(e.clientY + 16, maxY),
  };
};
</script>
