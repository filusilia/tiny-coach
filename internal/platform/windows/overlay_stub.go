//go:build !windows

package windows

import "github.com/wailsapp/wails/v3/pkg/application"

// DetachWindowOwner 非 Windows 平台的空实现
func DetachWindowOwner(hwnd uintptr) {}

// ForceTopMost 非 Windows 平台的空实现
func ForceTopMost(hwnd uintptr) {}

// ApplyMouseThrough 非 Windows 平台的空实现
func ApplyMouseThrough(hwnd uintptr, ignore bool) {}

// EnsureOverlayExtendedStyle 非 Windows 平台的空实现
func EnsureOverlayExtendedStyle(hwnd uintptr) {}

// SetupOverlay 非 Windows 平台的空实现
func SetupOverlay(win *application.WebviewWindow) func() {
	return func() {}
}

// StartTopMostRefresher 非 Windows 平台的空实现
func StartTopMostRefresher(win *application.WebviewWindow) {}
