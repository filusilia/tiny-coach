package overlay

import (
	"encoding/json"
	"log"
	"os"
	"os/exec"
	"path/filepath"
	"sync"

	"aegis-coach/internal/events"
	"aegis-coach/internal/platform/windows"
	"aegis-coach/internal/storage"

	"github.com/wailsapp/wails/v3/pkg/application"
	wailsEvents "github.com/wailsapp/wails/v3/pkg/events"
)

// OverlayService 独立桌面置顶悬浮窗服务
type OverlayService struct {
	mu                    sync.RWMutex
	isOverlay             bool
	isMouseThrough        bool
	pendingDestroyWindows map[string]bool
	hookedWindows         map[string]bool
	window                *application.WebviewWindow
	overlayWindow         *application.WebviewWindow
	overlayCmd            *exec.Cmd
}

var (
	overlayServiceInstance *OverlayService
	overlayServiceOnce     sync.Once
)

// GetOverlayService 获取 OverlayService 单例（不再受 sync.Once 首次参数陷阱限制）
func GetOverlayService(_ bool) *OverlayService {
	overlayServiceOnce.Do(func() {
		overlayServiceInstance = &OverlayService{
			hookedWindows:         make(map[string]bool),
			pendingDestroyWindows: make(map[string]bool),
		}
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
		s.attachWindowHooksLocked(w)
	}
}

// SetOverlayWindow 显式关联独立悬浮窗原生窗口句柄
func (s *OverlayService) SetOverlayWindow(w *application.WebviewWindow) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.overlayWindow = w
	if w != nil {
		s.attachWindowHooksLocked(w)
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
	// 严守边界：在非 overlay 模式下找不到悬浮窗时返回 nil，绝不能回退返回主窗口 s.window
	if s.isOverlay {
		return s.window
	}
	return nil
}

func (s *OverlayService) getPositionFilePath() string {
	cacheDir := storage.GetManager().GetCacheDirectory()
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

// attachWindowHooksLocked 要求调用方已持有 s.mu
func (s *OverlayService) attachWindowHooksLocked(win *application.WebviewWindow) {
	if win == nil {
		return
	}

	if s.hookedWindows == nil {
		s.hookedWindows = make(map[string]bool)
	}
	winKey := win.Name()
	if winKey == "" {
		winKey = "game-overlay"
	}
	if s.hookedWindows[winKey] {
		return
	}
	s.hookedWindows[winKey] = true

	win.OnWindowEvent(wailsEvents.Windows.WindowEndMove, func(e *application.WindowEvent) {
		x, y := win.Position()
		s.savePosition(x, y)
	})
	win.OnWindowEvent(wailsEvents.Common.WindowDidMove, func(e *application.WindowEvent) {
		x, y := win.Position()
		s.savePosition(x, y)
	})
	win.OnWindowEvent(wailsEvents.Common.WindowClosing, func(e *application.WindowEvent) {
		s.mu.Lock()
		if s.pendingDestroyWindows != nil && s.pendingDestroyWindows[winKey] {
			delete(s.pendingDestroyWindows, winKey)
			delete(s.hookedWindows, winKey)
			s.mu.Unlock()
			// 外部显式调用了 CloseOverlay()，放行操作系统原生窗口销毁
			return
		}
		isOverlay := s.isOverlay
		s.mu.Unlock()

		x, y := win.Position()
		s.savePosition(x, y)

		if isOverlay {
			if appInst := application.Get(); appInst != nil {
				appInst.Quit()
			}
			return
		}

		e.Cancel()
		win.Hide()
		events.Emit("overlay_state_change", false)
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
			windows.ApplyMouseThrough(hwnd, ignore)
		}
		events.Emit("overlay_mouse_through_change", ignore)
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

// SetOverlaySize 动态调整悬浮窗原生窗口尺寸，实现对折叠态与不同战局状态的紧凑贴合
func (s *OverlayService) SetOverlaySize(width, height int) {
	if width <= 0 || height <= 0 {
		return
	}
	s.mu.RLock()
	win := s.getOverlayWindow()
	s.mu.RUnlock()

	if win != nil {
		win.SetSize(width, height)
	}
}

// ToggleOverlayWindow 切换局内独立置顶经济悬浮窗显隐状态
func (s *OverlayService) ToggleOverlayWindow() bool {
	s.mu.RLock()
	isOverlay := s.isOverlay
	s.mu.RUnlock()

	appInst := application.Get()

	// 0. 若当前实例本身就是独立悬浮窗子进程，切换显隐即为关闭该独立进程
	if isOverlay {
		if appInst != nil {
			x, y := 0, 0
			s.mu.RLock()
			mainWin := s.window
			s.mu.RUnlock()
			if mainWin != nil {
				x, y = mainWin.Position()
				s.savePosition(x, y)
				mainWin.Hide()
			}
			events.Emit("overlay_state_change", false)
			appInst.Quit()
			return false
		}
	}

	// 1. 若进程内已存在悬浮窗实例，直接在锁外切换显隐与置顶
	if appInst != nil && appInst.Window != nil {
		s.mu.RLock()
		existingWin := s.overlayWindow
		s.mu.RUnlock()

		if existingWin == nil {
			if win, exists := appInst.Window.GetByName("game-overlay"); exists && win != nil {
				if wWin, ok := win.(*application.WebviewWindow); ok {
					existingWin = wWin
					s.mu.Lock()
					s.overlayWindow = wWin
					s.mu.Unlock()
				}
			}
		}

		if existingWin != nil {
			if existingWin.IsVisible() {
				x, y := existingWin.Position()
				s.savePosition(x, y)
				existingWin.Hide()
				events.Emit("overlay_state_change", false)
				return false
			}
			existingWin.Show()
			existingWin.Focus()
			hwnd := uintptr(existingWin.NativeWindow())
			if hwnd != 0 {
				windows.DetachWindowOwner(hwnd)
				windows.EnsureOverlayExtendedStyle(hwnd)
				windows.ForceTopMost(hwnd)
			}
			events.Emit("overlay_state_change", true)
			return true
		}

		// 若当前进程内尚未创建悬浮窗，在锁外加载坐标并准备配置创建原生窗口 (避免持锁与子进程启动开销)
		var overlayWin *application.WebviewWindow
		x, y, hasPos := s.LoadSavedPosition()
		opts := application.WebviewWindowOptions{
			Name:              "game-overlay",
			Title:             "神盾教练 - 局内经济悬浮窗",
			URL:               "/index.html?window=overlay&mode=overlay#/overlay",
			Width:             340,
			Height:            260,
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

		application.InvokeSync(func() {
			defer func() {
				if r := recover(); r != nil {
					log.Printf("[OverlayService] 创建悬浮窗异常: %v, 将降级使用子进程启动", r)
					overlayWin = nil
				}
			}()
			overlayWin = appInst.Window.NewWithOptions(opts)
		})

		if overlayWin != nil {
			s.mu.Lock()
			s.overlayWindow = overlayWin
			s.attachWindowHooksLocked(overlayWin)
			s.mu.Unlock()

			if hasPos {
				overlayWin.SetPosition(x, y)
			}
			overlayWin.Show()
			overlayWin.Focus()
			hwnd := uintptr(overlayWin.NativeWindow())
			if hwnd != 0 {
				windows.DetachWindowOwner(hwnd)
				windows.EnsureOverlayExtendedStyle(hwnd)
				windows.ForceTopMost(hwnd)
			}
			go windows.StartTopMostRefresher(overlayWin)
			events.Emit("overlay_state_change", true)
			return true
		}
	}

	// 2. 若子进程方式运行，检查子进程生命周期
	s.mu.Lock()
	cmd := s.overlayCmd
	s.overlayCmd = nil
	s.mu.Unlock()

	if cmd != nil && cmd.Process != nil {
		_ = cmd.Process.Kill()
		events.Emit("overlay_state_change", false)
		return false
	}

	// 3. 双保险备用：启动独立悬浮窗子进程
	exePath, err := os.Executable()
	if err != nil {
		log.Printf("[OverlayService] 获取可执行程序路径失败: %v", err)
		return false
	}

	newCmd := exec.Command(exePath, "--overlay")
	if err := newCmd.Start(); err != nil {
		log.Printf("[OverlayService] 启动独立悬浮窗进程失败: %v", err)
		return false
	}

	s.mu.Lock()
	s.overlayCmd = newCmd
	s.mu.Unlock()

	go func() {
		_ = newCmd.Wait()
		s.mu.Lock()
		s.overlayCmd = nil
		s.mu.Unlock()
		events.Emit("overlay_state_change", false)
	}()

	events.Emit("overlay_state_change", true)
	return true
}

// StopTerminatingOverlay 优雅终止悬浮窗子进程
func (s *OverlayService) StopTerminatingOverlay() {
	s.mu.Lock()
	cmd := s.overlayCmd
	s.overlayCmd = nil
	s.mu.Unlock()

	if cmd != nil && cmd.Process != nil {
		_ = cmd.Process.Kill()
	}
}

// CloseOverlay 隐藏悬浮窗（窗口实例与子进程保持常驻热备）
func (s *OverlayService) CloseOverlay() {
	s.mu.Lock()
	isOverlay := s.isOverlay
	win := s.overlayWindow
	s.mu.Unlock()

	// 0. 若当前实例本身就是独立悬浮窗进程，直接退出程序
	if isOverlay {
		if appInst := application.Get(); appInst != nil {
			x, y := 0, 0
			s.mu.RLock()
			mainWin := s.window
			s.mu.RUnlock()
			if mainWin != nil {
				x, y = mainWin.Position()
				s.savePosition(x, y)
				mainWin.Hide()
			}
			events.Emit("overlay_state_change", false)
			appInst.Quit()
		}
		return
	}

	// 1. 若进程内存在原生 WebviewWindow 实例，先保存位置并隐藏（保持常驻热备，绝不销毁实例句柄）
	if win != nil {
		x, y := win.Position()
		s.savePosition(x, y)
		win.Hide()
	}

	if appInst := application.Get(); appInst != nil && appInst.Window != nil {
		if namedWin, exists := appInst.Window.GetByName("game-overlay"); exists && namedWin != nil {
			if wWin, ok := namedWin.(*application.WebviewWindow); ok && wWin != win {
				x, y := wWin.Position()
				s.savePosition(x, y)
				wWin.Hide()
			}
		}
	}

	// 2. 终止可能启动的悬浮窗独立子进程
	s.StopTerminatingOverlay()
	events.Emit("overlay_state_change", false)
}
