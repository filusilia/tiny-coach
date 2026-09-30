package tts

import (
	"context"
	"encoding/base64"
	"encoding/binary"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"aegis-coach/internal/events"
	"aegis-coach/internal/storage"

	"github.com/getcharzp/go-speech/tts/melotts"
)

// MeloTtsEngine MeloTTS 本地离线引擎（支持中英混合合成）
type MeloTtsEngine struct {
	mu             sync.Mutex
	inferMu        sync.Mutex
	storageManager *storage.Manager
	engine         *melotts.Engine
	isDownloading  bool
	progress       int
	lastError      string

	// 路径探测缓存：解决每次合成重复 stat 的问题（必须在 mu 锁保护下操作）
	pathsOnce   sync.Once
	dllPath     string
	modelPath   string
	lexiconPath string
	tokensPath  string
	pathsReady  bool
}

func NewMeloTtsEngine() *MeloTtsEngine {
	return &MeloTtsEngine{
		storageManager: storage.GetManager(),
	}
}

func (m *MeloTtsEngine) Name() string { return "melotts" }

// ---------------------------------------------------------------------------
// 路径探测（带缓存，必须在 m.mu 锁内调用）
// ---------------------------------------------------------------------------

// resolvePathsLocked 只执行一次路径探测，结果缓存（必须由调用方持有 m.mu）
func (m *MeloTtsEngine) resolvePathsLocked() {
	m.pathsOnce.Do(func() {
		m.dllPath, m.pathsReady = m.findDllPath()
		if m.pathsReady {
			m.modelPath, m.lexiconPath, m.tokensPath, m.pathsReady = m.findMeloPaths()
		}
	})
}

// RefreshPaths 强制重新探测路径（模型下载完成后或释放时调用）
func (m *MeloTtsEngine) RefreshPaths() {
	m.inferMu.Lock()
	defer m.inferMu.Unlock()
	m.mu.Lock()
	defer m.mu.Unlock()
	m.pathsOnce = sync.Once{}
	m.pathsReady = false
	if m.engine != nil {
		m.engine.Destroy()
		m.engine = nil // 显式销毁旧引擎 session 并释放 C 侧 ONNX 资源
	}
}

func (m *MeloTtsEngine) findDllPath() (string, bool) {
	candidates := []string{
		"lib/onnxruntime.dll",
	}
	if exe, err := os.Executable(); err == nil {
		candidates = append(candidates,
			filepath.Join(filepath.Dir(exe), "lib", "onnxruntime.dll"),
			filepath.Join(filepath.Dir(exe), "onnxruntime.dll"),
		)
	}
	baseCache := m.storageManager.GetCacheDirectory()
	candidates = append(candidates, filepath.Join(baseCache, "lib", "onnxruntime.dll"))

	for _, p := range candidates {
		abs, err := filepath.Abs(p)
		if err != nil {
			continue
		}
		if fi, err := os.Stat(abs); err == nil && !fi.IsDir() && fi.Size() > 0 {
			return abs, true
		}
	}
	return "", false
}

func (m *MeloTtsEngine) findMeloPaths() (string, string, string, bool) {
	// 候选目录：包含 MODELS_DIR 环境变量对应的路径，与 storage.Manager 保持一致
	candidates := []string{
		"melo_weights",
		filepath.Join(m.storageManager.GetCacheDirectory(), "models", "tts", "melotts"),
	}
	if modelsDir := strings.TrimSpace(os.Getenv("MODELS_DIR")); modelsDir != "" {
		candidates = append(candidates, filepath.Join(modelsDir, "models", "tts", "melotts"))
	}
	if exe, err := os.Executable(); err == nil {
		candidates = append(candidates,
			filepath.Join(filepath.Dir(exe), "melo_weights"),
			filepath.Join(filepath.Dir(exe), "models", "tts", "melotts"),
		)
	}

	for _, dir := range candidates {
		mPath := filepath.Join(dir, "model.onnx")
		lPath := filepath.Join(dir, "lexicon.txt")
		tPath := filepath.Join(dir, "tokens.txt")

		fiM, errM := os.Stat(mPath)
		fiL, errL := os.Stat(lPath)
		fiT, errT := os.Stat(tPath)

		if errM == nil && !fiM.IsDir() && fiM.Size() > 50*1024*1024 &&
			errL == nil && !fiL.IsDir() && fiL.Size() > 0 &&
			errT == nil && !fiT.IsDir() && fiT.Size() > 0 {
			absM, _ := filepath.Abs(mPath)
			absL, _ := filepath.Abs(lPath)
			absT, _ := filepath.Abs(tPath)
			return absM, absL, absT, true
		}
	}
	return "", "", "", false
}

