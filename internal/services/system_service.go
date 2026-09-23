package services

import (
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/url"
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
			storageManager: storage.GetManager(), // ★ 使用全局单例，与 PlayerService 共享
		}
	})
	return systemServiceInstance
}

// GetStorageManager 暴露内部的 storage.Manager 供其他服务注入或统一访问
func (s *SystemService) GetStorageManager() *storage.Manager {
	return s.storageManager
}

// SetWindow 设置关联的原生主窗口句柄
func (s *SystemService) SetWindow(w *application.WebviewWindow) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.window = w
}

func (s *SystemService) GetDefaultCacheDirectory() string {
	return s.storageManager.GetDefaultCacheDirectory()
}

func (s *SystemService) GetCacheDirectory() string {
	return s.storageManager.GetCacheDirectory()
}

func (s *SystemService) SetCacheDirectory(customDir string) (string, error) {
	return s.storageManager.SetCacheDirectory(customDir)
}

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

		// ★ 仅当目录存在时设置起始目录，避免无效路径导致对话框失败
		if stat, err := os.Stat(current); err == nil && stat.IsDir() {
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
// ★ 去掉 -NonInteractive，避免 GUI 窗体被安全策略抑制
func (s *SystemService) selectDirectoryViaPowerShell() string {
	script := `Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; $f.Description = '选择神盾教练数据缓存文件夹'; $f.ShowNewFolderButton = $true; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Write($f.SelectedPath) }`
	cmd := exec.Command("powershell", "-NoProfile", "-STA", "-Command", script)
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

		if stat, err := os.Stat(current); err == nil && stat.IsDir() {
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
// ★ 去掉 -NonInteractive
func (s *SystemService) selectModelFileViaPowerShell() string {
	script := `Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.Title = '选择本地 AI 模型文件 (支持 GGUF / ONNX / Bin)'; $f.Filter = 'AI 模型文件 (*.gguf;*.onnx;*.bin)|*.gguf;*.onnx;*.bin|所有文件 (*.*)|*.*'; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Write($f.FileName) }`
	cmd := exec.Command("powershell", "-NoProfile", "-STA", "-Command", script)
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

// getDataFilePath 获取安全规整的本地游戏数据缓存文件绝对路径
func (s *SystemService) getDataFilePath(dataType string) (string, error) {
	cleanType := filepath.Clean(dataType)
	cleanType = filepath.Base(cleanType)
	cleanType = strings.TrimSuffix(cleanType, ".json")
	if cleanType == "" || cleanType == "." || cleanType == "/" || cleanType == "\\" {
		return "", fmt.Errorf("无效的数据类型: %s", dataType)
	}
	baseDir := s.GetCacheDirectory()
	dataDir := filepath.Join(baseDir, "data")
	if err := os.MkdirAll(dataDir, 0755); err != nil {
		return "", fmt.Errorf("创建数据缓存目录失败: %w", err)
	}
	return filepath.Join(dataDir, cleanType+".json"), nil
}

// LoadCachedGameData 优先从本地缓存文件夹中读取结构化游戏数据 (如 herostats, heroes_details, items, patch_info)
func (s *SystemService) LoadCachedGameData(dataType string) (string, error) {
	filePath, err := s.getDataFilePath(dataType)
	if err != nil {
		return "", err
	}
	data, err := os.ReadFile(filePath)
	if err != nil {
		return "", err
	}
	if len(data) == 0 {
		return "", fmt.Errorf("缓存数据为空: %s", dataType)
	}
	return string(data), nil
}

// SaveCachedGameData 将从官方/OpenDota拉取的最新完整结构化数据持久化落盘至本地缓存目录
func (s *SystemService) SaveCachedGameData(dataType string, content string) error {
	if strings.TrimSpace(content) == "" {
		return fmt.Errorf("待持久化的缓存内容不能为空")
	}
	filePath, err := s.getDataFilePath(dataType)
	if err != nil {
		return err
	}
	return os.WriteFile(filePath, []byte(content), 0644)
}

// CacheAllIcons 并发下载官方英雄头像与物品图标至本地缓存目录
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

	// 常用高频物品清单 (用于快速构建本地缓存池)
	commonItems := []string{
		"tango", "branches", "magic_stick", "magic_wand", "circlet", "slippers", "gauntlets",
		"mantle", "wraith_band", "bracer", "null_talisman", "power_treads", "phase_boots",
		"arcane_boots", "blink", "black_king_bar", "ultimate_scepter", "aghanims_shard",
		"manta", "bfury", "satanic", "greater_crit", "assault", "shivas_guard", "heart",
		"butterfly", "skadi", "refresher", "heavens_halberd", "armlet", "dragon_lance",
		"hurricane_pike", "diffusal_blade", "octarine_core", "glimmer_cape", "force_staff",
		"recipe", "clarity", "flask", "dust", "ward_observer", "ward_sentry", "bottle",
	}

	// 常用英雄清单
	commonHeroes := []string{
		"antimage", "axe", "bane", "bloodseeker", "crystal_maiden", "drow_ranger",
		"earthshaker", "juggernaut", "mirana", "nevermore", "morphling", "phantom_lancer",
		"puck", "pudge", "razor", "sand_king", "storm_spirit", "sven", "tiny", "vengefulspirit",
		"windrunner", "zuus", "kunkka", "lina", "lion", "shadow_shaman", "slardar", "tidehunter",
		"witch_doctor", "lich", "riki", "enigma", "tinker", "sniper", "necrolyte", "warlock",
		"beastmaster", "queenofpain", "venomancer", "faceless_void", "skeleton_king", "death_prophet",
		"phantom_assassin", "pugna", "templar_assassin", "viper", "luna", "dragon_knight",
		"dazzle", "clockwerk", "leshrac", "furion", "life_stealer", "dark_seer", "clinkz",
		"omniknight", "enchantress", "huskar", "night_stalker", "broodmother", "bounty_hunter",
		"weaver", "jakiro", "batrider", "chen", "spectre", "ancient_apparition", "doom_bringer",
		"ursa", "spirit_breaker", "gyrocopter", "alchemist", "invoker", "silencer", "outworld_destroyer",
		"lycan", "brewmaster", "shadow_demon", "lone_druid", "chaos_knight", "meepo", "treant",
		"ogre_magi", "undying", "rubick", "disruptor", "nyx_assassin", "naga_siren", "keeper_of_the_light",
		"wisp", "visage", "slark", "medusa", "troll_warlord", "centaur", "magnataur", "shredder",
		"bristleback", "tusk", "skywrath_mage", "abaddon", "elder_titan", "legion_commander",
		"techies", "ember_spirit", "earth_spirit", "abyssal_underlord", "terrorblade", "phoenix",
		"oracle", "winter_wyvern", "arc_warden", "monkey_king", "pangolier", "dark_willow",
		"grimstroke", "mars", "void_spirit", "snapfire", "hoodwink", "dawnbreaker", "marci",
		"primal_beast", "muerta", "ringmaster", "kez",
	}

	downloadWorker := func(category string, items []string, targetDir string) {
		client := &http.Client{Timeout: 5 * time.Second}
		for _, name := range items {
			localFile := filepath.Join(targetDir, name+".png")
			if stat, err := os.Stat(localFile); err == nil && stat.Size() > 0 {
				continue
			}
			cdnUrl := fmt.Sprintf("https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/%s/%s.png", category, name)
			req, err := http.NewRequest("GET", cdnUrl, nil)
			if err != nil {
				continue
			}
			req.Header.Set("User-Agent", "DotaAICoach/1.0")
			resp, err := client.Do(req)
			if err == nil && resp.StatusCode == http.StatusOK {
				data, readErr := io.ReadAll(resp.Body)
				resp.Body.Close()
				if readErr == nil && len(data) > 0 {
					_ = os.WriteFile(localFile, data, 0644)
					stats["downloaded"]++
				}
			} else if resp != nil {
				resp.Body.Close()
			}
		}
	}

	// 快速拉取英雄与物品图标
	downloadWorker("heroes", commonHeroes, heroesDir)
	downloadWorker("items", commonItems, itemsDir)

	// 再次统计本地已有缓存文件
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

// GetPrimarySettingsFilePath 获取主设置目录内的 settings.json 绝对路径 (固定在用户文档根目录，永不随缓存分区变动而漂移)
func (s *SystemService) GetPrimarySettingsFilePath() string {
	return filepath.Join(storage.GetUserDocumentsDir(), "DotaAICoach", "settings.json")
}

// GetSettingsFilePath 获取设置文件夹内的核心配置文件绝对路径
func (s *SystemService) GetSettingsFilePath() string {
	return s.GetPrimarySettingsFilePath()
}

// LoadSettings 遵循“先从设置文件夹内取设置，没有再新建”的权威法则：
// 优先多源容灾探测已有配置文件；只有当全部分区均未找到任何有效配置时，才初始化默认设置落盘新建。
func (s *SystemService) LoadSettings() (string, error) {
	primaryPath := s.GetPrimarySettingsFilePath()
	cacheDirPath := filepath.Join(s.GetCacheDirectory(), "settings.json")

	candidates := []string{primaryPath}
	if cacheDirPath != primaryPath {
		candidates = append(candidates, cacheDirPath)
	}

	// 1. 优先遍历候选路径，寻找已存在的合法设置文件
	for _, p := range candidates {
		if data, err := os.ReadFile(p); err == nil && len(data) > 0 {
			var check map[string]any
			if err := json.Unmarshal(data, &check); err == nil && len(check) > 0 {
				log.Printf("[SystemService] 成功从设置路径加载既有配置: %s", p)

				// 若设置中指定了有效的自定义缓存目录，立即令 Go 后端存储单例生效
				if customDir, ok := check["cache_directory"].(string); ok && strings.TrimSpace(customDir) != "" {
					_, _ = s.SetCacheDirectory(customDir)
				}

				// 确保主设置文件目录同样具备该最新内容
				if p != primaryPath {
					_ = os.MkdirAll(filepath.Dir(primaryPath), 0755)
					_ = os.WriteFile(primaryPath, data, 0644)
				}

				return string(data), nil
			}
		}
	}

	// 2. 所有候选路径均未命中或已损坏：新建官方默认设置并安全落盘
	log.Printf("[SystemService] 未在任何设置路径检测到有效配置，正在新建默认设置: %s", primaryPath)
	defaultSettings := map[string]any{
		"gsi_port":                   4444,
		"auto_start_gsi":             true,
		"smart_coach_enabled":        true,
		"voice_broadcast_mode":       2,
		"voice_volume":               100,
		"voice_speed":                1.0,
		"voice_name":                 "zh-CN-YunxiNeural",
		"edge_voice_name":            "zh-CN-YunxiNeural",
		"voice_lexicon_enabled":      true,
		"voice_ssml_enabled":         true,
		"auto_show_overlay":          false,
		"auto_overlay_on_game_start": true,
		"overlay_opacity":            90,
		"hero_rank_tier":             7,
		"hero_sort_mode":             "win_desc",
		"cache_directory":            s.GetCacheDirectory(),
		"dota_player_id":             "",
		"dota_player_name":           "",
		"dota_player_avatar":         "",
		"custom_tts_enabled":         false,
		"custom_tts_engine":          "edge",
		"custom_tts_endpoint":        "http://127.0.0.1:9233",
		"custom_tts_api_key":         "",
		"custom_tts_voice":           "",
		"ai_mode":                    "local_npu",
		"npu_endpoint":               "http://127.0.0.1:11434/v1",
		"npu_model":                  "qwen3.5:1.5b",
		"cloud_api_key":              "",
		"cloud_base_url":             "https://api.deepseek.com/v1",
		"cloud_model":                "deepseek-chat",
		"user_context":               "",
		"reminder_modes": map[string]int{
			"macro_tempo":        2,
			"roshan_tactics":     2,
			"tormentor_tactics":  2,
			"rune_control":       2,
			"buyback_strategy":   2,
			"item_timing_threat": 2,
			"tactical_ai_ask":    2,
			"ability_ready":      2,
			"death_respawn":      2,
		},
	}

	encoded, err := json.MarshalIndent(defaultSettings, "", "  ")
	if err != nil {
		return "{}", fmt.Errorf("构建默认设置异常: %w", err)
	}

	_ = os.MkdirAll(filepath.Dir(primaryPath), 0755)
	if err := os.WriteFile(primaryPath, encoded, 0644); err != nil {
		log.Printf("[SystemService] 新建默认设置落盘失败: %v", err)
	} else {
		log.Printf("[SystemService] 已成功在主设置目录新建默认配置: %s", primaryPath)
	}

	return string(encoded), nil
}

// SaveSettings 将应用配置安全落盘保存至主设置文件夹及自定义缓存目录（双保险持久化）
func (s *SystemService) SaveSettings(settingsJson string) error {
	cleanJson := strings.TrimSpace(settingsJson)
	if cleanJson == "" {
		return fmt.Errorf("设置内容不能为空")
	}

	var check map[string]any
	if err := json.Unmarshal([]byte(cleanJson), &check); err != nil {
		return fmt.Errorf("无效的 JSON 设置格式: %w", err)
	}

	// 若设置中指定了有效的自定义缓存目录，同步后端存储管理器
	var customCacheDir string
	if customDir, ok := check["cache_directory"].(string); ok && strings.TrimSpace(customDir) != "" {
		customCacheDir = strings.TrimSpace(customDir)
		_, _ = s.SetCacheDirectory(customCacheDir)
	}

	var formatted []byte
	if pretty, err := json.MarshalIndent(check, "", "  "); err == nil {
		formatted = pretty
	} else {
		formatted = []byte(cleanJson)
	}

	// 1. 始终优先写入固定可靠的主设置路径
	primaryPath := s.GetPrimarySettingsFilePath()
	_ = os.MkdirAll(filepath.Dir(primaryPath), 0755)
	if err := os.WriteFile(primaryPath, formatted, 0644); err != nil {
		return fmt.Errorf("写入主设置文件失败 %s: %w", primaryPath, err)
	}

	// 2. 若用户自定义了其他缓存分区，镜像保存一份
	if customCacheDir != "" && filepath.Clean(customCacheDir) != filepath.Clean(filepath.Dir(primaryPath)) {
		mirrorPath := filepath.Join(customCacheDir, "settings.json")
		_ = os.MkdirAll(customCacheDir, 0755)
		_ = os.WriteFile(mirrorPath, formatted, 0644)
	}

	log.Printf("[SystemService] 配置已成功持久化至: %s", primaryPath)
	return nil
}

// FetchLatestPatchInfo 从官方 datafeed 安全拉取最新 Dota 2 版本号与详细更新日志，彻底消除前端浏览器同源策略 (CORS) 拦截
func (s *SystemService) FetchLatestPatchInfo() (string, error) {
	client := &http.Client{Timeout: 8 * time.Second}

	var latestVersion string

	// 1. 阶段 A: 从 Valve 官方 datafeed/patchnoteslist 获取最新版本号
	reqList, err := http.NewRequest(http.MethodGet, "https://www.dota2.com/datafeed/patchnoteslist?language=schinese", nil)
	if err == nil {
		reqList.Header.Set("User-Agent", "DotaAICoach/1.0")
		reqList.Header.Set("Accept", "application/json")
		if resp, err := client.Do(reqList); err == nil && resp.StatusCode == http.StatusOK {
			var listResp struct {
				Success bool `json:"success"`
				Patches []struct {
					PatchNumber string `json:"patch_number"`
					PatchName   string `json:"patch_name"`
				} `json:"patches"`
			}
			if err := json.NewDecoder(resp.Body).Decode(&listResp); err == nil && len(listResp.Patches) > 0 {
				last := listResp.Patches[len(listResp.Patches)-1]
				latestVersion = strings.TrimSpace(last.PatchNumber)
				if latestVersion == "" {
					latestVersion = strings.TrimSpace(last.PatchName)
				}
			}
			resp.Body.Close()
		}
	}

	// 降级: 若官方请求受限，从 OpenDota 探测最新版本号
	if latestVersion == "" {
		reqOd, err := http.NewRequest(http.MethodGet, "https://api.opendota.com/api/constants/patch", nil)
		if err == nil {
			reqOd.Header.Set("User-Agent", "DotaAICoach/1.0")
			if resp, err := client.Do(reqOd); err == nil && resp.StatusCode == http.StatusOK {
				var odPatches []struct {
					Name string `json:"name"`
				}
				if err := json.NewDecoder(resp.Body).Decode(&odPatches); err == nil && len(odPatches) > 0 {
					latestVersion = strings.TrimSpace(odPatches[len(odPatches)-1].Name)
				}
				resp.Body.Close()
			}
		}
	}

	// 2. 阶段 B: 若获得了版本号，拉取该版本的详细更新说明
	if latestVersion != "" {
		notesUrl := fmt.Sprintf("https://www.dota2.com/datafeed/patchnotes?version=%s&language=schinese", url.QueryEscape(latestVersion))
		reqNotes, err := http.NewRequest(http.MethodGet, notesUrl, nil)
		if err == nil {
			reqNotes.Header.Set("User-Agent", "DotaAICoach/1.0")
			reqNotes.Header.Set("Accept", "application/json")
			if resp, err := client.Do(reqNotes); err == nil && resp.StatusCode == http.StatusOK {
				var notesRaw struct {
					Success bool `json:"success"`
					Heroes  []struct {
						HeroId    int `json:"hero_id"`
						HeroNotes []struct {
							Note string `json:"note"`
						} `json:"hero_notes"`
						Abilities []struct {
							AbilityId    int `json:"ability_id"`
							AbilityNotes []struct {
								Note string `json:"note"`
							} `json:"ability_notes"`
						} `json:"abilities"`
					} `json:"heroes"`
					Items []struct {
						AbilityId    int `json:"ability_id"`
						AbilityNotes []struct {
							Note string `json:"note"`
						} `json:"ability_notes"`
					} `json:"items"`
				}

				if err := json.NewDecoder(resp.Body).Decode(&notesRaw); err == nil {
					updatedHeroIds := make([]int, 0)
					updatedItemIds := make([]int, 0)
					heroPatchNotes := make(map[string]map[string][]string)
					heroGeneralNotes := make(map[string][]string)
					itemPatchNotes := make(map[string][]string)

					for _, h := range notesRaw.Heroes {
						if h.HeroId > 0 {
							updatedHeroIds = append(updatedHeroIds, h.HeroId)
							hidStr := fmt.Sprintf("%d", h.HeroId)

							var gNotes []string
							for _, hn := range h.HeroNotes {
								if strings.TrimSpace(hn.Note) != "" {
									gNotes = append(gNotes, strings.TrimSpace(hn.Note))
								}
							}
							if len(gNotes) > 0 {
								heroGeneralNotes[hidStr] = gNotes
							}

							aMap := make(map[string][]string)
							for _, a := range h.Abilities {
								if a.AbilityId > 0 {
									var aNotes []string
									for _, an := range a.AbilityNotes {
										if strings.TrimSpace(an.Note) != "" {
											aNotes = append(aNotes, strings.TrimSpace(an.Note))
										}
									}
									if len(aNotes) > 0 {
										aMap[fmt.Sprintf("%d", a.AbilityId)] = aNotes
									}
								}
							}
							if len(aMap) > 0 {
								heroPatchNotes[hidStr] = aMap
							}
						}
					}

					for _, it := range notesRaw.Items {
						if it.AbilityId > 0 {
							updatedItemIds = append(updatedItemIds, it.AbilityId)
							var iNotes []string
							for _, in := range it.AbilityNotes {
								if strings.TrimSpace(in.Note) != "" {
									iNotes = append(iNotes, strings.TrimSpace(in.Note))
								}
							}
							if len(iNotes) > 0 {
								itemPatchNotes[fmt.Sprintf("%d", it.AbilityId)] = iNotes
							}
						}
					}

					result := map[string]any{
						"version":          latestVersion,
						"updatedHeroIds":   updatedHeroIds,
						"updatedItemIds":   updatedItemIds,
						"heroPatchNotes":   heroPatchNotes,
						"heroGeneralNotes": heroGeneralNotes,
						"itemPatchNotes":   itemPatchNotes,
					}

					if encoded, err := json.Marshal(result); err == nil {
						// 自动异步落盘至本地缓存目录
						_ = s.SaveCachedGameData("patch_info", string(encoded))
						resp.Body.Close()
						return string(encoded), nil
					}
				}
				resp.Body.Close()
			}
		}
	}

	// 3. 阶段 C: 若网络异常，优先读取本地已有的 patch_info 缓存
	if cached, err := s.LoadCachedGameData("patch_info"); err == nil && strings.TrimSpace(cached) != "" {
		return cached, nil
	}

	// 4. 阶段 D: 兜底返回规范结构 (零伪造：绝不虚构假英雄)
	fallback := map[string]any{
		"version":          "7.41f",
		"updatedHeroIds":   []int{},
		"updatedItemIds":   []int{},
		"heroPatchNotes":   map[string]any{},
		"heroGeneralNotes": map[string]any{},
		"itemPatchNotes":   map[string]any{},
	}
	encoded, _ := json.Marshal(fallback)
	return string(encoded), nil
}
