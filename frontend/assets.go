package frontend

import "embed"

// Dist 静态嵌入前端生产构建产物 (位于 frontend/dist)
//
//go:embed all:dist
var Dist embed.FS
