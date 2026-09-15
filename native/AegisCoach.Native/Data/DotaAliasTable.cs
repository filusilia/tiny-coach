using System.Text.RegularExpressions;
using AegisCoach.Core.Models;
using AegisCoach.Native.Models;

namespace AegisCoach.Native.Data;

/// <summary>
/// Dota 2 英雄与物品别名检索字典：支持拼音缩写（sa/pa/ta/am）、民间黑话（隐刺/水人/电锤/跳刀）、
/// 简繁同义及装备核心简称匹配。
/// </summary>
public static class DotaAliasTable
{
    /// <summary>
    /// 英雄英文系统标识 / 中文官方名称 -> 搜索别名列表
    /// </summary>
    private static readonly Dictionary<string, string[]> HeroAliasMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 敏捷英雄
        ["npc_dota_hero_antimage"] = ["am", "敌法", "敌法师", "玛吉纳", "df", "dfs"],
        ["npc_dota_hero_drow_ranger"] = ["dr", "小黑", "卓尔游侠", "黑弓", "崔希丝", "xh"],
        ["npc_dota_hero_juggernaut"] = ["jug", "jugg", "剑圣", "主宰", "尤涅若", "js", "bkb男"],
        ["npc_dota_hero_mirana"] = ["pom", "白虎", "米拉娜", "月之女祭司", "虎女", "bh"],
        ["npc_dota_hero_morphling"] = ["morph", "水人", "变体精灵", "变体", "sr"],
        ["npc_dota_hero_phantom_lancer"] = ["pl", "猴子", "幻长", "幻影长矛手", "hz"],
        ["npc_dota_hero_vengefulspirit"] = ["vs", "复仇", "复仇之魂", "红杖vs", "fc"],
        ["npc_dota_hero_riki"] = ["sa", "力丸", "隐刺", "隐形刺客", "隐身刺客", "lm", "yc"],
        ["npc_dota_hero_sniper"] = ["sniper", "火枪", "火枪手", "狙击手", "矮人狙击手", "卡德尔", "hq"],
        ["npc_dota_hero_templar_assassin"] = ["ta", "圣堂", "圣堂刺客", "拉娜娅", "st"],
        ["npc_dota_hero_luna"] = ["luna", "月骑", "露娜", "月之骑士", "yq"],
        ["npc_dota_hero_bounty_hunter"] = ["bh", "赏金", "赏金猎人", "刚铎", "sj"],
        ["npc_dota_hero_weaver"] = ["weaver", "蚂蚁", "地穴编织者", "编织者", "my"],
        ["npc_dota_hero_spectre"] = ["spec", "sp", "幽鬼", "墨特烈", "yg"],
        ["npc_dota_hero_meepo"] = ["meepo", "地狗", "米波", "狗头人", "狗头", "mb"],
        ["npc_dota_hero_phantom_assassin"] = ["pa", "幻刺", "幻影刺客", "茉崔蒂", "hc", "bkb女"],
        ["npc_dota_hero_clinkz"] = ["clinkz", "小骷髅", "骷髅弓箭手", "克林克兹", "骨弓", "xkl", "gg"],
        ["npc_dota_hero_slark"] = ["slark", "小鱼", "小鱼人", "斯拉克", "夜行者", "xy", "xyr"],
        ["npc_dota_hero_medusa"] = ["medusa", "一姐", "美杜莎", "蛇发女妖", "mds"],
        ["npc_dota_hero_troll_warlord"] = ["troll", "巨魔", "巨魔战将", "jm"],
        ["npc_dota_hero_gyrocopter"] = ["gyro", "飞机", "矮人直升机", "直升机", "fj"],
        ["npc_dota_hero_terrorblade"] = ["tb", "魂守", "灵魂守卫", "恐怖利刃", "hs"],
        ["npc_dota_hero_monkey_king"] = ["mk", "大圣", "猴子", "齐天大圣", "孙悟空", "ds", "swk"],
        ["npc_dota_hero_pangolier"] = ["pango", "滚滚", "穿山甲", "石鳞剑士", "gg"],
        ["npc_dota_hero_hoodwink"] = ["hoodwink", "松鼠", "森海飞霞", "ss"],
        ["npc_dota_hero_kez"] = ["kez", "凯", "鸟人", "鸡哥", "jg"],

