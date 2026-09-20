package storage

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"

	"golang.org/x/sys/windows/registry"
)

// Manager 本地缓存路径与文件持久化管理器
type Manager struct {
	mu             sync.RWMutex
	customCacheDir string
}

// NewManager 构造本地持久化管理器
func NewManager() *Manager {
	return &Manager{}
}

// GetUserDocumentsDir 读取 Windows 用户真实的“我的文档”物理路径
//
// 因果机制分析：
// 1. 许多玩家将 Windows 文档目录重定向至非系统盘（如 E:\documents）；
// 2. 传统 %USERPROFILE%\Documents 会发生路径错位与无权写入错误；
// 3. 本函数优先读取注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders\Personal 获取绝对物理路径；
// 4. 若注册表缺失则以 %USERPROFILE%\Documents 安全兜底。
func GetUserDocumentsDir() string {
	// 1. 优先读取 Windows 注册表配置的用户实际文档目录 (Personal)
	if k, err := registry.OpenKey(registry.CURRENT_USER, `Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders`, registry.QUERY_VALUE); err == nil {
		defer k.Close()
		if val, _, err := k.GetStringValue("Personal"); err == nil && strings.TrimSpace(val) != "" {
			expanded := os.ExpandEnv(strings.TrimSpace(val))
			return filepath.Clean(expanded)
		}
	}

	// 2. 兜底：%USERPROFILE%\Documents
	userProfile := strings.TrimSpace(os.Getenv("USERPROFILE"))
	if userProfile == "" {
		if home, err := os.UserHomeDir(); err == nil {
			userProfile = home
		}
	}
	return filepath.Join(userProfile, "Documents")
}

// ResolveBaseCacheDir 解析基础缓存根目录（支持环境变量 MODELS_DIR 覆盖，默认：{真实用户文档目录}\DotaAICoach）
func (m *Manager) ResolveBaseCacheDir(override string) string {
	if strings.TrimSpace(override) != "" {
		return filepath.Clean(strings.TrimSpace(override))
	}

	m.mu.RLock()
	custom := m.customCacheDir
	m.mu.RUnlock()
	if strings.TrimSpace(custom) != "" {
		return filepath.Clean(strings.TrimSpace(custom))
	}

	if envModelsDir := strings.TrimSpace(os.Getenv("MODELS_DIR")); envModelsDir != "" {
		return filepath.Clean(envModelsDir)
	}

	docDir := GetUserDocumentsDir()
	return filepath.Join(docDir, "DotaAICoach")
}

// EnsureCacheDirectories 首次启动自动创建标准化缓存文件夹结构（heroes, items, tts, rag, matches）
// 幂等设计：若目录已存在则不重复创建，保证历史数据不被覆盖
func (m *Manager) EnsureCacheDirectories(customDir string) (string, error) {
	baseDir := m.ResolveBaseCacheDir(customDir)

	subDirs := []string{
		"heroes",  // 英雄静态数据与官方快照缓存
		"items",   // 现行物品图谱与合成路线缓存
		"tts",     // TTS 离线音频缓存（预录固定短语与解说片段）
		"rag",     // RAG 战术知识库向量索引缓存
		"matches", // 单场比赛详情与复盘数据本地持久化缓存
	}

	for _, sub := range subDirs {
		target := filepath.Join(baseDir, sub)
		if err := os.MkdirAll(target, 0755); err != nil {
			return baseDir, fmt.Errorf("创建缓存子目录失败 %s: %w", target, err)
		}
	}

	return baseDir, nil
}

// GetDefaultCacheDirectory 获取默认的系统级数据缓存根目录
func (m *Manager) GetDefaultCacheDirectory() string {
	if envModelsDir := strings.TrimSpace(os.Getenv("MODELS_DIR")); envModelsDir != "" {
		return filepath.Clean(envModelsDir)
	}
	docDir := GetUserDocumentsDir()
	return filepath.Join(docDir, "DotaAICoach")
}

// GetCacheDirectory 获取当前正在生效的数据缓存根目录
func (m *Manager) GetCacheDirectory() string {
	return m.ResolveBaseCacheDir("")
}

// SetCacheDirectory 设置用户自定义的数据缓存根目录
func (m *Manager) SetCacheDirectory(customDir string) (string, error) {
	cleanDir := strings.TrimSpace(customDir)
	if cleanDir == "" {
		cleanDir = m.GetDefaultCacheDirectory()
	}

	m.mu.Lock()
	m.customCacheDir = cleanDir
	m.mu.Unlock()

	return m.EnsureCacheDirectories(cleanDir)
}