// ---------------------------------------------------------------------------
// 状态查询
// ---------------------------------------------------------------------------

func (m *MeloTtsEngine) IsReady() bool {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.resolvePathsLocked()
	return m.pathsReady && !m.isDownloading
}

func (m *MeloTtsEngine) Status() EngineStatus {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.resolvePathsLocked()

	ready := m.pathsReady && !m.isDownloading
	msg := "MeloTTS 未就绪"
	switch {
	case m.isDownloading:
		msg = fmt.Sprintf("正在后台加载模型 (%d%%)", m.progress)
	case ready:
		msg = "MeloTTS 就绪"
	case m.lastError != "":
		msg = "MeloTTS 加载失败"
	}

	return EngineStatus{
		Engine:      "melotts",
		Ready:       ready,
		Downloading: m.isDownloading,
		Progress:    m.progress,
		Message:     msg,
		Error:       m.lastError,
	}
}

// ---------------------------------------------------------------------------
// 合成
// ---------------------------------------------------------------------------

func (m *MeloTtsEngine) Synthesize(ctx context.Context, text string, speed float64) (string, error) {
	cleanText := strings.TrimSpace(text)
	if cleanText == "" {
		return "", fmt.Errorf("待合成文本不能为空")
	}

	// 提前响应上下文取消
	select {
	case <-ctx.Done():
		return "", ctx.Err()
	default:
	}

	// speed 边界校验（MeloTTS 官方范围 0.5 ~ 2.0）
	spd := float32(speed)
	if spd < 0.5 || spd > 2.0 {
		spd = 1.0
	}

	m.mu.Lock()
	m.resolvePathsLocked()
	if !m.pathsReady {
		m.mu.Unlock()
		// 懒加载入口：首次需要时触发后台下载
		go m.EnsureReadyInBackground()
		return "", fmt.Errorf("MeloTTS 正在后台加载，请稍候")
	}

	// 引擎懒加载
	if m.engine == nil {
		log.Printf("[MeloTtsEngine] 装载引擎: model=%s", m.modelPath)
		cfg := melotts.Config{
			OnnxRuntimeLibPath: m.dllPath,
			ModelPath:          m.modelPath,
			LexiconPath:        m.lexiconPath,
			TokenPath:          m.tokensPath,
		}
		engine, err := melotts.NewEngine(cfg)
		if err != nil {
			m.lastError = err.Error()
			m.mu.Unlock()
			return "", fmt.Errorf("装载 MeloTTS 引擎失败: %w", err)
		}
		m.engine = engine
	}
	engine := m.engine
	m.mu.Unlock()

	// ONNX Runtime Session 非 goroutine 安全，使用独立的 inferMu 互斥排队，
	// 避免耗时推理（500ms~2s）长时间占用 m.mu 阻塞前端 IPC 状态查询 (GetTtsStatus)
	m.inferMu.Lock()
	start := time.Now()
	wavBytes, err := engine.SynthesizeToWav(cleanText, spd)
	m.inferMu.Unlock()

	if err != nil {
		m.mu.Lock()
		m.lastError = err.Error()
		m.mu.Unlock()
		return "", fmt.Errorf("MeloTTS 合成失败: %w", err)
	}

	// 注入 80ms 前置静音 buffer，彻底消除 Windows 声卡 DAC 唤醒与滤波平滑导致的首字音量偏小/吞字
	wavBytes = addLeadingSilenceToWav(wavBytes, 80*time.Millisecond)

	log.Printf("[MeloTtsEngine] 合成完毕: 耗时 %v, 大小 %d 字节", time.Since(start), len(wavBytes))

	return "data:audio/wav;base64," + base64.StdEncoding.EncodeToString(wavBytes), nil
}

