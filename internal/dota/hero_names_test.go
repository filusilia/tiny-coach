package dota

import "testing"

func TestDotaHeroConstantsReadOnly(t *testing.T) {
	name := GetHeroChineseName(1)
	if name != "敌法师" {
		t.Fatalf("期望英雄 1 中文名为 '敌法师'，实际为: %s", name)
	}

	all := GetAllHeroChineseNames()
	if len(all) == 0 {
		t.Fatalf("GetAllHeroChineseNames 返回为空")
	}

	// 篡改 clone map 不会影响原始静态表
	all[1] = "篡改测试"
	if GetHeroChineseName(1) != "敌法师" {
		t.Fatalf("原始英雄中文名被外部修改篡改")
	}
}
