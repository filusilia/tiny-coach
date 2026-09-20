package main

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"

	"aegis-coach/internal/ai"
	"aegis-coach/internal/dota"
	"aegis-coach/internal/overlay"
	"aegis-coach/internal/player"
	"aegis-coach/internal/storage"

	"github.com/wailsapp/wails/v3/pkg/application"
	"github.com/wailsapp/wails/v3/pkg/w32"
)

var (
	// overlayProcessMu 保护独立悬浮窗子进程生命周期操作的互斥锁
	overlayProcessMu sync.Mutex
	// overlayCmd 当前由主程序启动的独立悬浮窗进程句柄
	overlayCmd *exec.Cmd
)

// App 神盾教练核心应用程序控制中枢 (Wails v3 Service 统一门面 Facade)
// 统一协调 internal/ai, internal/player, internal/dota, internal/storage 等领域模块
type App struct {
	ctx           context.Context
	window        *application.WebviewWindow
	isOverlay     bool
	storage       *storage.Manager
	gsiReceiver   *dota.GsiReceiver
	aiService     *ai.Service
	playerService *player.Service
}

// NewApp 构造神盾教练 App 协调中枢实体
func NewApp(isOverlay bool) *App {
	sm := storage.NewManager()
	app := &App{
		isOverlay:     isOverlay,
		storage:       sm,
		aiService:     ai.NewService(),
		playerService: player.NewService(sm),
	}
	app.gsiReceiver = dota.NewGsiReceiver(4444, emitEvent)
	return app
}

// SetWindow 关联当前原生窗口句柄到服务实体
func (a *App) SetWindow(w *application.WebviewWindow) {
	a.window = w
}

// ServiceStartup Wails v3 服务启动生命周期钩子
func (a *App) ServiceStartup(ctx context.Context, options application.ServiceOptions) error {
	a.ctx = ctx

	// 首次启动时自动创建缓存文件夹结构（用户真实文档目录），幂等且不覆盖
	_, _ = a.storage.EnsureCacheDirectories("")

	// 仅主程序模式启动 4444 端口 GSI 接收服务，防止与独立悬浮窗冲突
	if !a.isOverlay {
		go func() {
			if err := a.gsiReceiver.Start(); err != nil {
				println("[GSI Receiver Error]:", err.Error())
			}
		}()
	}
	return nil
}

// ServiceShutdown Wails v3 服务退出清理钩子
func (a *App) ServiceShutdown() error {
	if !a.isOverlay {
		_ = a.gsiReceiver.Stop()

		overlayProcessMu.Lock()
		if overlayCmd != nil && overlayCmd.Process != nil {
			_ = overlayCmd.Process.Kill()
			overlayCmd = nil
		}
		overlayProcessMu.Unlock()
	}
	return nil
}

// emitEvent 统一通过 Wails v3 事件总线向前端派发事件
func emitEvent(name string, data ...any) {
	if app := application.Get(); app != nil && app.Event != nil {
		app.Event.Emit(name, data...)
	}
}

// ==============================================================================
// 1. 悬浮窗控制与 WinAPI 桌面置顶 (委托给 internal/overlay)
// ==============================================================================

// SetIgnoreMouseEvents 供前端动态切换当前悬浮窗的鼠标事件穿透属性
func (a *App) SetIgnoreMouseEvents(ignore bool) {
	if a.window != nil {
		a.window.SetIgnoreMouseEvents(ignore)
	}
}

// IsOverlayMode 供前端判断当前是否为独立置顶小窗
func (a *App) IsOverlayMode() bool {
	return a.isOverlay
}