// ---------------------------------------------------------------------------
// 后台下载（懒加载）
// ---------------------------------------------------------------------------

func (m *MeloTtsEngine) EnsureReadyInBackground() {
	m.mu.Lock()
	if m.isDownloading {
		m.mu.Unlock()
		return
	}
	m.resolvePathsLocked()
	if m.pathsReady {
		m.mu.Unlock()
		return
	}
	m.isDownloading = true
	m.progress = 0
	m.mu.Unlock()

	defer func() {
		m.mu.Lock()
		m.isDownloading = false
		m.mu.Unlock()
	}()

	targetDir := filepath.Join(m.storageManager.GetCacheDirectory(), "models", "tts", "melotts")
	if err := os.MkdirAll(targetDir, 0755); err != nil {
		log.Printf("[MeloTtsEngine] 创建模型目录失败: %v", err)
		m.setError(err.Error())
		return
	}

	files := []struct {
		relPath string
		url     string
	}{
		{"tokens.txt", "https://www.modelscope.cn/models/getcharzp/go-speech/resolve/master/melo_weights/tokens.txt"},
		{"lexicon.txt", "https://www.modelscope.cn/models/getcharzp/go-speech/resolve/master/melo_weights/lexicon.txt"},
		{"model.onnx", "https://www.modelscope.cn/models/getcharzp/go-speech/resolve/master/melo_weights/model.onnx"},
	}

	client := &http.Client{Timeout: 15 * time.Minute}
	total := len(files)
	allExisted := true

	for i, item := range files {
		destPath := filepath.Join(targetDir, item.relPath)
		if fi, err := os.Stat(destPath); err == nil && fi.Size() > 0 {
			continue
		}

		allExisted = false
		m.updateProgress(i*100/total, "正在下载: "+item.relPath)

		if err := downloadFile(client, item.url, destPath); err != nil {
			m.setError(fmt.Sprintf("下载 %s 失败: %v", item.relPath, err))
			return
		}

		m.updateProgress((i+1)*100/total, "已完成: "+item.relPath)
	}

	if allExisted {
		m.updateProgress(100, "就绪")
	}

	// 下载完成，刷新路径并预加载引擎
	log.Printf("[MeloTtsEngine] 模型下载完毕，开始预加载引擎")
	m.RefreshPaths()

	m.mu.Lock()
	m.resolvePathsLocked()
	if m.pathsReady {
		cfg := melotts.Config{
			OnnxRuntimeLibPath: m.dllPath,
			ModelPath:          m.modelPath,
			LexiconPath:        m.lexiconPath,
			TokenPath:          m.tokensPath,
		}
		if engine, err := melotts.NewEngine(cfg); err == nil {
			m.engine = engine
			log.Printf("[MeloTtsEngine] 引擎预加载完成")
		} else {
			m.lastError = err.Error()
			log.Printf("[MeloTtsEngine] 引擎预加载失败: %v", err)
		}
	}
	m.mu.Unlock()

	m.updateProgress(100, "就绪")
}

func (m *MeloTtsEngine) updateProgress(p int, msg string) {
	m.mu.Lock()
	m.progress = p
	m.mu.Unlock()
	events.Emit("tts_download_progress", map[string]any{
		"engine":   "melotts",
		"progress": p,
		"message":  msg,
	})
}

func (m *MeloTtsEngine) setError(errMsg string) {
	m.mu.Lock()
	m.lastError = errMsg
	m.mu.Unlock()
	events.Emit("tts_download_progress", map[string]any{
		"engine":   "melotts",
		"progress": 0,
		"message":  "加载失败",
		"error":    errMsg,
	})
}

