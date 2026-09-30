package player

import (
	"testing"
)

func TestPlayerServicePathTraversalDefense(t *testing.T) {
	svc := GetPlayerService()

	// 1. 测试 MatchDetails 路径遍历防御
	traversalIds := []string{
		"../../etc/passwd",
		"..\\..\\windows\\system32\\cmd.exe",
		"12345/../../evil",
		"abc_match",
		"",
		"   ",
	}

	for _, badId := range traversalIds {
		_, err := svc.GetMatchDetails(badId)
		if err == nil {
			t.Errorf("期望 GetMatchDetails(%q) 拦截非法 ID，但未返回错误", badId)
		}
	}

	// 2. 测试 PlayerProfile 非法/路径遍历输入防御
	badAccounts := []string{
		"../../hack",
		"..\\..\\system.ini",
		"invalid_non_numeric",
	}

	for _, badAcc := range badAccounts {
		_, err := svc.GetPlayerProfile(badAcc)
		if err == nil {
			t.Errorf("期望 GetPlayerProfile(%q) 拦截非法账号输入，但未返回错误", badAcc)
		}
	}
}
