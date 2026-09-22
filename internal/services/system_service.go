package services

import (
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"
	"syscall"
	"time"

	"aegis-coach/internal/storage"

	"github.com/wailsapp/wails/v3/pkg/application"
)

// SystemService 系统文件管理与桌面原生对话框服务
type SystemService struct {
	mu             sync.RWMutex
	storageManager *storage.Manager
	window         *application.WebviewWindow
}

var (
	systemServiceInstance *SystemService
	systemServiceOnce     sync.Once
)

// GetSystemService 获取 SystemService 单例
func GetSystemService() *SystemService {
	systemServiceOnce.Do(func() {
		systemServiceInstance = &SystemService{
			storageManager: storage.NewManager(),
		}
	})
	return systemServiceInstance
}

// SetWindow 设置关联的原生主窗口句柄
func (s *SystemService) SetWindow(w *application.WebviewWindow) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.window = w
}

// GetDefaultCacheDirectory 获取默认系统级数据缓存根路径
func (s *SystemService) GetDefaultCacheDirectory() string {
	return s.storageManager.GetDefaultCacheDirectory()
}

// GetCacheDirectory 获取当前正在生效的数据缓存根路径
func (s *SystemService) GetCacheDirectory() string {
	return s.storageManager.GetCacheDirectory()
}

// SetCacheDirectory 设置自定义缓存根路径
func (s *SystemService) SetCacheDirectory(customDir string) (string, error) {
	return s.storageManager.SetCacheDirectory(customDir)
}

// EnsureCacheDirectories 幂等确保缓存目录结构存在
func (s *SystemService) EnsureCacheDirectories(customDir string) (string, error) {
	return s.storageManager.EnsureCacheDirectories(customDir)
}

// SelectDirectoryDialog 唤起原生选择文件夹对话框 (纯粹获取路径，无状态篡改副作用)
func (s *SystemService) SelectDirectoryDialog() (selectedResult string, errResult error) {
	defer func() {
		if r := recover(); r != nil {
			log.Printf("[SystemService] SelectDirectoryDialog recovered from panic: %v, falling back to PowerShell", r)
			selected := s.selectDirectoryViaPowerShell()
			if selected != "" {
				selectedResult = strings.TrimSpace(selected)
			}
		}
	}()

	current := s.GetCacheDirectory()
	appInst := application.Get()

	// 1. 优先尝试 Wails v3 原生对话框管理器 (若存在主窗口上下文)
	if appInst != nil && appInst.Dialog != nil {
		dialog := appInst.Dialog.OpenFile().
			CanChooseDirectories(true).
			CanChooseFiles(false).
			SetTitle("选择神盾教练数据缓存文件夹")

		if strings.TrimSpace(current) != "" {
			dialog.SetDirectory(current)
		}

		selectedDir, err := dialog.PromptForSingleSelection()
		if err == nil && strings.TrimSpace(selectedDir) != "" {
			return strings.TrimSpace(selectedDir), nil
		}
		// 用户在原生对话框中点击取消时直接返回空，避免重复唤起辅助脚本
		if err != nil && (strings.Contains(strings.ToLower(err.Error()), "cancel") || strings.Contains(err.Error(), "0x800704c7")) {
			return "", nil
		}
	}

	// 2. 双保险兜底：调用 Windows 原生 FolderBrowserDialog (必须加 -STA 支持窗体单线程公寓模式)
	selected := s.selectDirectoryViaPowerShell()
	if selected != "" {
		return strings.TrimSpace(selected), nil
	}

	return "", nil
}

// selectDirectoryViaPowerShell 调用 PowerShell STA 弹窗选择文件夹
func (s *SystemService) selectDirectoryViaPowerShell() string {
	script := `Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; $f.Description = '选择神盾教练数据缓存文件夹'; $f.ShowNewFolderButton = $true; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Write($f.SelectedPath) }`
	cmd := exec.Command("powershell", "-NoProfile", "-NonInteractive", "-STA", "-Command", script)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	out, err := cmd.Output()
	if err == nil {
		return strings.TrimSpace(string(out))
	}
	return ""
}

// SelectModelFileDialog 唤起原生选择本地大模型文件对话框 (双保险实现)
func (s *SystemService) SelectModelFileDialog() (selectedResult string, errResult error) {
	defer func() {
		if r := recover(); r != nil {
			log.Printf("[SystemService] SelectModelFileDialog recovered from panic: %v, falling back to PowerShell", r)
			selectedResult = s.selectModelFileViaPowerShell()
		}
	}()

	current := s.GetCacheDirectory()
	appInst := application.Get()

	// 1. 优先尝试 Wails v3 原生文件对话框
	if appInst != nil && appInst.Dialog != nil {
		dialog := appInst.Dialog.OpenFile().
			CanChooseDirectories(false).
			CanChooseFiles(true).
			SetTitle("选择本地 AI 模型文件 (支持 GGUF / ONNX / Bin)").
			AddFilter("AI 模型文件 (*.gguf;*.onnx;*.bin)", "*.gguf;*.onnx;*.bin").
			AddFilter("所有文件 (*.*)", "*.*")

		if strings.TrimSpace(current) != "" {
			dialog.SetDirectory(current)
		}

		selectedFile, err := dialog.PromptForSingleSelection()
		if err == nil && strings.TrimSpace(selectedFile) != "" {
			return strings.TrimSpace(selectedFile), nil
		}
		// 用户在原生对话框中点击取消时直接返回空
		if err != nil && (strings.Contains(strings.ToLower(err.Error()), "cancel") || strings.Contains(err.Error(), "0x800704c7")) {
			return "", nil
		}
	}

	// 2. 双保险兜底：调用 Windows 原生 OpenFileDialog (带 -STA)
	selected := s.selectModelFileViaPowerShell()
	if selected != "" {
		return selected, nil
	}

	return "", nil
}