        // 力量英雄
        ["npc_dota_hero_earthshaker"] = ["es", "牛头", "小牛", "撼地神牛", "撼地者", "nt", "xn"],
        ["npc_dota_hero_sven"] = ["sven", "斯温", "流浪", "流浪剑客", "真男人", "sw", "ll"],
        ["npc_dota_hero_tiny"] = ["tiny", "小小", "山岭巨人", "石巨人", "xx"],
        ["npc_dota_hero_kunkka"] = ["kunkka", "船长", "昆卡", "幽灵船", "cz"],
        ["npc_dota_hero_beastmaster"] = ["bm", "兽王", "卡洛克", "sw"],
        ["npc_dota_hero_dragon_knight"] = ["dk", "龙骑", "龙骑士", "达维安", "lq"],
        ["npc_dota_hero_clockwerk"] = ["cw", "发条", "发条技师", "发条地精", "ft"],
        ["npc_dota_hero_omniknight"] = ["omni", "全能", "全能骑士", "ok", "qn"],
        ["npc_dota_hero_huskar"] = ["huskar", "神灵", "神灵武士", "哈斯卡", "sl"],
        ["npc_dota_hero_alchemist"] = ["alch", "炼金", "炼金术士", "ga", "lj"],
        ["npc_dota_hero_brewmaster"] = ["brew", "熊猫", "酒仙", "熊猫酒仙", "xm", "jx"],
        ["npc_dota_hero_treant"] = ["treant", "大树", "树精卫士", "树人", "ds"],
        ["npc_dota_hero_centaur"] = ["cw", "centaur", "人马", "半人马战行者", "半人马酋长", "rm"],
        ["npc_dota_hero_shredder"] = ["timbersaw", "伐木机", "地精撕裂者", "fmj"],
        ["npc_dota_hero_bristleback"] = ["bb", "钢背", "钢背兽", "刚背", "猪", "gb"],
        ["npc_dota_hero_tusk"] = ["tusk", "海民", "巨牙海民", "雪人", "hm"],
        ["npc_dota_hero_elder_titan"] = ["et", "大牛", "牛头人酋长", "上古巨神", "dn"],
        ["npc_dota_hero_legion_commander"] = ["lc", "军团", "军团指挥官", "特蕾斯汀", "jt"],
        ["npc_dota_hero_earth_spirit"] = ["es", "土猫", "大地之灵", "考林", "tm"],
        ["npc_dota_hero_phoenix"] = ["phoenix", "凤凰", "火凤凰", "fh"],
        ["npc_dota_hero_mars"] = ["mars", "战神", "马尔斯", "玛尔斯", "zs"],
        ["npc_dota_hero_dawnbreaker"] = ["db", "大锤", "女锤", "破晓辰星", "dc"],
        ["npc_dota_hero_primal_beast"] = ["pb", "兽", "跑跑豚", "跑跑兽"],
        ["npc_dota_hero_axe"] = ["axe", "斧王", "蒙戈汗", "fw"],
        ["npc_dota_hero_pudge"] = ["pudge", "屠夫", "帕吉", "胖子", "肉钩", "tf"],
        ["npc_dota_hero_sand_king"] = ["sk", "沙王", "克里瑟历斯", "sw"],
        ["npc_dota_hero_slardar"] = ["slardar", "大鱼", "大鱼人", "斯拉达", "鱼人守卫", "dy", "dyr"],
        ["npc_dota_hero_tidehunter"] = ["th", "潮汐", "潮汐猎人", "利维坦", "西瓜皮", "cx"],
        ["npc_dota_hero_skeleton_king"] = ["snk", "wk", "骷髅王", "冥魂大帝", "绿绿大帝", "李奥瑞克", "klw"],
        ["npc_dota_hero_life_stealer"] = ["naix", "ls", "小狗", "噬魂鬼", "食尸鬼", "xg"],
        ["npc_dota_hero_night_stalker"] = ["ns", "夜魔", "暗夜魔王", "巴拉那", "ym"],
        ["npc_dota_hero_doom_bringer"] = ["doom", "末日", "末日使者", "末日使者", "爸爸", "mr"],
        ["npc_dota_hero_spirit_breaker"] = ["sb", "白牛", "裂魂人", "巴拉森", "推土机", "bn"],
        ["npc_dota_hero_lycan"] = ["lycan", "狼人", "狼狗", "lr"],
        ["npc_dota_hero_chaos_knight"] = ["ck", "混沌", "混沌骑士", "hd"],
        ["npc_dota_hero_undying"] = ["undying", "尸王", "不朽尸王", "挽歌", "sw"],
        ["npc_dota_hero_magnataur"] = ["mag", "猛犸", "半人猛犸", "马格纳斯", "mm"],
        ["npc_dota_hero_abaddon"] = ["loa", "死骑", "亚巴顿", "地狱领主", "sq"],
        ["npc_dota_hero_underlord"] = ["大屁股", "孽主", "深渊领主", "dpg", "nz"],

