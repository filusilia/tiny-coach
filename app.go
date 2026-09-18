package main

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"strings"
	"sync"
	"time"

	"github.com/wailsapp/wails/v2/pkg/runtime"
)

var (
	overlayProcessMu sync.Mutex
	overlayCmd       *exec.Cmd
)

// App struct
type App struct {
	ctx           context.Context
	isOverlay     bool
	latestGsiMu   sync.RWMutex
	latestGsiData []byte
}

// NewApp creates a new App application struct
func NewApp(isOverlay bool) *App {
	return &App{
		isOverlay: isOverlay,
	}
}

// startup is called when the app starts.
func (a *App) startup(ctx context.Context) {
	a.ctx = ctx

	// 仅主程序启动 4444 端口 GSI 接收服务，防止端口冲突
	if !a.isOverlay {
		go a.startGsiReceiver(4444)
	}
}

// shutdown 在主程序关闭时被调用，彻底注销并杀死悬浮窗子进程（铁律 12）
func (a *App) shutdown(ctx context.Context) {
	if !a.isOverlay {
		overlayProcessMu.Lock()
		if overlayCmd != nil && overlayCmd.Process != nil {
			_ = overlayCmd.Process.Kill()
			overlayCmd = nil
		}
		overlayProcessMu.Unlock()
	}
}

// IsOverlayMode 供前端判断当前是否为独立脱离的悬浮窗实例
func (a *App) IsOverlayMode() bool {
	return a.isOverlay
}

// ToggleOverlayWindow 主程序调用以开启/关闭独立桌面悬浮窗
func (a *App) ToggleOverlayWindow() bool {
	if a.isOverlay {
		return false
	}

	overlayProcessMu.Lock()
	defer overlayProcessMu.Unlock()

	// 若已运行，则杀死关闭
	if overlayCmd != nil && overlayCmd.Process != nil {
		_ = overlayCmd.Process.Kill()
		overlayCmd = nil
		if a.ctx != nil {
			runtime.EventsEmit(a.ctx, "overlay_state_change", false)
		}
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

	// 异步监听子进程生命周期
	go func() {
		_ = cmd.Wait()
		overlayProcessMu.Lock()
		if overlayCmd == cmd {
			overlayCmd = nil
		}
		overlayProcessMu.Unlock()
		if a.ctx != nil {
			runtime.EventsEmit(a.ctx, "overlay_state_change", false)
		}
	}()

	if a.ctx != nil {
		runtime.EventsEmit(a.ctx, "overlay_state_change", true)
	}
	return true
}

// IsOverlayRunning 返回独立悬浮窗是否正在运行
func (a *App) IsOverlayRunning() bool {
	overlayProcessMu.Lock()
	defer overlayProcessMu.Unlock()
	return overlayCmd != nil && overlayCmd.Process != nil
}

// GetLatestGsiSnapshot 供前端主动获取最新快照
func (a *App) GetLatestGsiSnapshot() string {
	a.latestGsiMu.RLock()
	defer a.latestGsiMu.RUnlock()
	if len(a.latestGsiData) == 0 {
		return `{"connected": false}`
	}
	return string(a.latestGsiData)
}

// 启动 GSI HTTP 监听服务 (支持接收 POST 快照，并支持 GET /snapshot 分发给悬浮窗)
func (a *App) startGsiReceiver(port int) {
	mux := http.NewServeMux()

	// 1. 接收 Dota 2 客户端 GSI POST
	mux.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		// 支持悬浮窗通过 GET / 或 GET /snapshot 读取
		if r.Method == http.MethodGet {
			w.Header().Set("Content-Type", "application/json")
			w.Header().Set("Access-Control-Allow-Origin", "*")
			w.WriteHeader(http.StatusOK)
			a.latestGsiMu.RLock()
			if len(a.latestGsiData) > 0 {
				w.Write(a.latestGsiData)
			} else {
				w.Write([]byte(`{"connected": false}`))
			}
			a.latestGsiMu.RUnlock()
			return
		}

		if r.Method != http.MethodPost {
			w.WriteHeader(http.StatusOK)
			w.Write([]byte("AegisCoach GSI Endpoint Ready"))
			return
		}

		body, err := io.ReadAll(r.Body)
		if err != nil {
			w.WriteHeader(http.StatusBadRequest)
			return
		}
		defer r.Body.Close()

		// 快速响应 200 OK 避免阻塞 Dota 2 客户端
		w.WriteHeader(http.StatusOK)

		a.latestGsiMu.Lock()
		a.latestGsiData = body
		a.latestGsiMu.Unlock()

		// 通过 Wails 事件总线推送到前端 Vue 3
		if a.ctx != nil {
			var raw map[string]interface{}
			if err := json.Unmarshal(body, &raw); err == nil {
				runtime.EventsEmit(a.ctx, "dota_gsi_update", raw)
			}
		}
	})

	server := &http.Server{
		Addr:         fmt.Sprintf("127.0.0.1:%d", port),
		Handler:      mux,
		ReadTimeout:  2 * time.Second,
		WriteTimeout: 2 * time.Second,
	}

	for {
		if err := server.ListenAndServe(); err != nil && !strings.Contains(err.Error(), "Server closed") {
			time.Sleep(1 * time.Second)
		}
	}
}
