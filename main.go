package main

import (
	"embed"
	"log"
	"log/slog"
	"os"
	"syscall"

	internalApp "aegis-coach/internal/app"
	"aegis-coach/internal/models"
	"aegis-coach/internal/overlay"
	"aegis-coach/internal/services"

	"github.com/wailsapp/wails/v3/pkg/application"
	"github.com/wailsapp/wails/v3/pkg/events"
	"golang.org/x/sys/windows"
)

var (
	// Version 应用程序发布版本号（可在编译时通过 -ldflags "-X main.Version=x.y.z" 动态注入）
	Version = "2.0.0"
	// BuildTime 应用程序构建时间戳
	BuildTime = "2026-09-22"
	// GitCommit 源代码 Git 提交指纹
	GitCommit = "HEAD"
)

// assets 静态嵌入前端生产打包输出文件 (Vite + Vue 3 构建产物位于 frontend/dist)
// 由 Go 编译器在编译时全量嵌入二进制文件中，实现无需外部文件依赖的单文件绿色客户端
//
//go:embed all:frontend/dist
var assets embed.FS

// trayIconData 嵌入应用程序原生 Windows 图标作为系统托盘图标
//
//go:embed build/windows/icon.ico
var trayIconData []byte

// emitEvent 统一通过 Wails v3 事件总线向前端派发事件
func emitEvent(name string, data ...any) {
	if app := application.Get(); app != nil && app.Event != nil {
		app.Event.Emit(name, data...)
	}
}

// App 位于 package main 的统一 IPC 门面，内嵌 internalApp.App
// 确保 Wails v3 反射生成的 FQN 为标准的 main.App.<MethodName>，完美兼容前端调用
type App struct {
	*internalApp.App
}

func NewApp(isOverlay bool) *App {
	return &App{
		App: internalApp.NewApp(isOverlay),
	}
}

// ==============================================================================
// 显式声明核心 IPC 门面方法
// 消除跨包匿名结构体内嵌在静态 AST 代码生成器与反射导出时的盲区，确保 main.App.<Method> 100% 存在
// ==============================================================================

func (a *App) SelectDirectoryDialog() (string, error) {
	return a.App.SelectDirectoryDialog()
}

func (a *App) SelectModelFileDialog() (string, error) {
	return a.App.SelectModelFileDialog()
}

func (a *App) GetDefaultCacheDirectory() string {
	return a.App.GetDefaultCacheDirectory()
}

func (a *App) GetCacheDirectory() string {
	return a.App.GetCacheDirectory()
}

func (a *App) SetCacheDirectory(customDir string) (string, error) {
	return a.App.SetCacheDirectory(customDir)
}

func (a *App) EnsureCacheDirectories(customDir string) (string, error) {
	return a.App.EnsureCacheDirectories(customDir)
}

func (a *App) OpenCacheDirectory(dir string) error {
	return a.App.OpenCacheDirectory(dir)
}

func (a *App) ClearCacheDirectory(dir string) error {
	return a.App.ClearCacheDirectory(dir)
}

func (a *App) ScanLocalModelFiles() []string {
	return a.App.ScanLocalModelFiles()
}

func (a *App) IsOverlayMode() bool {
	return a.App.IsOverlayMode()
}

func (a *App) IsOverlayRunning() bool {
	return a.App.IsOverlayRunning()
}

func (a *App) ToggleOverlayWindow() bool {
	return a.App.ToggleOverlayWindow()
}

func (a *App) SetIgnoreMouseEvents(ignore bool) {
	a.App.SetIgnoreMouseEvents(ignore)
}

func (a *App) ToggleMouseThrough() bool {
	return a.App.ToggleMouseThrough()
}

func (a *App) ResetOverlayPosition() {
	a.App.ResetOverlayPosition()
}

func (a *App) GetLatestGsiSnapshot() string {
	return a.App.GetLatestGsiSnapshot()
}

func (a *App) IsGsiRunning() bool {
	return a.App.IsGsiRunning()
}

func (a *App) IsGsiConnected() bool {
	return a.App.IsGsiConnected()
}

func (a *App) NormalizeDotaAccountId(input string) (string, error) {
	return a.App.NormalizeDotaAccountId(input)
}

func (a *App) GetPlayerProfile(accountIdInput string) (*models.PlayerProfileAnalysis, error) {
	return a.App.GetPlayerProfile(accountIdInput)
}