        // 智力英雄
        ["npc_dota_hero_crystal_maiden"] = ["cm", "冰女", "水晶室女", "莉莱", "bn"],
        ["npc_dota_hero_puck"] = ["puck", "帕克", "精灵龙", "仙女龙", "pk"],
        ["npc_dota_hero_storm_spirit"] = ["st", "ss", "蓝猫", "风暴之灵", "雷金", "lm"],
        ["npc_dota_hero_windrunner"] = ["wr", "风行", "风行者", "莱瑞蕾", "fx"],
        ["npc_dota_hero_zeus"] = ["zeus", "宙斯", "众神之王", "雷神", "zs"],
        ["npc_dota_hero_lina"] = ["lina", "火女", "秀逗魔导士", "莉娜", "hn"],
        ["npc_dota_hero_shadow_shaman"] = ["ss", "小Y", "小y", "暗影萨满", "萨满", "蛇棒", "xy"],
        ["npc_dota_hero_tinker"] = ["tk", "修补匠", "激光修补匠", "鲍什", "xbj"],
        ["npc_dota_hero_nature_prophet"] = ["np", "furion", "先知", "自然先知", "玛法里奥", "xz"],
        ["npc_dota_hero_enchantress"] = ["ench", "小鹿", "魅惑魔女", "艾席拉", "xl"],
        ["npc_dota_hero_jakiro"] = ["thd", "双头龙", "杰奇洛", "冰火两重天", "stl"],
        ["npc_dota_hero_chen"] = ["chen", "陈", "圣骑士"],
        ["npc_dota_hero_silencer"] = ["silencer", "沉默", "沉默术士", "诺门", "cm"],
        ["npc_dota_hero_ogre_magi"] = ["om", "蓝胖", "食人魔魔法师", "阿加诺斯", "lp"],
        ["npc_dota_hero_rubick"] = ["rubick", "大魔导师", "拉比克", "偷技能", "lbk"],
        ["npc_dota_hero_disruptor"] = ["disruptor", "萨尔", "干扰者", "大萨尔", "se"],
        ["npc_dota_hero_keeper_of_the_light"] = ["kotl", "光法", "光之守卫", "甘道夫", "gf"],
        ["npc_dota_hero_skywrath_mage"] = ["sky", "天怒", "天怒法师", "扎卡龙", "tn"],
        ["npc_dota_hero_oracle"] = ["oracle", "神谕", "神谕者", "奈里夫", "sy"],
        ["npc_dota_hero_grimstroke"] = ["grim", "笔仙", "墨客", "天涯墨客", "mk"],
        ["npc_dota_hero_witch_doctor"] = ["wd", "巫医", "祖尔", "摇摇乐", "wy"],
        ["npc_dota_hero_lich"] = ["lich", "巫妖", "弹弹乐", "埃瑟瑞格", "wy"],
        ["npc_dota_hero_lion"] = ["lion", "恶魔巫师", "莱恩", "插起抽", "emws"],
        ["npc_dota_hero_enigma"] = ["enigma", "谜团", "黑洞", "mt"],
        ["npc_dota_hero_necrolyte"] = ["nec", "死灵法", "瘟疫法师", "死灵法师", "老头", "slf"],
        ["npc_dota_hero_warlock"] = ["warlock", "术士", "地狱火", "ss"],
        ["npc_dota_hero_queenofpain"] = ["qop", "女王", "痛苦女王", "阿卡莎", "nw"],
        ["npc_dota_hero_death_prophet"] = ["dp", "大妈", "死亡先知", "克洛索", "swxz"],
        ["npc_dota_hero_pugna"] = ["pugna", "骨法", "帕格纳", "遗忘法师", "gf"],
        ["npc_dota_hero_dazzle"] = ["dazzle", "暗牧", "戴泽", "薄葬", "am"],
        ["npc_dota_hero_leshrac"] = ["ts", "老鹿", "拉席克", "受折磨的灵魂", "ll"],
        ["npc_dota_hero_dark_seer"] = ["ds", "兔子", "黑暗贤者", "相声兔", "tz"],
        ["npc_dota_hero_batrider"] = ["bat", "蝙蝠", "蝙蝠骑士", "bf"],
        ["npc_dota_hero_ancient_apparition"] = ["aa", "冰魂", "远古冰魄", "极寒幽魂", "bh"],
        ["npc_dota_hero_invoker"] = ["invo", "kaer", "卡尔", "祈求者", "召唤师", "ke"],
        ["npc_dota_hero_obsidian_destroyer"] = ["od", "黑鸟", "殁境神蚀者", "禁锢", "hn"],
        ["npc_dota_hero_shadow_demon"] = ["sd", "毒狗", "暗影恶魔", "埃尔达", "dg"],
        ["npc_dota_hero_visage"] = ["visage", "死灵龙", "维萨吉", "黄泉引路人", "sll"],
        ["npc_dota_hero_winter_wyvern"] = ["ww", "冰龙", "寒冬飞龙", "bl"],
        ["npc_dota_hero_arc_warden"] = ["aw", "arc", "电狗", "天穹守望者", "天穹", "dg"],
        ["npc_dota_hero_dark_willow"] = ["dw", "小仙女", "花仙子", "邪影芳灵", "hxz"],
        ["npc_dota_hero_void_spirit"] = ["vs", "紫猫", "虚无之灵", "伊奈辛", "zm"],
        ["npc_dota_hero_muerta"] = ["muerta", "女枪", "琼英碧灵", "死神", "nq"],
        ["npc_dota_hero_ringmaster"] = ["ringmaster", "小丑", "百戏大王", "xc"],

