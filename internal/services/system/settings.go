package system

import (
	"encoding/json"
	"fmt"
	"log"
	"os"
	"path/filepath"
	"strings"

	"aegis-coach/internal/storage"
)

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
					if _, err := s.SetCacheDirectory(customDir); err != nil {
						log.Printf("[SystemService] LoadSettings 恢复自定义缓存目录 [%s] 失败: %v", customDir, err)
					}
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
		"voice_name":                 "MeloTTS 超保真拟人",
		"voice_lexicon_enabled":      true,
		"voice_ssml_enabled":         true,
		"melo_auto_download":         true,
		"auto_show_overlay":          false,
		"auto_overlay_on_game_start": true,
		"overlay_opacity":            90,
		"hero_rank_tier":             7,
		"hero_sort_mode":             "win_desc",
		"cache_directory":            s.GetCacheDirectory(),
		"dota_player_id":             "",
		"dota_player_name":           "",
		"dota_player_avatar":         "",
		"ai_mode":                    "ollama",
		"ai_endpoint":                "http://127.0.0.1:11434/v1",
		"ai_api_key":                 "",
		"ai_model":                   "qwen3.5:1.5b",
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
		if _, err := s.SetCacheDirectory(customCacheDir); err != nil {
			log.Printf("[SystemService] SaveSettings 设置自定义缓存目录 [%s] 失败: %v", customCacheDir, err)
		}
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
	if customCacheDir != "" {
		primaryDir, _ := filepath.Abs(filepath.Dir(primaryPath))
		customDir, _ := filepath.Abs(customCacheDir)
		if !strings.EqualFold(primaryDir, customDir) {
			mirrorPath := filepath.Join(customCacheDir, "settings.json")
			_ = os.MkdirAll(customCacheDir, 0755)
			_ = os.WriteFile(mirrorPath, formatted, 0644)
		}
	}

	log.Printf("[SystemService] 配置已成功持久化至: %s", primaryPath)
	return nil
}
