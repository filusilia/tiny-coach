package dota

import (
	"fmt"
	"io"
	"net/http"
	"sync"
	"time"
)

// GsiReceiver Dota 2 Game State Integration (GSI) 本地 HTTP 接收器
// 监听 Valve 客户端向本机 4444 端口推送的高频 JSON 快照 (10Hz)
type GsiReceiver struct {
	mu            sync.RWMutex
	latestData    []byte
	lastHeartbeat time.Time
	port          int
	server        *http.Server
	emitEvent     func(name string, data ...any)
}

// NewGsiReceiver 构造 GSI 接收器实例
// 参数 emitEvent: 事件广播钩子函数，用于向前端电竞 HUD 推送最新快照
func NewGsiReceiver(port int, emitEvent func(name string, data ...any)) *GsiReceiver {
	if port <= 0 {
		port = 4444
	}
	return &GsiReceiver{
		port:      port,
		emitEvent: emitEvent,
	}
}

// Start 启动本地 HTTP GSI 单消费者接收服务 (阻塞或作为 Goroutine 启动)
func (r *GsiReceiver) Start() error {
	mux := http.NewServeMux()
	mux.HandleFunc("/", r.handleGsiPayload)

	r.server = &http.Server{
		Addr:         fmt.Sprintf("127.0.0.1:%d", r.port),
		Handler:      mux,
		ReadTimeout:  5 * time.Second,
		WriteTimeout: 5 * time.Second,
	}

	println(fmt.Sprintf("[GSI] 正在监听 127.0.0.1:%d 本机 Valve GSI 数据流...", r.port))
	if err := r.server.ListenAndServe(); err != nil && err != http.ErrServerClosed {
		return fmt.Errorf("GSI 监听启动失败: %w", err)
	}
	return nil
}

// Stop 优雅终止 GSI HTTP 监听服务
func (r *GsiReceiver) Stop() error {
	if r.server != nil {
		return r.server.Close()
	}
	return nil
}

// handleGsiPayload 消费来自 Dota 2 客户端的 POST HTTP 实体
func (r *GsiReceiver) handleGsiPayload(w http.ResponseWriter, req *http.Request) {
	if req.Method != http.MethodPost {
		http.Error(w, "Only POST allowed", http.StatusMethodNotAllowed)
		return
	}

	body, err := io.ReadAll(req.Body)
	if err != nil {
		http.Error(w, "Read body failed", http.StatusBadRequest)
		return
	}
	defer req.Body.Close()

	if len(body) == 0 {
		w.WriteHeader(http.StatusOK)
		return
	}

	r.mu.Lock()
	r.latestData = body
	r.lastHeartbeat = time.Now()
	r.mu.Unlock()

	// 通过事件总线向前端电竞 HUD 派发原始 GSI 快照
	if r.emitEvent != nil {
		r.emitEvent("gsi_data", string(body))
	}

	w.WriteHeader(http.StatusOK)
	_, _ = w.Write([]byte("OK"))
}

// GetLatestSnapshot 线程安全读取最新一帧 GSI 游戏快照 JSON 字符串
func (r *GsiReceiver) GetLatestSnapshot() string {
	r.mu.RLock()
	defer r.mu.RUnlock()
	if len(r.latestData) == 0 {
		return `{"connected": false}`
	}
	return string(r.latestData)
}

// IsConnected 查询最近是否收到活跃的 GSI 心跳 (3 秒内视为连接在线)
func (r *GsiReceiver) IsConnected() bool {
	r.mu.RLock()
	defer r.mu.RUnlock()
	if r.lastHeartbeat.IsZero() {
		return false
	}
	return time.Since(r.lastHeartbeat) < 3*time.Second
}