        // 全才 / 其他
        ["npc_dota_hero_bane"] = ["bane", "祸乱", "祸乱之源", "阿特洛波斯", "hl"],
        ["npc_dota_hero_bloodseeker"] = ["bs", "血魔", "史斯盖", "xm"],
        ["npc_dota_hero_shadow_fiend"] = ["sf", "影魔", "奈文摩尔", "摇大", "ym"],
        ["npc_dota_hero_razor"] = ["razor", "电魂", "雷泽", "闪电幽魂", "抽攻", "dh"],
        ["npc_dota_hero_venomancer"] = ["veno", "剧毒", "剧毒术士", "蛇棒", "jd"],
        ["npc_dota_hero_viper"] = ["viper", "毒龙", "冥界亚龙", "皮皮虾", "dl"],
        ["npc_dota_hero_broodmother"] = ["brood", "蜘蛛", "育母蜘蛛", "zz"],
        ["npc_dota_hero_techies"] = ["炸弹", "炸弹人", "工程师", "自爆", "zdr"],
        ["npc_dota_hero_lone_druid"] = ["ld", "熊德", "德鲁伊", "小熊", "xd"],
        ["npc_dota_hero_wisp"] = ["io", "wisp", "小精灵", "艾欧", "精灵", "xjl"],
        ["npc_dota_hero_abyssal_underlord"] = ["大屁股", "孽主", "dpg"],
        ["npc_dota_hero_snapfire"] = ["奶奶", "老奶奶", "电炎绝手", "lnn"],
        ["npc_dota_hero_marci"] = ["marci", "玛西", "哑巴", "女仆", "mx"],
    };

    /// <summary>
    /// 物品代码标识 / 物品标准名 -> 别名黑话列表
    /// </summary>
    private static readonly Dictionary<string, string[]> ItemAliasMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 核心输出与法系大件
        ["item_maelstrom"] = ["电锤", "小电锤", "漩涡", "dc", "xdc"],
        ["item_mjollnir"] = ["大电锤", "雷神之锤", "电锤", "雷锤", "ddc", "lszc"],
        ["item_blink"] = ["跳刀", "闪烁匕首", "td", "ssbs"],
        ["item_overwhelming_blink"] = ["跳刀", "大跳刀", "力量跳刀", "力量跳", "强袭跳刀"],
        ["item_swift_blink"] = ["跳刀", "大跳刀", "敏捷跳刀", "敏跳", "迅捷跳刀"],
        ["item_arcane_blink"] = ["跳刀", "大跳刀", "智力跳刀", "智跳", "奥术跳刀"],
        ["item_black_king_bar"] = ["bkb", "黑皇杖", "魔免", "金身", "hhz"],
        ["item_sheepstick"] = ["羊刀", "邪恶镰刀", "变羊", "yd", "xeld"],
        ["item_cyclone"] = ["风杖", "吹风", "吹风杖", "尤尔的神圣法杖", "fz", "cf"],
        ["item_wind_waker"] = ["大风杖", "大吹风", "风灵之杖", "宽恕权杖", "dfz"],
        ["item_dagon"] = ["红杖", "大根", "达戈之神力", "hz", "dg"],
        ["item_dagon_2"] = ["红杖", "大根", "2级红杖", "hz", "dg"],
        ["item_dagon_3"] = ["红杖", "大根", "3级红杖", "hz", "dg"],
        ["item_dagon_4"] = ["红杖", "大根", "4级红杖", "hz", "dg"],
        ["item_dagon_5"] = ["红杖", "大根", "5级红杖", "五级大根", "hz", "dg"],
        ["item_orchid"] = ["紫苑", "沉默权杖", "紫苑权杖", "zy"],
        ["item_bloodthorn"] = ["血棘", "大紫苑", "暴击紫苑", "xj", "dzy"],
        ["item_invis_sword"] = ["隐刀", "影刃", "洛萨之锋", "洛萨", "yd", "yr"],
        ["item_silver_edge"] = ["大隐刀", "白银之锋", "破被动", "dyd", "byzf"],
        ["item_bfury"] = ["狂战", "狂战斧", "刷钱斧", "kz", "kzf"],
        ["item_greater_crit"] = ["大炮", "代达罗斯之殇", "暴击刀", "dp"],
        ["item_lesser_crit"] = ["水晶剑", "小炮", "小暴击", "sjj", "xp"],
        ["item_manta"] = ["分身", "分身斧", "幻影斧", "曼塔", "fs", "fsf"],
        ["item_sange_and_yasha"] = ["双刀", "散夜对剑", "sd"],
        ["item_kaya_and_sange"] = ["法系双刀", "散慧对剑", "蓝红双刀"],
        ["item_yasha_and_kaya"] = ["蓝绿双刀", "夜慧对剑"],
        ["item_skadi"] = ["冰眼", "斯嘉蒂之眼", "减速刀", "by", "sjdy"],
        ["item_assault"] = ["强袭", "强袭胸甲", "减甲甲", "qx"],
        ["item_monkey_king_bar"] = ["mkb", "金箍棒", "克敌先机", "jgb"],
        ["item_armlet"] = ["臂章", "莫尔迪基安的臂章", "切臂章", "bz"],
        ["item_rapier"] = ["圣剑", "翻盘神器", "sj"],
        ["item_heart"] = ["龙心", "恐鳌之心", "回血心", "lx"],
        ["item_pipe"] = ["笛子", "洞察烟斗", "魔抗笛", "dz", "dcyd"],
        ["item_mekansm"] = ["梅肯", "梅肯斯姆", "加血", "mk", "mksm"],
        ["item_urn_of_shadows"] = ["骨灰", "影之灵龛", "骨灰盒", "gh"],
        ["item_spirit_vessel"] = ["大骨灰", "魂之灵龛", "减疗盒", "dgh"],
        ["item_meteor_hammer"] = ["陨星锤", "锤子", "砸塔锤", "yxc"],
        ["item_ancient_janggo"] = ["战鼓", "古之忍耐姜歌", "zg"],
        ["item_vladmir"] = ["祭品", "弗拉迪米尔的祭品", "吸血光环", "jp"],
        ["item_diffusal_blade"] = ["散失", "散失之刃", "净魂之刃", "削蓝刀", "ss"],
        ["item_disperser"] = ["大散失", "散灵之刃", "群体加速刀", "dss"],
        ["item_hand_of_midas"] = ["点金", "点金手", "迈达斯之手", "dj", "djs"],
        ["item_lotus_orb"] = ["莲花", "清莲宝珠", "解控制", "lh"],
        ["item_force_staff"] = ["推推", "推推棒", "原力法杖", "tt"],
        ["item_hurricane_pike"] = ["大推推", "飓风长戟", "长戟", "dtt"],
        ["item_aether_lens"] = ["以太", "以太之镜", "施法距离", "yt"],
        ["item_octarine_core"] = ["玲珑心", "减CD", "减冷却", "llx"],
        ["item_blade_mail"] = ["刃甲", "反伤甲", "rj"],
        ["item_vanguard"] = ["先锋盾", "格挡盾", "xfd"],
        ["item_crimson_guard"] = ["赤红甲", "群体物抗", "chj"],
        ["item_shivas_guard"] = ["冰甲", "希瓦", "希瓦的守护", "bj", "xw"],
        ["item_radiance"] = ["辉耀", "烫金", "灼烧", "hy"],
        ["item_desolator"] = ["暗灭", "黯灭", "减甲刀", "am"],
        ["item_satanic"] = ["撒旦", "撒旦之邪力", "大吸血", "sd"],
        ["item_echo_sabre"] = ["连击刀", "回音战刃", "两刀", "ljd"],
        ["item_harpoon"] = ["鱼叉", "拉人鱼叉", "yc"],
        ["item_heavens_halberd"] = ["天堂", "天堂之戟", "缴械", "tt"],
        ["item_nullifier"] = ["否决", "否决挂饰", "驱散刀", "fj"],
        ["item_aeon_disk"] = ["盘子", "永恒之盘", "保命盘", "pz"],
        ["item_revenants_brooch"] = ["胸针", "英灵胸针", "法系平A", "xz"],
        ["item_holy_locket"] = ["圣洁法杖", "大奶瓶", "能量点加血", "sj"],
        ["item_basher"] = ["晕锤", "碎颅锤", "小晕锤", "yc"],
        ["item_abyssal_blade"] = ["大晕锤", "深渊之刃", "深渊", "dyc"],
        ["item_refresher_orb"] = ["刷新", "刷新球", "刷新核心", "sx"],
        ["item_refresher_shard"] = ["刷新碎片", "三代肉山刷新", "sx"],
        ["item_moon_shard"] = ["银月", "银月之晶", "吃攻速", "yy"],
        ["item_ultimate_scepter"] = ["a杖", "A杖", "阿哈利姆神杖", "神杖", "az"],
        ["item_aghanims_shard"] = ["魔晶", "阿哈利姆魔晶", "碎片", "mj"],

        // 鞋类
        ["item_boots"] = ["草鞋", "速度之靴", "cx"],
        ["item_power_treads"] = ["假腿", "动力鞋", "切属性鞋", "jt"],
        ["item_phase_boots"] = ["相位", "相位鞋", "加速鞋", "xw"],
        ["item_tranquil_boots"] = ["绿鞋", "静谧鞋", "回血鞋", "lx"],
        ["item_arcane_boots"] = ["秘法", "秘法鞋", "回蓝鞋", "mf"],
        ["item_guardian_greaves"] = ["大鞋", "卫士胫甲", "解状态大鞋", "dx"],
        ["item_travel_boots"] = ["飞鞋", "远行鞋", "传送鞋", "fx"],
        ["item_travel_boots_2"] = ["2级飞鞋", "大飞鞋", "远行鞋2级", "dfx"],

        // 消耗品与基础辅助
        ["item_tango"] = ["吃树", "树之祭祀", "树", "cs"],
        ["item_clarity"] = ["小蓝", "净化药水", "回蓝药水", "xl"],
        ["item_flask"] = ["大药", "治疗药膏", "回血药膏", "dy"],
        ["item_magic_stick"] = ["魔棒", "小魔棒", "充能棒", "mb"],
        ["item_magic_wand"] = ["大魔棒", "魔杖", "万能魔杖", "dmb", "mz"],
        ["item_smoke_of_deceit"] = ["开雾", "诡计之雾", "雾", "kw"],
        ["item_dust"] = ["粉", "显影之尘", "真视粉", "撒粉"],
        ["item_ward_observer"] = ["假眼", "侦查守卫", "眼", "高台眼", "jy"],
        ["item_ward_sentry"] = ["真眼", "岗哨守卫", "排眼", "zy"],
        ["item_ward_dispenser"] = ["双眼", "守卫合辑", "眼盒", "sy"],
        ["item_tpscroll"] = ["tp", "TP", "回城", "回城卷轴", "回城卷", "hc"],
        ["item_bottle"] = ["魔瓶", "瓶子", "装符瓶", "mp", "pz"],
        ["item_enchanted_mango"] = ["芒果", "魔法芒果", "瞬回蓝", "mg"],
        ["item_faerie_fire"] = ["仙灵之火", "仙灵草", "救命草", "吃火", "xl"],
        ["item_blood_grenade"] = ["手雷", "血怒手雷", "血色手雷", "sl"],
        ["item_tome_of_knowledge"] = ["经验书", "知识之书", "经验", "jys"],
        ["item_infused_raindrop"] = ["雨露", "凝魂之露", "抵挡魔法伤害", "yl"],
        ["item_gem"] = ["真视宝石", "宝石", "看隐身", "bs"],
        ["item_cheese"] = ["奶酪", "肉山奶酪", "nl"],
    };

    /// <summary>
    /// 判断关键字是否命中英雄的名称、英文名、ID或别名/缩写（如 sa -> 力丸, pa -> 幻影刺客）
    /// </summary>
    public static bool MatchesHero(HeroCardItem hero, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        var cleanKey = keyword.Trim().ToLowerInvariant();

        // 1. 官方中文名与英文系统名匹配
        if (hero.Name.Contains(cleanKey, StringComparison.CurrentCultureIgnoreCase)) return true;
        if (hero.Hero.Name.Contains(cleanKey, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(hero.Hero.EnglishName) && hero.Hero.EnglishName.Contains(cleanKey, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(hero.Hero.LocalizedName) && hero.Hero.LocalizedName.Contains(cleanKey, StringComparison.OrdinalIgnoreCase)) return true;

        // 2. 别名表匹配
        if (HeroAliasMap.TryGetValue(hero.Hero.Name, out var aliases))
        {
            foreach (var alias in aliases)
            {
                if (alias.Equals(cleanKey, StringComparison.OrdinalIgnoreCase)) return true;
                if (cleanKey.Length >= 2 && alias.Contains(cleanKey, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        // 3. 汉语拼音首字母简单模糊匹配（如 "lk" 匹配 "力丸"）
        if (cleanKey.Length >= 2 && PinyinAcronymMatches(hero.Name, cleanKey))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 判断关键字是否命中物品的标准名、内部代码、系统标记或别名/缩写（如 电锤 -> 漩涡/雷神之锤, bkb -> 黑皇杖）
    /// </summary>
    public static bool MatchesItem(CatalogItem item, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return true;
        var cleanKey = keyword.Trim().ToLowerInvariant();

        // 1. 标准名称、副标题、标签匹配
        if (item.Name.Contains(cleanKey, StringComparison.CurrentCultureIgnoreCase)) return true;
        if (item.Subtitle?.Contains(cleanKey, StringComparison.OrdinalIgnoreCase) == true) return true;
        if (item.SearchTokens.Contains(cleanKey, StringComparison.OrdinalIgnoreCase)) return true;

        // 2. 内部代号比对 (item_maelstrom / maelstrom 等)
        var rawId = (item.Id ?? string.Empty).ToLowerInvariant();
        var keyWithPrefix = rawId.StartsWith("item_") ? rawId : "item_" + rawId;
        var keyWithoutPrefix = rawId.StartsWith("item_") ? rawId[5..] : rawId;

        if (ItemAliasMap.TryGetValue(rawId, out var aliases) ||
            ItemAliasMap.TryGetValue(keyWithPrefix, out aliases) ||
            ItemAliasMap.TryGetValue(keyWithoutPrefix, out aliases))
        {
            foreach (var alias in aliases)
            {
                if (alias.Equals(cleanKey, StringComparison.OrdinalIgnoreCase)) return true;
                if (cleanKey.Length >= 2 && alias.Contains(cleanKey, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        // 3. 拼音首字母匹配
        if (cleanKey.Length >= 2 && PinyinAcronymMatches(item.Name, cleanKey))
        {
            return true;
        }

        return false;
    }

    private static bool PinyinAcronymMatches(string chinese, string acronym)
    {
        // 快速提取汉字首拼音进行比对（覆盖常见场景）
        var pinyin = GetChineseInitialLetters(chinese);
        return !string.IsNullOrEmpty(pinyin) && pinyin.Contains(acronym, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetChineseInitialLetters(string chinese)
    {
        if (string.IsNullOrWhiteSpace(chinese)) return string.Empty;
        var chars = new List<char>();
        foreach (var c in chinese)
        {
            if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9')
            {
                chars.Add(char.ToLowerInvariant(c));
                continue;
            }

            var initial = GetSinglePinyinInitial(c);
            if (initial != '\0') chars.Add(initial);
        }
        return new string(chars.ToArray());
    }

    private static char GetSinglePinyinInitial(char c)
    {
        try
        {
            // 常见汉字区段首字母速查
            var bytes = System.Text.Encoding.GetEncoding("GB2312").GetBytes(c.ToString());
            if (bytes.Length < 2) return '\0';
            var code = (bytes[0] << 8) + bytes[1];
            return code switch
            {
                >= 0xB0A1 and <= 0xB0C4 => 'a',
                >= 0xB0C5 and <= 0xB2C0 => 'b',
                >= 0xB2C1 and <= 0xB4ED => 'c',
                >= 0xB4EE and <= 0xB6E9 => 'd',
                >= 0xB6EA and <= 0xB7A1 => 'e',
                >= 0xB7A2 and <= 0xB8C0 => 'f',
                >= 0xB8C1 and <= 0xB9FD => 'g',
                >= 0xB9FE and <= 0xBBF6 => 'h',
                >= 0xBBF7 and <= 0xBFA5 => 'j',
                >= 0xBFA6 and <= 0xC0AB => 'k',
                >= 0xC0AC and <= 0xC2E7 => 'l',
                >= 0xC2E8 and <= 0xC4C2 => 'm',
                >= 0xC4C3 and <= 0xC5B5 => 'n',
                >= 0xC5B6 and <= 0xC5BD => 'o',
                >= 0xC5BE and <= 0xC6D9 => 'p',
                >= 0xC6DA and <= 0xC8BA => 'q',
                >= 0xC8BB and <= 0xC8F5 => 'r',
                >= 0xC8F6 and <= 0xCBF9 => 's',
                >= 0xCBFA and <= 0xCDD9 => 't',
                >= 0xCDDA and <= 0xCEF3 => 'w',
                >= 0xCEF4 and <= 0xD1B8 => 'x',
                >= 0xD1B9 and <= 0xD4D0 => 'y',
                >= 0xD4D1 and <= 0xD7F9 => 'z',
                _ => '\0'
            };
        }
        catch
        {
            return '\0';
        }
    }
}
