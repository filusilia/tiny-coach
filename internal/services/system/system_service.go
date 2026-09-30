package system

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"aegis-coach/internal/storage"

	"github.com/wailsapp/wails/v3/pkg/application"
)

// SystemService 系统文件管理与桌面原生对话框服务
type SystemService struct {
	mu             sync.RWMutex
	storageManager *storage.Manager
	window         *application.WebviewWindow
	httpClient     *http.Client
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
			httpClient: &http.Client{
				Timeout: 8 * time.Second,
				Transport: &http.Transport{
					MaxIdleConns:        50,
					MaxIdleConnsPerHost: 10,
					IdleConnTimeout:     60 * time.Second,
				},
			},
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

// isDialogCancelError 判定原生对话框错误是否属于「用户主动取消」的正常中断。
// 取消不应被当作系统异常向上抛（铁律 7 统一取消语义）。
func isDialogCancelError(err error) bool {
	if err == nil {
		return false
	}
	msg := strings.ToLower(err.Error())
	return strings.Contains(msg, "cancel") || strings.Contains(msg, "0x800704c7")
}

// SelectDirectoryDialog 唤起原生选择文件夹对话框 (纯粹获取路径，无状态篡改副作用)
//
// 严格遵循「原生 API 使用规范」：仅走 Wails v3 官方 app.Dialog.OpenFile()。
// 用户取消 → 返回 ("", nil)；官方对话框不可用 / 调用失败 → 返回错误，由前端 Toast 如实呈现。
func (s *SystemService) SelectDirectoryDialog() (selectedResult string, errResult error) {
	defer func() {
		if r := recover(); r != nil {
			log.Printf("[SystemService] SelectDirectoryDialog 原生对话框 panic: %v", r)
			selectedResult = ""
			errResult = fmt.Errorf("原生文件夹选择对话框异常: %v", r)
		}
	}()

	current := s.GetCacheDirectory()
	appInst := application.Get()
	if appInst == nil || appInst.Dialog == nil {
		return "", fmt.Errorf("原生对话框不可用：当前未处于 Wails 桌面运行时")
	}

	dialog := appInst.Dialog.OpenFile().
		CanChooseDirectories(true).
		CanChooseFiles(false).
		SetTitle("选择神盾教练数据缓存文件夹")

	// ★ 仅当目录存在时设置起始目录，避免无效路径导致对话框失败
	if stat, err := os.Stat(current); err == nil && stat.IsDir() {
		dialog.SetDirectory(current)
	}

	selectedDir, err := dialog.PromptForSingleSelection()
	if err != nil {
		if isDialogCancelError(err) {
			return "", nil
		}
		return "", fmt.Errorf("打开原生文件夹选择对话框失败: %w", err)
	}

	return strings.TrimSpace(selectedDir), nil
}

// SelectModelFileDialog 唤起原生选择本地大模型文件对话框
//
// 严格遵循「原生 API 使用规范」：仅走 Wails v3 官方 app.Dialog.OpenFile() 并追加过滤器。
func (s *SystemService) SelectModelFileDialog() (selectedResult string, errResult error) {
	defer func() {
		if r := recover(); r != nil {
			log.Printf("[SystemService] SelectModelFileDialog 原生对话框 panic: %v", r)
			selectedResult = ""
			errResult = fmt.Errorf("原生模型文件选择对话框异常: %v", r)
		}
	}()

	current := s.GetCacheDirectory()
	appInst := application.Get()
	if appInst == nil || appInst.Dialog == nil {
		return "", fmt.Errorf("原生对话框不可用：当前未处于 Wails 桌面运行时")
	}

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
	if err != nil {
		if isDialogCancelError(err) {
			return "", nil
		}
		return "", fmt.Errorf("打开原生模型文件选择对话框失败: %w", err)
	}

	return strings.TrimSpace(selectedFile), nil
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
	baseCache := filepath.Clean(s.storageManager.GetCacheDirectory())
	// 安全防御：确保目标目录限定在合法缓存目录范围内
	if !strings.EqualFold(target, baseCache) && !strings.HasPrefix(strings.ToLower(target), strings.ToLower(baseCache)+string(filepath.Separator)) {
		target = baseCache
	}
	if _, err := s.storageManager.EnsureCacheDirectories(target); err != nil {
		return err
	}
	_ = os.MkdirAll(target, 0755)

	// 严格遵循「原生 API 使用规范」：仅使用 Wails v3 官方 Env.OpenFileManager，
	// 不再降级到 explorer.exe / cmd start / rundll32（铁律 6 禁用绕过官方 API 的兜底，
	// 且 exec.Command(...).Start() 未 Wait/Release 会造成进程句柄不回收）。
	appInst := application.Get()
	if appInst == nil || appInst.Env == nil {
		return fmt.Errorf("原生资源管理器调用不可用：当前未处于 Wails 桌面运行时")
	}
	if err := appInst.Env.OpenFileManager(target, false); err != nil {
		return fmt.Errorf("在系统资源管理器中打开目录失败: %w", err)
	}
	return nil
}

// ClearCacheDirectory 安全清理指定缓存子目录临时文件
func (s *SystemService) ClearCacheDirectory(dir string) error {
	target := s.storageManager.ResolveBaseCacheDir(dir)
	target = filepath.Clean(target)
	baseCache := filepath.Clean(s.storageManager.GetCacheDirectory())
	if !strings.EqualFold(target, baseCache) && !strings.HasPrefix(strings.ToLower(target), strings.ToLower(baseCache)+string(filepath.Separator)) {
		target = baseCache
	}
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

// TestTtsEndpoint 测试第三方 TTS 端点（已废弃：已全面收敛至本地 MeloTTS 超保真离线引擎）
func (s *SystemService) TestTtsEndpoint(endpoint, apiKey, engineType, voice string) (string, error) {
	return "", fmt.Errorf("自定义 TTS 端点已停用，当前仅支持本地 MeloTTS 超保真拟人引擎")
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

	req, err := http.NewRequestWithContext(context.Background(), http.MethodGet, cdnUrl, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("User-Agent", "DotaAICoach/1.0")

	resp, err := s.httpClient.Do(req)
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

	var mu sync.Mutex
	downloadConcurrently := func(category string, items []string, targetDir string) {
		type job struct {
			name string
		}
		jobs := make(chan job, len(items))
		for _, name := range items {
			jobs <- job{name: name}
		}
		close(jobs)

		var wg sync.WaitGroup
		concurrency := 8
		for i := 0; i < concurrency; i++ {
			wg.Add(1)
			go func() {
				defer wg.Done()
				for j := range jobs {
					localFile := filepath.Join(targetDir, j.name+".png")
					if stat, err := os.Stat(localFile); err == nil && stat.Size() > 0 {
						continue
					}
					cdnUrl := fmt.Sprintf("https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/%s/%s.png", category, j.name)
					req, err := http.NewRequestWithContext(context.Background(), http.MethodGet, cdnUrl, nil)
					if err != nil {
						continue
					}
					req.Header.Set("User-Agent", "DotaAICoach/1.0")
					resp, err := s.httpClient.Do(req)
					if err == nil && resp.StatusCode == http.StatusOK {
						data, readErr := io.ReadAll(resp.Body)
						resp.Body.Close()
						if readErr == nil && len(data) > 0 {
							_ = os.WriteFile(localFile, data, 0644)
							mu.Lock()
							stats["downloaded"]++
							mu.Unlock()
						}
					} else if resp != nil {
						resp.Body.Close()
					}
				}
			}()
		}
		wg.Wait()
	}

	// 并发极速拉取英雄与物品图标
	downloadConcurrently("heroes", commonHeroes, heroesDir)
	downloadConcurrently("items", commonItems, itemsDir)

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

// FetchLatestPatchInfo 从官方 datafeed 安全拉取最新 Dota 2 版本号与详细更新日志，彻底消除前端浏览器同源策略 (CORS) 拦截
func (s *SystemService) FetchLatestPatchInfo() (string, error) {
	client := &http.Client{
		Timeout: 8 * time.Second,
		CheckRedirect: func(req *http.Request, via []*http.Request) error {
			// 严禁重定向至本地防火墙/行为管理网关 (如 1.1.1.2)
			if !strings.Contains(req.URL.Host, "dota2.com") && !strings.Contains(req.URL.Host, "valvesoftware.com") {
				return http.ErrUseLastResponse
			}
			return nil
		},
	}

	var latestVersion string

	// 1. 阶段 A: 从 Valve 官方 datafeed/patchnoteslist 获取最新版本号
	reqList, err := http.NewRequest(http.MethodGet, "https://www.dota2.com/datafeed/patchnoteslist?language=schinese", nil)
	if err == nil {
		reqList.Header.Set("User-Agent", "DotaAICoach/1.0")
		reqList.Header.Set("Accept", "application/json")
		if resp, err := client.Do(reqList); err == nil && resp != nil {
			func() {
				defer resp.Body.Close()
				ct := strings.ToLower(resp.Header.Get("Content-Type"))
				if resp.StatusCode != http.StatusOK || strings.Contains(ct, "text/html") {
					return
				}
				var listResp struct {
					Success bool `json:"success"`
					Patches []struct {
						PatchNumber string `json:"patch_number"`
						PatchName   string `json:"patch_name"`
					} `json:"patches"`
				}
				if err := json.NewDecoder(resp.Body).Decode(&listResp); err == nil && len(listResp.Patches) > 0 {
					var validPatches []string
					for _, p := range listResp.Patches {
						v := strings.TrimSpace(p.PatchNumber)
						if v == "" {
							v = strings.TrimSpace(p.PatchName)
						}
						if v != "" {
							validPatches = append(validPatches, v)
						}
					}
					if len(validPatches) > 0 {
						latestVersion = validPatches[len(validPatches)-1]
					}
				}
			}()
		}
	}

	// 2. 阶段 B: 若获得了版本号，拉取该版本的详细更新说明
	if latestVersion != "" {
		var fetchedResult string
		notesUrl := fmt.Sprintf("https://www.dota2.com/datafeed/patchnotes?version=%s&language=schinese", url.QueryEscape(latestVersion))
		reqNotes, err := http.NewRequest(http.MethodGet, notesUrl, nil)
		if err == nil {
			reqNotes.Header.Set("User-Agent", "DotaAICoach/1.0")
			reqNotes.Header.Set("Accept", "application/json")
			if resp, err := client.Do(reqNotes); err == nil && resp != nil {
				func() {
					defer resp.Body.Close()
					ct := strings.ToLower(resp.Header.Get("Content-Type"))
					if resp.StatusCode != http.StatusOK || strings.Contains(ct, "text/html") {
						return
					}
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
							_ = s.SaveCachedGameData("patch_info", string(encoded))
							fetchedResult = string(encoded)
						}
					}
				}()
			}
		}
		if fetchedResult != "" {
			return fetchedResult, nil
		}
	}

	// 3. 阶段 C: 若网络异常，优先读取本地已有的 patch_info 缓存
	if cached, err := s.LoadCachedGameData("patch_info"); err == nil && strings.TrimSpace(cached) != "" {
		return cached, nil
	}

	// 4. 阶段 D: 没有网络或访问不到时，版本号直接置空，严禁任何兜底行为 (零伪造原则)
	fallback := map[string]any{
		"version":          latestVersion, // 未获取到时即为空字符串 ""
		"updatedHeroIds":   []int{},
		"updatedItemIds":   []int{},
		"heroPatchNotes":   map[string]any{},
		"heroGeneralNotes": map[string]any{},
		"itemPatchNotes":   map[string]any{},
	}
	encoded, _ := json.Marshal(fallback)
	return string(encoded), nil
}
