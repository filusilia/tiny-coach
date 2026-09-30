package models

// CaptureResult 原生截屏执行结果
type CaptureResult struct {
	Success      bool   `json:"success"`
	ImageBase64  string `json:"imageBase64"`  // 带 data:image/png;base64, 前缀
	ScreenWidth  int    `json:"screenWidth"`  // 真实物理像素宽
	ScreenHeight int    `json:"screenHeight"` // 真实物理像素高
	CropX        int    `json:"cropX"`        // 裁剪起始 X 物理像素
	CropY        int    `json:"cropY"`        // 裁剪起始 Y 物理像素
	CropWidth    int    `json:"cropWidth"`    // 裁剪区域物理像素宽
	CropHeight   int    `json:"cropHeight"`   // 裁剪区域物理像素高
	Timestamp    int64  `json:"timestamp"`    // 毫秒级 Unix 时间戳
	DurationMs   int64  `json:"durationMs"`   // 截屏与编码总耗时（毫秒）
	ErrorMessage string `json:"errorMessage,omitempty"`
}
