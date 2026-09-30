package build

import _ "embed"

// Icon 嵌入应用程序原生 Windows 图标作为系统托盘图标
//
//go:embed windows/icon.ico
var Icon []byte
