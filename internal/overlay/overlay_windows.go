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

	// GWL_STYLE 官方定义为 -16（有符号 int32），此处通过 ^uintptr(15) 取无符号补码表示
	GWL_STYLE = ^uintptr(15)

	// GWL_EXSTYLE 官方定义为 -20（有符号 int32），此处通过 ^uintptr(19) 取无符号补码表示
	GWL_EXSTYLE = ^uintptr(19)

	// GWLP_HWNDPARENT 官方定义为 -8，用于获取/设置顶级窗口的属主窗口 (Owner Window)
	GWLP_HWNDPARENT = ^uintptr(7)

	// 基础窗口样式 (GWL_STYLE)
	WS_POPUP       = 0x80000000
	WS_CAPTION     = 0x00C00000 // 包含 WS_BORDER | WS_DLGFRAME，代表标准标题栏
	WS_THICKFRAME  = 0x00040000 // 可调大小粗边框
	WS_SYSMENU     = 0x00080000 // 包含系统菜单及右上角系统关闭按钮
	WS_MINIMIZEBOX = 0x00020000
	WS_MAXIMIZEBOX = 0x00010000

	// 扩展窗口样式 (GWL_EXSTYLE)
	WS_EX_DLGMODALFRAME = 0x00000001
	WS_EX_WINDOWEDGE    = 0x00000100
	WS_EX_CLIENTEDGE    = 0x00000200
	WS_EX_LAYERED       = 0x00080000
	WS_EX_TOOLWINDOW    = 0x00000080
	WS_EX_TOPMOST       = 0x00000008
	WS_EX_TRANSPARENT   = 0x00000020 // 点击穿透核心扩展样式
)

// ---------------------------------------------------------------------------
// 底层窗口操作（带错误日志）
// ---------------------------------------------------------------------------

// DetachWindowOwner 拔除窗口的父属主 (Owner) 关系，将其提升为真正的顶级无属主独立窗口
// 彻底避免主窗口最小化或聚焦时小窗口跟随最小化或受到 Z 序牵连
func DetachWindowOwner(hwnd uintptr) {
	if hwnd == 0 {
		return
	}
	ret, _, err := procSetWindowLongPtrW.Call(hwnd, GWLP_HWNDPARENT, 0)
	if ret == 0 && err != syscall.Errno(0) {
		log.Printf("[overlay] DetachWindowOwner failed (GWLP_HWNDPARENT): %v", err)
	}
}

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

// ApplyMouseThrough 针对指定 HWND 显式应用或关闭物理鼠标穿透
func ApplyMouseThrough(hwnd uintptr, ignore bool) {
	if hwnd == 0 {
		return
	}
	exStyleRet, _, _ := procGetWindowLongPtrW.Call(hwnd, GWL_EXSTYLE)
	if exStyleRet == 0 {
		return
	}
	currExStyle := uintptr(exStyleRet)
	var targetExStyle uintptr
	if ignore {
		targetExStyle = (currExStyle | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT) &^ (WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_WINDOWEDGE)
	} else {
		targetExStyle = (currExStyle | WS_EX_TOOLWINDOW | WS_EX_TOPMOST) &^ (WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_WINDOWEDGE | WS_EX_LAYERED | WS_EX_TRANSPARENT)
	}

	if currExStyle != targetExStyle {
		_, _, _ = procSetWindowLongPtrW.Call(hwnd, GWL_EXSTYLE, targetExStyle)
		ret, _, err := procSetWindowPos.Call(
			hwnd,
			HWND_TOPMOST,
			0, 0, 0, 0,
			SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_FRAMECHANGED|SWP_SHOWWINDOW,
		)
		if ret == 0 {
			log.Printf("[overlay] ApplyMouseThrough SetWindowPos failed: %v", err)
		}
	}
}

// EnsureOverlayExtendedStyle 确保悬浮窗具备无边框透明及工具栏隐藏的扩展样式
// 彻底剥离 Windows 系统原生标题栏、边框以及右上角原生小关闭按钮，且在开启鼠标穿透时严密保留穿透样式
func EnsureOverlayExtendedStyle(hwnd uintptr) {
	if hwnd == 0 {
		return
	}

	// 1. 首先确保脱离任何父属主关系，成为真正的顶层独立窗口
	DetachWindowOwner(hwnd)

	styleChanged := false

	// 2. 彻底剥离系统窗口边框与原生小标题栏/右上角小关闭按钮 (GWL_STYLE)
	// 剔除 WS_CAPTION, WS_THICKFRAME, WS_SYSMENU, WS_MINIMIZEBOX, WS_MAXIMIZEBOX
	// 注入 WS_POPUP，杜绝 Windows DWM 绘制任何自带白色标题栏与右上角小叉按钮
	styleRet, _, _ := procGetWindowLongPtrW.Call(hwnd, GWL_STYLE)
	if styleRet != 0 {
		currStyle := uintptr(styleRet)
		targetStyle := (currStyle &^ (WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)) | WS_POPUP
		if currStyle != targetStyle {
			_, _, _ = procSetWindowLongPtrW.Call(hwnd, GWL_STYLE, targetStyle)
			styleChanged = true
		}
	}

	// 3. 配置扩展样式 (GWL_EXSTYLE)
	// 保持 ToolWindow (不在任务栏占位) 与 TopMost (全局置顶)
	// 若当前处于鼠标穿透状态 (含 WS_EX_TRANSPARENT)，必须严密保留 WS_EX_TRANSPARENT 与 WS_EX_LAYERED
	exStyleRet, _, _ := procGetWindowLongPtrW.Call(hwnd, GWL_EXSTYLE)
	if exStyleRet != 0 {
		currExStyle := uintptr(exStyleRet)
		isTransparent := (currExStyle & WS_EX_TRANSPARENT) != 0

		var targetExStyle uintptr
		if isTransparent {
			targetExStyle = (currExStyle | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT) &^ (WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_WINDOWEDGE)
		} else {
			targetExStyle = (currExStyle | WS_EX_TOOLWINDOW | WS_EX_TOPMOST) &^ (WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_WINDOWEDGE | WS_EX_LAYERED | WS_EX_TRANSPARENT)
		}

		if currExStyle != targetExStyle {
			_, _, _ = procSetWindowLongPtrW.Call(hwnd, GWL_EXSTYLE, targetExStyle)
			styleChanged = true
		}
	}

	// 4. 仅在样式真正发生变更时，才通知框架变更重新排版；避免高频重绘闪烁
	if styleChanged {
		ret, _, err := procSetWindowPos.Call(
			hwnd,
			HWND_TOPMOST,
			0, 0, 0, 0,
			SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_FRAMECHANGED|SWP_SHOWWINDOW,
		)
		if ret == 0 {
			log.Printf("[overlay] SetWindowPos(SWP_FRAMECHANGED) failed: %v", err)
		}
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
