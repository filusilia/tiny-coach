<template>
  <div v-if="loading" class="flex-1 flex items-center justify-center text-dota-muted text-base bg-dota-bg">
    <div class="animate-spin rounded-full h-8 w-8 border-t-2 border-b-2 border-dota-gold mr-3" />
    正在读取 127 英雄官方图鉴与技能数据库...
  </div>

  <!-- 视图 2：单独英雄全屏官方风格详情视图 (对齐 https://www.dota2.com.cn/hero/xxx 官方布局) -->
  <div v-else-if="activeHero" class="flex-1 flex flex-col overflow-hidden bg-dota-bg select-none">
    <!-- 顶部导航条 -->
    <div class="h-12 border-b border-dota-border px-6 flex items-center justify-between bg-dota-surface shrink-0 z-10 shadow-sm">
      <button
        @click="activeHero = null"
        class="flex items-center space-x-2 px-3 py-1.5 rounded-lg bg-dota-panel hover:bg-dota-hover border border-dota-border text-xs text-dota-gold hover:text-dota-goldLight transition-all cursor-pointer shadow-sm font-medium"
      >
        <ArrowLeft class="w-4 h-4" />
        <span>返回英雄大图鉴</span>
      </button>

      <div class="flex items-center space-x-4 text-xs">
        <div class="flex items-center space-x-1.5 text-dota-subtle font-mono">
          <span>英雄编号:</span>
          <span class="text-dota-cyan font-bold">#{{ activeHero.id }}</span>
        </div>
        <div class="h-3 w-[1px] bg-dota-border" />
        <div class="flex items-center space-x-1.5">
          <img
            :src="activeHero.attack_type === 'Melee' ? '/assets/icons/melee.svg' : '/assets/icons/ranged.svg'"
            alt="攻击类型"
            class="w-3.5 h-3.5 object-contain"
          />
          <span class="text-dota-text font-medium">
            {{ activeHero.attack_type === 'Melee' ? '近战攻击' : '远程攻击' }}
          </span>
        </div>
        <div class="h-3 w-[1px] bg-dota-border" />
        <div class="flex items-center space-x-1 text-dota-gold">
          <span class="text-dota-subtle">复杂度:</span>
          <span class="font-mono font-bold tracking-wider">
            {{ '◆'.repeat(activeDetails?.complexity || 1) }}{{ '◇'.repeat(Math.max(0, 3 - (activeDetails?.complexity || 1))) }}
          </span>
        </div>
      </div>
    </div>

    <!-- 详情内容可滚动大画幅区 -->
    <div class="flex-1 overflow-y-auto p-6 space-y-6">
      <!-- 1. 英雄全景大横幅 (Hero Header Showcase) -->
      <div class="relative rounded-2xl bg-gradient-to-r from-dota-panel via-dota-panel/90 to-dota-surface border border-dota-border overflow-hidden p-6 shadow-xl flex flex-col lg:flex-row items-start justify-between gap-6">
        <!-- 背景半透光效与立绘 -->
        <div
          v-if="activeHero.img"
          class="absolute right-0 top-0 bottom-0 w-1/2 opacity-15 pointer-events-none bg-no-repeat bg-cover bg-right"
          :style="{ backgroundImage: `url(https://cdn.cloudflare.steamstatic.com${activeHero.img})` }"
        />

        <!-- 左侧：立绘头像、名称、英文代号、定位与生平 -->
        <div class="relative z-10 flex items-start space-x-5 max-w-xl">
          <img
            :src="`https://cdn.cloudflare.steamstatic.com${activeHero.img}`"
            :alt="activeHero.localized_name"
            class="w-28 h-18 object-cover rounded-xl shadow-2xl border-2 border-dota-border/80 shrink-0"
          />
          <div class="space-y-1.5">
            <div class="flex items-baseline space-x-3">
              <h1 class="text-3xl font-extrabold text-dota-gold tracking-wide">{{ activeHero.localized_name }}</h1>
              <span class="text-xs text-dota-subtle font-mono uppercase tracking-widest">{{ activeHero.name.replace('npc_dota_hero_', '') }}</span>
            </div>
            
            <div class="flex items-center space-x-2 text-xs text-dota-muted flex-wrap gap-y-1">
              <span class="text-dota-gold font-medium">核心定位:</span>
              <span>{{ activeHero.roles.join(' · ') }}</span>
            </div>

            <!-- 背景简述 -->
            <p v-if="activeDetails?.hype_loc" class="text-xs text-dota-subtle leading-relaxed line-clamp-2 pt-1">
              {{ activeDetails.hype_loc }}
            </p>
          </div>
        </div>

        <!-- 右侧：官方三大核心属性卡片 + 生命魔法条 + 8格衍生属性网格 -->
        <div class="relative z-10 flex flex-col space-y-3 w-full lg:w-auto shrink-0 font-sans">
          <!-- 三大属性横排卡片 (力量、敏捷、智力) -->
          <div class="grid grid-cols-3 gap-2 text-xs">
            <!-- 力量卡片 -->
            <div
              :class="[
                'p-2.5 rounded-xl border flex items-center space-x-2.5 transition-all',
                activeHero.primary_attr === 'str'
                  ? 'bg-red-950/40 border-dota-red/80 shadow-[0_0_12px_rgba(239,68,68,0.25)]'
                  : 'bg-dota-surface/70 border-dota-border/60'
              ]"
            >
              <img src="/assets/icons/hero_strength.png" alt="力量" class="w-6 h-6 object-contain" />
              <div>
                <div class="text-[11px] text-dota-subtle font-medium flex items-center space-x-1">
                  <span>力量</span>
                  <span v-if="activeHero.primary_attr === 'str'" class="text-[9px] px-1 rounded bg-dota-red/30 text-dota-red font-bold">主属性</span>
                </div>
                <div class="text-dota-text font-mono font-bold text-sm">
                  {{ activeDetails?.str_base || activeHero.base_str }}
                  <span class="text-[11px] text-dota-subtle font-normal">+{{ activeDetails?.str_gain?.toFixed(1) || activeHero.str_gain?.toFixed(1) }}</span>
                </div>
              </div>
            </div>

            <!-- 敏捷卡片 -->
            <div
              :class="[
                'p-2.5 rounded-xl border flex items-center space-x-2.5 transition-all',
                activeHero.primary_attr === 'agi'
                  ? 'bg-emerald-950/40 border-dota-green/80 shadow-[0_0_12px_rgba(16,185,129,0.25)]'
                  : 'bg-dota-surface/70 border-dota-border/60'
              ]"
            >
              <img src="/assets/icons/hero_agility.png" alt="敏捷" class="w-6 h-6 object-contain" />
              <div>
                <div class="text-[11px] text-dota-subtle font-medium flex items-center space-x-1">
                  <span>敏捷</span>
                  <span v-if="activeHero.primary_attr === 'agi'" class="text-[9px] px-1 rounded bg-dota-green/30 text-dota-green font-bold">主属性</span>
                </div>
                <div class="text-dota-text font-mono font-bold text-sm">
                  {{ activeDetails?.agi_base || activeHero.base_agi }}
                  <span class="text-[11px] text-dota-subtle font-normal">+{{ activeDetails?.agi_gain?.toFixed(1) || activeHero.agi_gain?.toFixed(1) }}</span>
                </div>
              </div>
            </div>

            <!-- 智力卡片 -->
            <div
              :class="[
                'p-2.5 rounded-xl border flex items-center space-x-2.5 transition-all',
                activeHero.primary_attr === 'int'
                  ? 'bg-blue-950/40 border-dota-cyan/80 shadow-[0_0_12px_rgba(56,189,248,0.25)]'
                  : 'bg-dota-surface/70 border-dota-border/60'
              ]"
            >
              <img src="/assets/icons/hero_intelligence.png" alt="智力" class="w-6 h-6 object-contain" />
              <div>
                <div class="text-[11px] text-dota-subtle font-medium flex items-center space-x-1">
                  <span>智力</span>
                  <span v-if="activeHero.primary_attr === 'int'" class="text-[9px] px-1 rounded bg-dota-cyan/30 text-dota-cyan font-bold">主属性</span>
                </div>
                <div class="text-dota-text font-mono font-bold text-sm">
                  {{ activeDetails?.int_base || activeHero.base_int }}
                  <span class="text-[11px] text-dota-subtle font-normal">+{{ activeDetails?.int_gain?.toFixed(1) || activeHero.int_gain?.toFixed(1) }}</span>
                </div>
              </div>
            </div>
          </div>

          <!-- 生命槽与魔法槽 (官方风格双进度条) -->
          <div v-if="activeLevel1Stats" class="space-y-1.5 font-mono text-xs">
            <div class="h-6 rounded-lg bg-emerald-950/60 border border-emerald-800/60 px-3 flex items-center justify-between text-dota-green font-bold shadow-inner">
              <span>生命值 {{ activeLevel1Stats.hp }}</span>
              <span class="text-[11px] font-normal text-emerald-400">+{{ activeLevel1Stats.hpRegen }}/s</span>
            </div>
            <div class="h-6 rounded-lg bg-blue-950/60 border border-blue-800/60 px-3 flex items-center justify-between text-dota-cyan font-bold shadow-inner">
              <span>魔法值 {{ activeLevel1Stats.mana }}</span>
              <span class="text-[11px] font-normal text-cyan-300">+{{ activeLevel1Stats.manaRegen }}/s</span>
            </div>
          </div>

          <!-- 官方 8 大衍生属性网格 (搭配官方专属图标) -->
          <div v-if="activeLevel1Stats" class="grid grid-cols-4 gap-1.5 text-xs font-mono pt-1">
            <!-- 攻击力 -->
            <div class="p-1.5 rounded-lg bg-dota-surface/80 border border-dota-border/60 flex items-center space-x-1.5" title="基础攻击力">
              <img src="/assets/icons/icon_damage.png" alt="攻击" class="w-4 h-4 object-contain shrink-0" />
              <span class="text-dota-text font-bold">{{ activeLevel1Stats.attackMin }}-{{ activeLevel1Stats.attackMax }}</span>
            </div>
            <!-- 护甲 -->
            <div class="p-1.5 rounded-lg bg-dota-surface/80 border border-dota-border/60 flex items-center space-x-1.5" title="物理护甲">
              <img src="/assets/icons/icon_armor.png" alt="护甲" class="w-4 h-4 object-contain shrink-0" />
              <span class="text-dota-text font-bold">{{ activeLevel1Stats.armor }}</span>
            </div>
            <!-- 移速 -->
            <div class="p-1.5 rounded-lg bg-dota-surface/80 border border-dota-border/60 flex items-center space-x-1.5" title="基础移动速度">
              <img src="/assets/icons/icon_movement_speed.png" alt="移速" class="w-4 h-4 object-contain shrink-0" />
              <span class="text-dota-text font-bold">{{ activeLevel1Stats.moveSpeed }}</span>
            </div>
            <!-- 攻击间隔 -->
            <div class="p-1.5 rounded-lg bg-dota-surface/80 border border-dota-border/60 flex items-center space-x-1.5" title="基础攻击间隔 (BAT)">
              <img src="/assets/icons/icon_attack_time.png" alt="攻速" class="w-4 h-4 object-contain shrink-0" />
              <span class="text-dota-text font-bold">{{ activeDetails?.attack_rate || 1.7 }}s</span>
            </div>
            <!-- 攻击距离 -->
            <div class="p-1.5 rounded-lg bg-dota-surface/80 border border-dota-border/60 flex items-center space-x-1.5" title="攻击距离">
              <img src="/assets/icons/icon_attack_range.png" alt="射程" class="w-4 h-4 object-contain shrink-0" />
              <span class="text-dota-text font-bold">{{ activeDetails?.attack_range || activeHero.base_attack_min }}</span>
            </div>
            <!-- 魔法抗性 -->
            <div class="p-1.5 rounded-lg bg-dota-surface/80 border border-dota-border/60 flex items-center space-x-1.5" title="基础魔法抗性">
              <img src="/assets/icons/icon_magic_resist.png" alt="魔抗" class="w-4 h-4 object-contain shrink-0" />
              <span class="text-dota-text font-bold">{{ activeDetails?.magic_resistance || 25 }}%</span>
            </div>
            <!-- 转身速率 -->
            <div class="p-1.5 rounded-lg bg-dota-surface/80 border border-dota-border/60 flex items-center space-x-1.5" title="转身速率">
              <img src="/assets/icons/icon_turn_rate.png" alt="转身" class="w-4 h-4 object-contain shrink-0" />
              <span class="text-dota-text font-bold">{{ activeDetails?.turn_rate || 0.6 }}</span>
            </div>
            <!-- 视野范围 -->
            <div class="p-1.5 rounded-lg bg-dota-surface/80 border border-dota-border/60 flex items-center space-x-1.5" title="白天 / 夜晚视野">
              <img src="/assets/icons/icon_vision.png" alt="视野" class="w-4 h-4 object-contain shrink-0" />
              <span class="text-dota-text font-bold">{{ activeDetails?.sight_range_day || 1800 }}/{{ activeDetails?.sight_range_night || 800 }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 2. 官方技能展示区 (水平图标选项卡选择器 + 展开大画幅技能卡片) -->
      <div v-if="activeDetails?.abilities && activeDetails.abilities.length > 0" class="space-y-4">
        <div class="flex items-center justify-between border-b border-dota-border pb-2">
          <h2 class="text-sm font-bold text-dota-gold flex items-center space-x-2">
            <Zap class="w-4 h-4" />
            <span>官方技能体系 (点击技能图标切换详情)</span>
          </h2>
          <span class="text-xs text-dota-subtle">当前收录 {{ activeDetails.abilities.length }} 项主动/被动/升级技能</span>
        </div>

        <!-- 水平技能图标选择栏 (官方经典 Tab 排列) -->
        <div class="flex items-center space-x-3 overflow-x-auto py-2 scrollbar-none">
          <button
            v-for="(ability, idx) in activeDetails.abilities"
            :key="ability.id"
            @click="activeAbilityIndex = idx"
            :class="[
              'relative rounded-xl p-1 border-2 transition-all cursor-pointer shrink-0 group flex flex-col items-center',
              activeAbilityIndex === idx
                ? 'border-dota-gold bg-dota-panel shadow-[0_0_16px_rgba(229,193,88,0.4)] scale-105'
                : 'border-dota-border bg-dota-surface/80 hover:border-dota-gold/50 hover:bg-dota-panel'
            ]"
          >
            <!-- 技能图标 -->
            <div class="w-14 h-14 rounded-lg overflow-hidden relative bg-dota-card">
              <img
                :src="`https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/abilities/${ability.name}.png`"
                :alt="ability.name_loc || ability.name"
                class="w-full h-full object-cover"
                @error="(e) => ((e.target as HTMLElement).style.display = 'none')"
              />

              <!-- 角标标识 (先天技能 / A杖 / 魔晶) -->
              <div v-if="ability.ability_is_innate || ability.is_innate" class="absolute bottom-0 inset-x-0 bg-emerald-600/90 text-white text-[10px] text-center font-bold">
                先天
              </div>
              <div v-else-if="ability.ability_is_granted_by_scepter" class="absolute bottom-0 inset-x-0 bg-dota-gold/90 text-black text-[10px] text-center font-bold">
                神杖
              </div>
              <div v-else-if="ability.ability_is_granted_by_shard" class="absolute bottom-0 inset-x-0 bg-dota-cyan/90 text-black text-[10px] text-center font-bold">
                魔晶
              </div>
            </div>

            <span class="text-[11px] font-medium text-dota-text mt-1 truncate max-w-[64px] text-center">
              {{ ability.name_loc || ability.name }}
            </span>

            <!-- 选中高光下三角指针 -->
            <div
              v-if="activeAbilityIndex === idx"
              class="absolute -bottom-2 w-0 h-0 border-l-[6px] border-l-transparent border-r-[6px] border-r-transparent border-t-[6px] border-t-dota-gold"
            />
          </button>
        </div>

        <!-- 选中的技能全景展示大卡片 -->
        <div
          v-if="selectedAbility"
          class="p-6 rounded-2xl bg-dota-panel border border-dota-border/80 shadow-xl space-y-4 transition-all"
        >
          <!-- 技能顶栏：大图标 + 技能名字 + 快捷键 + 伤害机制 + 冷却魔耗 -->
          <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-dota-border/60 pb-4">
            <div class="flex items-center space-x-4">
              <div class="w-16 h-16 rounded-xl overflow-hidden bg-dota-card border-2 border-dota-border shrink-0 shadow-lg relative">
                <img
                  :src="`https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/abilities/${selectedAbility.name}.png`"
                  :alt="selectedAbility.name_loc || selectedAbility.name"
                  class="w-full h-full object-cover"
                />
              </div>

              <div class="space-y-1">
                <div class="flex items-center space-x-2.5 flex-wrap gap-y-1">
                  <h3 class="text-xl font-extrabold text-dota-goldLight">{{ selectedAbility.name_loc || selectedAbility.name }}</h3>
                  
                  <!-- 机制分类标签 -->
                  <span v-if="selectedAbility.ability_is_innate || selectedAbility.is_innate" class="text-xs px-2 py-0.5 rounded bg-emerald-950/60 text-emerald-400 border border-emerald-800/60 font-bold">
                    先天技能 (Innate)
                  </span>
                  <span v-if="selectedAbility.ability_is_granted_by_scepter" class="text-xs px-2 py-0.5 rounded bg-dota-gold/20 text-dota-gold border border-dota-gold/40 font-bold">
                    阿哈利姆神杖赋予新技能
                  </span>
                  <span v-if="selectedAbility.ability_is_granted_by_shard" class="text-xs px-2 py-0.5 rounded bg-dota-cyan/20 text-dota-cyan border border-dota-cyan/40 font-bold">
                    阿哈利姆魔晶赋予新技能
                  </span>

                  <!-- 伤害类型 -->
                  <span v-if="selectedAbility.damage === 1" class="text-xs px-2 py-0.5 rounded bg-orange-950/60 text-orange-400 border border-orange-800/60 font-mono">
                    物理伤害
                  </span>
                  <span v-else-if="selectedAbility.damage === 2" class="text-xs px-2 py-0.5 rounded bg-blue-950/60 text-blue-400 border border-blue-800/60 font-mono">
                    魔法伤害
                  </span>
                  <span v-else-if="selectedAbility.damage === 3 || selectedAbility.damage === 4" class="text-xs px-2 py-0.5 rounded bg-amber-950/60 text-amber-300 border border-amber-700/60 font-mono">
                    纯粹伤害
                  </span>

                  <!-- 减益免疫贯通 -->
                  <span v-if="selectedAbility.immunity === 1 || selectedAbility.immunity === 3" class="text-xs px-2 py-0.5 rounded bg-emerald-950/50 text-emerald-400 border border-emerald-800/50 font-mono">
                    无视减益免疫
                  </span>
                </div>

                <div class="text-xs text-dota-subtle font-mono">
                  技术代码: {{ selectedAbility.name }}
                </div>
              </div>
            </div>

            <!-- 冷却时间与魔法消耗 -->
            <div class="flex items-center space-x-4 text-xs font-mono bg-dota-surface/80 px-3.5 py-2 rounded-xl border border-dota-border/60 shrink-0">
              <div v-if="selectedAbility.cooldowns && selectedAbility.cooldowns.length > 0" class="flex items-center space-x-1.5 text-dota-gold">
                <Clock class="w-4 h-4" />
                <span class="font-bold">{{ selectedAbility.cooldowns.join(' / ') }}s</span>
              </div>
              <div v-if="selectedAbility.cooldowns?.length && selectedAbility.mana_costs?.length" class="w-[1px] h-3 bg-dota-border" />
              <div v-if="selectedAbility.mana_costs && selectedAbility.mana_costs.length > 0" class="flex items-center space-x-1.5 text-dota-cyan">
                <Droplet class="w-4 h-4 fill-current" />
                <span class="font-bold">{{ selectedAbility.mana_costs.join(' / ') }}</span>
              </div>
            </div>
          </div>

          <!-- 技能中文详细机制描述 -->
          <div class="text-sm text-dota-text leading-relaxed whitespace-pre-line bg-dota-surface/40 p-4 rounded-xl border border-dota-border/40">
            {{ resolveAbilityText(selectedAbility.desc_loc, selectedAbility, 'none', activeDetails || undefined) }}
          </div>

          <!-- 官方阿哈利姆神杖升级卡片 -->
          <div
            v-if="isEffectiveScepterUpgrade(selectedAbility, activeDetails || undefined)"
            class="p-4 rounded-xl bg-gradient-to-r from-dota-gold/15 to-transparent border border-dota-gold/40 text-xs text-dota-goldLight flex items-start space-x-3 shadow-md"
          >
            <img src="/assets/icons/ultimate_scepter.png" alt="A杖" class="w-8 h-6 object-contain shrink-0 mt-0.5" />
            <div class="space-y-1">
              <div class="font-bold text-dota-gold text-sm flex items-center space-x-1.5">
                <span>阿哈利姆神杖强化 (Aghanim's Scepter Upgrade)</span>
              </div>
              <p class="leading-relaxed text-dota-text">
                {{ resolveAbilityText(selectedAbility.scepter_loc, selectedAbility, 'scepter', activeDetails || undefined) }}
              </p>
            </div>
          </div>

          <!-- 官方阿哈利姆魔晶升级卡片 -->
          <div
            v-if="isEffectiveShardUpgrade(selectedAbility, activeDetails || undefined)"
            class="p-4 rounded-xl bg-gradient-to-r from-dota-cyan/15 to-transparent border border-dota-cyan/40 text-xs text-dota-cyan flex items-start space-x-3 shadow-md"
          >
            <img src="/assets/icons/aghanims_shard.png" alt="魔晶" class="w-8 h-6 object-contain shrink-0 mt-0.5" />
            <div class="space-y-1">
              <div class="font-bold text-dota-cyan text-sm flex items-center space-x-1.5">
                <span>阿哈利姆魔晶强化 (Aghanim's Shard Upgrade)</span>
              </div>
              <p class="leading-relaxed text-dota-text">
                {{ resolveAbilityText(selectedAbility.shard_loc, selectedAbility, 'shard', activeDetails || undefined) }}
              </p>
            </div>
          </div>

          <!-- 英雄背景故事 (折叠小面板) -->
          <div v-if="activeDetails?.bio_loc" class="pt-2">
            <details class="text-xs text-dota-subtle bg-dota-surface/30 p-3 rounded-lg border border-dota-border/40 cursor-pointer">
              <summary class="font-medium text-dota-muted hover:text-dota-gold transition-colors">
                阅读 {{ activeHero.localized_name }} 官方背景故事传记...
              </summary>
              <p class="mt-2.5 leading-relaxed text-dota-muted whitespace-pre-line">
                {{ activeDetails.bio_loc }}
              </p>
            </details>
          </div>
        </div>
      </div>

      <!-- 3. 官方对阵式天赋树 (居中等级圆环 + 左右翼对阵分支) -->
      <div v-if="activeDetails?.talents && activeDetails.talents.length > 0" class="space-y-3 pt-2">
        <h2 class="text-sm font-bold text-dota-gold flex items-center space-x-2">
          <Shield class="w-4 h-4" />
          <span>英雄天赋树 (官方对阵式加成)</span>
        </h2>

        <div class="bg-dota-panel p-5 rounded-2xl border border-dota-border space-y-3 max-w-3xl mx-auto shadow-xl">
          <div v-for="(lvl, index) in [25, 20, 15, 10]" :key="lvl" class="flex items-center text-xs">
            <!-- 左翼天赋选项 -->
            <div class="flex-1 text-right p-3 rounded-xl bg-dota-surface text-dota-text mr-4 hover:bg-dota-hover border border-dota-border/60 hover:border-dota-gold/40 transition-all font-medium text-xs leading-normal">
              {{ resolveTalentText(activeDetails.talents[index * 2 + 1], activeDetails || undefined) }}
            </div>

            <!-- 居中天赋等级金色徽章 -->
            <div class="w-10 h-10 rounded-full bg-gradient-to-b from-dota-gold/30 to-dota-panel border-2 border-dota-gold text-dota-gold font-bold flex items-center justify-center shrink-0 shadow-lg">
              <span class="font-mono text-sm">{{ lvl }}</span>
            </div>

            <!-- 右翼天赋选项 -->
            <div class="flex-1 text-left p-3 rounded-xl bg-dota-surface text-dota-text ml-4 hover:bg-dota-hover border border-dota-border/60 hover:border-dota-gold/40 transition-all font-medium text-xs leading-normal">
              {{ resolveTalentText(activeDetails.talents[index * 2], activeDetails || undefined) }}
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>

  <!-- 视图 1：全屏铺满 127 英雄大图鉴网格 -->
  <div v-else class="flex-1 flex flex-col overflow-hidden bg-dota-bg select-none">
    <!-- 顶部单行高密度控制栏 -->
    <div class="h-12 border-b border-dota-border px-4 flex items-center justify-between bg-dota-surface shrink-0 gap-4">
      <!-- 左侧属性分类 Tabs -->
      <div class="flex items-center space-x-1">
        <button
          v-for="tab in ATTR_TABS"
          :key="tab.id"
          @click="selectedAttr = tab.id"
          :class="[
            'flex items-center space-x-1.5 px-3 py-1.5 rounded-lg text-xs transition-all cursor-pointer font-medium',
            selectedAttr === tab.id
              ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40 shadow-sm'
              : 'text-dota-muted hover:bg-dota-hover hover:text-dota-text'
          ]"
        >
          <img :src="tab.icon" :alt="tab.label" class="w-4 h-4 object-contain" />
          <span>{{ tab.label }}</span>
        </button>
      </div>

      <!-- 中间段位选择 -->
      <div class="flex items-center space-x-2 text-xs">
        <span class="text-dota-subtle">段位环境:</span>
        <select
          v-model="selectedRank"
          class="h-7 bg-dota-panel border border-dota-border rounded px-2 text-xs text-dota-gold outline-none focus:border-dota-gold cursor-pointer"
        >
          <option v-for="rank in RANK_OPTIONS" :key="rank.id" :value="rank.id">
            {{ rank.label }}
          </option>
        </select>
      </div>

      <!-- 右侧排序规则 -->
      <div class="flex items-center space-x-1 text-xs">
        <span class="text-dota-subtle mr-1">排序:</span>
        <button
          @click="sortMode = 'win_desc'"
          :class="['px-2.5 py-1 rounded text-xs transition-colors cursor-pointer font-medium', sortMode === 'win_desc' ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40' : 'text-dota-muted hover:text-dota-text']"
        >
          胜率最高
        </button>
        <button
          @click="sortMode = 'pick_desc'"
          :class="['px-2.5 py-1 rounded text-xs transition-colors cursor-pointer font-medium', sortMode === 'pick_desc' ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40' : 'text-dota-muted hover:text-dota-text']"
        >
          出场最多
        </button>
        <button
          @click="sortMode = 'id_asc'"
          :class="['px-2.5 py-1 rounded text-xs transition-colors cursor-pointer font-medium', sortMode === 'id_asc' ? 'bg-dota-panel text-dota-gold font-bold border border-dota-gold/40' : 'text-dota-muted hover:text-dota-text']"
        >
          编号排序
        </button>
      </div>
    </div>

    <!-- 英雄大卡片网格列表 -->
    <div class="flex-1 overflow-y-auto p-4">
      <div class="grid grid-cols-4 sm:grid-cols-6 md:grid-cols-8 lg:grid-cols-10 xl:grid-cols-12 gap-2.5">
        <div
          v-for="hero in filteredHeroes"
          :key="hero.id"
          @click="openHeroDetail(hero)"
          class="group relative rounded-lg bg-dota-panel border border-dota-border/80 hover:border-dota-gold overflow-hidden cursor-pointer transition-all hover:scale-105 hover:shadow-lg flex flex-col"
        >
          <div class="aspect-[16/9] w-full bg-dota-surface relative overflow-hidden">
            <img
              :src="`https://cdn.cloudflare.steamstatic.com${hero.img}`"
              :alt="hero.localized_name"
              class="w-full h-full object-cover group-hover:scale-110 transition-transform duration-300"
              loading="lazy"
              @error="(e) => ((e.target as HTMLElement).style.display = 'none')"
            />
            <div class="absolute bottom-0 inset-x-0 bg-gradient-to-t from-black/80 to-transparent p-1">
              <div class="text-xs font-bold text-dota-text truncate text-center group-hover:text-dota-gold transition-colors">
                {{ hero.localized_name }}
              </div>
            </div>
          </div>
          <div class="p-1.5 text-[11px] font-mono flex items-center justify-between text-dota-subtle bg-dota-surface/40">
            <span class="text-dota-gold font-bold">{{ getWinRate(hero) }}%</span>
            <span>{{ getPickCount(hero) }}场</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { HeroStat } from '../types';
import { HERO_CHINESE_NAMES } from '../data/heroNames';
import { HERO_ALIASES } from '../data/aliases';
import { calculateLevel1Stats } from '../utils/heroStats';
import {
  OfficialHero,
  resolveAbilityText,
  resolveTalentText,
  isEffectiveScepterUpgrade,
  isEffectiveShardUpgrade,
} from '../utils/abilityTextResolver';
import { useAppStore } from '../store';
import { ArrowLeft, Zap, Shield, Clock, Droplet } from 'lucide-vue-next';

const RANK_OPTIONS = [
  { id: 7, label: '冠绝一世' },
  { id: 6, label: '超凡入圣' },
  { id: 5, label: '万古流芳' },
  { id: 4, label: '传奇' },
  { id: 3, label: '统帅' },
  { id: 2, label: '中军' },
  { id: 1, label: '先锋/卫士' },
];

const ATTR_TABS = [
  { id: 'all', label: '全部', icon: '/assets/icons/hero_universal.png' },
  { id: 'str', label: '力量', icon: '/assets/icons/hero_strength.png' },
  { id: 'agi', label: '敏捷', icon: '/assets/icons/hero_agility.png' },
  { id: 'int', label: '智力', icon: '/assets/icons/hero_intelligence.png' },
  { id: 'all_attr', label: '全才', icon: '/assets/icons/hero_universal.png' },
];

const { state } = useAppStore();
const heroes = ref<HeroStat[]>([]);
const officialDetails = ref<Record<number, OfficialHero>>({});
const selectedAttr = ref<string>('all');
const selectedRank = ref<number>(7);
const sortMode = ref<'win_desc' | 'pick_desc' | 'id_asc'>('win_desc');
const activeHero = ref<HeroStat | null>(null);
const activeAbilityIndex = ref<number>(0);
const loading = ref(true);

const openHeroDetail = (hero: HeroStat) => {
  activeHero.value = hero;
  activeAbilityIndex.value = 0;
};

onMounted(async () => {
  try {
    const [statsRes, officialRes] = await Promise.all([
      fetch('/assets/data/herostats.json'),
      fetch('/assets/data/heroes-details-official.json')
    ]);
    const statsData: HeroStat[] = await statsRes.json();
    const officialData = await officialRes.json();

    // 100% 静态常数兜底英雄中文名（防退化红线）
    const enriched = statsData.map((h) => ({
      ...h,
      localized_name: HERO_CHINESE_NAMES[h.id] || h.localized_name,
    }));
    heroes.value = enriched;

    if (officialData?.heroes) {
      const dict: Record<number, OfficialHero> = {};
      officialData.heroes.forEach((h: OfficialHero) => {
        dict[h.id] = h;
      });
      officialDetails.value = dict;
    }
  } catch (err) {
    console.error('加载英雄数据失败', err);
  } finally {
    loading.value = false;
  }
});

const getWinRate = (hero: HeroStat): string => {
  const pick = (hero as any)[`${selectedRank.value}_pick`] || 0;
  const win = (hero as any)[`${selectedRank.value}_win`] || 0;
  if (pick <= 0) return '0.0';
  return ((win / pick) * 100).toFixed(1);
};

const getPickCount = (hero: HeroStat): number => {
  return (hero as any)[`${selectedRank.value}_pick`] || 0;
};

const filteredHeroes = computed(() => {
  let list = heroes.value.filter((h) => {
    if (selectedAttr.value === 'str' && h.primary_attr !== 'str') return false;
    if (selectedAttr.value === 'agi' && h.primary_attr !== 'agi') return false;
    if (selectedAttr.value === 'int' && h.primary_attr !== 'int') return false;
    if (selectedAttr.value === 'all_attr' && h.primary_attr !== 'all') return false;

    if (state.searchQuery.trim()) {
      const q = state.searchQuery.toLowerCase().trim();
      const nameMatch = h.localized_name.toLowerCase().includes(q) || h.name.toLowerCase().includes(q);
      const aliases = HERO_ALIASES[String(h.id)] || [];
      const aliasMatch = aliases.some((a) => a.toLowerCase().includes(q));
      return nameMatch || aliasMatch;
    }
    return true;
  });

  return list.sort((a, b) => {
    if (sortMode.value === 'win_desc') {
      const aPick = (a as any)[`${selectedRank.value}_pick`] || 0;
      const aWin = (a as any)[`${selectedRank.value}_win`] || 0;
      const aRate = aPick > 0 ? aWin / aPick : 0;

      const bPick = (b as any)[`${selectedRank.value}_pick`] || 0;
      const bWin = (b as any)[`${selectedRank.value}_win`] || 0;
      const bRate = bPick > 0 ? bWin / bPick : 0;

      return bRate - aRate;
    }
    if (sortMode.value === 'pick_desc') {
      const aPick = (a as any)[`${selectedRank.value}_pick`] || 0;
      const bPick = (b as any)[`${selectedRank.value}_pick`] || 0;
      return bPick - aPick;
    }
    return a.id - b.id;
  });
});

const activeDetails = computed(() => {
  return activeHero.value ? officialDetails.value[activeHero.value.id] : null;
});

const activeLevel1Stats = computed(() => {
  return activeHero.value ? calculateLevel1Stats(activeHero.value) : null;
});

const selectedAbility = computed(() => {
  const abilities = activeDetails.value?.abilities || [];
  if (abilities.length === 0) return null;
  return abilities[activeAbilityIndex.value] || abilities[0];
});
</script>
