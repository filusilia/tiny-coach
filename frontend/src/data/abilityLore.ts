/**
 * 技能背景介绍（Lore）解析与补充文案
 *
 * ## 取值优先级（零伪造原则）
 * 1. **优先使用官网原文** `lore_loc`：官网有原文时必须原样展示，禁止改写、扩写或套用其他版本译文。
 *    当前 734 个技能中 **621 个**官网自带原文，必须原样透出。
 * 2. **官网缺失时才使用补充文案**：剩余 **113 个**技能官网 `lore_loc` 为空字符串（覆盖 103 个英雄），
 *    此时使用本文件的项目自撰文案，并在 UI 上标注「项目补充」，绝不冒充官网原文。
 *
 * ## 补充文案写作口径
 * 与官网力丸技能 lore 保持一致——第三人称、单句、克制、只写气质不解释机制、不出现任何数值。
 * 参考官方原文（力丸）：
 * - 烟幕：自从力丸在背叛之夜逃脱后，他就很看重利用朴素的烟幕来迷惑对手。
 * - 闪烁突袭：隐形刺客迅捷的动作让他的目标几乎不可能逃脱。
 * - 绝杀秘技：隐形刺客从不忌讳使用肮脏的战斗手法，尤其擅长从对手的背后发动攻击。
 * - 刀光谍影：力丸来找你了。
 *
 * ## 覆盖校验
 * 官网补空后，127 英雄 / 734 技能应 100% 拥有背景介绍。刷新官方快照后应复查本表：
 * 官网补齐原文的条目必须删除，让官网原文重新生效。
 */

/** 技能背景介绍来源标记 */
export type AbilityLoreSource = 'official' | 'project'

/** 技能背景介绍解析结果 */
export interface AbilityLore {
  /** 背景介绍正文（不含书名号） */
  text: string
  /** 来源：official = 官网原文；project = 项目自撰补充 */
  source: AbilityLoreSource
}

/**
 * 项目自撰补充文案表
 *
 * 仅在官网 `lore_loc` 为空时生效，键为技能内部英文名，按英雄 ID 顺序组织。
 */
