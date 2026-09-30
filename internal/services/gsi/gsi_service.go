package gsi

import (
	"context"
	"errors"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"sync"
	"time"

	"aegis-coach/internal/events"
)

const maxGsiPayloadSize = 1 << 20

// GsiService Dota 2 战局 GSI 接收器服务 (10Hz 本地 HTTP 单消费者流水线)
type GsiService struct {
	mu            sync.RWMutex
	latestData    string
	lastHeartbeat time.Time
	listening     bool
	port          int
	server        *http.Server
}

var (
	gsiServiceInstance *GsiService
	gsiServiceOnce     sync.Once
)

func GetGsiService(port int) *GsiService {
	gsiServiceOnce.Do(func() {
		if port <= 0 {
			port = 4444
		}
		gsiServiceInstance = &GsiService{port: port}
	})
	return gsiServiceInstance
}

// SetPort 在服务未运行时动态更新监听端口
func (s *GsiService) SetPort(port int) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if port > 0 && port < 65536 && !s.listening {
		s.port = port
	}
}

// GetPort 获取当前配置的端口
func (s *GsiService) GetPort() int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.port
}

func (s *GsiService) Start() error {
	s.mu.Lock()
	if s.listening {
		s.mu.Unlock()
		return nil
	}
	port := s.port
	s.mu.Unlock()

	addr := fmt.Sprintf("127.0.0.1:%d", port)
	listener, err := net.Listen("tcp", addr)
	if err != nil {
		return fmt.Errorf("GSI 端口 [%d] 监听绑定失败: %w", port, err)
	}

	mux := http.NewServeMux()
	mux.HandleFunc("/", s.handleGsiPayload)

	srv := &http.Server{
		Addr:         addr,
		Handler:      mux,
		ReadTimeout:  5 * time.Second,
		WriteTimeout: 5 * time.Second,
	}

	s.mu.Lock()
	s.server = srv
	s.listening = true
	s.mu.Unlock()

	log.Printf("[GsiService] 成功监听 %s 本机 Valve GSI 数据流...", addr)
	if err := srv.Serve(listener); err != nil && !errors.Is(err, http.ErrServerClosed) {
		s.mu.Lock()
		s.listening = false
		s.mu.Unlock()
		return fmt.Errorf("GSI 监听服务退出: %w", err)
	}
	return nil
}

func (s *GsiService) Stop() error {
	s.mu.Lock()
	srv := s.server
	s.server = nil
	s.listening = false
	s.mu.Unlock()

	if srv == nil {
		return nil
	}

	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	return srv.Shutdown(ctx)
}

// GetLatestSnapshot 返回最新快照，空态返回明确的 disconnected 标记
func (s *GsiService) GetLatestSnapshot() string {
	s.mu.RLock()
	defer s.mu.RUnlock()
	if s.latestData == "" {
		return `{"connected": false}`
	}
	return s.latestData
}

// IsRunning 判断 GSI 监听是否活跃 (基于 listening 状态，避免假阳性)
func (s *GsiService) IsRunning() bool {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.listening
}

// IsConnected 判断最近 3 秒内是否收到活跃心跳
func (s *GsiService) IsConnected() bool {
	s.mu.RLock()
	defer s.mu.RUnlock()
	if s.lastHeartbeat.IsZero() {
		return false
	}
	return time.Since(s.lastHeartbeat) < 3*time.Second
}

func (s *GsiService) handleGsiPayload(w http.ResponseWriter, req *http.Request) {
	if req.Method != http.MethodPost {
		http.Error(w, "Only POST allowed", http.StatusMethodNotAllowed)
		return
	}
	defer func() { _ = req.Body.Close() }()

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

	// 统一事件名为 gsi_data，使用 internal/events.Emit
	events.Emit("gsi_data", payload)

	w.WriteHeader(http.StatusOK)
	_, _ = w.Write([]byte("OK"))
}
