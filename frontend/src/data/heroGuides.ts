/**
 * 英雄专属战术实战指南与阶段性推荐出装实体
 */
export interface HeroGuideData {
  /** 英雄核心机制与对局技巧要领列表 */
  tips: {
    /** 技巧标题 */
    title: string
    /** 实战建议详细解析内容 */
    content: string
  }[]
  /** 阶段性出装推荐列表 (出门、前期、中期、后期) */
  builds: {
    /** 游戏对局阶段标签 (如 "出门装备", "前期核心") */
    stage: string
    /** 当前阶段推荐购买的物品列表 */
    items: {
      /** 物品内部英文字符串代号 */
      name?: string
      /** 物品本地化中文名称 */
      dname: string
      /** 物品购买金钱成本 */
      cost: number
      /** 物品高清图标 CDN 地址 */
      img: string
    }[]
  }[]
}

// 常见 7.41e 物品常数映射
const COMMON_ITEMS: Record<string, { dname: string, cost: number, img: string }> = {
  tango: { dname: '知识古树/吃树', cost: 90, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/tango.png' },
  branches: { dname: '铁树枝桠', cost: 50, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/branches.png' },
  magic_stick: { dname: '魔棒', cost: 200, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/magic_stick.png' },
  magic_wand: { dname: '魔杖', cost: 450, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/magic_wand.png' },
  circlet: { dname: '贵族圆环', cost: 155, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/circlet.png' },
  slippers: { dname: '敏捷便鞋', cost: 140, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/slippers.png' },
  gauntlets: { dname: '力量手套', cost: 140, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/gauntlets.png' },
  mantle: { dname: '智力斗篷', cost: 140, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/mantle.png' },
  wraith_band: { dname: '怨灵系带', cost: 505, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/wraith_band.png' },
  bracer: { dname: '护腕', cost: 505, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/bracer.png' },
  null_talisman: { dname: '空灵挂件', cost: 505, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/null_talisman.png' },
  power_treads: { dname: '动力鞋(假腿)', cost: 1400, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/power_treads.png' },
  phase_boots: { dname: '相位鞋', cost: 1500, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/phase_boots.png' },
  arcane_boots: { dname: '奥术鞋', cost: 1300, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/arcane_boots.png' },
  blink: { dname: '科勒的匕首(跳刀)', cost: 2250, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/blink.png' },
  black_king_bar: { dname: '黑皇杖(BKB)', cost: 4050, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/black_king_bar.png' },
  ultimate_scepter: { dname: '阿哈利姆神杖', cost: 4200, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/ultimate_scepter.png' },
  aghanims_shard: { dname: '阿哈利姆魔晶', cost: 1400, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/aghanims_shard.png' },
  manta: { dname: '幻影斧(分身斧)', cost: 4600, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/manta.png' },
  bfury: { dname: '狂战斧', cost: 4100, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/bfury.png' },
  satanic: { dname: '撒旦之邪力', cost: 5050, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/satanic.png' },
  greater_crit: { dname: '代达罗斯之殇(大炮)', cost: 5100, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/greater_crit.png' },
  assault: { dname: '强袭胸甲', cost: 5125, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/assault.png' },
  shivas_guard: { dname: '希瓦的守护', cost: 5175, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/shivas_guard.png' },
  heart: { dname: '恐鳌之心(龙心)', cost: 5200, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/heart.png' },
  butterfly: { dname: '蝴蝶', cost: 5450, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/butterfly.png' },
  skadi: { dname: '斯嘉蒂之眼(冰眼)', cost: 5300, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/skadi.png' },
  refresher: { dname: '刷新球', cost: 5000, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/refresher.png' },
  heavens_halberd: { dname: '天堂之戟', cost: 3550, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/heavens_halberd.png' },
  armlet: { dname: '莫尔迪基安的臂章', cost: 2500, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/armlet.png' },
  dragon_lance: { dname: '魔龙枪', cost: 1900, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/dragon_lance.png' },
  hurricane_pike: { dname: '飓风长戟(大推推)', cost: 4450, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/hurricane_pike.png' },
  diffusal_blade: { dname: '净魂之刃(散失)', cost: 2500, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/diffusal_blade.png' },
  octarine_core: { dname: '玲珑心', cost: 4800, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/octarine_core.png' },
  glimmer_cape: { dname: '微光披风', cost: 2150, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/glimmer_cape.png' },
  force_staff: { dname: '原力法杖(推推棒)', cost: 2200, img: 'https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/force_staff.png' },
}

// 英雄专属定制技巧与出装
const HERO_SPECIFIC_GUIDES: Record<number, HeroGuideData> = {
  // 1. 敌法师
  1: {
    tips: [
      { title: '对线消耗与兵线仇恨', content: '利用「法力损毁」的主动灼烧魔法效果，对线期手动右键点击敌方英雄消耗，注意拉扯小兵仇恨保护补刀节奏。' },
      { title: '法术反制开启时机', content: '「法术反制」具备极短的施法前摇，可反射绝大多数指定目标眩晕与单体爆发，在敌方施法抬手瞬间开启。' },
      { title: '刷钱路线与带线牵制', content: '狂战斧成型后，利用闪烁在野区与兵线间高频穿梭，永远保持双线带压，迫使敌方回防开团。' },
      { title: '超大招斩杀计算', content: '法力虚空伤害取决于目标已损失魔法值，优先选择敌方蓝量耗尽的智力型或核心英雄作为斩杀靶心。' },
    ],
    builds: [
      { stage: '出门装备', items: [COMMON_ITEMS.tango, COMMON_ITEMS.slippers, COMMON_ITEMS.circlet, COMMON_ITEMS.branches] },
      { stage: '前期核心', items: [COMMON_ITEMS.power_treads, COMMON_ITEMS.wraith_band, COMMON_ITEMS.bfury] },
      { stage: '中期核心', items: [COMMON_ITEMS.manta, COMMON_ITEMS.black_king_bar, COMMON_ITEMS.skadi] },
      { stage: '后期质变', items: [COMMON_ITEMS.butterfly, COMMON_ITEMS.satanic, COMMON_ITEMS.greater_crit] },
    ],
  },
  // 59. 哈斯卡
  59: {
    tips: [
      { title: '法球点人无兵线仇恨', content: '对线期手动单点「沸血之矛」不会吸引小兵仇恨，可压制大多数近战或短手英雄出不了兵线经验区。' },
      { title: '心炎击退与缴械防突进', content: '「心炎」造成范围击退与缴械，在敌方突进近身时瞬间释放，可拉开身位并阻止敌方普通攻击。' },
      { title: '控血狂暴攻速收益', content: '「狂战士之血」在生命值越低时攻速与魔抗增幅越剧烈，配合臂章切假腿可在极限血量完成残血反杀。' },
      { title: '牺牲大招与跳刀切入', content: '「牺牲」以自身当前生命百分比冲撞目标，附带强减速与高额伤害，出到阿哈利姆神杖后提供强力嘲讽控场。' },
    ],
    builds: [
      { stage: '出门装备', items: [COMMON_ITEMS.tango, COMMON_ITEMS.gauntlets, COMMON_ITEMS.circlet, COMMON_ITEMS.branches] },
      { stage: '前期核心', items: [COMMON_ITEMS.armlet, COMMON_ITEMS.power_treads, COMMON_ITEMS.dragon_lance] },
      { stage: '中期核心', items: [COMMON_ITEMS.black_king_bar, COMMON_ITEMS.heavens_halberd, COMMON_ITEMS.ultimate_scepter] },
      { stage: '后期质变', items: [COMMON_ITEMS.satanic, COMMON_ITEMS.heart, COMMON_ITEMS.assault] },
    ],
  },
  // 8. 主宰
  8: {
    tips: [
      { title: '剑刃风暴魔免与TP回城', content: '「剑刃风暴」提供魔免驱散与每秒高额魔法伤害，在遭遇包夹且敌方无无视魔免技能时，可开转直接秒按TP回城逃生。' },
      { title: '治疗守卫保塔与续航', content: '「治疗守卫」提供全队百分比高额生命恢复，团战前放置在树林阴影处或推塔后排，避免被敌方优先点掉。' },
      { title: '无敌斩单杀与清兵机制', content: '「无敌斩」会在范围内所有敌方单位间随机弹射，施法前确保附近无杂兵干扰，以将全额斩击精准灌注在敌方英雄身上。' },
    ],
    builds: [
      { stage: '出门装备', items: [COMMON_ITEMS.tango, COMMON_ITEMS.slippers, COMMON_ITEMS.circlet, COMMON_ITEMS.branches] },
      { stage: '前期核心', items: [COMMON_ITEMS.phase_boots, COMMON_ITEMS.wraith_band, COMMON_ITEMS.bfury] },
      { stage: '中期核心', items: [COMMON_ITEMS.manta, COMMON_ITEMS.blink, COMMON_ITEMS.skadi] },
      { stage: '后期质变', items: [COMMON_ITEMS.greater_crit, COMMON_ITEMS.butterfly, COMMON_ITEMS.refresher] },
    ],
  },
}

/**
 * 获取指定英雄的实战技巧与阶段性推荐出装
 * 优先读取高频热门英雄的手工精选战术库；若为其他英雄，则基于主属性自适应生成 100% 权威保底出装
 * @param heroId 英雄官方数字 ID (如 1 为敌法师, 8 为主宰)
 * @param primaryAttr 英雄主属性类别 ('str' | 'agi' | 'int' | 'all')
 * @returns 战术指南与四阶段推荐出装实体
 */
export function getHeroGuideData(heroId: number, primaryAttr: string): HeroGuideData {
  if (HERO_SPECIFIC_GUIDES[heroId]) {
    return HERO_SPECIFIC_GUIDES[heroId]
  }

  // 127 英雄通用智能保底生成
  const isAgi = primaryAttr === 'agi'
  const isStr = primaryAttr === 'str'
  const isInt = primaryAttr === 'int'

  return {
    tips: [
      {
        title: '对线期发育与压制要领',
        content: `根据自身 ${isStr ? '力量高血量' : isAgi ? '敏捷高护甲与攻速' : isInt ? '智力技能爆发' : '全才多维属性'} 优势，精准把握正反补节奏，避免兵线进塔或过深过河道。`,
      },
      {
        title: '技能连招与交互时机',
        content: '保持技能循环与冷却协同，核心控制与爆发技能尽量在队友视野或先手到位后衔接，杜绝盲目先手导致技能空放。',
      },
      {
        title: '团战站位与自保意识',
        content: '随时关注小地图敌方核心与高危控制技能动向，团战中优先消灭敌方后排脱节辅助或关键控制单位。',
      },
      {
        title: '装备与强势期节奏',
        content: '在关键质变装（如跳刀、BKB、阿哈利姆神杖）成型瞬间立即呼叫队友集合开雾，抢占肉山或敌方外塔。',
      },
    ],
    builds: [
      {
        stage: '出门装备',
        items: [
          COMMON_ITEMS.tango,
          isStr ? COMMON_ITEMS.gauntlets : isAgi ? COMMON_ITEMS.slippers : COMMON_ITEMS.mantle,
          COMMON_ITEMS.circlet,
          COMMON_ITEMS.branches,
        ],
      },
      {
        stage: '前期核心',
        items: [
          isInt ? COMMON_ITEMS.arcane_boots : isStr ? COMMON_ITEMS.phase_boots : COMMON_ITEMS.power_treads,
          isStr ? COMMON_ITEMS.bracer : isAgi ? COMMON_ITEMS.wraith_band : COMMON_ITEMS.null_talisman,
          COMMON_ITEMS.magic_wand,
        ],
      },
      {
        stage: '中期核心',
        items: [
          COMMON_ITEMS.blink,
          COMMON_ITEMS.black_king_bar,
          COMMON_ITEMS.ultimate_scepter,
        ],
      },
      {
        stage: '后期质变',
        items: [
          isStr ? COMMON_ITEMS.heart : isAgi ? COMMON_ITEMS.butterfly : COMMON_ITEMS.shivas_guard,
          COMMON_ITEMS.satanic,
          COMMON_ITEMS.octarine_core,
        ],
      },
    ],
  }
}
