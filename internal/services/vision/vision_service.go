package vision

import (
	"log"
	"sync"

	"aegis-coach/internal/models"
	winPlatform "aegis-coach/internal/platform/windows"

	"github.com/wailsapp/wails/v3/pkg/application"
)

// VisionService 屏幕视觉识别与原生截图服务
type VisionService struct {
	mu            sync.RWMutex
	app           *application.App
	window        *application.WebviewWindow
	hotkeyActive  bool
	lastCapture   *models.CaptureResult
}

var (
	visionServiceInstance *VisionService
	visionServiceOnce     sync.Once
)

// GetVisionService 获取 VisionService 全局单例
func GetVisionService() *VisionService {
	visionServiceOnce.Do(func() {
		visionServiceInstance = &VisionService{}
	})
	return visionServiceInstance
}

// SetApp 关联 Wails Application 实例，用于 Event.Emit
func (s *VisionService) SetApp(app *application.App) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.app = app
}

// SetWindow 关联原生窗口句柄
func (s *VisionService) SetWindow(w *application.WebviewWindow) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.window = w
}

// CaptureRect 抽象通用底层函数：截取屏幕指定物理像素矩形区域
func (s *VisionService) CaptureRect(x, y, w, h int) (*models.CaptureResult, error) {
	res, err := winPlatform.CaptureRect(x, y, w, h)
	if err != nil {
		log.Printf("[VisionService] CaptureRect 失败 (%d,%d,%d,%d): %v", x, y, w, h, err)
		return &models.CaptureResult{
			Success:      false,
			ErrorMessage: err.Error(),
		}, nil // 铁律 7：统一取消/异常语义，返回结构体携带错误信息避免阻断前端
	}

	s.mu.Lock()
	s.lastCapture = res
	s.mu.Unlock()

	return res, nil
}

// CaptureTopScreen 上层调用：截取屏幕顶部 18% 区域（用于双方 10 英雄槽位识别）
func (s *VisionService) CaptureTopScreen() (*models.CaptureResult, error) {
	screenW, screenH := winPlatform.GetPhysicalScreenDimensions()
	cropH := int(float64(screenH) * 0.18)
	if cropH <= 0 {
		cropH = int(float64(screenH) * 0.18)
	}

	res, err := s.CaptureRect(0, 0, screenW, cropH)
	if err == nil && res != nil && res.Success {
		s.emitCaptureEvent(res)
	}
	return res, err
}

// CaptureMinimap 上层调用：截取屏幕左下角小地图区域（预留给小地图安全分析）
func (s *VisionService) CaptureMinimap() (*models.CaptureResult, error) {
	_, screenH := winPlatform.GetPhysicalScreenDimensions()
	// DOTA 2 经典小地图位于左下角，标准高宽约 298x298
	const mapSize = 298
	const margin = 14
	x := margin
	y := screenH - mapSize - margin
	if y < 0 {
		y = 0
	}

	return s.CaptureRect(x, y, mapSize, mapSize)
}

// StartF10CaptureHotkey 启动全局 F10 截屏热键监听
func (s *VisionService) StartF10CaptureHotkey() bool {
	s.mu.Lock()
	defer s.mu.Unlock()

	ok := winPlatform.GetHotkeyManager().StartF10Listener(func() {
		log.Printf("[VisionService] 捕获到全局 F10 按下，正在触发顶部截屏...")
		res, err := s.CaptureTopScreen()
		if err != nil {
			log.Printf("[VisionService] F10 触发截图失败: %v", err)
		} else if res != nil && res.Success {
			log.Printf("[VisionService] F10 顶部截屏成功: 物理分辨率 %dx%d, 裁剪尺寸 %dx%d, 耗时 %dms",
				res.ScreenWidth, res.ScreenHeight, res.CropWidth, res.CropHeight, res.DurationMs)
		}
	})

	s.hotkeyActive = ok
	return ok
}

// StopF10CaptureHotkey 停止全局 F10 热键监听
func (s *VisionService) StopF10CaptureHotkey() {
	s.mu.Lock()
	defer s.mu.Unlock()

	winPlatform.GetHotkeyManager().Stop()
	s.hotkeyActive = false
	log.Printf("[VisionService] 全局 F10 热键监听已停止")
}

// IsHotkeyActive 查询热键激活状态
func (s *VisionService) IsHotkeyActive() bool {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.hotkeyActive
}

// GetLastCapture 获取最近一次成功截屏缓存
func (s *VisionService) GetLastCapture() *models.CaptureResult {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.lastCapture
}

func (s *VisionService) emitCaptureEvent(res *models.CaptureResult) {
	s.mu.RLock()
	app := s.app
	s.mu.RUnlock()

	if app != nil {
		app.Event.Emit("vision:capture-completed", res)
	}
}