// ToggleOverlayWindow 主程序调用以开启/关闭独立桌面置顶悬浮窗
func (a *App) ToggleOverlayWindow() bool {
	if a.isOverlay {
		return false
	}

	appInst := application.Get()
	if appInst != nil && appInst.Window != nil {
		if overlayWin, exists := appInst.Window.GetByName("game-overlay"); exists && overlayWin != nil {
			if overlayWin.IsVisible() {
				overlayWin.Hide()
				emitEvent("overlay_state_change", false)
				return false
			}
			overlayWin.Show()
			overlayWin.Focus()
			hwnd := uintptr(overlayWin.NativeWindow())
			if hwnd != 0 {
				overlay.EnsureOverlayExtendedStyle(hwnd)
				overlay.ForceTopMost(hwnd)
			}
			emitEvent("overlay_state_change", true)
			return true
		}

		// 进程内创建无边框透明置顶小窗
		newOverlay := appInst.Window.NewWithOptions(application.WebviewWindowOptions{
			Name:              "game-overlay",
			Title:             "神盾教练 - 局内经济悬浮窗",
			Width:             340,
			Height:            460,
			Frameless:         true,
			AlwaysOnTop:       true,
			URL:               "/?window=overlay",
			IgnoreMouseEvents: false,
			BackgroundType:    application.BackgroundTypeTransparent,
			Windows: application.WindowsWindow{
				HiddenOnTaskbar: true,
				ExStyle:         w32.WS_EX_TOOLWINDOW,
			},
		})
		if newOverlay != nil {
			go overlay.StartTopMostRefresher(newOverlay)
			newOverlay.Show()
			newOverlay.Focus()
			emitEvent("overlay_state_change", true)
			return true
		}
	}

	// 平滑回退至独立子进程模式
	overlayProcessMu.Lock()
	defer overlayProcessMu.Unlock()

	if overlayCmd != nil && overlayCmd.Process != nil {
		_ = overlayCmd.Process.Kill()
		overlayCmd = nil
		emitEvent("overlay_state_change", false)
		return false
	}

	exePath, err := os.Executable()
	if err != nil {
		return false
	}

	cmd := exec.Command(exePath, "-overlay")
	if err := cmd.Start(); err != nil {
		return false
	}
	overlayCmd = cmd

	go func() {
		_ = cmd.Wait()
		overlayProcessMu.Lock()
		if overlayCmd == cmd {
			overlayCmd = nil
		}
		overlayProcessMu.Unlock()
		emitEvent("overlay_state_change", false)
	}()

	emitEvent("overlay_state_change", true)
	return true
}

// IsOverlayRunning 查询悬浮窗是否正在后台运行或可见
func (a *App) IsOverlayRunning() bool {
	if appInst := application.Get(); appInst != nil && appInst.Window != nil {
		if overlayWin, exists := appInst.Window.GetByName("game-overlay"); exists && overlayWin != nil {
			if overlayWin.IsVisible() {
				return true
			}
		}
	}

	overlayProcessMu.Lock()
	defer overlayProcessMu.Unlock()
	return overlayCmd != nil && overlayCmd.Process != nil
}

// GetLatestGsiSnapshot 获取最新 GSI 游戏快照
func (a *App) GetLatestGsiSnapshot() string {
	return a.gsiReceiver.GetLatestSnapshot()
}

// ==============================================================================
// 2. 本地缓存与文件系统管理 (委托给 internal/storage)
// ==============================================================================

// EnsureCacheDirectories 创建缓存子目录结构
func (a *App) EnsureCacheDirectories(customDir string) (string, error) {
	return a.storage.EnsureCacheDirectories(customDir)
}

// GetDefaultCacheDirectory 获取默认系统级缓存根路径
func (a *App) GetDefaultCacheDirectory() string {
	return a.storage.GetDefaultCacheDirectory()
}

// GetCacheDirectory 获取当前正在生效的缓存根路径
func (a *App) GetCacheDirectory() string {
	return a.storage.GetCacheDirectory()
}

// SetCacheDirectory 设置自定义缓存根路径
func (a *App) SetCacheDirectory(customDir string) (string, error) {
	return a.storage.SetCacheDirectory(customDir)
}

// SelectDirectoryDialog 唤起操作系统原生的选择文件夹对话框
func (a *App) SelectDirectoryDialog() (string, error) {
	current := a.GetCacheDirectory()
	appInst := application.Get()
	if appInst == nil || appInst.Dialog == nil {
		return current, fmt.Errorf("dialog manager not available")
	}

	dialog := appInst.Dialog.OpenFile().
		CanChooseDirectories(true).
		CanChooseFiles(false).
		SetTitle("选择神盾教练数据缓存文件夹").
		SetDirectory(current)

	selectedDir, err := dialog.PromptForSingleSelection()
	if err != nil {
		return "", err
	}
	if strings.TrimSpace(selectedDir) == "" {
		return current, nil
	}
	return a.SetCacheDirectory(selectedDir)
}

