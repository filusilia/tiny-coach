//go:build windows

package windows

import (
	"bytes"
	"encoding/base64"
	"fmt"
	"image"
	"image/png"
	"sync"
	"syscall"
	"time"
	"unsafe"

	"aegis-coach/internal/models"
)

var (
	modgdi32 = syscall.NewLazyDLL("gdi32.dll")

	procGetDeviceCaps          = modgdi32.NewProc("GetDeviceCaps")
	procCreateCompatibleDC     = modgdi32.NewProc("CreateCompatibleDC")
	procCreateCompatibleBitmap = modgdi32.NewProc("CreateCompatibleBitmap")
	procSelectObject           = modgdi32.NewProc("SelectObject")
	procBitBlt                 = modgdi32.NewProc("BitBlt")
	procGetDIBits              = modgdi32.NewProc("GetDIBits")
	procDeleteDC               = modgdi32.NewProc("DeleteDC")
	procDeleteObject           = modgdi32.NewProc("DeleteObject")

	procGetDC            = moduser32.NewProc("GetDC")
	procReleaseDC        = moduser32.NewProc("ReleaseDC")
	procGetSystemMetrics = moduser32.NewProc("GetSystemMetrics")
	procGetDesktopWindow = moduser32.NewProc("GetDesktopWindow")
	procGetWindowDC      = moduser32.NewProc("GetWindowDC")
)

const (
	DESKTOPVERTRES = 117 // 真实物理垂直分辨率（穿透 Windows DPI 缩放）
	DESKTOPHORZRES = 118 // 真实物理水平分辨率（穿透 Windows DPI 缩放）
	SRCCOPY        = 0x00CC0020
	DIB_RGB_COLORS = 0
	BI_RGB         = 0
)

// BITMAPINFOHEADER Windows DIB 位图信息头结构（40 字节）
type BITMAPINFOHEADER struct {
	BiSize          uint32
	BiWidth         int32
	BiHeight        int32
	BiPlanes        uint16
	BiBitCount      uint16
	BiCompression   uint32
	BiSizeImage     uint32
	BiXPelsPerMeter int32
	BiYPelsPerMeter int32
	BiClrUsed       uint32
	BiClrImportant  uint32
}

var captureMu sync.Mutex

// GetPhysicalScreenDimensions 通过 GDI DESKTOPHORZRES/DESKTOPVERTRES 获取真实物理分辨率
func GetPhysicalScreenDimensions() (int, int) {
	hwnd, _, _ := procGetDesktopWindow.Call()
	hdc, _, _ := procGetDC.Call(hwnd)
	if hdc == 0 {
		return 1920, 1080
	}
	defer procReleaseDC.Call(hwnd, hdc)

	w, _, _ := procGetDeviceCaps.Call(hdc, uintptr(DESKTOPHORZRES))
	h, _, _ := procGetDeviceCaps.Call(hdc, uintptr(DESKTOPVERTRES))

	if w == 0 || h == 0 {
		w, _, _ = procGetSystemMetrics.Call(0) // SM_CXSCREEN 兜底
		h, _, _ = procGetSystemMetrics.Call(1) // SM_CYSCREEN 兜底
	}

	if w == 0 {
		w = 1920
	}
	if h == 0 {
		h = 1080
	}

	return int(w), int(h)
}

