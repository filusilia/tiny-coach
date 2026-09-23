//go:build !windows

package overlay

import (
	"github.com/wailsapp/wails/v3/pkg/application"
)

// ForceTopMost 非 Windows 空实现
func ForceTopMost(hwnd uintptr) {}

// EnsureOverlayExtendedStyle 非 Windows 空实现
func EnsureOverlayExtendedStyle(hwnd uintptr) {}

// ApplyMouseThrough 非 Windows 空实现
func ApplyMouseThrough(hwnd uintptr, ignore bool) {}

// DetachWindowOwner 非 Windows 空实现
func DetachWindowOwner(hwnd uintptr) {}

// StartTopMostRefresher 非 Windows 空实现
func StartTopMostRefresher(win *application.WebviewWindow) {}