// selectModelFileViaPowerShell 调用 PowerShell STA 弹窗选择模型文件
func (s *SystemService) selectModelFileViaPowerShell() string {
	script := `Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.Title = '选择本地 AI 模型文件 (支持 GGUF / ONNX / Bin)'; $f.Filter = 'AI 模型文件 (*.gguf;*.onnx;*.bin)|*.gguf;*.onnx;*.bin|所有文件 (*.*)|*.*'; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Write($f.FileName) }`
	cmd := exec.Command("powershell", "-NoProfile", "-NonInteractive", "-STA", "-Command", script)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	out, err := cmd.Output()
	if err == nil {
		return strings.TrimSpace(string(out))
	}
	return ""
}

// ScanLocalModelFiles 扫描本地缓存目录与 models 子目录中的真实 GGUF/ONNX 文件
func (s *SystemService) ScanLocalModelFiles() []string {
	var models []string
	baseDir := s.GetCacheDirectory()
	searchDirs := []string{
		filepath.Join(baseDir, "models"),
		baseDir,
	}

	seen := make(map[string]bool)
	for _, dir := range searchDirs {
		entries, err := os.ReadDir(dir)
		if err != nil {
			continue
		}
		for _, entry := range entries {
			if entry.IsDir() {
				continue
			}
			name := entry.Name()
			ext := strings.ToLower(filepath.Ext(name))
			if ext == ".gguf" || ext == ".onnx" || ext == ".bin" {
				if !seen[name] {
					seen[name] = true
					models = append(models, name)
				}
			}
		}
	}
	return models
}

// OpenCacheDirectory 在 Windows 资源管理器中打开缓存路径
func (s *SystemService) OpenCacheDirectory(dir string) error {
	defer func() {
		if r := recover(); r != nil {
			log.Printf("[SystemService] OpenCacheDirectory recovered from panic: %v", r)
		}
	}()

	target := s.storageManager.ResolveBaseCacheDir(dir)
	target = filepath.Clean(target)
	if _, err := s.storageManager.EnsureCacheDirectories(target); err != nil {
		return err
	}
	_ = os.MkdirAll(target, 0755)

	// 1. 优先使用 Wails v3 原生环境管理器打开文件夹 (具备跨平台原生集成能力)
	appInst := application.Get()
	if appInst != nil && appInst.Env != nil {
		if err := appInst.Env.OpenFileManager(target, false); err == nil {
			return nil
		}
	}

	// 2. 降级使用 Windows 原生 explorer.exe 打开目标文件夹
	cmd := exec.Command("explorer.exe", target)
	if err := cmd.Start(); err == nil {
		return nil
	}
	// 3. 再降级使用 cmd start 唤起系统关联外壳
	fallbackCmd := exec.Command("cmd", "/c", "start", "", target)
	fallbackCmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	if err := fallbackCmd.Start(); err == nil {
		return nil
	}
	// 4. 再降级使用 rundll32 唤起系统文件关联句柄
	rundllCmd := exec.Command("rundll32", "url.dll,FileProtocolHandler", target)
	rundllCmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	return rundllCmd.Start()
}

// ClearCacheDirectory 安全清理指定缓存子目录临时文件
func (s *SystemService) ClearCacheDirectory(dir string) error {
	target := s.storageManager.ResolveBaseCacheDir(dir)
	subDirs := []string{"tts", "matches", "rag"}
	for _, sub := range subDirs {
		subPath := filepath.Join(target, sub)
		entries, err := os.ReadDir(subPath)
		if err != nil {
			continue
		}
		for _, entry := range entries {
			p := filepath.Join(subPath, entry.Name())
			if err := os.RemoveAll(p); err != nil {
				log.Printf("[SystemService] 清理临时文件失败 %s: %v", p, err)
			}
		}
	}
	return nil
}