export const ABILITY_LORE_SUPPLEMENT: Record<string, string> = {
  // 1 敌法师 · 绝人之路
  antimage_persectur: '法师的魔力越枯竭，越能体会被他盯上的绝望。',
  // 2 斧王 · 一人之军
  axe_one_man_army: '斧王从不指望援军——他本身就是一整支军队。',
  // 3 祸乱之源 · 妮塔莎脓血
  bane_ichor_of_nyctasha: '在祸乱之源的恐惧中死去的人，死后也逃不掉。',
  // 4 血魔 · 食血动物
  bloodseeker_sanguivore: '血魔从不缺少食物，他只是缺少一场屠杀。',
  // 5 水晶室女 · 冰川护体
  crystal_maiden_glacial_guard: '她倾注的每一分魔力，都会在身上凝成一层寒霜。',
  // 7 撼地者 · 强击图腾
  earthshaker_slugger: '撼地者的图腾从不留全尸。',
  // 8 主宰 · 剑心犹在
  juggernaut_bladeform: '只要没人能伤到他，主宰的剑意就不会冷却。',
  // 9 米拉娜 · 天界箭袋
  mirana_celestial_quiver: '月神赐下的箭矢，从不空手而归。',
  // 10 变体精灵 · 潮涨潮落
  morphling_ebb_and_flow: '他随潮水改变自己，也随潮水改变战局。',
  // 12 幻影长矛手 · 灵幻兵械
  phantom_lancer_illusory_armaments: '即便是幻象，手里的长矛也一样致命。',
  // 13 帕克 · 顽皮克敌
  puck_puckish: '躲开的每一次攻击，都是帕克的一场游戏。',
  // 14 帕吉 · 腐肉堆积
  pudge_innate_graft_flesh: '帕吉吃下的每一具尸体，都让他更难被杀死。',
  // 15 雷泽 · 不稳定电流
  razor_unstable_current: '雷泽脚下的电流从未停歇，他只是习惯了奔跑。',
  // 17 风暴之灵 · 通电
  storm_spirit_galvanized: '每一次击杀都在为风暴之灵充能。',
  // 18 斯温 · 神之愤怒
  sven_wrath_of_god: '斯温的怒火有多重，他的剑就有多重。',
  // 19 小小 · 不可逾越
  tiny_insurmountable: '越是沉重的身躯，越难被撼动。',
  // 20 复仇之魂 · 恶有恶报
  vengefulspirit_retribution: '杀死她的人，终会在她的注视下偿还。',
  // 21 风行者 · 一路顺风
  windrunner_tailwind: '风行者施法的瞬间，风总会站在她这一边。',
  // 25 莉娜 · 慢热
  lina_slow_burn: '莉娜的火焰从不会一次烧尽，它只是慢慢吞噬。',
  // 26 莱恩 · 下地狱再上来
  lion_to_hell_and_back: '莱恩去过地狱，所以死亡对他来说只是往返。',
  // 27 暗影萨满 · 巨蟒之瓮
  shadow_shaman_urnaconda: '罐子里装着的从来不是蛇，是灾祸。',
  // 28 斯拉达 · 汪洋前哨
  slardar_seaborn_sentinel: '只要脚下还有水，斯拉达就是深海的哨兵。',
  // 29 潮汐猎人 · 利维坦的渔获
  tidehunter_leviathans_catch: '利维坦从不空手离开自己的猎场。',
  // 30 巫医 · 巫毒变身术 / 驱邪护符
  witch_doctor_voodoo_switcheroo: '巫医把自己藏进了最危险的守卫里。',
  witch_doctor_gris_gris: '死亡夺走的金币，最后都会回到他的护符里。',
  // 31 巫妖 · 献身
  lich_death_charge: '巫妖从不把战友的牺牲称作浪费。',
  // 32 力丸 · 背刺
  riki_innate_backstab: '力丸从不与对手正面交锋，真正致命的那一刀总是落在对方视线之外。',
  // 33 谜团 · 事件视界
  enigma_event_horizon: '在谜团的引力里，逃跑只是另一种靠近。',
  // 34 修补匠 · 部署炮塔 / 尤里卡！
  tinker_deploy_turrets: '修补匠从不亲自追杀，他只是先铺好战场。',
  tinker_eureka: '想得越深，手里的器物转得越快。',
  // 35 狙击手 · 基恩瞄准镜
  sniper_keen_scope: '距离越远，基恩的子弹越致命。',
  // 36 瘟疫法师 · 施虐之心
  necrolyte_sadist: '每一次死亡都在滋养瘟疫法师，尤其是英雄的。',
  // 37 术士 · 邪术召唤
  warlock_eldritch_summoning: '死在术士法术下的亡魂，还要再为他卖一次命。',
  // 39 痛苦女王 · 魅魔
  queenofpain_succubus: '靠得越近，痛苦女王得到的越多。',
  // 40 剧毒术士 · 毒蛇撕咬
  venomancer_snakebite: '阿克特克的幼体懂得一件事：咬住了就不要松口。',
  // 41 虚空假面 · 扭曲力场
  faceless_void_distortion_field: '射向虚空假面的箭矢，得先穿过扭曲的时间。',
  // 42 冥魂大帝 · 吸血灵魂
  skeleton_king_vampiric_spirit: '即便身躯倒下，冥魂大帝也要先讨回一笔。',
  // 43 死亡先知 · 巫术精研
  death_prophet_witchcraft: '死亡先知活得越久，她的低语就越快应验。',
  // 44 幻影刺客 · 飘忽不定
  phantom_assassin_immaterial: '致命的刀锋从不落在看得见的地方。',
  // 45 帕格纳 · 湮灭专家
  pugna_oblivion_savant: '帕格纳一边施法一边拆塔，两件事他都很擅长。',
  // 46 圣堂刺客 · 心怀安宁
  templar_assassin_inner_peace: '静止不动的圣堂刺客，才是最危险的。',
  // 47 冥界亚龙 · 掠食
  viper_predator: '血腥味只会让冥界亚龙咬得更深。',
  // 48 发条技师 · 装甲力量
  rattletrap_armor_power: '发条技师的护甲越厚，拳头就越重。',
  // 52 拉席克 · 大肆污染
  leshrac_defilement: '拉席克的智慧有多广，他的亵渎就有多广。',
  // 53 自然先知 · 丛林之魂
  furion_spirit_of_the_forest: '每一棵树都在替自然先知挥拳。',
  // 55 黑暗贤者 · 才思敏捷
  dark_seer_aggrandize: '黑暗贤者想得越快，出手就越快。',
  // 56 克林克兹 · 地狱之裂
  clinkz_infernal_shred: '克林克兹的箭能撕裂的，不只是皮肉。',
  // 58 魅惑魔女 · 煽动野怪
  enchantress_rabblerouser: '被她魅惑的野怪，比她的言语更凶。',
  // 59 哈斯卡 · 血魔法
  huskar_blood_magic: '哈斯卡不相信魔力，他只相信自己的血。',
  // 60 暗夜魔王 · 午夜盛宴
  night_stalker_midnight_feast: '夜色越深，暗夜魔王的胃口越好。',
  // 61 育母蜘蛛 · 蜘蛛奶
  broodmother_spiders_milk: '育母蜘蛛的孩子们，喝的是猎物的命。',
  // 62 赏金猎人 · 职业猎人
  bounty_hunter_big_game_hunter: '越值钱的脑袋，赏金猎人越感兴趣。',
  // 63 编织者 · 命运之线
  weaver_threads_of_fate: '在编织者身边待得够久，就再也挣不脱了。',
  // 64 杰奇洛 · 天生一对
  jakiro_double_trouble: '两个头一起咬，杰奇洛从不觉得自己吃亏。',
  // 65 蝙蝠骑士 · 闷烧树脂
  batrider_smoldering_resin: '蝙蝠骑士的火焰会黏在身上，甩不掉。',
  // 66 陈 · 狂热者
  chen_zealot: '陈倒下时，总有一位狂热者替他站起来。',
  // 68 远古冰魄 · 刺骨严寒
  ancient_apparition_bone_chill: '被远古冰魄触碰过的人，连血液都会变慢。',
  // 69 末日使者 · 等级？痛苦
  doom_bringer_lvl_pain: '末日使者的诅咒从不立刻发作，它只是让你等待。',
  // 70 熊战士 · 暴烈之爪
  ursa_maul: '伤得越重，熊战士的爪子越狠。',
  // 71 裂魂人 · 神行太保
  spirit_breaker_bull_rush: '每一次撞晕对手，都让裂魂人跑得更快。',
  // 72 矮人直升机 · 加力燃烧器 / 侧翼机枪
  gyrocopter_afterburner: '开火的瞬间，矮人直升机就已经冲了出去。',
  gyrocopter_side_gunner_spawn_ability: '侧面的火力从不问它的主人要不要。',
  // 75 沉默术士 · 默默受苦
  silencer_brain_drain: '被沉默术士封住嘴的人，连死都安静。',
  // 76 殁境神蚀者 · 责难
  obsidian_destroyer_objurgation: '殁境神蚀者的奥术有多深，他的护盾就有多厚。',
  // 77 狼人 · 顶级掠食者
  lycan_apex_predator: '在丛林里，狼人才是食物链的顶端。',
  // 78 酒仙 · 壮胆酒
  brewmaster_liquid_courage: '酒仙的酒意越浓，脚步越飘，骨头越硬。',
  // 79 暗影恶魔 · 威胁
  shadow_demon_menace: '被暗影恶魔盯上的人，会越来越脆。',
  // 80 独行德鲁伊 · 缠绕之根
  lone_druid_entangle: '森林自己会伸出根须，替独行德鲁伊留住敌人。',
  // 81 混沌骑士 · 基本法则锻造
  chaos_knight_fundamental_forging: '混沌骑士的运气，连他捡到的器物都知道。',
  // 82 米波 · 地卜术
  meepo_geomancy: '每一只米波脚下的土地，都会分给所有米波。',
  // 84 食人魔魔法师 · 傻福
  ogre_magi_dumb_luck: '食人魔魔法师没有脑子，但他有力气。',
  // 85 拉比克 · 奇心
  rubick_curiosity: '看得越多，拉比克的好奇心就越致命。',
  // 88 司夜刺客 · 法力燃烧
  nyx_assassin_neuro_sting: '司夜刺客的每一次刺击，都顺手烧掉对手的理智。',
  // 89 娜迦海妖 · 鳗鱼皮
  naga_siren_eelskin: '娜迦海妖的鳞片滑得连刀锋都挂不住。',
  // 90 光之守卫 · 光明速度
  keeper_of_the_light_bright_speed: '光之守卫走过的地方，黑暗留不下来。',
  // 91 艾欧 · 拉近幽魂 / 拉远幽魂 / 衡势
  wisp_spirits_in: '艾欧想把光收拢一些，好照清眼前的人。',
  wisp_spirits_out: '艾欧把光推出去，照亮更远的地方。',
  wisp_equilibrium: '越是充盈，艾欧的光越锋利；越是枯竭，它越温柔。',
  // 93 斯拉克 · 海浪短刀
  slark_saltwater_shiv: '斯拉克划开的每一刀，都让对手慢一点。',
  // 96 半人马战行者 · 开足马力
  centaur_horsepower: '半人马战行者的蹄声，从不因为重量而变慢。',
  // 97 马格纳斯 · 坚固核心
  magnataur_solid_core: '能把马格纳斯推开的冲击，还没有出现过。',
  // 98 伐木机 · 暴露疗法
  shredder_exposure_therapy: '伐木机越砍越清醒，每一棵树都是燃料。',
  // 99 钢背兽 · 尖刺在背
  bristleback_prickly: '从背后挨钢背兽一下，代价总是双倍。',
  // 100 巨牙海民 · 严寒
  tusk_bitter_chill: '巨牙海民身边，连挥刀都会变慢。',
  // 102 亚巴顿 · 凋零迷雾
  abaddon_withering_mist: '在亚巴顿的迷雾里，伤口只会越烂越深。',
  // 103 上古巨神 · 动量
  elder_titan_momentum: '上古巨神跑得越快，身上的石甲越厚。',
  // 104 军团指挥官 · 迎难而战！
  legion_commander_outfight_them: '军团指挥官的战吼，会让身边的人一起变硬。',
  // 105 工程师 · 同归于尽
  techies_mutually_assured_destruction: '工程师倒下时留下的，从来不是遗言。',
  // 106 灰烬之灵 · 献祭心
  ember_spirit_immolation: '灰烬之灵身边的火，从不需要点燃。',
  // 108 孽主 · 侵略大军
  abyssal_underlord_raid_boss: '穿过孽主之门的人，都带着他的庇护落地。',
  // 109 恐怖利刃 · 暗黑团结
  terrorblade_dark_unity: '恐怖利刃的每一个倒影，都握着同样的刀。',
  // 110 凤凰 · 消逝之光
  phoenix_dying_light: '凤凰越是濒死，它身上的光越灼人。',
  // 111 神谕者 · 预言
  oracle_prognosticate: '神谕者宣布的从来不是预言，是已经发生的事。',
  // 112 寒冬飞龙 · 古龙诗集
  winter_wyvern_eldwurms_edda: '古龙写下的每一个字，寒冬飞龙都读得懂。',
  // 113 天穹守望者 · 神符灌注
  arc_warden_runic_infusion: '每饮下一道神符，天穹守望者就更接近神性。',
  // 114 齐天大圣 · 出神入化
  monkey_king_transfiguration: '在猴群里找出齐天大圣，从来都是不可能的事。',
  // 119 邪影芳灵 · 仙灵粉尘
  dark_willow_pixie_dust: '抓不住邪影芳灵的时候，她反而在悄悄恢复。',
  // 120 石鳞剑士 · 天佑勇者
  pangolier_fortune_favors_the_bold: '命运偏爱敢冲进敌阵的石鳞剑士。',
  // 121 天涯墨客 · 墨痕
  grimstroke_ink_trail: '被天涯墨客画过的人，回头也找不到他的笔。',
  // 123 森海飞霞 · 林渊旅人
  hoodwink_mistwoods_wayfarer: '森海飞霞的敌人，常常发现自己在跟一棵树较劲。',
  // 126 虚无之灵 · 内在优势
  void_spirit_intrinsic_edge: '虚无之灵的每一分属性，都比别人用得更狠。',
  // 128 电炎绝手 · 猎枪
  snapfire_boomstick: '离电炎绝手越近，她的霰弹越不讲道理。',
  // 129 玛尔斯 · 无畏
  mars_dauntless: '敌人围得越密，玛尔斯笑得越开心。',
  // 131 百戏大王 · 暗黑狂欢纪念品系列
  ringmaster_dark_carnival_souvenirs: '每一次死亡，都是百戏大王送出的纪念品。',
  ringmaster_funhouse_mirror: '哈哈镜里走出来的，都是有些不对劲的自己。',
  ringmaster_strongman_tonic: '一口强力水下去，再瘦弱的英雄也能撑起场面。',
  ringmaster_whoopee_cushion: '百戏大王的玩笑，总能让人向前扑个满怀。',
  ringmaster_summon_unicycle: '骑上独轮车的百戏大王，谁也拦不住。',
  // 135 破晓辰星 · 辰星破晓
  dawnbreaker_break_of_dawn: '太阳升起的时候，破晓辰星才刚热身。',
  // 136 獸 · 庞
  primal_beast_colossal: '獸的身躯越大，他的怒火覆盖得越广。',
  // 138 琼英碧灵 · 幽魂子弹 / 超自然
  muerta_spectral_slug: '琼英碧灵的子弹能穿过身体，却穿不过她的判决。',
  muerta_supernatural: '死在她身边的人越多，琼英碧灵就越不像活人。',
  // 145 朗戈 · 安可
  largo_encore: '朗戈的曲子只要起了头，就不肯轻易停下。',
}

/**
 * 解析技能的官方技能背景介绍
 *
 * @param ability 官方技能实体（允许为空，返回 null）
 * @returns 背景介绍结果；官网与补充文案都缺失时返回 null（由 UI 自行整块隐藏，不展示占位假文案）
 */
export function resolveAbilityLore(ability?: {
  lore_loc?: string
  name?: string
} | null): AbilityLore | null {
  const official = (ability?.lore_loc || '').trim()
  if (official) return { text: official, source: 'official' }

  const supplement = ability?.name
    ? (ABILITY_LORE_SUPPLEMENT[ability.name] || '').trim()
    : ''
  if (supplement) return { text: supplement, source: 'project' }

  return null
}
