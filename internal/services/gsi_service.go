package services

import (
	"context"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"sync"
	"time"

	"github.com/wailsapp/wails/v3/pkg/application"
)

const maxGsiPayloadSize = 1 << 20 // 1 MiB

// GsiService Dota 2 战局 GSI 接收器服务 (10Hz 本地 HTTP 单消费者流水线)
type GsiService struct {
	mu            sync.RWMutex
	latestData    string
	lastHeartbeat time.Time
	port          int
	server        *http.Server
}

var (
	gsiServiceInstance *GsiService
	gsiServiceOnce     sync.Once
)

// GetGsiService 获取 GsiService 单例
func GetGsiService(port int) *GsiService {
	gsiServiceOnce.Do(func() {
		if port <= 0 {
			port = 4444
		}
		gsiServiceInstance = &GsiService{
			port: port,
		}
	})
	return gsiServiceInstance
}

// Start 启动 4444 端口原生 HTTP GSI 单通道监听服务
func (s *GsiService) Start() error {
	mux := http.NewServeMux()
	mux.HandleFunc("/", s.handleGsiPayload)

	srv := &http.Server{
		Addr:         fmt.Sprintf("127.0.0.1:%d", s.port),
		Handler:      mux,
		ReadTimeout:  5 * time.Second,
		WriteTimeout: 5 * time.Second,
	}

	s.mu.Lock()
	s.server = srv
	s.mu.Unlock()

	log.Printf("[GsiService] 正在监听 127.0.0.1:%d 本机 Valve GSI 数据流...", s.port)
	if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
		return fmt.Errorf("GSI 监听启动失败: %w", err)
	}
	return nil
}

// Stop 优雅关闭 GSI 服务
func (s *GsiService) Stop() error {
	s.mu.Lock()
	srv := s.server
	s.server = nil
	s.mu.Unlock()

	if srv == nil {
		return nil
	}

	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	return srv.Shutdown(ctx)
}

// GetLatestSnapshot 线程安全读取最新一帧 GSI 游戏快照 JSON 字符串
func (s *GsiService) GetLatestSnapshot() string {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.latestData
}

// IsRunning 检查 GSI 监听服务当前是否处于活跃运行状态
func (s *GsiService) IsRunning() bool {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.server != nil
}

// handleGsiPayload 消费来自 Valve Dota 2 客户端的 POST HTTP 实体
func (s *GsiService) handleGsiPayload(w http.ResponseWriter, req *http.Request) {
	if req.Method != http.MethodPost {
		http.Error(w, "Only POST allowed", http.StatusMethodNotAllowed)
		return
	}

	// 关键规则：defer 必须在 ReadAll 之前注册，防止异常时 Body 泄漏
	defer func() { _ = req.Body.Close() }()

	// 限制读取大小，超限自动 413
	limited := http.MaxBytesReader(w, req.Body, maxGsiPayloadSize)
	body, err := io.ReadAll(limited)
	if err != nil {
		http.Error(w, "Read body failed", http.StatusBadRequest)
		return
	}

	if len(body) == 0 {
		w.WriteHeader(http.StatusOK)
		return
	}

	payload := string(body)

	s.mu.Lock()
	s.latestData = payload
	s.lastHeartbeat = time.Now()
	s.mu.Unlock()

	// 统一派发标准 "gsi_data" 与兼容 "dota_gsi_update" 事件到 Wails 事件总线 (高频推送规范)
	if app := application.Get(); app != nil && app.Event != nil {
		app.Event.Emit("gsi_data", payload)
		app.Event.Emit("dota_gsi_update", payload)
	}

	w.WriteHeader(http.StatusOK)
	_, _ = w.Write([]byte("OK"))
}
