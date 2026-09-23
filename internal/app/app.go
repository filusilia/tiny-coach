package app

import (
	"context"
	"log"
	"sync"

	"aegis-coach/internal/models"
	"aegis-coach/internal/services"

	"github.com/wailsapp/wails/v3/pkg/application"
)

// App 神盾教练应用程序生命周期与 Wails 服务总门面
type App struct {
	mu             sync.RWMutex
	ctx            context.Context
	window         *application.WebviewWindow
	isOverlay      bool
	systemService  *services.SystemService
	overlayService *services.OverlayService
	gsiService     *services.GsiService
	playerService  *services.PlayerService
	aiService      *services.AiService
}

// NewApp 构造 App 门面实体
func NewApp(isOverlay bool) *App {
	overlaySvc := services.GetOverlayService(isOverlay)
	overlaySvc.SetOverlayMode(isOverlay) // ★ 显式设置，避免 sync.Once 首次参数陷阱

	return &App{
		isOverlay:      isOverlay,
		systemService:  services.GetSystemService(),
		overlayService: overlaySvc,
		gsiService:     services.GetGsiService(4444),
		playerService:  services.GetPlayerService(),
		aiService:      services.GetAiService(),
	}
}

// SetWindow 关联原生 WebviewWindow 句柄
func (a *App) SetWindow(w *application.WebviewWindow) {
	a.mu.Lock()
	defer a.mu.Unlock()
	a.window = w
	a.systemService.SetWindow(w)
	a.overlayService.SetWindow(w)
}

// ServiceStartup Wails v3 生命周期启动钩子
func (a *App) ServiceStartup(ctx context.Context, options application.ServiceOptions) error {
	a.mu.Lock()
	a.ctx = ctx
	a.mu.Unlock()

	// 幂等确保缓存目录存在
	_, _ = a.systemService.EnsureCacheDirectories("")

	// 仅主窗口启动 GSI 监听服务
	if !a.isOverlay {
		go func() {
			if err := a.gsiService.Start(); err != nil {
				log.Printf("[GsiService Error]: %v", err)
			}
		}()
	}
	return nil
}

// ServiceShutdown Wails v3 生命周期终止清理钩子
func (a *App) ServiceShutdown() error {
	if !a.isOverlay {
		_ = a.gsiService.Stop()
		a.overlayService.StopTerminatingOverlay()
	}
	return nil
}

// ==============================================================================
// 兼容性导出方法（供过渡阶段调用，内部全部委托给对应 Domain Service）
// ==============================================================================

func (a *App) IsOverlayMode() bool {
	return a.overlayService.IsOverlayMode()
}

func (a *App) IsOverlayRunning() bool {
	return a.overlayService.IsOverlayRunning()
}

func (a *App) ToggleOverlayWindow() bool {
	return a.overlayService.ToggleOverlayWindow()
}

func (a *App) SetIgnoreMouseEvents(ignore bool) {
	a.overlayService.SetIgnoreMouseEvents(ignore)
}

func (a *App) ToggleMouseThrough() bool {
	return a.overlayService.ToggleMouseThrough()
}

func (a *App) ResetOverlayPosition() {
	a.overlayService.ResetOverlayPosition()
}

func (a *App) GetLatestGsiSnapshot() string {
	return a.gsiService.GetLatestSnapshot()
}

func (a *App) IsGsiRunning() bool {
	return a.gsiService.IsRunning()
}

func (a *App) IsGsiConnected() bool {
	return a.gsiService.IsConnected()
}

func (a *App) GetDefaultCacheDirectory() string {
	return a.systemService.GetDefaultCacheDirectory()
}

func (a *App) GetCacheDirectory() string {
	return a.systemService.GetCacheDirectory()
}

func (a *App) SetCacheDirectory(customDir string) (string, error) {
	return a.systemService.SetCacheDirectory(customDir)
}

func (a *App) EnsureCacheDirectories(customDir string) (string, error) {
	return a.systemService.EnsureCacheDirectories(customDir)
}

func (a *App) SelectDirectoryDialog() (string, error) {
	return a.systemService.SelectDirectoryDialog()
}

func (a *App) SelectModelFileDialog() (string, error) {
	return a.systemService.SelectModelFileDialog()
}

func (a *App) ScanLocalModelFiles() []string {
	return a.systemService.ScanLocalModelFiles()
}

func (a *App) OpenCacheDirectory(dir string) error {
	return a.systemService.OpenCacheDirectory(dir)
}

func (a *App) ClearCacheDirectory(dir string) error {
	return a.systemService.ClearCacheDirectory(dir)
}

func (a *App) NormalizeDotaAccountId(input string) (string, error) {
	return a.playerService.NormalizeAccountId(input)
}

func (a *App) GetPlayerProfile(accountIdInput string) (*models.PlayerProfileAnalysis, error) {
	return a.playerService.GetPlayerProfile(accountIdInput)
}

func (a *App) GetMatchDetails(matchId string) (string, error) {
	return a.playerService.GetMatchDetails(matchId)
}

func (a *App) GetPlayerRecentMatches(accountId string) (string, error) {
	return a.playerService.GetPlayerRecentMatches(accountId)
}

func (a *App) TestAiEndpoint(mode, endpoint, apiKey, model string) (*models.AiEndpointTestResultDTO, error) {
	return a.aiService.TestAiEndpoint(mode, endpoint, apiKey, model)
}

func (a *App) FetchCloudModels(baseUrl, apiKey string) ([]string, error) {
	return a.aiService.FetchCloudModels(baseUrl, apiKey)
}

func (a *App) AskCoach(question string, ctx *models.BattleContextDTO, mode, endpoint, apiKey, model string) (string, error) {
	return a.aiService.AskCoach(question, ctx, mode, endpoint, apiKey, model)
}

func (a *App) GenerateMatchReview(matchId, matchDataJson string, playerSlot int, mode, endpoint, apiKey, model string) (*models.MatchReviewResultDTO, error) {
	return a.aiService.GenerateMatchReview(matchId, matchDataJson, playerSlot, mode, endpoint, apiKey, model)
}

func (a *App) PullLocalModel(modelName, endpoint string) (string, error) {
	return a.aiService.PullLocalModel(modelName, endpoint)
}

func (a *App) TestTtsEndpoint(endpoint, apiKey, engineType, voice string) (string, error) {
	return a.systemService.TestTtsEndpoint(endpoint, apiKey, engineType, voice)
}

func (a *App) CacheAllIcons() (map[string]int, error) {
	return a.systemService.CacheAllIcons()
}

func (a *App) LoadCachedGameData(dataType string) (string, error) {
	return a.systemService.LoadCachedGameData(dataType)
}

func (a *App) SaveCachedGameData(dataType string, content string) error {
	return a.systemService.SaveCachedGameData(dataType, content)
}

func (a *App) LoadSettings() (string, error) {
	return a.systemService.LoadSettings()
}

func (a *App) SaveSettings(settingsJson string) error {
	return a.systemService.SaveSettings(settingsJson)
}

func (a *App) FetchLatestPatchInfo() (string, error) {
	return a.systemService.FetchLatestPatchInfo()
}

func (a *App) CloseOverlay() {
	a.overlayService.CloseOverlay()
}
