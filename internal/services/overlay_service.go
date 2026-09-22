package services

import (
	"log"
	"os"
	"os/exec"
	"sync"

	"aegis-coach/internal/overlay"

	"github.com/wailsapp/wails/v3/pkg/application"
)

// OverlayService 独立桌面置顶悬浮窗服务
type OverlayService struct {
	mu            sync.RWMutex
	isOverlay     bool
	window        *application.WebviewWindow
	overlayWindow *application.WebviewWindow
	overlayCmd    *exec.Cmd
}

var (
	overlayServiceInstance *OverlayService
	overlayServiceOnce     sync.Once
)

// GetOverlayService 获取 OverlayService 单例
func GetOverlayService(isOverlay bool) *OverlayService {
	overlayServiceOnce.Do(func() {
		overlayServiceInstance = &OverlayService{
			isOverlay: isOverlay,
		}
	})
	return overlayServiceInstance
}

// SetWindow 关联原生窗口句柄
func (s *OverlayService) SetWindow(w *application.WebviewWindow) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.window = w
}

// IsOverlayMode 查询当前实例是否为独立悬浮窗进程模式
func (s *OverlayService) IsOverlayMode() bool {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.isOverlay
}

// IsOverlayRunning 查询悬浮窗是否正在后台运行或处于可见状态
func (s *OverlayService) IsOverlayRunning() bool {
	s.mu.RLock()
	defer s.mu.RUnlock()

	if appInst := application.Get(); appInst != nil && appInst.Window != nil {
		if overlayWin, exists := appInst.Window.GetByName("game-overlay"); exists && overlayWin != nil {
			if overlayWin.IsVisible() {
				return true
			}
		}
	}

	return s.overlayCmd != nil && s.overlayCmd.Process != nil
}

// SetIgnoreMouseEvents 设置悬浮窗是否忽略鼠标事件 (点击完全穿透至游戏)
func (s *OverlayService) SetIgnoreMouseEvents(ignore bool) {
	s.mu.RLock()
	win := s.overlayWindow
	if win == nil {
		win = s.window
	}
	s.mu.RUnlock()

	if win != nil {
		win.SetIgnoreMouseEvents(ignore)
	}
}

// ToggleOverlayWindow 切换局内独立置顶经济悬浮窗显隐状态
func (s *OverlayService) ToggleOverlayWindow() bool {
	s.mu.Lock()
	defer s.mu.Unlock()

	appInst := application.Get()

	// 1. 若进程内已存在悬浮窗实例，直接切换显隐与置顶
	if appInst != nil && appInst.Window != nil {
		if overlayWin, exists := appInst.Window.GetByName("game-overlay"); exists && overlayWin != nil {
			if overlayWin.IsVisible() {
				overlayWin.Hide()
				emitGlobalEvent("overlay_state_change", false)
				return false
			}
			overlayWin.Show()
			overlayWin.Focus()
			hwnd := uintptr(overlayWin.NativeWindow())
			if hwnd != 0 {
				overlay.DetachWindowOwner(hwnd)
				overlay.EnsureOverlayExtendedStyle(hwnd)
				overlay.ForceTopMost(hwnd)
			}
			emitGlobalEvent("overlay_state_change", true)
			return true
		}

		// 若当前进程内尚未创建悬浮窗，直接新建独立置顶原生窗口 (避免子进程启动开销)
		var overlayWin *application.WebviewWindow
		func() {
			defer func() {
				if r := recover(); r != nil {
					log.Printf("[OverlayService] 创建悬浮窗异常: %v, 将降级使用子进程启动", r)
					overlayWin = nil
				}
			}()
			overlayWin = appInst.Window.NewWithOptions(application.WebviewWindowOptions{
				Name:              "game-overlay",
				Title:             "神盾教练 - 局内经济悬浮窗",
				URL:               "/index.html?window=overlay#/overlay",
				Width:             340,
				Height:            460,
				Frameless:         true,
				AlwaysOnTop:       true,
				IgnoreMouseEvents: false,
				BackgroundType:    application.BackgroundTypeTransparent,
				BackgroundColour:  application.RGBA{Red: 0, Green: 0, Blue: 0, Alpha: 0},
				Windows: application.WindowsWindow{
					HiddenOnTaskbar:                   true,
					DisableFramelessWindowDecorations: true,
					Theme:                             application.Dark,
				},
			})
		}()

		if overlayWin != nil {
			s.overlayWindow = overlayWin
			overlayWin.Show()
			overlayWin.Focus()
			hwnd := uintptr(overlayWin.NativeWindow())
			if hwnd != 0 {
				overlay.DetachWindowOwner(hwnd)
				overlay.EnsureOverlayExtendedStyle(hwnd)
				overlay.ForceTopMost(hwnd)
			}
			go overlay.StartTopMostRefresher(overlayWin)
			emitGlobalEvent("overlay_state_change", true)
			return true
		}
	}

	// 2. 若子进程方式运行，检查子进程生命周期
	if s.overlayCmd != nil && s.overlayCmd.Process != nil {
		_ = s.overlayCmd.Process.Kill()
		s.overlayCmd = nil
		emitGlobalEvent("overlay_state_change", false)
		return false
	}

	// 3. 双保险备用：启动独立悬浮窗子进程
	exePath, err := os.Executable()
	if err != nil {
		log.Printf("[OverlayService] 获取可执行程序路径失败: %v", err)
		return false
	}

	cmd := exec.Command(exePath, "--overlay")
	if err := cmd.Start(); err != nil {
		log.Printf("[OverlayService] 启动独立悬浮窗进程失败: %v", err)
		return false
	}

	s.overlayCmd = cmd
	go func() {
		_ = cmd.Wait()
		s.mu.Lock()
		s.overlayCmd = nil
		s.mu.Unlock()
		emitGlobalEvent("overlay_state_change", false)
	}()

	emitGlobalEvent("overlay_state_change", true)
	return true
}

// StopTerminatingOverlay 优雅终止悬浮窗子进程
func (s *OverlayService) StopTerminatingOverlay() {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.overlayCmd != nil && s.overlayCmd.Process != nil {
		_ = s.overlayCmd.Process.Kill()
		s.overlayCmd = nil
	}
}

func emitGlobalEvent(name string, data ...any) {
	if app := application.Get(); app != nil && app.Event != nil {
		app.Event.Emit(name, data...)
	}
}
