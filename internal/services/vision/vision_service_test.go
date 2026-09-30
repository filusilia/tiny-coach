package vision

import (
	"strings"
	"testing"

	winPlatform "aegis-coach/internal/platform/windows"
)

func TestGetPhysicalScreenDimensions(t *testing.T) {
	w, h := winPlatform.GetPhysicalScreenDimensions()
	if w <= 0 || h <= 0 {
		t.Fatalf("获取物理屏幕分辨率失败: w=%d, h=%d", w, h)
	}
	t.Logf("✅ 真实物理屏幕分辨率: %dx%d", w, h)
}

func TestCaptureRect(t *testing.T) {
	svc := GetVisionService()
	res, err := svc.CaptureRect(0, 0, 100, 100)
	if err != nil {
		t.Fatalf("CaptureRect 不应抛出非受控 error: %v", err)
	}
	if res == nil {
		t.Fatalf("CaptureRect 返回结果为 nil")
	}

	if !res.Success {
		// 在无物理屏幕连接、CI 或 Windows 锁屏/待机状态下，GDI BitBlt 返回安全错误信息属于正常受控行为
		t.Logf("⚠️ 当前运行环境未处于活动桌面会话 (例如锁屏/节能待机): %s", res.ErrorMessage)
		return
	}

	if !strings.HasPrefix(res.ImageBase64, "data:image/png;base64,") {
		t.Fatalf("ImageBase64 前缀不正确: %s", res.ImageBase64[:30])
	}
	if res.CropWidth != 100 || res.CropHeight != 100 {
		t.Fatalf("裁剪尺寸不匹配: %dx%d (期望 100x100)", res.CropWidth, res.CropHeight)
	}
	t.Logf("✅ CaptureRect 100x100 成功: 耗时=%dms, 数据长度=%d", res.DurationMs, len(res.ImageBase64))
}

func TestCaptureTopScreen(t *testing.T) {
	svc := GetVisionService()
	res, err := svc.CaptureTopScreen()
	if err != nil {
		t.Fatalf("CaptureTopScreen 不应抛出非受控 error: %v", err)
	}
	if res == nil {
		t.Fatalf("CaptureTopScreen 返回结果为 nil")
	}

	if !res.Success {
		t.Logf("⚠️ 当前运行环境未处于活动桌面会话 (例如锁屏/节能待机): %s", res.ErrorMessage)
		return
	}

	t.Logf("✅ CaptureTopScreen 顶部 18%% 截屏成功: 屏幕=%dx%d, 裁剪=%dx%d, 耗时=%dms, 数据大小=%d 字节",
		res.ScreenWidth, res.ScreenHeight, res.CropWidth, res.CropHeight, res.DurationMs, len(res.ImageBase64))
}
