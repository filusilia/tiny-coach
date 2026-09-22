package dota

import (
	"context"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"sync"
	"time"
)

// maxGsiBodySize GSI 快照大小上限（正常 < 100KB），防止异常请求撑爆内存
const maxGsiBodySize = 1 << 20 // 1 MiB

// GsiReceiver Dota 2 Game State Integration (GSI) 本地 HTTP 接收器
// 监听 Valve 客户端向本机 4444 端口推送的高频 JSON 快照 (10Hz)
type GsiReceiver struct {
	mu            sync.RWMutex
	latestData    string // 直接存 string，避免 GetLatestSnapshot 反复转换
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

// Start 启动本地 HTTP GSI 单消费者接收服务（阻塞，建议作为 Goroutine 启动）
func (r *GsiReceiver) Start() error {
	mux := http.NewServeMux()
	mux.HandleFunc("/", r.handleGsiPayload)

	srv := &http.Server{
		Addr:         fmt.Sprintf("127.0.0.1:%d", r.port),
		Handler:      mux,
		ReadTimeout:  5 * time.Second,
		WriteTimeout: 5 * time.Second,
	}

	// 加锁发布 server，保证 Stop 能安全看到
	r.mu.Lock()
	r.server = srv
	r.mu.Unlock()

	log.Printf("[GSI] 正在监听 127.0.0.1:%d 本机 Valve GSI 数据流...", r.port)
	if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
		return fmt.Errorf("GSI 监听启动失败: %w", err)
	}
	return nil
}

// Stop 优雅终止 GSI HTTP 监听服务（幂等，可重复调用）
func (r *GsiReceiver) Stop() error {
	r.mu.Lock()
	srv := r.server
	r.server = nil
	r.mu.Unlock()

	if srv == nil {
		return nil
	}

	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	return srv.Shutdown(ctx)
}

// handleGsiPayload 消费来自 Dota 2 客户端的 POST HTTP 实体
func (r *GsiReceiver) handleGsiPayload(w http.ResponseWriter, req *http.Request) {
	if req.Method != http.MethodPost {
		http.Error(w, "Only POST allowed", http.StatusMethodNotAllowed)
		return
	}

	// 关键：defer 必须在 ReadAll 之前注册，否则 ReadAll 出错会泄漏 Body
	defer func() { _ = req.Body.Close() }()

	// 限制读取大小，超限自动 413
	limited := http.MaxBytesReader(w, req.Body, maxGsiBodySize)
	body, err := io.ReadAll(limited)
	if err != nil {
		http.Error(w, "Read body failed", http.StatusBadRequest)
		return
	}

	if len(body) == 0 {
		w.WriteHeader(http.StatusOK)
		return
	}

	// 一次性转 string，同时避免保留 body 底层数组
	payload := string(body)

	r.mu.Lock()
	r.latestData = payload
	r.lastHeartbeat = time.Now()
	r.mu.Unlock()

	// 锁外派发事件，避免回调阻塞或死锁
	if r.emitEvent != nil {
		r.emitEvent("gsi_data", payload)
	}

	w.WriteHeader(http.StatusOK)
	_, _ = w.Write([]byte("OK"))
}

// GetLatestSnapshot 线程安全读取最新一帧 GSI 游戏快照 JSON 字符串
func (r *GsiReceiver) GetLatestSnapshot() string {
	r.mu.RLock()
	defer r.mu.RUnlock()
	if r.latestData == "" {
		return `{"connected": false}`
	}
	return r.latestData
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
