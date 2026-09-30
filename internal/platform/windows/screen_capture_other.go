//go:build !windows

package windows

import (
	"errors"

	"aegis-coach/internal/models"
)

func GetPhysicalScreenDimensions() (int, int) {
	return 1920, 1080
}

func CaptureRect(x, y, w, h int) (*models.CaptureResult, error) {
	return nil, errors.New("原生硬件截屏功能仅在 Windows 系统下支持")
}
