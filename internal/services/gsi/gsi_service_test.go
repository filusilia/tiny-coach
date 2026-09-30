package gsi

import (
	"testing"
)

func TestGsiServicePortConfiguration(t *testing.T) {
	gsi := GetGsiService(4444)
	_ = gsi.Stop() // 确保未在监听中，避免 SetPort 被静默忽略

	origPort := gsi.GetPort()
	defer gsi.SetPort(origPort)

	if origPort != 4444 {
		gsi.SetPort(4444)
	}

	if gsi.GetPort() != 4444 {
		t.Errorf("期望 GSI 默认端口 4444，实际: %d", gsi.GetPort())
	}

	gsi.SetPort(5555)
	if gsi.GetPort() != 5555 {
		t.Errorf("期望 GSI 设置端口 5555，实际: %d", gsi.GetPort())
	}
}
