/**
 * 英雄主属性类型枚举
 * - str: 力量 (Strength)
 * - agi: 敏捷 (Agility)
 * - int: 智力 (Intelligence)
 * - all: 全属性 (Universal / All)
 */
export type PrimaryAttr = 'str' | 'agi' | 'int' | 'all'

/**
 * 英雄攻击距离类型枚举
 * - Melee: 近战
 * - Ranged: 远程
 */
export type AttackType = 'Melee' | 'Ranged'

/**
 * 英雄 1 级基础属性与成长属性核心实体
 * 遵循官方数据规范与 1 级基础属性计算公式
 */
export interface HeroStat {
  /** 英雄官方数字 ID (如 1 为敌法师) */
  id: number
  /** 英雄内部英文标识符 (如 "npc_dota_hero_antimage") */
  name: string
  /** 英雄本地化中文译名 (如 "敌法师") */
  localized_name: string
  /** 英雄主属性类别 ('str' | 'agi' | 'int' | 'all') */
  primary_attr: PrimaryAttr
  /** 英雄攻击类别 ('Melee' | 'Ranged') */
  attack_type: AttackType
  /** 英雄官方职能定位标签列表 (如 ["Carry", "Escape", "Nuker"]) */
  roles: string[]
  /** 英雄高分辨率立绘/半身像 CDN 相对相对路径 */
  img: string
  /** 英雄正方形小头像 CDN 路径 */
  icon: string
  /** 基础生命值 (未计入力量加成前的白字底数，通常为 120) */
  base_health: number
  /** 基础生命恢复速率 (HP/s) */
  base_health_regen: number
  /** 基础魔法值 (未计入智力加成前的白字底数，通常为 75) */
  base_mana: number
  /** 基础魔法恢复速率 (MP/s) */
  base_mana_regen: number
  /** 基础护甲 (未计入敏捷护甲转化前的底数，可为负数) */
  base_armor: number
  /** 基础魔法抗性百分比 (大部分英雄为 25%) */
  base_mr: number
  /** 基础攻击力下限 (未计入属性攻击加成前的纯白字底数) */
  base_attack_min: number
  /** 基础攻击力上限 (未计入属性攻击加成前的纯白字底数) */
  base_attack_max: number
  /** 1 级初始基础力量值 (白字力量) */
  base_str: number
  /** 1 级初始基础敏捷值 (白字敏捷) */
  base_agi: number
  /** 1 级初始基础智力值 (白字智力) */
  base_int: number
  /** 每升一级获得的力量成长值 */
  str_gain: number
  /** 每升一级获得的敏捷成长值 */
  agi_gain: number
  /** 每升一级获得的智力成长值 */
  int_gain: number
  /** 基础攻击距离 (单位: 码) */
  attack_range: number
  /** 普通攻击弹道飞行速度 (单位: 码/秒，近战通常为 0 即瞬发) */
  projectile_speed: number
  /** 基础攻击速率系数 (默认 1.0) */
  attack_rate: number
  /** 基础攻击间隔 BAT (Base Attack Time，绝大部分为 1.7) */
  base_attack_time: number
  /** 基础移动速度 (单位: 码/秒) */
  move_speed: number
  /** 转身速率 (单位: rad/s 或比率，可选) */
  turn_rate?: number
  /** 队长模式 (Captains Mode) 是否已启用 */
  cm_enabled: boolean
  /** 模型腿数 (Dota 趣味统计与游戏底层判定) */
  legs: number
  /** 白天基础视野范围 (默认 1800) */
  day_vision: number
  /** 夜晚基础视野范围 (默认 800) */
  night_vision: number
  /** 兼容字段: 英雄数字 ID */
  hero_id?: number
  /** 加速模式挑选次数 (可选) */
  turbo_picks?: number
  /** 加速模式胜场次数 (可选) */
  turbo_wins?: number
  /** 职业比赛胜场次数 (可选) */
  pro_win?: number
  /** 职业比赛挑选次数 (可选) */
  pro_pick?: number
  /** 职业比赛禁用次数 (可选) */
  pro_ban?: number
  /** 扩展属性泛型支持 */
  [key: string]: unknown
}

