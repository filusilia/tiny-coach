package main

import (
	"embed"
	"os"

	"github.com/wailsapp/wails/v2"
	"github.com/wailsapp/wails/v2/pkg/options"
	"github.com/wailsapp/wails/v2/pkg/options/assetserver"
	"github.com/wailsapp/wails/v2/pkg/options/windows"
)

//go:embed all:frontend/dist
var assets embed.FS

func main() {
	isOverlay := false
	for _, arg := range os.Args[1:] {
		if arg == "-overlay" || arg == "--overlay" {
			isOverlay = true
			break
		}
	}

	app := NewApp(isOverlay)

	if isOverlay {
		// 独立脱离主 App 的原生桌面置顶经济悬浮窗
		err := wails.Run(&options.App{
			Title:         "神盾教练 - 局内经济悬浮窗",
			Width:         340,
			Height:        460,
			Frameless:     true,
			AlwaysOnTop:   true,
			DisableResize: false,
			AssetServer: &assetserver.Options{
				Assets: assets,
			},
			BackgroundColour: &options.RGBA{R: 11, G: 14, B: 20, A: 240},
			OnStartup:        app.startup,
			Bind: []interface{}{
				app,
			},
			Windows: &windows.Options{
				WebviewIsTransparent: true,
				WindowIsTranslucent:  true,
				BackdropType:         windows.None,
				Theme:                windows.Dark,
			},
		})
		if err != nil {
			println("独立悬浮窗启动失败:", err.Error())
		}
		return
	}

	// 主程序全功能窗口
	err := wails.Run(&options.App{
		Title:  "神盾教练 - Dota 2 实时战术助手",
		Width:  1280,
		Height: 820,
		AssetServer: &assetserver.Options{
			Assets: assets,
		},
		BackgroundColour: &options.RGBA{R: 11, G: 14, B: 20, A: 255},
		OnStartup:        app.startup,
		OnShutdown:       app.shutdown,
		Bind: []interface{}{
			app,
		},
		Windows: &windows.Options{
			WebviewIsTransparent: false,
			WindowIsTranslucent:  false,
			BackdropType:         windows.None,
			Theme:                windows.Dark, // 原生沉浸式暗黑标题栏，杜绝刺眼白条
		},
	})

	if err != nil {
		println("主程序启动失败:", err.Error())
	}
}
