package main

import (
	"embed"
	"os"

	"aegis-coach/internal/overlay"

	"github.com/wailsapp/wails/v3/pkg/application"
	"github.com/wailsapp/wails/v3/pkg/w32"
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

// main 应用程序入口函数 (基于 Wails v3.0.0-beta.23)
//
// 支持两类运行模式：
// 1. 默认主程序模式：全要素电竞战术教练、英雄图鉴、物品数据库、赛后复盘与设置中枢。
// 2. 独立置顶悬浮窗模式 (-overlay / --overlay)：极轻量电竞 HUD 悬浮小窗，始终置顶于游戏上方。
func main() {
	isOverlay := false
	for _, arg := range os.Args[1:] {
		if arg == "-overlay" || arg == "--overlay" {
			isOverlay = true
			break
		}
	}

	appLogic := NewApp(isOverlay)

	app := application.New(application.Options{
		Name: "Dota AI Coach",
		Services: []application.Service{
			application.NewService(appLogic),
		},
		Assets: application.AssetOptions{
			Handler: application.AssetFileServerFS(assets),
		},
	})

	if isOverlay {
		// 独立脱离主 App 的原生桌面置顶经济悬浮窗 (无边框、透明、始终置顶、Alt+Tab 隐藏)
		window := app.Window.NewWithOptions(application.WebviewWindowOptions{
			Name:              "game-overlay",
			Title:             "神盾教练 - 局内经济悬浮窗",
			Width:             340,
			Height:            460,
			Frameless:         true,
			AlwaysOnTop:       true,
			IgnoreMouseEvents: false,
			BackgroundType:    application.BackgroundTypeTransparent,
			Windows: application.WindowsWindow{
				HiddenOnTaskbar: true,
				ExStyle:         w32.WS_EX_TOOLWINDOW,
			},
		})
		appLogic.SetWindow(window)

		// 启动 WinAPI 全局置顶失效恢复保活协程 (500ms 周期保活)
		go overlay.StartTopMostRefresher(window)

		if err := app.Run(); err != nil {
			println("独立悬浮窗运行失败:", err.Error())
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
	appLogic.SetWindow(window)

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
			app.Quit()
		})

	// 2. 视图与导航菜单 (View)
	viewMenu := mainMenu.AddSubmenu("视图")
	viewMenu.Add("切换对局悬浮窗").
		SetAccelerator("Alt+F2").
		OnClick(func(ctx *application.Context) {
			appLogic.ToggleOverlayWindow()
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
		println("主程序运行失败:", err.Error())
	}
}