/**
 * 技能特有数值与加成结构实体
 * 解析自官方 NPC 脚本或官方 API，用于动态替换技能描述中的宏占位符 (%var%)
 */
export interface SpecialValue {
  /** 变量标识符 (如 "damage", "radius", "duration") */
  name: string
  /** 浮点型各级数值列表 */
  values_float?: number[]
  /** 整型各级数值列表 */
  values_int?: number[]
  /** 混合或字符串格式的数值列表 */
  values?: (number | string)[]
  /** 阿哈利姆神杖加成后的专属数值列表 */
  values_scepter?: (number | string)[]
  /** 阿哈利姆魔晶加成后的专属数值列表 */
  values_shard?: (number | string)[]
  /** 本地化标题文本 (如 "伤害：", "作用范围：") */
  heading_loc?: string
  /** 该数值是否为百分比格式 */
  is_percentage?: boolean
  /** 天赋或神杖对该数值的操作修饰器列表 */
  bonuses?: {
    name: string
    value: number
    operation: number
  }[]
}

/**
 * 官方标准技能与天赋实体
 * 涵盖主动技能、被动技能、先天技能 (Innate) 以及阿哈利姆神杖/魔晶强化说明
 */
export interface OfficialAbility {
  /** 技能数字 ID */
  id: number
  /** 技能英文内部标识名 (如 "antimage_blink") */
  name: string
  /** 技能本地化中文名称 (如 "闪烁") */
  name_loc: string
  /** 技能本地化中文说明文本 (包含待解析宏占位符) */
  desc_loc: string
  /** 技能背景传奇传记 (Lore) */
  lore_loc?: string
  /** 技能机制额外注解说明列表 */
  notes_loc?: string[]
  /** 阿哈利姆魔晶升级效果专有说明文本 */
  shard_loc?: string
  /** 阿哈利姆神杖升级效果专有说明文本 */
  scepter_loc?: string
  /** 技能类型枚举标志 */
  type: number
  /** 技能施法行为位掩码 (如 点目标、无目标、被动等) */
  behavior: string | number
  /** 目标队伍限制标志 (友方、敌方、两者) */
  target_team: number
  /** 目标类别标志 (英雄、小兵、建筑) */
  target_type: number
  /** 伤害类型 (1: 物理, 2: 魔法, 4: 纯粹) */
  damage: number
  /** 技能免疫穿透状态 (是否无视黑皇杖 BKB) */
  immunity: number
  /** 弱驱散/强驱散判定状态 */
  dispellable: number
  /** 技能最高可加点等级 (普通技能 4，终极技能 3) */
  max_level: number
  /** 各等级施法距离列表 (单位: 码) */
  cast_ranges?: number[]
  /** 各等级施法前摇点列表 (单位: 秒) */
  cast_points?: number[]
  /** 各等级持续施法时间列表 (单位: 秒) */
  channel_times?: number[]
  /** 各等级冷却时间列表 (单位: 秒) */
  cooldowns?: number[]
  /** 各等级持续生效时间列表 (单位: 秒) */
  durations?: number[]
  /** 各等级伤害数值列表 */
  damages?: number[]
  /** 各等级魔法消耗数值列表 */
  mana_costs?: number[]
  /** 技能动态特殊变量数值列表 (用于文本宏替换) */
  special_values: SpecialValue[]
  /** 是否为先天技能 (7.36+ 引入) */
  is_innate?: boolean
  /** 兼容字段: 是否为先天技能 */
  ability_is_innate?: boolean
  /** 是否具有阿哈利姆神杖升级机制 */
  ability_has_scepter?: boolean
  /** 是否具有阿哈利姆魔晶升级机制 */
  ability_has_shard?: boolean
  /** 是否为购买阿哈利姆神杖后额外赋予的新增技能 */
  ability_is_granted_by_scepter?: boolean
  /** 是否为购买阿哈利姆魔晶后额外赋予的新增技能 */
  ability_is_granted_by_shard?: boolean
}

