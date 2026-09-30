package config

import (
	"os"
	"path/filepath"
	"strings"

	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/registry"
)

// GetUserDocumentsDir 读取 Windows 用户真实的“我的文档”物理路径
//
// 因果机制分析：
// 1. 许多玩家将 Windows 文档目录重定向至非系统盘（如 E:\documents）；
// 2. 传统 %USERPROFILE%\Documents 会发生路径错位与无权写入错误；
// 3. 本函数依次通过 KnownFolderPath -> 注册表 User Shell Folders -> 注册表 Shell Folders -> 环境变量 -> 用户目录多级权威兜底。
func GetUserDocumentsDir() string {
	// 1. 优先尝试 Windows KnownFolder API
	if dir, err := windows.KnownFolderPath(windows.FOLDERID_Documents, windows.KF_FLAG_DEFAULT); err == nil && strings.TrimSpace(dir) != "" {
		return filepath.Clean(dir)
	}

	// 2. 优先读取 Windows 注册表配置的用户实际文档目录 (User Shell Folders)
	if k, err := registry.OpenKey(registry.CURRENT_USER, `Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders`, registry.QUERY_VALUE); err == nil {
		defer k.Close()
		if val, _, err := k.GetStringValue("Personal"); err == nil && strings.TrimSpace(val) != "" {
			expanded := os.ExpandEnv(strings.TrimSpace(val))
			return filepath.Clean(expanded)
		}
	}

	// 3. 读取经典注册表 Shell Folders
	if k, err := registry.OpenKey(registry.CURRENT_USER, `Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders`, registry.QUERY_VALUE); err == nil {
		defer k.Close()
		if val, _, err := k.GetStringValue("Personal"); err == nil && strings.TrimSpace(val) != "" {
			expanded := os.ExpandEnv(strings.TrimSpace(val))
			return filepath.Clean(expanded)
		}
	}

	// 4. 兜底：%USERPROFILE%\Documents
	userProfile := strings.TrimSpace(os.Getenv("USERPROFILE"))
	if userProfile == "" {
		if home, err := os.UserHomeDir(); err == nil {
			userProfile = home
		}
	}
	return filepath.Join(userProfile, "Documents")
}

// GetDefaultCacheDirectory 获取默认的系统级数据缓存根目录
func GetDefaultCacheDirectory() string {
	if envModelsDir := strings.TrimSpace(os.Getenv("MODELS_DIR")); envModelsDir != "" {
		return filepath.Clean(envModelsDir)
	}
	docDir := GetUserDocumentsDir()
	return filepath.Join(docDir, AppDirectoryName)
}
