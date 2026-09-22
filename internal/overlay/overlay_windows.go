//go:build windows

package overlay

import (
	"log"
	"sync"
	"syscall"
	"time"

	"github.com/wailsapp/wails/v3/pkg/application"
	"github.com/wailsapp/wails/v3/pkg/events"
)

// ---------------------------------------------------------------------------
// Windows API 绑定
// ---------------------------------------------------------------------------

var (
	moduser32 = syscall.NewLazyDLL("user32.dll")

	procSetWindowPos      = moduser32.NewProc("SetWindowPos")
	procGetWindowLongPtrW = moduser32.NewProc("GetWindowLongPtrW")
	procSetWindowLongPtrW = moduser32.NewProc("SetWindowLongPtrW")
)

const (
	HWND_TOPMOST = ^uintptr(0) // (HWND)-1

	SWP_NOSIZE       = 0x0001
	SWP_NOMOVE       = 0x0002
	SWP_NOACTIVATE   = 0x0010
	SWP_SHOWWINDOW   = 0x0040
	SWP_FRAMECHANGED = 0x0020 // 通知窗口框架已更改，强制重绘

	// GWL_EXSTYLE 官方定义为 -20（有符号 int32），此处通过 ^uintptr(19) 取无符号补码表示
	GWL_EXSTYLE = ^uintptr(19)

	WS_EX_LAYERED    = 0x00080000
	WS_EX_TOOLWINDOW = 0x00000080
	WS_EX_TOPMOST    = 0x00000008
)

// ---------------------------------------------------------------------------
// 底层窗口操作（带错误日志）
// ---------------------------------------------------------------------------

// ForceTopMost 强制将窗口置顶
func ForceTopMost(hwnd uintptr) {
	if hwnd == 0 {
		return
	}
	ret, _, err := procSetWindowPos.Call(
		hwnd,
		HWND_TOPMOST,
		0, 0, 0, 0,
		SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW,
	)
	if ret == 0 {
		log.Printf("[overlay] SetWindowPos(HWND_TOPMOST) failed: %v", err)
	}
}

// EnsureOverlayExtendedStyle 确保悬浮窗具备无边框透明及工具栏隐藏的扩展样式
// 使用 SetWindowLongPtrW 保证 64 位兼容性
func EnsureOverlayExtendedStyle(hwnd uintptr) {
	if hwnd == 0 {
		return
	}

	// 读取当前扩展样式
	ret, _, err := procGetWindowLongPtrW.Call(hwnd, GWL_EXSTYLE)
	if ret == 0 {
		log.Printf("[overlay] GetWindowLongPtrW(GWL_EXSTYLE) failed: %v", err)
		return
	}

	currStyle := uintptr(ret)
	targetStyle := currStyle | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TOPMOST

	if currStyle == targetStyle {
		return
	}

	// 写回新样式
	ret, _, err = procSetWindowLongPtrW.Call(hwnd, GWL_EXSTYLE, targetStyle)
	if ret == 0 {
		log.Printf("[overlay] SetWindowLongPtrW(GWL_EXSTYLE) failed: %v", err)
		return
	}

	// 关键：修改框架样式后必须调用 SetWindowPos + SWP_FRAMECHANGED 才能生效
	ret, _, err = procSetWindowPos.Call(
		hwnd,
		0, // hWndInsertAfter 不关心
		0, 0, 0, 0,
		SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_FRAMECHANGED,
	)
	if ret == 0 {
		log.Printf("[overlay] SetWindowPos(SWP_FRAMECHANGED) failed: %v", err)
	}
}

// ---------------------------------------------------------------------------
// 顶层刷新循环（支持优雅退出）
// ---------------------------------------------------------------------------

// topMostRefresher 封装单个窗口的刷新循环状态
type topMostRefresher struct {
	win    *application.WebviewWindow
	stopCh chan struct{}
	once   sync.Once
}

// Stop 安全地停止刷新循环（幂等）
func (r *topMostRefresher) Stop() {
	r.once.Do(func() {
		close(r.stopCh)
	})
}

// run 执行刷新循环，直到 stopCh 被关闭
func (r *topMostRefresher) run() {
	ticker := time.NewTicker(500 * time.Millisecond)
	defer ticker.Stop()

	for {
		select {
		case <-r.stopCh:
			log.Printf("[overlay] topmost refresher stopped for window %q", r.win.Name())
			return
		case <-ticker.C:
			if !r.win.IsVisible() {
				continue
			}
			hwnd := uintptr(r.win.NativeWindow())
			if hwnd == 0 {
				continue
			}
			EnsureOverlayExtendedStyle(hwnd)
			ForceTopMost(hwnd)
		}
	}
}

// ---------------------------------------------------------------------------
// 对外入口
// ---------------------------------------------------------------------------

// SetupOverlay 为窗口配置置顶保活及扩展样式。
//
// 优先使用 Wails v3 原生 SetAlwaysOnTop，其次在后台循环中持续保活，
// 并在窗口关闭时自动停止循环、释放资源。
//
// 返回值 refresher 可用于手动提前停止（通常不需要，窗口关闭会自动清理）。
// SetupOverlay 为窗口配置置顶保活及扩展样式。
//
// 返回值 stop 用于手动停止后台保活循环（幂等，可重复调用）。
// 通常无需手动调用：窗口关闭时会自动清理。
func SetupOverlay(win *application.WebviewWindow) (stop func()) {
	if win == nil {
		return func() {}
	}

	win.SetAlwaysOnTop(true)

	r := &topMostRefresher{
		win:    win,
		stopCh: make(chan struct{}),
	}

	win.OnWindowEvent(events.Common.WindowClosing, func(e *application.WindowEvent) {
		r.Stop()
	})

	go r.run()

	return r.Stop
}

// StartTopMostRefresher 启动全局置顶保活后台循环（兼容旧版直接调用接口）
func StartTopMostRefresher(win *application.WebviewWindow) {
	SetupOverlay(win)
}