/**
 * 包含技能与天赋树的官方英雄全量本地化实体
 */
export interface LocalizedHeroData {
  /** 英雄数字 ID */
  id: number
  /** 英雄内部名称 */
  name: string
  /** 英雄中文名称 */
  name_loc: string
  /** 英雄简短背景介绍与特色提炼 */
  hype_loc?: string
  /** 官方主属性数字标识 (0: 力量, 1: 敏捷, 2: 智力, 3: 全属性) */
  primary_attr: number
  /** 英雄常规技能与先天技能列表 */
  abilities: OfficialAbility[]
  /** 英雄 8 个天赋树节点列表 (10/15/20/25 级左右分支) */
  talents: OfficialAbility[]
}

/**
 * 物品白名单与基础属性实体
 * 严格遵照 Dota 2 官网 7.41f 标准目录与分类规范
 */
export interface ItemData {
  /** 物品数字 ID */
  id: number
  /** 物品内部英文标识 (如 "item_blink") */
  name: string
  /** 物品总购买金钱花费 */
  cost: number
  /** 是否为神秘商店专属物品 (0 或 1) */
  secret_shop: number
  /** 遗留字段: 路边野外商店专属物品 */
  side_shop: number
  /** 是否包含或本身是配方卷轴 (0 或 1) */
  recipe: number
  /** 物品本地化中文名称 (如 "闪烁匕首") */
  localized_name: string
  /** 合成该物品所需的子配件物品名称列表 */
  components?: string[] | null
  /** 是否属于合成产物 */
  created?: boolean
  /** 物品是否支持充能或初始充能点数 */
  charges?: boolean | number
  /** 中立生物掉落物品等级 (1 到 5 阶，常规物品为 null) */
  tier?: number | null
}

/**
 * 战术建议与告警紧急程度分类
 * - Critical: 致命/极其紧急 (红牌告警，如高危暴毙、肉山盾即将失效)
 * - Warning: 战术预警 (黄牌注意，如敌方关键装就位、魔晶刷新)
 * - Info: 宏观情报 (蓝牌/绿牌，如神符刷新、控线推进建议)
 */
export type AdviceSeverity = 'Critical' | 'Warning' | 'Info'

/**
 * 实时教练战术建议与事件提醒核心实体
 * 承载规则引擎生成的确定性告警以及 AI 扩展旁路分析
 */
export interface CoachAdvice {
  /** 告警唯一追踪 UUID */
  id: string
  /** 宏观提醒所属类别分类键 (如 "roshan_tactics") */
  category?: ReminderCategoryKey
  /** 触发该建议的规则名称 */
  rule?: string
  /** 规则唯一英文字符串标识 */
  rule_id?: string
  /** 告警标题 (精炼醒目，展示在 HUD 卡片头部) */
  title: string
  /** 告警主要阐述文本 (展示于 UI 或送入语音播报) */
  text: string
  /** 告警紧急程度 (Critical / Warning / Info) */
  severity: AdviceSeverity
  /** 生成该告警的 ISO 8601 格式时间戳 */
  created_at: string
  /** 建议执行的具体操作指令 (如 "撤退回高地", "立即开雾") */
  suggested_action?: string
  /** 该告警是否已经完成语音播报 (用于防重播) */
  is_voice_spoken?: boolean
  /** AI 旁路深度解读与原因推导分析 (可选，支持动态追加) */
  ai_explanation?: string
}

/**
 * 提醒播报模式枚举
 * 0: 关闭该类别的提醒与展示
 * 1: 仅在前端卡片/看板做文字展示，静音
 * 2: 语音朗读并伴随文字卡片展示
 */
export type BroadcastMode = 0 | 1 | 2
export type VoiceMode = BroadcastMode

