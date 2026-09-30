package app

import (
	"context"
	"encoding/json"
	"sync"

	"aegis-coach/internal/config"
	"aegis-coach/internal/models"
	aiSvc "aegis-coach/internal/services/ai"
	gsiSvc "aegis-coach/internal/services/gsi"
	ovlSvc "aegis-coach/internal/services/overlay"
	playerSvc "aegis-coach/internal/services/player"
	sysSvc "aegis-coach/internal/services/system"
	ttsSvc "aegis-coach/internal/services/tts"
	visionSvc "aegis-coach/internal/services/vision"

	"github.com/wailsapp/wails/v3/pkg/application"
)

// App 神盾教练应用程序生命周期与 Wails 服务总门面
type App struct {
	mu             sync.RWMutex
	ctx            context.Context
	window         *application.WebviewWindow
	isOverlay      bool
	systemService  *sysSvc.SystemService
	overlayService *ovlSvc.OverlayService
	gsiService     *gsiSvc.GsiService
	playerService  *playerSvc.PlayerService
	aiService      *aiSvc.AiService
	ttsService     *ttsSvc.TtsService
	visionService  *visionSvc.VisionService
}

// NewApp 构造 App 门面实体
func NewApp(isOverlay bool) *App {
	overlay := ovlSvc.GetOverlayService(isOverlay)
	overlay.SetOverlayMode(isOverlay) // 显式设置，避免 sync.Once 首次参数陷阱

	system := sysSvc.GetSystemService()

	// 优先同步尝试从主设置中读取配置的 GSI 端口，避免启动阶段端口异步覆盖的竞态
	port := config.DefaultGsiPort
	if settingsStr, err := system.LoadSettings(); err == nil {
		var settingsMap map[string]any
		if err := json.Unmarshal([]byte(settingsStr), &settingsMap); err == nil {
			if pFloat, ok := settingsMap["gsi_port"].(float64); ok && pFloat > 0 && pFloat < 65536 {
				port = int(pFloat)
			}
		}
	}

	return &App{
		isOverlay:      isOverlay,
		systemService:  system,
		overlayService: overlay,
		gsiService:     gsiSvc.GetGsiService(port),
		playerService:  playerSvc.GetPlayerService(),
		aiService:      aiSvc.GetAiService(),
		ttsService:     ttsSvc.GetTtsService(),
		visionService:  visionSvc.GetVisionService(),
	}
}

// SetWindow 关联原生 WebviewWindow 句柄
func (a *App) SetWindow(w *application.WebviewWindow) {
	a.mu.Lock()
	defer a.mu.Unlock()
	a.window = w
	a.systemService.SetWindow(w)
	a.overlayService.SetWindow(w)
	a.visionService.SetWindow(w)
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

func (a *App) GetPlayerCard(accountId string) (string, error) {
	return a.playerService.GetPlayerCard(accountId)
}

func (a *App) GetHeroMatchupRecommendations(heroId int, enemyHeroIds []int) (*models.HeroMatchupRecommendation, error) {
	return a.playerService.GetHeroMatchupRecommendations(heroId, enemyHeroIds)
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

func (a *App) UpdateAiCredentials(mode, endpoint, apiKey string) {
	a.aiService.UpdateCredentials(mode, endpoint, apiKey)
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

func (a *App) SetOverlaySize(width, height int) {
	a.overlayService.SetOverlaySize(width, height)
}

func (a *App) GetGoSpeechStatus() (bool, error) {
	return a.ttsService.GetGoSpeechStatus()
}

func (a *App) SynthesizeSpeech(text string, speed float64) (string, error) {
	return a.ttsService.SynthesizeSpeech(text, speed)
}

func (a *App) TestGoSpeech(text string, speed float64) (string, error) {
	return a.ttsService.TestGoSpeech(text, speed)
}

func (a *App) GetTtsStatus() ttsSvc.EngineStatus {
	return a.ttsService.GetTtsStatus()
}

func (a *App) CaptureTopScreen() (*models.CaptureResult, error) {
	return a.visionService.CaptureTopScreen()
}

func (a *App) CaptureRect(x, y, w, h int) (*models.CaptureResult, error) {
	return a.visionService.CaptureRect(x, y, w, h)
}

func (a *App) CaptureMinimap() (*models.CaptureResult, error) {
	return a.visionService.CaptureMinimap()
}

func (a *App) StartF10CaptureHotkey() bool {
	return a.visionService.StartF10CaptureHotkey()
}

func (a *App) StopF10CaptureHotkey() {
	a.visionService.StopF10CaptureHotkey()
}

func (a *App) IsHotkeyActive() bool {
	return a.visionService.IsHotkeyActive()
}

func (a *App) GetLastCapture() *models.CaptureResult {
	return a.visionService.GetLastCapture()
}
