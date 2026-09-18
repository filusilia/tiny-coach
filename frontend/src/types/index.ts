export type PrimaryAttr = 'str' | 'agi' | 'int' | 'all';
export type AttackType = 'Melee' | 'Ranged';

export interface HeroStat {
  id: number;
  name: string;
  localized_name: string;
  primary_attr: PrimaryAttr;
  attack_type: AttackType;
  roles: string[];
  img: string;
  icon: string;
  base_health: number;
  base_health_regen: number;
  base_mana: number;
  base_mana_regen: number;
  base_armor: number;
  base_mr: number;
  base_attack_min: number;
  base_attack_max: number;
  base_str: number;
  base_agi: number;
  base_int: number;
  str_gain: number;
  agi_gain: number;
  int_gain: number;
  attack_range: number;
  projectile_speed: number;
  attack_rate: number;
  base_attack_time: number;
  move_speed: number;
  turn_rate?: number;
  cm_enabled: boolean;
  legs: number;
  day_vision: number;
  night_vision: number;
  hero_id?: number;
  turbo_picks?: number;
  turbo_wins?: number;
  pro_win?: number;
  pro_pick?: number;
  pro_ban?: number;
  [key: string]: unknown;
}

export interface SpecialValue {
  name: string;
  values_float?: number[];
  values_int?: number[];
  values?: (number | string)[];
  values_scepter?: (number | string)[];
  values_shard?: (number | string)[];
  heading_loc?: string;
  is_percentage?: boolean;
  bonuses?: {
    name: string;
    value: number;
    operation: number;
  }[];
}

export interface OfficialAbility {
  id: number;
  name: string;
  name_loc: string;
  desc_loc: string;
  lore_loc?: string;
  notes_loc?: string[];
  shard_loc?: string;
  scepter_loc?: string;
  type: number;
  behavior: string | number;
  target_team: number;
  target_type: number;
  damage: number;
  immunity: number;
  dispellable: number;
  max_level: number;
  cast_ranges?: number[];
  cast_points?: number[];
  channel_times?: number[];
  cooldowns?: number[];
  durations?: number[];
  damages?: number[];
  mana_costs?: number[];
  special_values: SpecialValue[];
  is_innate?: boolean;
  ability_is_innate?: boolean;
  ability_has_scepter?: boolean;
  ability_has_shard?: boolean;
  ability_is_granted_by_scepter?: boolean;
  ability_is_granted_by_shard?: boolean;
}

export interface LocalizedHeroData {
  id: number;
  name: string;
  name_loc: string;
  hype_loc?: string;
  primary_attr: number;
  abilities: OfficialAbility[];
  talents: OfficialAbility[];
}

export interface ItemData {
  id: number;
  name: string;
  cost: number;
  secret_shop: number;
  side_shop: number;
  recipe: number;
  localized_name: string;
  components?: string[] | null;
  created?: boolean;
  charges?: boolean | number;
  tier?: number | null;
}

export type AdviceSeverity = 'Critical' | 'Warning' | 'Info';

export interface CoachAdvice {
  id: string;
  rule?: string;
  rule_id?: string;
  title: string;
  text: string;
  severity: AdviceSeverity;
  created_at: string;
  suggested_action?: string;
  is_voice_spoken?: boolean;
  ai_explanation?: string;
}

export type VoiceMode = 0 | 1 | 2; // 0: 关闭, 1: 播报 (仅屏幕), 2: 播报-语音 (发声)

export interface AppSettings {
  gsi_port: number;
  auto_start_gsi: boolean;
  smart_coach_enabled: boolean;
  voice_broadcast_mode: VoiceMode;
  voice_volume: number;
  voice_speed: number;
  voice_name: string;
  edge_voice_name: string;
  voice_lexicon_enabled: boolean;
  voice_ssml_enabled: boolean;
  auto_show_overlay: boolean;
}