/**
 * 宏观战略与战术提醒分类键集合
 * - macro_tempo: 推进/对线期宏观运营节奏
 * - roshan_tactics: 肉山刷新区间与击杀战略
 * - tormentor_tactics: 魔方 (苦难魔军) 刷新与击杀时机
 * - rune_control: 神符刷新前夕控符提醒 (赏金/神水/极速等)
 * - buyback_strategy: 买活金钱储备与买活 CD 告警
 * - item_timing_threat: 敌我双方关键装 (BKB/跳刀/A杖) 质变时机
 * - tactical_ai_ask: AI 深度战术问询与策略解答
 * - ability_ready: 大招与终极技能冷却就绪
 * - death_respawn: 复活倒计时与阵亡买活评估
 */
export type ReminderCategoryKey
  = | 'macro_tempo'
    | 'roshan_tactics'
    | 'tormentor_tactics'
    | 'rune_control'
    | 'buyback_strategy'
    | 'item_timing_threat'
    | 'tactical_ai_ask'
    | 'ability_ready'
    | 'death_respawn'

/**
 * 提醒分类配置定义实体
 * 用于在设置页与战术面板中呈现各类别的触发说明与播报配置
 */
export interface ReminderCategoryConfig {
  /** 提醒分类唯一标识符 */
  key: ReminderCategoryKey
  /** 分类中文显示名称 */
  label: string
  /** 触发判定逻辑机制简述 */
  triggerCondition: string
  /** 系统默认预设播报模式 */
  defaultMode: BroadcastMode
  /** 当前用户配置的实际播报模式 */
  mode: BroadcastMode
  /** 该分类的核心战略价值描述 */
  description: string
  /** 分组归类 ('macro': 宏观战略, 'tactical': 微观战术) */
  group?: 'macro' | 'tactical'
}

/**
 * 提醒分类播报模式快速映射字典
 */
export type ReminderModesMap = Partial<Record<ReminderCategoryKey, BroadcastMode>>

/**
 * AI 推理引擎运行模式枚举
 * - response_api: 标准通用 Response API 模式 (无论本地 Ollama/LMStudio/网关 还是公有云 DeepSeek/OpenAI 均使用标准 /v1/chat/completions)
 * - ollama: 本地 Ollama 引擎 (/chat/completions)
 * - remote: 远程 Response API 引擎 (/responses)
 * - rule_only: 纯确定性规则引擎，完全关闭 LLM 推理
 * - local_npu / cloud_api / npu / cloud / response_api: 兼容历史模式别名
 */
export type AiEngineMode = 'ollama' | 'remote' | 'rule_only' | 'response_api' | 'local_npu' | 'cloud_api' | 'npu' | 'cloud'

/**
 * AI 端点可用性探测与健康检查结果实体
 */
export interface AiEndpointTestResult {
  /** 探测是否成功连通且成功返回响应 */
  success: boolean
  /** 端点往返网络与推理总时延 (毫秒) */
  latencyMs: number
  /** HTTP 响应状态码 (如 200) */
  statusCode?: number
  /** 实际响应并参与推理的模型名称 */
  modelUsed?: string
  /** 探测请求返回的简短文本预览 */
  responsePreview?: string
  /** 原始返回内容 (用于详细回显) */
  rawResponse?: string
  /** 探测失败时的详细排查错误信息 */
  errorMessage?: string
  /** 执行探测测试的时间戳 */
  testedAt: string
}

/**
 * 全局应用配置核心实体 (持久化至本地存储)
 * 涵盖 GSI 端口、教练总开关、语音引擎、悬浮窗、缓存目录、绑定玩家以及 AI 端点
 */
