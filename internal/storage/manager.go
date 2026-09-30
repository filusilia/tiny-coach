package storage

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"

	"aegis-coach/internal/config"
)

// Manager 本地缓存路径与文件持久化管理器
type Manager struct {
	mu             sync.RWMutex
	customCacheDir string
}

var (
	managerInstance *Manager
	managerOnce     sync.Once
)

// GetManager 获取全局唯一 storage.Manager 单例
// 关键：SystemService、PlayerService 必须共用同一实例，否则 SetCacheDirectory 后各服务看到不同的路径
func GetManager() *Manager {
	managerOnce.Do(func() {
		managerInstance = &Manager{}
	})
	return managerInstance
}

// ResetForTest 仅供测试使用，重置单例状态与自定义路径
func ResetForTest() {
	managerOnce = sync.Once{}
	managerInstance = nil
}

// NewManager 保留旧调用兼容性，内部返回全局单例
// Deprecated: 新代码请使用 GetManager()
func NewManager() *Manager {
	return GetManager()
}

// GetUserDocumentsDir 读取 Windows 用户真实的“我的文档”物理路径（委托给 internal/config）
func GetUserDocumentsDir() string {
	return config.GetUserDocumentsDir()
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

	return config.GetDefaultCacheDirectory()
}

// EnsureCacheDirectories 首次启动自动创建标准化缓存文件夹结构（heroes, items, tts, rag, matches）
// 幂等设计：若目录已存在则不重复创建，保证历史数据不被覆盖
func (m *Manager) EnsureCacheDirectories(customDir string) (string, error) {
	baseDir := m.ResolveBaseCacheDir(customDir)

	subDirs := []string{
		"data",      // 结构化设置与官方游戏数据缓存
		"heroes",    // 英雄静态数据与官方快照缓存
		"items",     // 现行物品图谱与合成路线缓存
		"abilities", // 技能图标与效果快照缓存
		"tts",       // TTS 离线音频缓存（预录固定短语与解说片段）
		"rag",       // RAG 战术知识库向量索引缓存
		"matches",   // 单场比赛详情与复盘数据本地持久化缓存
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
	return config.GetDefaultCacheDirectory()
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
