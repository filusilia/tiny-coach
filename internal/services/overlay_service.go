package services

import (
	"encoding/json"
	"log"
	"os"
	"os/exec"
	"path/filepath"
	"sync"

	"aegis-coach/internal/overlay"

	"github.com/wailsapp/wails/v3/pkg/application"
	"github.com/wailsapp/wails/v3/pkg/events"
)

// OverlayService 独立桌面置顶悬浮窗服务
type OverlayService struct {
	mu             sync.RWMutex
	isOverlay      bool
	isMouseThrough bool
	window         *application.WebviewWindow
	overlayWindow  *application.WebviewWindow
	overlayCmd     *exec.Cmd
}

var (
	overlayServiceInstance *OverlayService
	overlayServiceOnce     sync.Once
)

// GetOverlayService 获取 OverlayService 单例（不再受 sync.Once 首次参数陷阱限制）
func GetOverlayService(_ bool) *OverlayService {
	overlayServiceOnce.Do(func() {
		overlayServiceInstance = &OverlayService{}
	})
	return overlayServiceInstance
}

// SetOverlayMode 显式设置当前进程模式
// 用于彻底解决 sync.Once + 构造传参陷阱：单例全局唯一，但 isOverlay 需在初始化时被确定性更新
func (s *OverlayService) SetOverlayMode(isOverlay bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.isOverlay = isOverlay
}

// SetWindow 关联原生窗口句柄
func (s *OverlayService) SetWindow(w *application.WebviewWindow) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.window = w
	if s.isOverlay && s.overlayWindow == nil {
		s.overlayWindow = w
		s.attachWindowHooks(w)
	}
}

func (s *OverlayService) getOverlayWindow() *application.WebviewWindow {
	if s.overlayWindow != nil {
		return s.overlayWindow
	}
	if s.window != nil && s.isOverlay {
		return s.window
	}
	if appInst := application.Get(); appInst != nil && appInst.Window != nil {
		if win, exists := appInst.Window.GetByName("game-overlay"); exists && win != nil {
			if wWin, ok := win.(*application.WebviewWindow); ok {
				s.overlayWindow = wWin
				return wWin
			}
		}
	}
	return s.window
}

func (s *OverlayService) getPositionFilePath() string {
	cacheDir := GetSystemService().GetCacheDirectory()
	return filepath.Join(cacheDir, "overlay_position.json")
}

func (s *OverlayService) LoadSavedPosition() (int, int, bool) {
	p := s.getPositionFilePath()
	data, err := os.ReadFile(p)
	if err != nil {
		return 0, 0, false
	}
	var pos struct {
		X int `json:"x"`
		Y int `json:"y"`
	}
	if err := json.Unmarshal(data, &pos); err != nil {
		return 0, 0, false
	}
	// 过滤系统最小化 (-32000) 等极端非法坐标，只要坐标在正常屏幕合理跨度范围内即视为有效记忆
	if (pos.X != 0 || pos.Y != 0) && pos.X > -10000 && pos.Y > -10000 && pos.X < 50000 && pos.Y < 50000 {
		return pos.X, pos.Y, true
	}
	return 0, 0, false
}

func (s *OverlayService) savePosition(x, y int) {
	// 忽略系统最小化或非法负值异常点
	if x <= -10000 || y <= -10000 || (x <= 0 && y <= 0) {
		return
	}
	p := s.getPositionFilePath()
	_ = os.MkdirAll(filepath.Dir(p), 0755)
	pos := struct {
		X int `json:"x"`
		Y int `json:"y"`
	}{X: x, Y: y}
	data, _ := json.Marshal(pos)
	_ = os.WriteFile(p, data, 0644)
}

func (s *OverlayService) attachWindowHooks(win *application.WebviewWindow) {
	if win == nil {
		return
	}
	win.OnWindowEvent(events.Windows.WindowEndMove, func(e *application.WindowEvent) {
		x, y := win.Position()
		s.savePosition(x, y)
	})
	win.OnWindowEvent(events.Common.WindowDidMove, func(e *application.WindowEvent) {
		x, y := win.Position()
		s.savePosition(x, y)
	})
	win.OnWindowEvent(events.Common.WindowClosing, func(e *application.WindowEvent) {
		x, y := win.Position()
		s.savePosition(x, y)
		e.Cancel()
		win.Hide()
		emitGlobalEvent("overlay_state_change", false)
	})
}

// ResetOverlayPosition 重置悬浮窗至屏幕中央并清理记忆位置
func (s *OverlayService) ResetOverlayPosition() {
	s.mu.Lock()
	defer s.mu.Unlock()
	p := s.getPositionFilePath()
	_ = os.Remove(p)
	win := s.getOverlayWindow()
	if win != nil {
		win.Center()
	}
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
	s.mu.Lock()
	s.isMouseThrough = ignore
	win := s.getOverlayWindow()
	s.mu.Unlock()

	if win != nil {
		win.SetIgnoreMouseEvents(ignore)
		hwnd := uintptr(win.NativeWindow())
		if hwnd != 0 {
			overlay.ApplyMouseThrough(hwnd, ignore)
		}
		emitGlobalEvent("overlay_mouse_through_change", ignore)
	}
}

// ToggleMouseThrough 切换悬浮窗鼠标穿透
func (s *OverlayService) ToggleMouseThrough() bool {
	s.mu.Lock()
	target := !s.isMouseThrough
	s.mu.Unlock()
	s.SetIgnoreMouseEvents(target)
	return target
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
				x, y := overlayWin.Position()
				s.savePosition(x, y)
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

			x, y, hasPos := s.LoadSavedPosition()
			opts := application.WebviewWindowOptions{
				Name:              "game-overlay",
				Title:             "神盾教练 - 局内经济悬浮窗",
				URL:               "/index.html?window=overlay&mode=overlay#/overlay",
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
			}
			if hasPos {
				opts.InitialPosition = application.WindowXY
				opts.X = x
				opts.Y = y
			}

			overlayWin = appInst.Window.NewWithOptions(opts)
		}()

		if overlayWin != nil {
			s.overlayWindow = overlayWin
			s.attachWindowHooks(overlayWin)
			x, y, hasPos := s.LoadSavedPosition()
			if hasPos {
				overlayWin.SetPosition(x, y)
			}
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

// CloseOverlay 彻底关闭并销毁悬浮窗（窗口实例与子进程）
func (s *OverlayService) CloseOverlay() {
	s.mu.Lock()
	defer s.mu.Unlock()

	// 1. 若进程内存在原生 WebviewWindow 实例，先隐藏并关闭
	if s.overlayWindow != nil {
		x, y := s.overlayWindow.Position()
		s.savePosition(x, y)
		s.overlayWindow.Hide()
		s.overlayWindow.Close()
		s.overlayWindow = nil
	} else if appInst := application.Get(); appInst != nil && appInst.Window != nil {
		if win, exists := appInst.Window.GetByName("game-overlay"); exists && win != nil {
			if wWin, ok := win.(*application.WebviewWindow); ok {
				x, y := wWin.Position()
				s.savePosition(x, y)
				wWin.Hide()
				wWin.Close()
			}
		}
	}

	// 2. 终止可能启动的悬浮窗独立子进程
	if s.overlayCmd != nil && s.overlayCmd.Process != nil {
		_ = s.overlayCmd.Process.Kill()
		s.overlayCmd = nil
	}

	emitGlobalEvent("overlay_state_change", false)
}

func emitGlobalEvent(name string, data ...any) {
	if app := application.Get(); app != nil && app.Event != nil {
		app.Event.Emit(name, data...)
	}
}
