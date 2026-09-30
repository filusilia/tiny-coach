package events

import "github.com/wailsapp/wails/v3/pkg/application"

// Emit 统一通过 Wails v3 事件总线向前端派发事件
func Emit(name string, data ...any) {
	if app := application.Get(); app != nil && app.Event != nil {
		app.Event.Emit(name, data...)
	}
}
