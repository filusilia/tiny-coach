package player

import (
	"testing"

	"aegis-coach/internal/models"
)

func TestParsePopularityStage(t *testing.T) {
	raw := map[string]int{
		"11": 1500, // 补刀斧
		"44": 2000, // 吃树
		"16": 1200, // 铁树枝干
		"42": 9999, // 假眼 (应被自动过滤)
		"46": 8888, // TP (应被自动过滤)
	}

	items := parsePopularityStage(raw, "start", 2)
	if len(items) != 2 {
		t.Fatalf("预期只保留 2 件装备，实际得到 %d", len(items))
	}
	if items[0].ItemID != 44 {
		t.Errorf("预期第一件热度最高的物品为 44 吃树，实际为 %d", items[0].ItemID)
	}
	if items[1].ItemID != 11 {
		t.Errorf("预期第二件热度最高的物品为 11 补刀斧，实际为 %d", items[1].ItemID)
	}
}

func TestGenerateCounterItemsRealLogics(t *testing.T) {
	svc := GetPlayerService()

	// 1. 无威胁英雄时，严禁伪造任何离线假出装
	emptyCounters := svc.generateCounterItems(1, nil, nil)
	if len(emptyCounters) != 0 {
		t.Fatalf("无针对性敌方威胁时，预期针对出装为空，严禁伪造假数据，实际数量: %d", len(emptyCounters))
	}

	// 2. 模拟面对蓝猫 (17, 强法爆/机动) 和幻影刺客 (44, 闪避)
	threats := []models.ThreatHeroSummary{
		{HeroID: 17, HeroName: "风暴之灵", ThreatLevel: "extreme"},
		{HeroID: 44, HeroName: "幻影刺客", ThreatLevel: "high"},
	}
	counters := svc.generateCounterItems(1, []int{17, 44}, threats)

	hasBkb := false
	hasMkb := false
	for _, it := range counters {
		if it.ItemID == 116 {
			hasBkb = true
		}
		if it.ItemID == 135 {
			hasMkb = true
		}
	}

	if !hasBkb {
		t.Errorf("面对高爆发与强机动敌方，预期针对出装包含 BKB (116)")
	}
	if !hasMkb {
		t.Errorf("面对幻影刺客闪避，预期针对出装包含金箍棒 MKB (135)")
	}
}
