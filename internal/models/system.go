package models

// SystemConfigDTO 系统运行时配置与路径 DTO
type SystemConfigDTO struct {
	CacheDirectory   string `json:"cacheDirectory"`
	DefaultCacheDir  string `json:"defaultCacheDir"`
	IsOverlayRunning bool   `json:"isOverlayRunning"`
	IsOverlayMode    bool   `json:"isOverlayMode"`
}

// LocalModelDTO 本地模型文件与已检测模型 DTO
type LocalModelDTO struct {
	Name     string `json:"name"`
	Path     string `json:"path,omitempty"`
	Source   string `json:"source"` // "disk" 或 "endpoint"
	FileSize int64  `json:"fileSize,omitempty"`
}
