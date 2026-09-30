package overlay

import (
	"testing"
)

func TestOverlayServiceInstance(t *testing.T) {
	svc := GetOverlayService(false)
	if svc == nil {
		t.Fatalf("GetOverlayService 返回 nil")
	}
	if svc.IsOverlayMode() {
		t.Errorf("期望 IsOverlayMode 为 false，实际为 true")
	}
}
