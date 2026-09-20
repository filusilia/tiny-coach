//go:build windows

package overlay

import (
	"syscall"
	"time"

	"github.com/wailsapp/wails/v3/pkg/application"
)

var (
	moduser32               = syscall.NewLazyDLL("user32.dll")
	procSetWindowPos        = moduser32.NewProc("SetWindowPos")
	procGetWindowLongW      = moduser32.NewProc("GetWindowLongW")
	procSetWindowLongW      = moduser32.NewProc("SetWindowLongW")
	procSetLayeredWindowAtt = moduser32.NewProc("SetLayeredWindowAttributes")
)

const (
	HWND_TOPMOST   = ^uintptr(0) // (HWND)-1
	HWND_NOTOPMOST = ^uintptr(1) // (HWND)-2

	SWP_NOSIZE   = 0x0001
	SWP_NOMOVE   = 0x0002
	SWP_NOACTIVATE = 0x0010
	SWP_SHOWWINDOW = 0x0040

	GWL_EXSTYLE     = ^uintptr(19) // (int32)-20 的无符号补码
	WS_EX_LAYERED   = 0x00080000
	WS_EX_TRANSPARENT = 0x00000020
	WS_EX_TOOLWINDOW = 0x00000080
	WS_EX_TOPMOST   = 0x00000008

	LWA_ALPHA = 0x00000002
)

// ForceTopMost 强制将指定 Windows 原生窗口句柄 (HWND) 设置为最高级全局顶层
func ForceTopMost(hwnd uintptr) {
	if hwnd == 0 {
		return
	}
	_, _, _ = procSetWindowPos.Call(
		hwnd,
		HWND_TOPMOST,
		0, 0, 0, 0,
		SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW,
	)
}

// EnsureOverlayExtendedStyle 确保悬浮窗具备无边框透明及工具栏隐藏的扩展样式
func EnsureOverlayExtendedStyle(hwnd uintptr) {
	if hwnd == 0 {
		return
	}
	ret, _, _ := procGetWindowLongW.Call(hwnd, GWL_EXSTYLE)
	currStyle := int32(ret)

	targetStyle := currStyle | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TOPMOST

	if currStyle != targetStyle {
		_, _, _ = procSetWindowLongW.Call(hwnd, GWL_EXSTYLE, uintptr(targetStyle))
	}
}

// StartTopMostRefresher 启动全局置顶保活后台循环
func StartTopMostRefresher(win *application.WebviewWindow) {
	if win == nil {
		return
	}

	ticker := time.NewTicker(500 * time.Millisecond)
	defer ticker.Stop()

	for range ticker.C {
		if !win.IsVisible() {
			continue
		}
		hwnd := uintptr(win.NativeWindow())
		if hwnd == 0 {
			continue
		}
		EnsureOverlayExtendedStyle(hwnd)
		ForceTopMost(hwnd)
	}
}