func (a *App) GetMatchDetails(matchId string) (string, error) {
	return a.App.GetMatchDetails(matchId)
}

func (a *App) GetPlayerRecentMatches(accountId string) (string, error) {
	return a.App.GetPlayerRecentMatches(accountId)
}

func (a *App) TestAiEndpoint(mode, endpoint, apiKey, model string) (*models.AiEndpointTestResultDTO, error) {
	return a.App.TestAiEndpoint(mode, endpoint, apiKey, model)
}

func (a *App) FetchCloudModels(baseUrl, apiKey string) ([]string, error) {
	return a.App.FetchCloudModels(baseUrl, apiKey)
}

func (a *App) AskCoach(question string, ctx *models.BattleContextDTO, mode, endpoint, apiKey, model string) (string, error) {
	return a.App.AskCoach(question, ctx, mode, endpoint, apiKey, model)
}

func (a *App) GenerateMatchReview(matchId, matchDataJson string, playerSlot int, mode, endpoint, apiKey, model string) (*models.MatchReviewResultDTO, error) {
	return a.App.GenerateMatchReview(matchId, matchDataJson, playerSlot, mode, endpoint, apiKey, model)
}

func (a *App) PullLocalModel(modelName, endpoint string) (string, error) {
	return a.App.PullLocalModel(modelName, endpoint)
}

func (a *App) TestTtsEndpoint(endpoint, apiKey, engineType, voice string) (string, error) {
	return a.App.TestTtsEndpoint(endpoint, apiKey, engineType, voice)
}

func (a *App) CacheAllIcons() (map[string]int, error) {
	return a.App.CacheAllIcons()
}

func (a *App) LoadCachedGameData(dataType string) (string, error) {
	return a.App.LoadCachedGameData(dataType)
}

func (a *App) SaveCachedGameData(dataType string, content string) error {
	return a.App.SaveCachedGameData(dataType, content)
}

func (a *App) LoadSettings() (string, error) {
	return a.App.LoadSettings()
}

func (a *App) SaveSettings(settingsJson string) error {
	return a.App.SaveSettings(settingsJson)
}

func (a *App) FetchLatestPatchInfo() (string, error) {
	return a.App.FetchLatestPatchInfo()
}

func (a *App) CloseOverlay() {
	a.App.CloseOverlay()
}

// showExitConfirmDialog 唤起 Windows 原生模态确认弹窗：当悬浮窗开启时询问用户是否确定关闭软件
func showExitConfirmDialog(win *application.WebviewWindow) bool {
	var hwnd uintptr
	if win != nil && win.NativeWindow() != nil {
		hwnd = uintptr(win.NativeWindow())
	}
	title, _ := syscall.UTF16PtrFromString("神盾教练 - 退出确认")
	text, _ := syscall.UTF16PtrFromString("局内独立置顶悬浮窗当前处于开启状态。\n\n是否确定要退出神盾教练？\n（退出软件将同时关闭悬浮窗）")
	// MB_YESNO (0x00000004) | MB_ICONQUESTION (0x00000020) | MB_DEFBUTTON2 (0x00000100)
	ret, err := windows.MessageBox(windows.HWND(hwnd), text, title, windows.MB_YESNO|windows.MB_ICONQUESTION|windows.MB_DEFBUTTON2)
	if err != nil {
		log.Printf("[main-window] 弹出退出确认框失败: %v", err)
		return true
	}
	// IDYES = 6 代表用户点击“是”
	return ret == 6
}

