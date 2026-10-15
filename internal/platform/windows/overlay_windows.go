//go:build windows

package windows

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
	procEnumChildWindows  = moduser32.NewProc("EnumChildWindows")
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
	WsPopup       = 0x80000000
	WsCaption     = 0x00C00000 // 包含 WS_BORDER | WS_DLGFRAME，代表标准标题栏
	WsThickframe  = 0x00040000 // 可调大小粗边框
	WsSysmenu     = 0x00080000 // 包含系统菜单及右上角系统关闭按钮
	WsMinimizebox = 0x00020000
	WsMaximizebox = 0x00010000

	// 扩展窗口样式 (GWL_EXSTYLE)
	WsExDlgmodalframe = 0x00000001
	WsExWindowedge    = 0x00000100
	WsExClientedge    = 0x00000200
	WsExLayered       = 0x00080000
	WsExToolwindow    = 0x00000080
	WsExTopmost       = 0x00000008
	WsExTransparent   = 0x00000020 // 点击穿透核心扩展样式
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
	currentOwner, _, _ := procGetWindowLongPtrW.Call(hwnd, GWLP_HWNDPARENT)
	if currentOwner == 0 {
		return // 已经是无属主独立窗口，无需重复调用 SetWindowLongPtrW
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

func applySingleWindowMouseThrough(h uintptr, ignore bool) {
	if h == 0 {
		return
	}
	exStyleRet, _, _ := procGetWindowLongPtrW.Call(h, GWL_EXSTYLE)
	if exStyleRet == 0 {
		return
	}
	currExStyle := uintptr(exStyleRet)
	var targetExStyle uintptr
	if ignore {
		targetExStyle = (currExStyle | WsExLayered | WsExTransparent) &^ (WsExDlgmodalframe | WsExClientedge | WsExWindowedge)
	} else {
		targetExStyle = currExStyle &^ (WsExTransparent | WsExLayered)
	}

	if currExStyle != targetExStyle {
		_, _, _ = procSetWindowLongPtrW.Call(h, GWL_EXSTYLE, targetExStyle)
		ret, _, err := procSetWindowPos.Call(
			h,
			HWND_TOPMOST,
			0, 0, 0, 0,
			SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_FRAMECHANGED|SWP_SHOWWINDOW,
		)
		if ret == 0 {
			log.Printf("[overlay] applySingleWindowMouseThrough SetWindowPos failed: %v", err)
		}
	}
}

var (
	enumChildMouseThroughCallback uintptr
	enumChildCallbackOnce         sync.Once
)

func getEnumChildMouseThroughCallback() uintptr {
	enumChildCallbackOnce.Do(func() {
		enumChildMouseThroughCallback = syscall.NewCallback(func(childHwnd uintptr, lParam uintptr) uintptr {
			applySingleWindowMouseThrough(childHwnd, lParam != 0)
			return 1 // 返回 1 继续枚举下一子窗口
		})
	})
	return enumChildMouseThroughCallback
}

// ApplyMouseThrough 针对指定 HWND 及其所有子级 WebView2 窗口显式应用或关闭物理鼠标穿透
func ApplyMouseThrough(hwnd uintptr, ignore bool) {
	if hwnd == 0 {
		return
	}
	// 1. 设置顶级宿主窗口
	applySingleWindowMouseThrough(hwnd, ignore)

	// 2. 递归枚举所有子窗口 (Chrome_WidgetWin_0、Intermediate D3D Window 等) 统一穿透
	// 使用持久复用的单例回调与 lParam 传参，彻底杜绝重复调用导致 Go runtime 回调槽位泄漏
	lParam := uintptr(0)
	if ignore {
		lParam = 1
	}
	cb := getEnumChildMouseThroughCallback()
	_, _, _ = procEnumChildWindows.Call(hwnd, cb, lParam)
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
		targetStyle := (currStyle &^ (WsCaption | WsThickframe | WsSysmenu | WsMinimizebox | WsMaximizebox)) | WsPopup
		if currStyle != targetStyle {
			_, _, _ = procSetWindowLongPtrW.Call(hwnd, GWL_STYLE, targetStyle)
			styleChanged = true
		}
	}

	// 3. 配置扩展样式 (GWL_EXSTYLE)
	// 保持 ToolWindow (不在任务栏占位) 与 TopMost (全局置顶)
	// 关键：非鼠标穿透状态严禁添加 WsExLayered！Wails v3 使用 DirectComposition (WS_EX_NOREDIRECTIONBITMAP)
	// 强加 WsExLayered 会导致 DWM 认为分层 Alpha 未初始化而将窗口完全隐形抹去！
	exStyleRet, _, _ := procGetWindowLongPtrW.Call(hwnd, GWL_EXSTYLE)
	if exStyleRet != 0 {
		currExStyle := uintptr(exStyleRet)
		isTransparent := (currExStyle & WsExTransparent) != 0

		var targetExStyle uintptr
		if isTransparent {
			targetExStyle = (currExStyle | WsExToolwindow | WsExTopmost | WsExLayered | WsExTransparent) &^ (WsExDlgmodalframe | WsExClientedge | WsExWindowedge)
		} else {
			targetExStyle = (currExStyle | WsExToolwindow | WsExTopmost) &^ (WsExDlgmodalframe | WsExClientedge | WsExWindowedge | WsExTransparent | WsExLayered)
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
	defer func() {
		if rec := recover(); rec != nil {
			log.Printf("[overlay] topmost refresher recovered from panic: %v", rec)
		}
	}()
	ticker := time.NewTicker(500 * time.Millisecond)
	defer ticker.Stop()

	for {
		select {
		case <-r.stopCh:
			log.Printf("[overlay] topmost refresher stopped for window %q", r.win.Name())
			return
		case <-ticker.C:
			func() {
				defer func() {
					_ = recover()
				}()
				if r.win == nil || !r.win.IsVisible() {
					return
				}
				hwnd := uintptr(r.win.NativeWindow())
				if hwnd == 0 {
					return
				}
				EnsureOverlayExtendedStyle(hwnd)
				ForceTopMost(hwnd)
			}()
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
