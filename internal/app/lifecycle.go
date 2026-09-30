package app

import (
	"context"
	"log"

	"github.com/wailsapp/wails/v3/pkg/application"
)

// ServiceStartup Wails v3 生命周期启动钩子
func (a *App) ServiceStartup(ctx context.Context, options application.ServiceOptions) error {
	a.mu.Lock()
	a.ctx = ctx
	a.mu.Unlock()

	// 幂等确保缓存目录存在并妥善记录日志
	if _, err := a.systemService.EnsureCacheDirectories(""); err != nil {
		log.Printf("[App] EnsureCacheDirectories 失败: %v", err)
	}

	// 仅主窗口启动 GSI 监听服务
	if !a.isOverlay {
		go func() {
			defer func() {
				if r := recover(); r != nil {
					log.Printf("[GsiService] 启动 goroutine panic 恢复: %v", r)
				}
			}()

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
	_ = a.ttsService.Close()
	return nil
}