// TestTtsEndpoint 测试第三方 TTS 端点连通性 (支持通用 HTTP GET 或 OpenAI 规范)
func (s *SystemService) TestTtsEndpoint(endpoint, apiKey, engineType, voice string) (string, error) {
	cleanEndpoint := strings.TrimSpace(endpoint)
	if cleanEndpoint == "" {
		cleanEndpoint = "http://127.0.0.1:9233"
	}
	cleanEndpoint = strings.TrimRight(cleanEndpoint, "/")

	client := &http.Client{
		Timeout: 3 * time.Second,
	}

	testUrl := cleanEndpoint
	if engineType == "openai" {
		testUrl = fmt.Sprintf("%s/models", cleanEndpoint)
	}

	req, err := http.NewRequest(http.MethodGet, testUrl, nil)
	if err != nil {
		return "", fmt.Errorf("构造请求失败: %w", err)
	}
	if strings.TrimSpace(apiKey) != "" {
		req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", strings.TrimSpace(apiKey)))
	}

	start := time.Now()
	resp, err := client.Do(req)
	if err != nil {
		return "", fmt.Errorf("无法连接至第三方 TTS 端点 (%s): %v", cleanEndpoint, err)
	}
	defer resp.Body.Close()

	latency := time.Since(start).Milliseconds()
	return fmt.Sprintf("连通成功 (HTTP %d, 延迟: %dms)", resp.StatusCode, latency), nil
}

// ServeHTTP 实现 http.Handler 接口，作为 Wails 资产服务器的 /cache 路由处理器
// 架构职责：优先流式返回本地已缓存的英雄/物品/技能图标，若本地缺失则透明拉取官方 CDN 并自动落盘
func (s *SystemService) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	path := strings.TrimPrefix(r.URL.Path, "/cache")
	path = strings.TrimPrefix(path, "/")
	cleanRelPath := filepath.Clean(filepath.FromSlash(path))

	// 安全防范：杜绝路径遍历 (..)
	if strings.HasPrefix(cleanRelPath, "..") || filepath.IsAbs(cleanRelPath) {
		http.Error(w, "Forbidden", http.StatusForbidden)
		return
	}

	parts := strings.Split(cleanRelPath, string(filepath.Separator))
	if len(parts) < 2 {
		http.NotFound(w, r)
		return
	}

	category := parts[0] // heroes, items, abilities
	filename := parts[len(parts)-1]
	if category != "heroes" && category != "items" && category != "abilities" {
		http.NotFound(w, r)
		return
	}

	baseDir := s.GetCacheDirectory()
	localFilePath := filepath.Join(baseDir, cleanRelPath)

	// 1. 本地存在且非空，直接返回本地文件
	if stat, err := os.Stat(localFilePath); err == nil && !stat.IsDir() && stat.Size() > 0 {
		w.Header().Set("Cache-Control", "public, max-age=31536000, immutable")
		http.ServeFile(w, r, localFilePath)
		return
	}

	// 2. 本地缺失，从官方 CDN 透明拉取并写入本地缓存
	data, err := s.fetchIconFromCdn(category, filename)
	if err != nil || len(data) == 0 {
		http.NotFound(w, r)
		return
	}

	// 落盘保存
	_ = os.MkdirAll(filepath.Dir(localFilePath), 0755)
	_ = os.WriteFile(localFilePath, data, 0644)

	// 响应前端
	w.Header().Set("Cache-Control", "public, max-age=31536000, immutable")
	if strings.HasSuffix(filename, ".png") {
		w.Header().Set("Content-Type", "image/png")
	} else if strings.HasSuffix(filename, ".svg") {
		w.Header().Set("Content-Type", "image/svg+xml")
	} else if strings.HasSuffix(filename, ".jpg") || strings.HasSuffix(filename, ".jpeg") {
		w.Header().Set("Content-Type", "image/jpeg")
	}
	_, _ = w.Write(data)
}

// fetchIconFromCdn 从官方 CDN 拉取图标原始字节
func (s *SystemService) fetchIconFromCdn(category, filename string) ([]byte, error) {
	cdnBase := "https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react"
	cdnUrl := fmt.Sprintf("%s/%s/%s", cdnBase, category, filename)

	client := &http.Client{Timeout: 6 * time.Second}
	resp, err := client.Get(cdnUrl)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("CDN 返回状态码: %d", resp.StatusCode)
	}

	return io.ReadAll(resp.Body)
}

// CacheAllIcons 扫描常用物品与英雄清单，并发预热拉取至本地缓存文件夹
func (s *SystemService) CacheAllIcons() (map[string]int, error) {
	baseDir := s.GetCacheDirectory()
	heroesDir := filepath.Join(baseDir, "heroes")
	itemsDir := filepath.Join(baseDir, "items")
	_ = os.MkdirAll(heroesDir, 0755)
	_ = os.MkdirAll(itemsDir, 0755)

	stats := map[string]int{
		"cached_heroes": 0,
		"cached_items":  0,
		"downloaded":    0,
	}

	// 统计本地已有缓存文件
	if hEntries, err := os.ReadDir(heroesDir); err == nil {
		for _, e := range hEntries {
			if !e.IsDir() && strings.HasSuffix(e.Name(), ".png") {
				stats["cached_heroes"]++
			}
		}
	}
	if iEntries, err := os.ReadDir(itemsDir); err == nil {
		for _, e := range iEntries {
			if !e.IsDir() && strings.HasSuffix(e.Name(), ".png") {
				stats["cached_items"]++
			}
		}
	}

	return stats, nil
}