// SelectModelFileDialog 唤起选择本地 AI 模型文件对话框 (.gguf, .onnx, .bin)
func (a *App) SelectModelFileDialog() (string, error) {
	appInst := application.Get()
	if appInst == nil || appInst.Dialog == nil {
		return "", fmt.Errorf("dialog manager not available")
	}

	dialog := appInst.Dialog.OpenFile().
		CanChooseDirectories(false).
		CanChooseFiles(true).
		SetTitle("选择本地 AI 模型文件 (支持 GGUF / ONNX / Bin)").
		AddFilter("AI 模型文件 (*.gguf;*.onnx;*.bin)", "*.gguf;*.onnx;*.bin").
		AddFilter("所有文件 (*.*)", "*.*")

	selectedFile, err := dialog.PromptForSingleSelection()
	if err != nil {
		return "", err
	}
	return strings.TrimSpace(selectedFile), nil
}

// OpenCacheDirectory 在 Windows 资源管理器中打开缓存目录
func (a *App) OpenCacheDirectory(dir string) error {
	target := a.storage.ResolveBaseCacheDir(dir)
	if _, err := a.storage.EnsureCacheDirectories(target); err != nil {
		return err
	}
	cmd := exec.Command("explorer", target)
	return cmd.Start()
}

// ClearCacheDirectory 安全清理指定缓存目录下的临时文件
func (a *App) ClearCacheDirectory(dir string) error {
	target := a.storage.ResolveBaseCacheDir(dir)
	subDirs := []string{"heroes", "items", "tts", "rag", "matches"}
	for _, sub := range subDirs {
		subPath := filepath.Join(target, sub)
		entries, err := os.ReadDir(subPath)
		if err == nil {
			for _, entry := range entries {
				_ = os.RemoveAll(filepath.Join(subPath, entry.Name()))
			}
		}
	}
	return nil
}

// ==============================================================================
// 3. AI 实时战术推理中枢 (委托给 internal/ai)
// ==============================================================================

// AskCoach 执行实战战术即时问答 (Wails Binding)
func (a *App) AskCoach(
	question string,
	ctx ai.TacticalContext,
	mode string,
	endpoint string,
	apiKey string,
	model string,
	timeoutSec int,
) (*ai.CoachReply, error) {
	return a.aiService.AskCoach(question, ctx, mode, endpoint, apiKey, model, timeoutSec)
}

// GenerateMatchReview 针对单场历史比赛生成赛后 AI 深度复盘报告 (Wails Binding)
func (a *App) GenerateMatchReview(
	matchJson string,
	targetPlayerSlot int,
	targetAccountId string,
	mode string,
	endpoint string,
	apiKey string,
	model string,
) (*ai.MatchReviewResult, error) {
	return a.aiService.GenerateMatchReview(matchJson, targetPlayerSlot, targetAccountId, mode, endpoint, apiKey, model)
}

// TestAiEndpoint 测试目标 AI 端点连通性与模型响应 (Wails Binding)
func (a *App) TestAiEndpoint(mode string, endpoint string, apiKey string, model string) (*ai.AiEndpointTestResult, error) {
	return a.aiService.TestEndpoint(mode, endpoint, apiKey, model)
}

// FetchCloudModels 原生拉取云端可用模型列表 (Wails Binding)
func (a *App) FetchCloudModels(baseUrl string, apiKey string) ([]string, error) {
	return a.aiService.FetchCloudModels(baseUrl, apiKey)
}

// FetchLocalModels 原生探测本地 Ollama 已安装模型 (Wails Binding)
func (a *App) FetchLocalModels(endpoint string) ([]string, error) {
	return a.aiService.FetchLocalModels(endpoint)
}

// ==============================================================================
// 4. 玩家战绩与 OpenDota 网关 (委托给 internal/player)
// ==============================================================================

// NormalizeDotaAccountId 高鲁棒性 Dota 2 玩家数字 ID 提取器 (Wails Binding)
func (a *App) NormalizeDotaAccountId(rawInput string) (string, error) {
	return a.playerService.NormalizeAccountId(rawInput)
}

// GetPlayerProfile 拉取并分析玩家近 20 场公开战绩 (Wails Binding)
func (a *App) GetPlayerProfile(accountIdInput string) (*player.PlayerProfileAnalysis, error) {
	return a.playerService.GetPlayerProfile(accountIdInput)
}

// GetPlayerRecentMatches 原生获取玩家近期对局简报原始 JSON 字符串 (Wails Binding)
func (a *App) GetPlayerRecentMatches(accountId string) (string, error) {
	return a.playerService.GetPlayerRecentMatches(accountId)
}

// GetMatchDetails 获取单场历史比赛全量详情 (Wails Binding)
func (a *App) GetMatchDetails(matchId string) (string, error) {
	return a.playerService.GetMatchDetails(matchId)
}
