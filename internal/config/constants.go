package config

var (
	// Version 应用程序发布版本号（可在编译时通过 -ldflags "-X aegis-coach/internal/config.Version=x.y.z" 动态注入）
	Version = "2.0.0"
	// BuildTime 应用程序构建时间戳
	BuildTime = "2026-09-22"
	// GitCommit 源代码 Git 提交指纹
	GitCommit = "HEAD"

	// DefaultGsiPort Valve GSI 默认监听端口
	DefaultGsiPort = 4444

	// AppDirectoryName 本地数据缓存目录名
	AppDirectoryName = "DotaAICoach"

	// SettingsFileName 主设置文件名
	SettingsFileName = "settings.json"
)
