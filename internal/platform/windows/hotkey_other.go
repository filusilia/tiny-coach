//go:build !windows

package windows

type HotkeyManager struct{}

func GetHotkeyManager() *HotkeyManager {
	return &HotkeyManager{}
}

func (m *HotkeyManager) StartF10Listener(onTrigger func()) bool {
	return false
}

func (m *HotkeyManager) Stop() {}