// main 应用程序入口函数 (基于 Wails v3.0.0-beta.23)
// 职责收敛：仅负责 Wails 宿主初始化、窗口创建与多服务挂载
func main() {
	isOverlay := false
	for _, arg := range os.Args[1:] {
		if arg == "-overlay" || arg == "--overlay" {
			isOverlay = true
			break
		}
	}

	log.Printf("[AegisCoach] 正在启动神盾教练 (Version: %s, BuildTime: %s, Commit: %s, OverlayMode: %v)...",
		Version, BuildTime, GitCommit, isOverlay)

	appLogic := NewApp(isOverlay)
	systemService := services.GetSystemService()
	overlayService := services.GetOverlayService(isOverlay)
	overlayService.SetOverlayMode(isOverlay) // ★ 显式覆盖 sync.Once 首次参数陷阱
	gsiService := services.GetGsiService(4444)
	playerService := services.GetPlayerService()
	aiService := services.GetAiService()

	app := application.New(application.Options{
		Name:        "Dota AI Coach",
		Description: "神盾教练 - Dota 2 实时战术助手与 AI 复盘分析",
		LogLevel:    slog.LevelInfo,
		PanicHandler: func(details *application.PanicDetails) {
			log.Printf("[CRITICAL PANIC RECOVERED] Error: %v\nStack Trace:\n%s\nFull Stack:\n%s",
				details.Error, details.StackTrace, details.FullStackTrace)
		},
		Services: []application.Service{
			application.NewService(appLogic),
			// 注意：Route: "/cache" 专用于 SystemService 实现的 http.Handler，
			// 用于向前端提供本地英雄/物品图标的高性能流式读取（如 <img src="/cache/heroes/..." />）
			// 其绝不会篡改 RPC 绑定名 FQN，绝不可删除！
			application.NewServiceWithOptions(systemService, application.ServiceOptions{
				Route: "/cache",
			}),
			application.NewService(overlayService),
			application.NewService(gsiService),
			application.NewService(playerService),
			application.NewService(aiService),
		},
		Assets: application.AssetOptions{
			Handler: application.AssetFileServerFS(assets),
		},
	})

	if isOverlay {
		x, y, hasPos := overlayService.LoadSavedPosition()
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

		// 独立脱离主 App 的原生桌面置顶经济悬浮窗 (无边框、透明、始终置顶、Alt+Tab 隐藏)
		window := app.Window.NewWithOptions(opts)
		if window == nil {
			log.Fatalf("[FATAL] 独立悬浮窗创建失败：WebviewWindow 为 nil")
		}
		appLogic.SetWindow(window)
		if hasPos {
			window.SetPosition(x, y)
		}

		// 启动 WinAPI 全局置顶失效恢复保活协程 (500ms 周期保活)
		go overlay.StartTopMostRefresher(window)

		if err := app.Run(); err != nil {
			log.Printf("[FATAL] 独立悬浮窗运行失败: %v", err)
		}
		return
	}

	// 主程序全功能窗口
	window := app.Window.NewWithOptions(application.WebviewWindowOptions{
		Name:           "main-window",
		Title:          "神盾教练 - Dota 2 实时战术助手",
		Width:          1280,
		Height:         820,
		Frameless:      false,
		AlwaysOnTop:    false,
		BackgroundType: application.BackgroundTypeSolid,
		Windows: application.WindowsWindow{
			Theme: application.Dark, // 原生沉浸式暗黑标题栏，杜绝刺眼白条
		},
	})
	if window == nil {
		log.Fatalf("[FATAL] 主窗口创建失败：无法获取 WebviewWindow 句柄")
	}
	appLogic.SetWindow(window)

	// 为主窗口显式注册 Alt+F2 / Alt+F3 快捷键响应
	window.RegisterKeyBinding("Alt+F2", func(w application.Window) {
		appLogic.ToggleOverlayWindow()
	})
	window.RegisterKeyBinding("Alt+F3", func(w application.Window) {
		appLogic.ToggleMouseThrough()
	})

	// 主窗口关闭生命周期守护：
	// 若悬浮窗当前处于激活态，拦截关闭动作并唤起原生确认弹窗，提示用户“悬浮窗开启，是否要关闭”
	// 用户确认退出则同步关闭悬浮窗并退出软件；若悬浮窗未打开，直接退出程序
	window.OnWindowEvent(events.Common.WindowClosing, func(e *application.WindowEvent) {
		if appLogic.IsOverlayRunning() {
			e.Cancel()
			if showExitConfirmDialog(window) {
				log.Printf("[main-window] 用户确认关闭软件，正在销毁悬浮窗并退出应用程序...")
				appLogic.CloseOverlay()
				app.Quit()
			} else {
				log.Printf("[main-window] 用户取消退出，保持软件与悬浮窗继续运行")
			}
			return
		}
		log.Printf("[main-window] 主窗口正在关闭，退出软件...")
		appLogic.CloseOverlay()
		app.Quit()
	})

	// 构建 Wails v3 原生应用系统菜单 (Application Menu)
	mainMenu := app.NewMenu()

	// 1. 文件菜单 (File)
	fileMenu := mainMenu.AddSubmenu("文件")
	fileMenu.Add("首选项与 AI 配置...").
		SetAccelerator("CmdOrCtrl+,").
		OnClick(func(ctx *application.Context) {
			emitEvent("navigate_to", "/settings")
		})
	fileMenu.AddSeparator()
	fileMenu.Add("退出神盾教练").
		SetAccelerator("CmdOrCtrl+Q").
		OnClick(func(ctx *application.Context) {
			if appLogic.IsOverlayRunning() {
				if showExitConfirmDialog(window) {
					appLogic.CloseOverlay()
					app.Quit()
				}
				return
			}
			appLogic.CloseOverlay()
			app.Quit()
		})

	// 2. 视图与导航菜单 (View)
	viewMenu := mainMenu.AddSubmenu("视图")
	viewMenu.Add("切换对局悬浮窗").
		SetAccelerator("Alt+F2").
		OnClick(func(ctx *application.Context) {
			appLogic.ToggleOverlayWindow()
		})
	viewMenu.Add("切换悬浮窗鼠标穿透").
		SetAccelerator("Alt+F3").
		OnClick(func(ctx *application.Context) {
			appLogic.ToggleMouseThrough()
		})
	viewMenu.AddSeparator()
	viewMenu.Add("实时战况看板").
		SetAccelerator("CmdOrCtrl+1").
		OnClick(func(ctx *application.Context) {
			emitEvent("navigate_to", "/coach")
		})
	viewMenu.Add("赛后 AI 深度复盘").
		SetAccelerator("CmdOrCtrl+2").
		OnClick(func(ctx *application.Context) {
			emitEvent("navigate_to", "/analysis")
		})
	viewMenu.Add("英雄数据库与出装").
		SetAccelerator("CmdOrCtrl+3").
		OnClick(func(ctx *application.Context) {
			emitEvent("navigate_to", "/heroes")
		})
	viewMenu.Add("现行物品图谱与合成树").
		SetAccelerator("CmdOrCtrl+4").
		OnClick(func(ctx *application.Context) {
			emitEvent("navigate_to", "/items")
		})

	// 3. 帮助与关于菜单 (Help)
	helpMenu := mainMenu.AddSubmenu("帮助")
	helpMenu.Add("关于神盾教练").
		OnClick(func(ctx *application.Context) {
			emitEvent("navigate_to", "/settings")
		})

	app.Menu.SetApplicationMenu(mainMenu)

	// 构建 Wails v3 原生系统托盘 (System Tray)
	if app.SystemTray != nil {
		tray := app.SystemTray.New()
		if len(trayIconData) > 0 {
			tray.SetIcon(trayIconData)
		}
		tray.SetTooltip("神盾教练 (AegisCoach) - Dota 2 AI 战术助手")

		trayMenu := app.NewMenu()
		trayMenu.Add("显示主界面").OnClick(func(ctx *application.Context) {
			if window != nil {
				window.Show()
				window.Focus()
			}
		})
		trayMenu.Add("切换对局悬浮窗 (Alt+F2)").OnClick(func(ctx *application.Context) {
			appLogic.ToggleOverlayWindow()
		})
		trayMenu.Add("切换鼠标穿透 (Alt+F3)").OnClick(func(ctx *application.Context) {
			appLogic.ToggleMouseThrough()
		})
		trayMenu.AddSeparator()
		trayMenu.Add("实时战况看板").OnClick(func(ctx *application.Context) {
			if window != nil {
				window.Show()
				window.Focus()
			}
			emitEvent("navigate_to", "/coach")
		})
		trayMenu.Add("赛后 AI 复盘").OnClick(func(ctx *application.Context) {
			if window != nil {
				window.Show()
				window.Focus()
			}
			emitEvent("navigate_to", "/analysis")
		})
		trayMenu.AddSeparator()
		trayMenu.Add("退出程序").OnClick(func(ctx *application.Context) {
			if appLogic.IsOverlayRunning() {
				if showExitConfirmDialog(window) {
					appLogic.CloseOverlay()
					app.Quit()
				}
				return
			}
			appLogic.CloseOverlay()
			app.Quit()
		})
		tray.SetMenu(trayMenu)

		// 托盘图标左键单击激活主窗口
		tray.OnClick(func() {
			if window != nil {
				if !window.IsVisible() {
					window.Show()
				}
				window.Focus()
			}
		})
	}

	if err := app.Run(); err != nil {
		log.Printf("[FATAL] 主程序运行失败: %v", err)
	}
}