// CaptureRect 通用底层屏幕物理像素裁剪函数
// 严格执行句柄 defer 释放与 B/R 通道交换
func CaptureRect(x, y, w, h int) (*models.CaptureResult, error) {
	captureMu.Lock()
	defer captureMu.Unlock()

	startTime := time.Now()
	screenW, screenH := GetPhysicalScreenDimensions()

	// 边界裁剪与合法性检查
	if x < 0 {
		w += x
		x = 0
	}
	if y < 0 {
		h += y
		y = 0
	}
	if x >= screenW || y >= screenH {
		return nil, fmt.Errorf("截屏坐标超出屏幕物理范围: (%d, %d), 屏幕: (%d, %d)", x, y, screenW, screenH)
	}
	if x+w > screenW {
		w = screenW - x
	}
	if y+h > screenH {
		h = screenH - y
	}
	if w <= 0 || h <= 0 {
		return nil, fmt.Errorf("无效截屏尺寸: 宽=%d, 高=%d", w, h)
	}

	// 1. 获取屏幕设备上下文
	hdc, _, err := procGetDC.Call(0)
	if hdc == 0 {
		return nil, fmt.Errorf("GetDC(0) 失败: %v", err)
	}
	defer procReleaseDC.Call(0, hdc)

	// 2. 创建兼容内存设备上下文
	compDC, _, err := procCreateCompatibleDC.Call(hdc)
	if compDC == 0 {
		return nil, fmt.Errorf("CreateCompatibleDC 失败: %v", err)
	}
	defer procDeleteDC.Call(compDC)

	// 3. 创建兼容位图
	hBitmap, _, err := procCreateCompatibleBitmap.Call(hdc, uintptr(w), uintptr(h))
	if hBitmap == 0 {
		return nil, fmt.Errorf("CreateCompatibleBitmap 失败: %v", err)
	}
	defer procDeleteObject.Call(hBitmap)

	// 4. 将位图选入内存 DC，保存旧位图句柄以备恢复
	oldBmp, _, _ := procSelectObject.Call(compDC, hBitmap)
	defer procSelectObject.Call(compDC, oldBmp)

	// 5. 将屏幕指定区域像素复制到内存位图中
	ret, _, err := procBitBlt.Call(compDC, 0, 0, uintptr(w), uintptr(h), hdc, uintptr(x), uintptr(y), uintptr(SRCCOPY))
	if ret == 0 {
		return nil, fmt.Errorf("BitBlt 屏幕复制失败 (屏幕可能处于睡眠节电或锁屏保护状态): %v", err)
	}

	// 6. 配置 Top-down DIB 结构头读取原始像素
	var bi BITMAPINFOHEADER
	bi.BiSize = uint32(unsafe.Sizeof(bi))
	bi.BiWidth = int32(w)
	bi.BiHeight = -int32(h) // 负数高度代表 Top-down（第 0 行为顶端），无需再做垂直翻转
	bi.BiPlanes = 1
	bi.BiBitCount = 32
	bi.BiCompression = BI_RGB

	pixelBytes := make([]byte, w*h*4)
	retGet, _, err := procGetDIBits.Call(
		compDC,
		hBitmap,
		0,
		uintptr(h),
		uintptr(unsafe.Pointer(&pixelBytes[0])),
		uintptr(unsafe.Pointer(&bi)),
		uintptr(DIB_RGB_COLORS),
	)
	if retGet == 0 {
		return nil, fmt.Errorf("GetDIBits 读取像素失败: %v", err)
	}

	// 7. BGRA -> RGBA 通道交换，并强制 Alpha 为 255
	for i := 0; i < len(pixelBytes); i += 4 {
		// B(0) G(1) R(2) A(3) -> R(0) G(1) B(2) A(3)
		pixelBytes[i], pixelBytes[i+2] = pixelBytes[i+2], pixelBytes[i]
		pixelBytes[i+3] = 255
	}

	// 8. 封装为 Go RGBA 图像并使用快速压缩级别编码为 PNG Base64
	img := &image.RGBA{
		Pix:    pixelBytes,
		Stride: w * 4,
		Rect:   image.Rect(0, 0, w, h),
	}

	var buf bytes.Buffer
	encoder := png.Encoder{CompressionLevel: png.BestSpeed}
	if err := encoder.Encode(&buf, img); err != nil {
		return nil, fmt.Errorf("PNG 图像编码失败: %v", err)
	}

	duration := time.Since(startTime).Milliseconds()
	imageBase64 := "data:image/png;base64," + base64.StdEncoding.EncodeToString(buf.Bytes())

	return &models.CaptureResult{
		Success:      true,
		ImageBase64:  imageBase64,
		ScreenWidth:  screenW,
		ScreenHeight: screenH,
		CropX:        x,
		CropY:        y,
		CropWidth:    w,
		CropHeight:   h,
		Timestamp:    time.Now().UnixMilli(),
		DurationMs:   duration,
	}, nil
}
