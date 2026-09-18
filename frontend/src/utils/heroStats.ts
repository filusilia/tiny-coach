import { HeroStat } from "../types";

export function calculateLevel1Stats(hero: HeroStat) {
  const hp = 120 + Math.round(hero.base_str * 22);
  const hpRegen = +(hero.base_health_regen + hero.base_str * 0.1).toFixed(1);
  const mana = 75 + Math.round(hero.base_int * 12);
  const manaRegen = +(hero.base_mana_regen + hero.base_int * 0.05).toFixed(1);
  const armor = +(hero.base_armor + hero.base_agi / 6.0).toFixed(1);

  let bonusDmg = 0;
  if (hero.primary_attr === 'str') bonusDmg = hero.base_str;
  else if (hero.primary_attr === 'agi') bonusDmg = hero.base_agi;
  else if (hero.primary_attr === 'int') bonusDmg = hero.base_int;
  else if (hero.primary_attr === 'all') {
    bonusDmg = Math.floor((hero.base_str + hero.base_agi + hero.base_int) * 0.7);
  }

  const attackMin = hero.base_attack_min + bonusDmg;
  const attackMax = hero.base_attack_max + bonusDmg;

  return {
    hp,
    hpRegen,
    mana,
    manaRegen,
    armor,
    attackMin,
    attackMax,
    magicResist: hero.base_mr || 25,
    moveSpeed: hero.move_speed,
  };
}