// Close 显式销毁 ONNX Runtime Session 与 C++ 资源
func (m *MeloTtsEngine) Close() error {
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.engine != nil {
		m.engine.Destroy()
		m.engine = nil
	}
	return nil
}

// downloadFile 下载到 .part 临时文件后原子重命名，重命名失败时彻底清理 .part 临时文件
func downloadFile(client *http.Client, url, destPath string) error {
	req, err := http.NewRequest(http.MethodGet, url, nil)
	if err != nil {
		return err
	}
	req.Header.Set("User-Agent", "AegisCoach-Client/1.0")

	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("HTTP %d", resp.StatusCode)
	}

	tmpPath := destPath + ".part"
	out, err := os.Create(tmpPath)
	if err != nil {
		return err
	}

	if _, err := io.Copy(out, resp.Body); err != nil {
		out.Close()
		_ = os.Remove(tmpPath)
		return err
	}
	out.Close()

	_ = os.Remove(destPath)
	if err := os.Rename(tmpPath, destPath); err != nil {
		_ = os.Remove(tmpPath)
		return fmt.Errorf("原子重命名失败: %w", err)
	}
	return nil
}

// addLeadingSilenceToWav 在 WAV 音频头部注入纯净静音缓冲，消除硬件 DAC 唤醒与系统音频建立导致的首字音量偏小/吞音
func addLeadingSilenceToWav(wavData []byte, silenceDuration time.Duration) []byte {
	if len(wavData) < 44 || string(wavData[0:4]) != "RIFF" || string(wavData[8:12]) != "WAVE" {
		return wavData
	}

	var channels uint16 = 1
	var sampleRate uint32 = 44100
	var bitsPerSample uint16 = 16

	idx := 12
	for idx+8 <= len(wavData) {
		chunkID := string(wavData[idx : idx+4])
		chunkSize := binary.LittleEndian.Uint32(wavData[idx+4 : idx+8])
		if chunkID == "fmt " && chunkSize >= 16 && idx+8+16 <= len(wavData) {
			channels = binary.LittleEndian.Uint16(wavData[idx+8+2 : idx+8+4])
			sampleRate = binary.LittleEndian.Uint32(wavData[idx+8+4 : idx+8+8])
			bitsPerSample = binary.LittleEndian.Uint16(wavData[idx+8+14 : idx+8+16])
			break
		}
		idx += 8 + int(chunkSize)
	}

	bytesPerSample := uint32(bitsPerSample / 8)
	if bytesPerSample == 0 {
		bytesPerSample = 2
	}
	bytesPerSec := sampleRate * uint32(channels) * bytesPerSample
	silenceBytesCount := int(float64(bytesPerSec) * silenceDuration.Seconds())
	if silenceBytesCount <= 0 {
		return wavData
	}
	blockAlign := int(channels * (bitsPerSample / 8))
	if blockAlign > 0 {
		silenceBytesCount = (silenceBytesCount / blockAlign) * blockAlign
	}

	dataIdx := -1
	idx = 12
	for idx+8 <= len(wavData) {
		chunkID := string(wavData[idx : idx+4])
		chunkSize := binary.LittleEndian.Uint32(wavData[idx+4 : idx+8])
		if chunkID == "data" {
			dataIdx = idx
			break
		}
		idx += 8 + int(chunkSize)
	}

	if dataIdx == -1 {
		return wavData
	}

	silence := make([]byte, silenceBytesCount)
	newWav := make([]byte, 0, len(wavData)+silenceBytesCount)

	newWav = append(newWav, wavData[:dataIdx+8]...)
	newWav = append(newWav, silence...)
	newWav = append(newWav, wavData[dataIdx+8:]...)

	oldDataSize := binary.LittleEndian.Uint32(wavData[dataIdx+4 : dataIdx+8])
	binary.LittleEndian.PutUint32(newWav[dataIdx+4:dataIdx+8], oldDataSize+uint32(silenceBytesCount))

	oldRiffSize := binary.LittleEndian.Uint32(wavData[4:8])
	binary.LittleEndian.PutUint32(newWav[4:8], oldRiffSize+uint32(silenceBytesCount))

	return newWav
}