export interface AppSettings {
  /** GSI 监听端口号 (默认 4444) */
  gsi_port: number
  /** 启动应用时是否自动开启 GSI HTTP 监听服务 */
  auto_start_gsi: boolean
  /** 实时教练与智能告警核心总开关 */
  smart_coach_enabled: boolean
  /** 全局语音播报模式 (0: 关闭, 1: 仅文字, 2: 语音+文字) */
  voice_broadcast_mode: VoiceMode
  /** 各细分分类的独立播报偏好设置 */
  reminder_modes: Partial<Record<ReminderCategoryKey, BroadcastMode>>
  /** 语音播报音量大小 (0.0 ~ 1.0) */
  voice_volume: number
  /** 语音播报语速倍率 (0.5 ~ 2.0) */
  voice_speed: number
  /** 默认语音音色标识符（纯 MeloTTS 模式下为本地模型音色） */
  voice_name: string
  /** 微软 Edge TTS 在线高保真音色名称 (兼容历史配置) */
  edge_voice_name?: string
  /** 是否启用 Dota 专业电竞专有名词读音词典矫正 (如 "BKB", "A杖") */
  voice_lexicon_enabled: boolean
  /** 是否启用 SSML 标记语言以增强语音韵律与重音 */
  voice_ssml_enabled: boolean
  /** 是否在后台自动检测并下载 MeloTTS 缺失权重 */
  melo_auto_download?: boolean
  /** 对局启动时是否自动弹出置顶经济与战术悬浮窗 */
  auto_show_overlay: boolean
  /** 兼容字段: 游戏启动时自动呼出悬浮窗 */
  auto_overlay_on_game_start: boolean
  /** 局内经济悬浮窗不透明度 (30 ~ 100，默认 90) */
  overlay_opacity?: number
  /** 英雄数据库天梯段位环境偏好 (1~8，默认 7: 超凡入圣) */
  hero_rank_tier?: number
  /** 英雄数据库列表排序方式偏好 ('win_desc' | 'pick_desc' | 'id_asc') */
  hero_sort_mode?: 'win_desc' | 'pick_desc' | 'id_asc'
  /** 用户配置的自定义文件缓存存储目录路径 */
  cache_directory: string
  /** 绑定的 Dota 2 玩家 32 位数字账号 ID (如 12345678) */
  dota_player_id: string
  /** 绑定玩家的本地化昵称 (由 OpenDota 同步拉取) */
  dota_player_name?: string
  /** 绑定玩家的头像 URL 链接 */
  dota_player_avatar?: string
  /** 是否启用第三方或自定义 HTTP TTS 语音合成引擎 (兼容旧配置) */
  custom_tts_enabled?: boolean
  /** 自定义 TTS 引擎架构类型 (兼容旧配置) */
  custom_tts_engine?: 'edge' | 'go_speech' | 'cosyvoice' | 'chattts' | 'openai_tts' | 'custom_http'
  /** 自定义 TTS 服务的 HTTP POST 请求端点 URL (兼容旧配置) */
  custom_tts_endpoint?: string
  /** 自定义 TTS 服务的 API 认证密钥 (可选) */
  custom_tts_api_key?: string
  /** 自定义 TTS 服务的预设音色/发音人标识 (可选) */
  custom_tts_voice?: string
  /** AI 实时战术推理运行模式 ('response_api' | 'rule_only' | 兼容旧值) */
  ai_mode: AiEngineMode
  /** 通用 Response API 服务端点 (兼容本地 Ollama/网关或公有云，默认 http://127.0.0.1:11434/v1) */
  ai_endpoint?: string
  /** 通用 Response API 认证密钥 (公有云需填写，本地通常可留空) */
  ai_api_key?: string
  /** 通用 Response API 目标大模型名称 (如 qwen2.5:1.5b 或 deepseek-chat) */
  ai_model?: string
  /** 本地 Ollama 或端侧 NPU 的 HTTP 服务端点 (兼容历史字段) */
  npu_endpoint?: string
  /** 本地 Ollama 挂载的目标大模型名称 (兼容历史字段) */
  npu_model?: string
  /** 本地选中的 AI 模型文件绝对物理路径 (如 GGUF / ONNX / Bin 模型权重) */
  local_model_path?: string
  /** 远程云端大模型 API 访问密钥 (兼容历史字段) */
  cloud_api_key?: string
  /** 远程云端大模型 API Base URL (兼容历史字段) */
  cloud_base_url?: string
  /** 远程云端大模型名称 (兼容历史字段) */
  cloud_model?: string
  /** 用户个性化战术背景偏好或打法风格补充描述 */
  user_context?: string
}
